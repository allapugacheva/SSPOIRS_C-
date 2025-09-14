namespace PingUtility;

public class IcmpTimeExceededPacket : IcmpPacket
{
    public byte Type { get; set; } = 11;
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

        // IP header + 8 bytes of original datagram
        OriginalDatagram = new byte[28];
        Array.Copy(buffer, offset, OriginalDatagram, 0, Math.Min(28, buffer.Length - offset));
        offset += OriginalDatagram.Length;
    }
}