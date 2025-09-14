// namespace PingUtility;

// public class RawIPHeader
// {
//     public byte Version { get; set; }
//     public byte HeaderLength { get; set; }
//     public byte ToS { get; set; }
//     public byte Ttl { get; set; }
//     public byte Protocol { get; set; }
//     public byte Flags { get; set; }
//     public ushort Length { get; set; }
//     public ushort Identification { get; set; }
//     public ushort FragmentOffset { get; set; }
//     public ushort Checksum { get; set; }
//     public string Source { get; set; }
//     public string Destination { get; set; }

//     public virtual void Parse(byte[] source, ref int index)
//     {
//         Version = (byte)((source[index] & 0xF0) >> 4);

//         HeaderLength = (byte)((source[index] & 0x0F) * 4);
//         index++;

//         ToS = source[index];
//         index++;

//         Length = BitConverter.ToUInt16(source, index);
//         index += 2;

//         Identification = BitConverter.ToUInt16(source, index);
//         index += 2;

//         Flags = (byte)((source[index] & 0xE000) >> 13);
//         FragmentOffset = (byte)(BitConverter.ToUInt16(source, index) & 0x1FFF);
//         index += 2;

//         Ttl = source[index];
//         index++;

//         Protocol = source[index];
//         index++;

//         Checksum = BitConverter.ToUInt16(source, index);
//         index += 2;

//         Source = String.Format("{0}.{1}.{2}.{3}", source[index], source[index + 1], source[index + 2], source[index + 3]);
//         index += 4;

//         Destination = String.Format("{0}.{1}.{2}.{3}", source[index], source[index + 1], source[index + 2], source[index + 3]);
//         index += 4;

//         index = HeaderLength;
//     }
// }

using System.Net;
using System.Runtime.InteropServices;

namespace PingUtility;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class RawIPHeader
{
    public byte VersionAndHeaderLength { get; set; }
    public byte ToS { get; set; }
    public ushort Length { get; set; }
    public ushort Identification { get; set; }
    public byte FlagsAndFragmentOffset { get; set; }
    public byte Ttl { get; set; }
    public byte Protocol { get; set; }
    public ushort Checksum { get; set; }
    public uint Source { get; set; }
    public uint Destination { get; set; }

    public RawIPHeader()
    {

    }

    public RawIPHeader(uint src, uint dst, ushort packetLength, byte protocol = 1, byte ttl = 128)
    {
        VersionAndHeaderLength = 0x45;
        ToS = 0;
        Length = (ushort)IPAddress.HostToNetworkOrder((short)packetLength);
        Identification = (ushort)new Random().Next(0, 65535);
        FlagsAndFragmentOffset = 0;
        Ttl = ttl;
        Protocol = protocol;
        Checksum = 0;
        Source = src;
        Destination = dst;

        Checksum = CalculateIpChecksum();
    }

    public virtual void Parse(byte[] source, ref int index)
    {
        VersionAndHeaderLength = source[index];
        index++;

        ToS = source[index];
        index++;

        Length = BitConverter.ToUInt16(source, index);
        index += 2;

        Identification = BitConverter.ToUInt16(source, index);
        index += 2;

        FlagsAndFragmentOffset = source[index];
        index += 2;

        Ttl = source[index];
        index++;

        Protocol = source[index];
        index++;

        Checksum = BitConverter.ToUInt16(source, index);
        index += 2;

        Source = BitConverter.ToUInt32(source, index);
        //Source = String.Format("{0}.{1}.{2}.{3}", source[index], source[index + 1], source[index + 2], source[index + 3]);
        index += 4;

        Destination = BitConverter.ToUInt32(source, index);
        //Destination = String.Format("{0}.{1}.{2}.{3}", source[index], source[index + 1], source[index + 2], source[index + 3]);
        index += 4;

        index = (byte)(VersionAndHeaderLength & 0x0F) * 4;
    }

    private ushort CalculateIpChecksum()
    {
        byte[] buffer = new byte[20];
        IntPtr ptr = Marshal.AllocHGlobal(20);

        try
        {
            Marshal.StructureToPtr(this, ptr, false);
            Marshal.Copy(ptr, buffer, 0, 20);

            uint sum = 0;
            for (int i = 0; i < 20; i += 2)
            {
                sum += (uint)((buffer[i] << 8) + buffer[i + 1]);
            }

            while (sum >> 16 != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            return (ushort)~sum;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}