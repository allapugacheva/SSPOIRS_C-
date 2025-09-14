namespace PingUtility;

public class IcmpResponse
{
    public RawIPHeader IpHeader { get; }
    public IcmpPacket IcmpPacket { get; private set; }

    public IcmpResponse() : base()
    {
        IpHeader = new RawIPHeader();
        IcmpPacket = null;
    }

    // public IcmpResponse(string fakeSource) : base()
    // {
    //     IpHeader = new RawIPHeader();
    // }

    protected IcmpPacket CreatePacket(byte[] source, int index)
    {
        var packet = new IcmpPacket();

        packet.Parse(source, ref index);
        return CreatePacketByType(packet.PacketType);
    }

    protected virtual IcmpPacket CreatePacketByType(ushort packetType)
    {
        switch (packetType)
        {
            case 8: return new IcmpEchoPacket();
            case 0: return new IcmpEchoPacket();
            case 3: return new IcmpDestinationUnreachablePacket();
            case 11: return new IcmpTimeExceededPacket();
            default: return new IcmpPacket();
        }
    }

    public virtual void Parse(byte[] source, ref int index)
    {
        IpHeader.Parse(source, ref index);

        IcmpPacket = CreatePacket(source, index);
        try
        {
            IcmpPacket.Parse(source, ref index);
        }
        catch (Exception)
        {
            IcmpPacket = null;
            throw;
        }
    }
}