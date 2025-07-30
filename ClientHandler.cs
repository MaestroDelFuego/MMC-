using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MMC;

public static class ClientHandler
{
    private const int LOGIN_TIMEOUT_MS = 10000; // 10 seconds login timeout

    public static void Handle(TcpClient client)
    {
        using NetworkStream stream = client.GetStream();
        using BinaryReader reader = new BinaryReader(stream);
        using BinaryWriter writer = new BinaryWriter(stream);

        try
        {
            // Read handshake packet length and ID
            int packetLength = PacketUtils.ReadVarInt(reader);
            int packetId = PacketUtils.ReadVarInt(reader);

            if (packetId == 0x00) // Handshake
            {
                int protocolVersion = PacketUtils.ReadVarInt(reader);
                string serverAddress = PacketUtils.ReadString(reader);
                ushort serverPort = reader.ReadUInt16();
                int nextState = PacketUtils.ReadVarInt(reader);

                Logger.Log($"[Client] Handshake: proto={protocolVersion}, addr={serverAddress}, port={serverPort}, nextState={nextState}");

                if (nextState == 1) // Status
                {
                    HandleStatus(reader, writer);
                }
                else if (nextState == 2) // Login
                {
                    HandleLogin(client, stream, reader, writer);
                }
                else
                {
                    Logger.Log("[Client] Unsupported nextState (not 1 or 2) — not implemented.");
                }
            }
            else
            {
                Logger.Log($"[Client] Unexpected first packet ID 0x{packetId:X2} (expected handshake 0x00).");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("[Client] Exception: " + ex.Message);
        }
        finally
        {
            client.Close();
            Logger.Log("[Client] Disconnected.");
        }
    }

    private static void HandleStatus(BinaryReader reader, BinaryWriter writer)
    {
        Logger.Log("[Client] Status Request");

        string jsonResponse = "{\"version\":{\"name\":\"1.20.1\",\"protocol\":763},\"players\":{\"max\":20,\"online\":0},\"description\":{\"text\":\"C# Minecraft Server\"}}";
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonResponse);
        byte[] jsonLength = PacketUtils.EncodeVarInt(jsonBytes.Length);

        using MemoryStream statusPacket = new();
        statusPacket.Write(PacketUtils.EncodeVarInt(0x00)); // Packet ID
        statusPacket.Write(jsonLength);
        statusPacket.Write(jsonBytes);

        byte[] finalPacket = statusPacket.ToArray();
        writer.Write(PacketUtils.EncodeVarInt(finalPacket.Length));
        writer.Write(finalPacket);
        Logger.Log("[Client] Sent Status Response");

        // Read ping packet
        int pingLen = PacketUtils.ReadVarInt(reader);
        int pingId = PacketUtils.ReadVarInt(reader);
        long payload = reader.ReadInt64();
        Logger.Log($"[Client] Received Ping: {payload}");

        // Convert payload to big-endian byte array
        byte[] payloadBytes = PacketUtils.EncodeInt64BE(payload);

        // Send Pong response
        using MemoryStream pongPacket = new();
        pongPacket.Write(PacketUtils.EncodeVarInt(0x01)); // Pong packet ID
        pongPacket.Write(payloadBytes);

        byte[] pong = pongPacket.ToArray();
        writer.Write(PacketUtils.EncodeVarInt(pong.Length));
        writer.Write(pong);
        Logger.Log("[Client] Sent Pong");
    }


    private static void HandleLogin(TcpClient client, NetworkStream stream, BinaryReader reader, BinaryWriter writer)
    {
        Logger.Log("[Client] Login phase started.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool loginStartReceived = false;

        while (client.Connected && !loginStartReceived)
        {
            if (!stream.DataAvailable)
            {
                if (sw.ElapsedMilliseconds > LOGIN_TIMEOUT_MS)
                {
                    Logger.Log("[Client] Login timeout. Closing connection.");
                    client.Close();
                    return;
                }
                Thread.Sleep(50);
                continue;
            }

            byte[] loginPacket = PacketUtils.ReadFullPacket(reader);
            if (loginPacket == null || loginPacket.Length == 0)
            {
                Logger.Log("[Client] ERROR: No packet received during login.");
                client.Close();
                return;
            }

            Logger.Log($"[Client] Raw packet: {BitConverter.ToString(loginPacket)}");

            int idx = 0;
            int innerPacketId = PacketUtils.DecodeVarInt(loginPacket, ref idx);
            Logger.Log($"[Client] Login packet received: 0x{innerPacketId:X2}");

            switch (innerPacketId)
            {
                case 0x00: // Login Start
                    {
                        string playerName = PacketUtils.DecodeString(loginPacket, ref idx);
                        Logger.Log($"[Client] Login Start received. Player name: {playerName}");

                        if (playerName.Length > 16)
                        {
                            Logger.Log("[Client] ERROR: Player name too long (>16 chars).");
                            client.Close();
                            return;
                        }

                        // Generate offline UUID based on player name (no auth)
                        Guid playerUUID = PacketUtils.GenerateOfflineUUID(playerName);
                        Logger.Log($"[Client] Generated offline UUID: {playerUUID}");

                        // Send Login Success (packet ID 0x02)
                        using MemoryStream loginSuccessStream = new();

                        // Packet ID
                        loginSuccessStream.Write(PacketUtils.EncodeVarInt(0x02));

                        // UUID as raw 16 bytes
                        loginSuccessStream.Write(playerUUID.ToByteArray());

                        // Player name string
                        loginSuccessStream.Write(PacketUtils.EncodeString(playerName));

                        byte[] loginSuccessPacket = loginSuccessStream.ToArray();
                        PacketUtils.SendPacket(stream, loginSuccessPacket);
                        Logger.Log("[Client] Sent Login Success packet.");

                        using (MemoryStream joinGameStream = new())
                        {
                            joinGameStream.Write(PacketUtils.EncodeVarInt(0x26)); // Packet ID: Join Game

                            joinGameStream.Write(PacketUtils.EncodeInt32BE(0)); // Entity ID
                            joinGameStream.WriteByte(0x00); // Is Hardcore: false
                            joinGameStream.WriteByte(0x00); // Gamemode: Survival (0)
                            joinGameStream.WriteByte(0xFF); // Previous Gamemode: -1 (none)

                            joinGameStream.Write(PacketUtils.EncodeVarInt(1)); // World Count
                            joinGameStream.Write(PacketUtils.EncodeString("minecraft:overworld")); // World name

                            joinGameStream.Write(PacketUtils.GetDimensionCodec()); // Full NBT Codec (dimension + biome)

                            joinGameStream.Write(PacketUtils.EncodeString("minecraft:overworld")); // Dimension
                            joinGameStream.Write(PacketUtils.EncodeInt64BE(0L)); // Hashed Seed

                            joinGameStream.Write(PacketUtils.EncodeVarInt(20)); // Max Players (ignored)
                            joinGameStream.Write(PacketUtils.EncodeVarInt(10)); // View Distance
                            joinGameStream.Write(PacketUtils.EncodeVarInt(10)); // Simulation Distance

                            joinGameStream.WriteByte(0); // Reduced Debug Info: false
                            joinGameStream.WriteByte(1); // Enable respawn screen: true
                            joinGameStream.WriteByte(0); // Is debug: false
                            joinGameStream.WriteByte(0); // Is flat: false

                            joinGameStream.WriteByte(0); // Has death location: false (no extra fields)
                            joinGameStream.Write(PacketUtils.EncodeVarInt(0)); // Portal cooldown

                            byte[] final = joinGameStream.ToArray();
                            PacketUtils.SendPacket(stream, final);
                            Logger.Log("[Client] Sent Join Game packet.");
                        }

                        loginStartReceived = true;
                        break;
                    }

                case 0x01: // Login Plugin Request (optional)
                    {
                        int transactionId = PacketUtils.DecodeVarInt(loginPacket, ref idx);
                        Logger.Log($"[Client] Received Plugin Request: transactionId={transactionId}");

                        var responsePacket = PacketUtils.Combine(
                            PacketUtils.EncodeVarInt(0x06),
                            PacketUtils.EncodeVarInt(transactionId),
                            new byte[] { 1 } // success = true
                        );
                        PacketUtils.SendPacket(stream, responsePacket);
                        Logger.Log($"[Client] Sent Plugin Response (accepted) for transaction {transactionId}");
                        break;
                    }

                case 0x06: // Plugin Response
                    {
                        int transactionId = PacketUtils.DecodeVarInt(loginPacket, ref idx);
                        bool success = loginPacket[idx++] != 0;

                        Logger.Log($"[Client] Received Plugin Response: transactionId={transactionId}, success={success}");

                        if (!success)
                        {
                            string message = PacketUtils.DecodeString(loginPacket, ref idx);
                            Logger.Log($"[Client] Plugin Response failure message: {message}");
                        }
                        break;
                    }

                default:
                    Logger.Log($"[Client] Ignoring unexpected login packet ID 0x{innerPacketId:X2}.");
                    break;
            }

            sw.Restart();
        }

        if (!loginStartReceived)
        {
            Logger.Log("[Client] Login start packet never received, disconnecting.");
            client.Close();
        }
    }
}
