
// PacketUtils.cs
using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using fNbt;

public static class PacketUtils
{
    public static byte[] EncodeVarInt(int value)
    {
        using MemoryStream ms = new();

        do
        {
            byte temp = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                temp |= 0x80;
            }
            ms.WriteByte(temp);
        } while (value != 0);

        return ms.ToArray();
    }



    public static int DecodeVarInt(byte[] data, ref int index)
    {
        int numRead = 0;
        int result = 0;
        byte read;
        do
        {
            if (index >= data.Length) throw new Exception("VarInt out of range");
            read = data[index++];
            int value = (read & 0b01111111);
            result |= (value << (7 * numRead));

            numRead++;
            if (numRead > 5) throw new Exception("VarInt too big");
        } while ((read & 0b10000000) != 0);
        return result;
    }

    public static byte[] EncodeString(string str)
    {
        byte[] strBytes = Encoding.UTF8.GetBytes(str);
        byte[] length = EncodeVarInt(strBytes.Length);
        return Combine(length, strBytes);
    }

    public static string DecodeString(byte[] data, ref int index)
    {
        int length = DecodeVarInt(data, ref index);
        string result = Encoding.UTF8.GetString(data, index, length);
        index += length;
        return result;
    }

    public static byte[] Combine(params byte[][] arrays)
    {
        int length = 0;
        foreach (var arr in arrays) length += arr.Length;
        byte[] result = new byte[length];
        int offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }
    public static void SendPacket(Stream stream, byte[] packet)
    {
        byte[] lengthPrefix = EncodeVarInt(packet.Length);

        Logger.Log($"[SendPacket] Declaring length: {packet.Length}, varint={BitConverter.ToString(lengthPrefix)}");
        Logger.Log($"[SendPacket] Final packet = {BitConverter.ToString(lengthPrefix)} | {BitConverter.ToString(packet)}");

        try
        {
            stream.Write(lengthPrefix, 0, lengthPrefix.Length);
            stream.Write(packet, 0, packet.Length);
            stream.Flush(); // <-- force flush to send data immediately
        }
        catch (Exception ex)
        {
            Logger.Log($"[SendPacket] Exception sending packet: {ex}");
            throw;
        }
    }


    public static byte[] EncodeInt32BE(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    public static byte[] EncodeInt64BE(long value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }


    public static byte[] EncodeNbtDimension()
    {
        // Root compound
        var root = new NbtCompound("")

    {
        new NbtCompound("minecraft:dimension_type")
        {
            new NbtString("type", "minecraft:dimension_type"),
            new NbtList("value", NbtTagType.Compound)
            {
                new NbtCompound()
                {
                    new NbtString("name", "minecraft:overworld"),
                    new NbtCompound("element")
                    {
                        new NbtByte("piglin_safe", 0),
                        new NbtByte("has_raids", 1),
                        new NbtFloat("ambient_light", 0.0f),
                        new NbtString("infiniburn", "#minecraft:infiniburn_overworld"),
                        new NbtByte("ultrawarm", 0),
                        new NbtByte("natural", 1),
                        new NbtByte("has_ceiling", 0),
                        new NbtByte("bed_works", 1),
                        new NbtByte("respawn_anchor_works", 0),
                        new NbtByte("has_skylight", 1),
                        new NbtString("effects", "minecraft:overworld"),
                        new NbtInt("logical_height", 384),
                        new NbtFloat("coordinate_scale", 1.0f),
                        new NbtByte("has_custom_weather", 0),
                        new NbtByte("fixed_time", 0),
                        new NbtByte("min_y", 0),
                        new NbtInt("height", 384),
                        new NbtByte("raid_capable", 1),
                        new NbtByte("monster_spawn_light_level", 7),
                        new NbtByte("monster_spawn_block_light_limit", 0),
                        new NbtByte("natural_regeneration", 1)
                    }
                }
            }
        }
    };

        var nbtFile = new NbtFile(root);

        using MemoryStream ms = new();
        nbtFile.SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }
    public static byte[] GetDimensionCodec()
    {
        var overworldElement = new NbtCompound("element")
    {
        new NbtString("piglin_safe", "false"),
        new NbtString("natural", "true"),
        new NbtFloat("ambient_light", 0.0f),
        new NbtString("infiniburn", "#minecraft:infiniburn_overworld"),
        new NbtByte("respawn_anchor_works", 0),
        new NbtByte("has_skylight", 1),
        new NbtByte("bed_works", 1),
        new NbtString("effects", "minecraft:overworld"),
        new NbtByte("has_raids", 1),
        new NbtInt("logical_height", 384),
        new NbtFloat("coordinate_scale", 1.0f),
        new NbtByte("ultrawarm", 0),
        new NbtByte("has_ceiling", 0),
        new NbtByte("fixed_time", 0),
        new NbtInt("height", 384),
        new NbtByte("min_y", 0),
        new NbtByte("raid_capable", 1),
        new NbtByte("monster_spawn_light_level", 7),
        new NbtByte("monster_spawn_block_light_limit", 0),
        new NbtByte("natural_regeneration", 1),
    };

        var overworldCompound = new NbtCompound
    {
        new NbtString("name", "minecraft:overworld"),
        new NbtInt("id", 0),
        overworldElement
    };

        var dimensionTypeCompound = new NbtCompound("minecraft:dimension_type")
    {
        new NbtString("type", "minecraft:dimension_type"),
        new NbtList("value", NbtTagType.Compound)
        {
            overworldCompound
        }
    };

        var biomeCompound = new NbtCompound("minecraft:worldgen/biome")
    {
        new NbtString("type", "minecraft:worldgen/biome"),
        new NbtList("value", NbtTagType.Compound) // empty biome list is valid
    };

        var root = new NbtCompound("")
    {
        dimensionTypeCompound,
        biomeCompound
    };

        using var ms = new MemoryStream();
        new NbtFile(root).SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }



    public static Guid GenerateOfflineUUID(string playerName)
    {
        string offlineName = "OfflinePlayer:" + playerName;
        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(offlineName));

        // Set version to 3 (name-based MD5)
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        // Set variant to RFC 4122
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash);
    }
    public static int ReadVarInt(BinaryReader reader)
    {
        int value = 0;
        int position = 0;
        byte currentByte;

        while (true)
        {
            currentByte = reader.ReadByte();
            value |= (currentByte & 0x7F) << (7 * position);
            position++;

            if ((currentByte & 0x80) == 0)
                break;

            if (position > 5)
                throw new Exception("VarInt too big");
        }

        return value;
    }
    public static string ReadString(BinaryReader reader)
    {
        int length = ReadVarInt(reader);
        byte[] data = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(data);
    }
    public static byte[] ReadFullPacket(BinaryReader reader)
    {
        int length = ReadVarInt(reader);

        byte[] packetData = reader.ReadBytes(length);
        if (packetData.Length < length)
            throw new IOException("Stream closed while reading packet");

        return packetData;
    }



}