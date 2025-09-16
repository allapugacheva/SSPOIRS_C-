using System.Text;

namespace PingUtility;

public class IcmpPacket
{
    public byte PacketType { get; set; }
    public byte PacketCode { get; set; }
    public ushort Checksum { get; set; }
    public ushort Identifier { get; set; }
    public ushort SequenceNumber { get; set; }
    public string Data { get; set; }

    public IcmpPacket()
    {
        PacketType = 8;
        PacketCode = 0;
    }

    public virtual void Build(byte[] destination, ref int index)
    {
        destination[index] = PacketType;
        index++;
        destination[index] = PacketCode;
        index++;
        Array.Copy(BitConverter.GetBytes(Checksum), 0, destination, index, 2);
        index += 2;
        Array.Copy(BitConverter.GetBytes(Identifier), 0, destination, index, 2);
        index += 2;
        Array.Copy(BitConverter.GetBytes(SequenceNumber), 0, destination, index, 2);
        index += 2;
        var buf = Encoding.ASCII.GetBytes(Data);
        Array.Copy(buf, 0, destination, index, 32);
        index += 32;
    }

    public virtual void Parse(byte[] source, ref int index)
    {
        PacketType = source[index];
        index++;
        PacketCode = source[index];
        index++;
        Checksum = BitConverter.ToUInt16(source, index);
        index += 2;
        Identifier = BitConverter.ToUInt16(source, index);
        index += 2;
        SequenceNumber = BitConverter.ToUInt16(source, index);
        index += 2;
        Data = Encoding.ASCII.GetString(source, index, 32);
        index += 32;
    }
}