using System.Buffers.Binary;
using System.Text;

namespace STS2MobileIos.Steam;

// The small protobuf wire subset used by Steam login and Cloud messages.
// Field numbers follow SteamDatabase/Protobufs; no runtime code generation.
internal sealed class SteamWire
{
    private readonly MemoryStream _output = new();
    public SteamWire Number(int field, ulong value) { Varint((ulong)field << 3); Varint(value); return this; }
    public SteamWire Fixed(int field, ulong value)
    {
        Varint(((ulong)field << 3) | 1);
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _output.Write(bytes);
        return this;
    }
    public SteamWire Text(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    public SteamWire Bytes(int field, byte[] value)
    {
        Varint(((ulong)field << 3) | 2); Varint((ulong)value.Length); _output.Write(value); return this;
    }
    public byte[] ToArray() => _output.ToArray();
    private void Varint(ulong value)
    {
        while (value >= 128) { _output.WriteByte((byte)(value | 128)); value >>= 7; }
        _output.WriteByte((byte)value);
    }

    public readonly record struct Field(int Id, ulong Number, byte[] Data)
    {
        public string Text => Encoding.UTF8.GetString(Data ?? Array.Empty<byte>());
    }

    public static List<Field> Read(byte[] bytes)
    {
        var fields = new List<Field>();
        int position = 0;
        ulong ReadNumber()
        {
            ulong value = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                if (position >= bytes.Length) throw new InvalidDataException("Truncated Steam message.");
                byte next = bytes[position++];
                if (shift == 63 && next > 1) throw new InvalidDataException("Invalid Steam integer.");
                value |= (ulong)(next & 127) << shift;
                if (next < 128) return value;
            }
            throw new InvalidDataException("Invalid Steam integer.");
        }
        while (position < bytes.Length)
        {
            ulong tag = ReadNumber();
            int id = checked((int)(tag >> 3));
            if (id == 0) throw new InvalidDataException("Invalid Steam field.");
            int wire = (int)(tag & 7);
            if (wire == 0) { fields.Add(new(id, ReadNumber(), null)); continue; }
            int size = wire switch { 1 => 8, 2 => checked((int)ReadNumber()), 5 => 4, _ => throw new InvalidDataException("Unsupported Steam field.") };
            if (size < 0 || size > bytes.Length - position) throw new InvalidDataException("Truncated Steam field.");
            if (wire == 1) fields.Add(new(id, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position, size)), null));
            else if (wire == 5) fields.Add(new(id, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, size)), null));
            else fields.Add(new(id, 0, bytes.AsSpan(position, size).ToArray()));
            position += size;
        }
        return fields;
    }
}
