using System.Drawing;
using System.Text;

namespace PingUtility;

public class IcmpEchoPacket : IcmpPacket
{
    public ushort Identifier { get; set; }
    public ushort SequenceNumber { get; set; }
    public string Data { get; set; }

    public IcmpEchoPacket() : base()
    {
        PacketType = 8;
        PacketCode = 0;
    }

    public override void Build(byte[] destination, ref int index)
    {
        base.Build(destination, ref index);

        Array.Copy(BitConverter.GetBytes(Identifier), 0, destination, index, 2);
        index += 2;
        Array.Copy(BitConverter.GetBytes(SequenceNumber), 0, destination, index, 2);
        index += 2;
        var buf = Encoding.ASCII.GetBytes(Data);
        Array.Copy(buf, 0, destination, index, 32);
        index += 32;
    }

    public override void Parse(byte[] source, ref int index)
    {
        base.Parse(source, ref index);

        Identifier = BitConverter.ToUInt16(source, index);
        index += 2;
        SequenceNumber = BitConverter.ToUInt16(source, index);
        index += 2;
        Data = Encoding.ASCII.GetString(source, index, 32);
        index += 32;
    }
}