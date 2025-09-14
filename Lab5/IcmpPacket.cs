namespace PingUtility;

public class IcmpPacket
{
    public byte PacketType { get; set; }
    public byte PacketCode { get; set; }
    public ushort Checksum { get; set; }

    public virtual void Build(byte[] destination, ref int index)
    {
        destination[index] = PacketType;
        index++;
        destination[index] = PacketCode;
        index++;
        Array.Copy(BitConverter.GetBytes(Checksum), 0, destination, index, 2);
        index += 2;
    }

    public virtual void Parse(byte[] source, ref int index)
    {
        PacketType = source[index];
        index++;
        PacketCode = source[index];
        index++;
        Checksum = BitConverter.ToUInt16(source, index);
        index += 2;
    }
}