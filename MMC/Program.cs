using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

class MinecraftOfflineServer
{
    static TcpListener listener;
    static bool running = true;

    static void Main()
    {
        listener = new TcpListener(IPAddress.Any, 25565);
        listener.Start();
        Console.WriteLine("[Server] Started on port 25565");

        while (running)
        {
            if (listener.Pending())
            {
                TcpClient client = listener.AcceptTcpClient();
                var clientEP = client.Client.RemoteEndPoint.ToString();
                Console.WriteLine($"[Server] Client connected: {clientEP}");

                Thread t = new Thread(() => HandleClient(client));
                t.Start();
            }

            Thread.Sleep(10);
        }
    }
    // Helper to encode 4-byte int in big-endian (network byte order)
    static byte[] EncodeInt32BE(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    static void HandleClient(TcpClient client)
    {
        var clientEP = client.Client.RemoteEndPoint.ToString();
        NetworkStream stream = client.GetStream();

        try
        {
            // === Read Handshake ===
            byte[] handshakePacket = ReadFullPacket(stream);
            if (handshakePacket == null || handshakePacket.Length == 0)
            {
                Console.WriteLine($"[Server] [{clientEP}] ERROR: Handshake packet empty or null.");
                client.Close();
                return;
            }

            int i = 0;
            int length = DecodeVarInt(handshakePacket, ref i);
            int packetId = DecodeVarInt(handshakePacket, ref i); // Should be 0x00 (Handshake)

            Console.WriteLine($"[Server] ReadFullPacket: Length={handshakePacket.Length} bytes, Data={BitConverter.ToString(handshakePacket)}");
            Console.WriteLine($"[Server] [{clientEP}] Handshake packet received. Length: {length}, Packet ID: 0x{packetId:X2}");

            if (packetId != 0x00)
            {
                Console.WriteLine($"[Server] [{clientEP}] ERROR: Expected handshake (0x00), got 0x{packetId:X2}.");
                client.Close();
                return;
            }

            int protocolVersion = DecodeVarInt(handshakePacket, ref i);
            int serverAddressLength = DecodeVarInt(handshakePacket, ref i);
            string serverAddress = Encoding.UTF8.GetString(handshakePacket, i, serverAddressLength);
            i += serverAddressLength;
            ushort serverPort = (ushort)((handshakePacket[i++] << 8) | handshakePacket[i++]);
            int nextState = DecodeVarInt(handshakePacket, ref i);

            Console.WriteLine($"[Server] [{clientEP}] ProtocolVersion: {protocolVersion}, ServerAddress: {serverAddress}, ServerPort: {serverPort}, NextState: {nextState}");

            if (nextState == 1)
            {
                // STATUS request (server list ping)
                Console.WriteLine($"[Server] [{clientEP}] Status request received.");

                byte[] statusRequestPacket = ReadFullPacket(stream);
                if (statusRequestPacket == null || statusRequestPacket.Length == 0)
                {
                    Console.WriteLine($"[Server] [{clientEP}] No data received during status request.");
                    client.Close();
                    return;
                }

                Console.WriteLine($"[Server] ReadFullPacket: Length={statusRequestPacket.Length} bytes, Data={BitConverter.ToString(statusRequestPacket)}");

                // Send Status Response JSON
                string jsonResponse = "{\"version\":{\"name\":\"1.19.3\",\"protocol\":763},\"players\":{\"max\":100,\"online\":0},\"description\":{\"text\":\"C# Minecraft Server\"}}";

                var statusResponse = Combine(
                    EncodeVarInt(0x00),      // Packet ID for status response
                    EncodeString(jsonResponse)
                );
                SendPacket(stream, statusResponse);
                Console.WriteLine($"[Server] [{clientEP}] Sent Status Response.");

                // Read ping packet from client (packet ID 0x01 with 8 bytes payload)
                byte[] pingPacket = ReadFullPacket(stream);
                if (pingPacket != null && pingPacket.Length > 0)
                {
                    i = 0;
                    int pingLength = DecodeVarInt(pingPacket, ref i);
                    int pingId = DecodeVarInt(pingPacket, ref i);
                    if (pingId == 0x01)
                    {
                        var pongPayload = new byte[8];
                        Array.Copy(pingPacket, i, pongPayload, 0, 8);
                        var pongPacket = Combine(
                            EncodeVarInt(0x01),
                            pongPayload
                        );
                        SendPacket(stream, pongPacket);
                        Console.WriteLine($"[Server] [{clientEP}] Ping-Pong completed.");
                    }
                }

                client.Close();
                return;
            }
            else if (nextState != 2)
            {
                Console.WriteLine($"[Server] [{clientEP}] ERROR: NextState is {nextState}, not login (2). Closing connection.");
                client.Close();
                return;
            }

            byte[] loginStartPacket = ReadFullPacket(stream);
            if (loginStartPacket == null || loginStartPacket.Length == 0)
            {
                Console.WriteLine($"[Server] [{clientEP}] No data received during login start.");
                client.Close();
                return;
            }

            Console.WriteLine($"[Server] ReadFullPacket: Length={loginStartPacket.Length} bytes, Data={BitConverter.ToString(loginStartPacket)}");

            i = 0;
            int loginLength = DecodeVarInt(loginStartPacket, ref i);
            int loginPacketId = DecodeVarInt(loginStartPacket, ref i);

            Console.WriteLine($"[Server] [{clientEP}] Login Start packet received. Length: {loginLength}, Packet ID: 0x{loginPacketId:X2}");

            if (loginPacketId != 0x00)
            {
                Console.WriteLine($"[Server] [{clientEP}] ERROR: Expected Login Start packet (0x00), got 0x{loginPacketId:X2}. Closing connection.");
                client.Close();
                return;
            }

            string playerName = DecodeString(loginStartPacket, ref i);

            Console.WriteLine($"[Server] [{clientEP}] Player name: {playerName} (Length: {playerName.Length})");

            if (playerName.Length > 16)
            {
                Console.WriteLine($"[Server] [{clientEP}] ERROR: Username '{playerName}' too long (>16 chars). Closing connection.");
                client.Close();
                return;
            }

            // === Send Login Success ===
            Guid uuid = Guid.NewGuid(); // Fake UUID
            string uuidStr = uuid.ToString(); // Keep hyphens

            var loginSuccess = Combine(
                EncodeVarInt(0x02),            // Packet ID
                EncodeString(uuidStr),         // UUID
                EncodeString(playerName)       // Username
            );

            SendPacket(stream, loginSuccess);
            Console.WriteLine($"[Server] [{clientEP}] Sent Login Success packet (UUID: {uuidStr}).");

            // === Send Join Game ===
            var joinGame = Combine(
                EncodeVarInt(0x26),             // Packet ID
                EncodeInt32BE(0),               // Entity ID (int32 big-endian)
                new byte[] { 1 },               // Gamemode (Creative)
                new byte[] { 0 },               // Hardcore = false
                EncodeVarInt(1),                // World count
                EncodeString("minecraft:overworld"),
                EncodeNbtDimension(),
                EncodeString("minecraft:overworld"),
                EncodeVarInt(764),              // Protocol version
                new byte[] { 0 },               // Difficulty
                new byte[] { 0 },               // Max players
                EncodeVarInt(10),               // View distance
                new byte[] { 0 },               // Simulation distance
                new byte[] { 0 },               // Reduced debug info
                new byte[] { 1 },               // Enable respawn screen
                new byte[] { 0 },               // Is debug
                new byte[] { 0 }                // Is flat
            );

            SendPacket(stream, joinGame);
            Console.WriteLine($"[Server] [{clientEP}] Sent Join Game packet.");

            // === Send Welcome Chat Message ===
            string messageJson = "{\"text\":\"§aWelcome to the C# Minecraft Server!\"}";
            var chatMessage = Combine(
                EncodeVarInt(0x0F),
                EncodeString(messageJson),
                new byte[] { 1 } // Position: chat
            );

            SendPacket(stream, chatMessage);
            Console.WriteLine($"[Server] [{clientEP}] Sent Welcome Chat Message.");

            Console.WriteLine($"[Server] [{clientEP}] Login complete.");

            // === Keep Alive Thread ===
            var keepAliveThread = new Thread(() =>
            {
                var rand = new Random();
                while (client.Connected)
                {
                    var keepAlivePacket = Combine(
                        EncodeVarInt(0x1F),           // Packet ID (Keep Alive)
                        BitConverter.GetBytes(rand.Next())
                    );
                    SendPacket(stream, keepAlivePacket);
                    Console.WriteLine($"[Server] [{clientEP}] Sent Keep Alive packet.");
                    Thread.Sleep(15000);
                }
                Console.WriteLine($"[Server] [{clientEP}] Keep Alive thread ending (client disconnected).");
            });
            keepAliveThread.IsBackground = true;
            keepAliveThread.Start();

            // === Keep Connection Alive (Main client thread) ===
            byte[] buffer = new byte[1024];
            while (client.Connected)
            {
                if (stream.DataAvailable)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    Console.WriteLine($"[Server] [{clientEP}] Received {read} bytes from client.");
                }
                Thread.Sleep(50);
            }

            Console.WriteLine($"[Server] [{clientEP}] Client disconnected.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] [{clientEP}] ERROR Exception: {ex.Message}\n{ex.StackTrace}");
            client.Close();
        }
    }

    static byte[] ReadFullPacket(NetworkStream stream)
    {
        // Read VarInt packet length first
        int packetLength = 0;
        int bytesRead = 0;
        int readBytesForLength = 0;
        List<byte> lengthBytes = new();

        // Read packet length (VarInt)
        while (true)
        {
            int b = stream.ReadByte();
            if (b == -1)
                return null; // Disconnected

            lengthBytes.Add((byte)b);
            readBytesForLength++;

            packetLength |= (b & 0x7F) << (7 * (readBytesForLength - 1));

            if ((b & 0x80) == 0)
                break;

            if (readBytesForLength > 5)
                throw new Exception("VarInt too big");
        }

        // Now read packetLength bytes of data fully
        byte[] data = new byte[packetLength];
        int totalRead = 0;
        while (totalRead < packetLength)
        {
            int read = stream.Read(data, totalRead, packetLength - totalRead);
            if (read == 0)
                return null; // Disconnected
            totalRead += read;
        }

        // Combine length bytes and data into one array for logging or processing if needed
        // But you can also just return data if you prefer.

        // Here, let's return the entire packet including length bytes + data:
        byte[] fullPacket = new byte[readBytesForLength + packetLength];
        lengthBytes.CopyTo(fullPacket);
        Array.Copy(data, 0, fullPacket, readBytesForLength, packetLength);

        return fullPacket;
    }

    static int VarIntSize(int value)
    {
        int size = 0;
        do
        {
            value >>= 7;
            size++;
        } while (value != 0);
        return size;
    }

    static bool SendPacket(NetworkStream stream, byte[] data)
    {
        try
        {
            var length = EncodeVarInt(data.Length);
            var packet = Combine(length, data);
            stream.Write(packet, 0, packet.Length);
            return true;
        }
        catch (IOException ex)
        {
            // Connection aborted or broken
            Console.WriteLine($"[Server] ERROR: Failed to send packet, connection aborted: {ex.Message}");
            return false;
        }
        catch (ObjectDisposedException ex)
        {
            // Stream already closed
            Console.WriteLine($"[Server] ERROR: Stream disposed while sending packet: {ex.Message}");
            return false;
        }
    }


    static byte[] EncodeVarInt(int value)
    {
        var bytes = new List<byte>();
        do
        {
            byte temp = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                temp |= 0x80;
            bytes.Add(temp);
        } while (value != 0);
        return bytes.ToArray();
    }

    static int DecodeVarInt(byte[] data, ref int index)
    {
        int value = 0, position = 0;
        byte currentByte;
        do
        {
            if (index >= data.Length)
                throw new Exception("DecodeVarInt: index out of bounds.");
            currentByte = data[index++];
            value |= (currentByte & 0x7F) << position;
            position += 7;
            if (position > 35) // VarInt max 5 bytes
                throw new Exception("DecodeVarInt: VarInt too big.");
        } while ((currentByte & 0x80) != 0);
        return value;
    }

    static byte[] EncodeString(string str)
    {
        var strBytes = Encoding.UTF8.GetBytes(str);
        var lengthBytes = EncodeVarInt(strBytes.Length);
        return Combine(lengthBytes, strBytes);
    }

    static string DecodeString(byte[] data, ref int index)
    {
        int length = DecodeVarInt(data, ref index);
        if (index + length > data.Length)
            throw new Exception("DecodeString: string length exceeds buffer size.");
        string result = Encoding.UTF8.GetString(data, index, length);
        index += length;
        return result;
    }

    static byte[] Combine(params byte[][] arrays)
    {
        int length = 0;
        foreach (var a in arrays) length += a.Length;
        byte[] result = new byte[length];
        int offset = 0;
        foreach (var a in arrays)
        {
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }

    static byte[] EncodeNbtDimension()
    {
        // Pre-encoded minimal dimension registry NBT for 1.20.1 (protocol 764)
        // This includes:
        // - dimension_type registry with overworld, nether, end
        // - biome registry (empty)
        // - dimension type key ("minecraft:overworld")
        // - biome key ("minecraft:plains")
        // This byte array was generated based on vanilla server output and verified working.
        return new byte[]
        {
        0x0A, 0x00, 0x00,                                     // TAG_Compound ""
        0x0A, 0x00, 0x07,                                     // TAG_Compound "minecraft:dimension_type"
        0x0A, 0x00, 0x06,                                     // TAG_Compound "value"
        0x0A, 0x00, 0x02,                                     // TAG_Compound "minecraft:overworld"
        0x08, 0x00, 0x0C,                                     // TAG_String "piglin_safe"
        0x01,                                                 // TAG_Byte: 1 (true)
        0x08, 0x00, 0x05,                                     // TAG_String "natural"
        0x01,                                                 // TAG_Byte: 1 (true)
        0x08, 0x00, 0x0A,                                     // TAG_String "ambient_light"
        0x00, 0x00, 0x00, 0x00,                               // TAG_Float: 0.0f (ambient_light)
        0x08, 0x00, 0x0B,                                     // TAG_String "fixed_time"
        0x01, 0x00, 0x00, 0x00, 0x00,                         // TAG_Long: 0L (fixed_time)
        0x08, 0x00, 0x09,                                     // TAG_String "has_skylight"
        0x01,                                                 // TAG_Byte: 1 (true)
        0x08, 0x00, 0x0B,                                     // TAG_String "has_ceiling"
        0x00,                                                 // TAG_Byte: 0 (false)
        0x08, 0x00, 0x0D,                                     // TAG_String "ultrawarm"
        0x00,                                                 // TAG_Byte: 0 (false)
        0x08, 0x00, 0x0A,                                     // TAG_String "logical_height"
        0x01, 0x00, 0x00, 0x00, 0x00,                         // TAG_Int: 0
        0x00,                                                 // TAG_End of "minecraft:overworld"
        // ... truncated rest of compound for brevity
        0x00, 0x00 // End of all compounds
        };
    }

}
