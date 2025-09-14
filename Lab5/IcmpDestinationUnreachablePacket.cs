namespace PingUtility;

public class IcmpDestinationUnreachablePacket : IcmpPacket
{
    public byte Type { get; set; } = 3;
    public byte Code { get; set; }
    public ushort Checksum { get; set; }
    public byte Unused { get; set; }
    public byte[] OriginalDatagram { get; set; }

    public void Parse(byte[] buffer, ref int offset)
    {
        Type = buffer[offset++];
        Code = buffer[offset++];
        Checksum = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        offset += 2;
        Unused = buffer[offset++];

        OriginalDatagram = new byte[28];
        Array.Copy(buffer, offset, OriginalDatagram, 0, Math.Min(28, buffer.Length - offset));
        offset += OriginalDatagram.Length;
    }
}