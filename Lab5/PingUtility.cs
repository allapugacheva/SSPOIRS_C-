using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace PingUtility;

public class PingUtility
{
    private int _sequenceNumber = 0;
    private Socket _socket;
    private int _timeout;
    private object _obj;

    public PingUtility(int timeout)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);
        _socket.ReceiveBufferSize = 1024;
        _socket.EnableBroadcast = true;

        _timeout = timeout;

        _obj = new object();
    }

    public List<TracerouteResult> TraceRoute(string host, int maxHops = 30)
    {
        var results = new List<TracerouteResult>();

        try
        {
            IPAddress targetAddress;
            try
            {
                targetAddress = Dns.GetHostAddresses(host)[0];
            }
            catch
            {
                targetAddress = IPAddress.Parse(host);
            }

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                var result = new TracerouteResult { Hop = ttl };

                try
                {
                    var pingResult = Ping(host, ttl);

                    result.Status = pingResult.Status;
                    result.RoundTripTime = (long)pingResult.RoundTripTime;

                    if (pingResult.Status == IPStatus.Success || pingResult.Status == IPStatus.TtlExpired)
                    {
                        result.Address = pingResult.Address;

                        try
                        {
                            var hostEntry = Dns.GetHostEntry(pingResult.Address);
                            result.Hostname = hostEntry.HostName;
                        }
                        catch
                        {
                            result.Hostname = pingResult.Address.ToString();
                        }
                    }
                    else
                    {
                        result.Hostname = "*";
                    }
                }
                catch (Exception)
                {
                    result.Status = IPStatus.Unknown;
                    result.Hostname = "*";
                }
                results.Add(result);

                if (ShouldStopTracing(results, targetAddress))
                {
                    break;
                }

                Task.Delay(100);
            }
        }
        catch
        {
            results.Add(new TracerouteResult { Hop = 0, Hostname = host, Status = IPStatus.Unknown });
        }

        return results;
    }

    private bool ShouldStopTracing(List<TracerouteResult> results, IPAddress targetAddress)
    {
        var lastResult = results[^1];

        if (lastResult.Status == IPStatus.Success &&
            lastResult.Address != null &&
            lastResult.Address.Equals(targetAddress))
        {
            return true;
        }

        if (results.Count >= 3)
        {
            var lastThree = results.GetRange(results.Count - 3, 3);
            if (lastThree.All(r => r.Status == IPStatus.TimedOut))
            {
                return true;
            }
        }

        if (results.Count >= 30)
        {
            return true;
        }

        return false;
    }

    public PingResult Ping(string host, int ttl = 128, string? spoofSource = null)
    {
        ushort currentSequenceNumber = (ushort)_sequenceNumber;
        Interlocked.Increment(ref _sequenceNumber);

        ushort currentIdentifier = (ushort)Process.GetCurrentProcess().Id;
        var result = new PingResult { Host = host };

        try
        {
            IPAddress ipAddress;
            try
            {
                ipAddress = Dns.GetHostAddresses(host)[0];
            }
            catch
            {
                ipAddress = IPAddress.Parse(host);
            }
            result.Address = ipAddress;

            IPAddress sourceIp;
            if (spoofSource != null)
            {
                try
                {
                    sourceIp = Dns.GetHostAddresses(spoofSource)[0];
                }
                catch
                {
                    sourceIp = IPAddress.Parse(spoofSource);
                }
            }
            else
                sourceIp = IPAddress.Any;

            var buf = new byte[1024];
            int ind = 0;
            IcmpPacket request = new IcmpPacket();
            request.Identifier = currentIdentifier;
            request.SequenceNumber = currentSequenceNumber;
            request.Data = "abcdefghijklmnopqrstuvwabcdefghi";
            request.Build(buf, ref ind);
            if (request.Checksum == 0)
            {
                ushort checksum = CalculateChecksum(buf, 0, ind);
                Buffer.BlockCopy(BitConverter.GetBytes(checksum), 0, buf, 2, 2);
            }

            byte[] fullPacket = CreateFullIpPacket(buf, ind, sourceIp, ipAddress, ttl);

            var sendTimestamp = Stopwatch.GetTimestamp();
            lock (_obj)
            {
                _socket.SendTo(fullPacket, SocketFlags.None, new IPEndPoint(ipAddress, 0));
            }
            byte[] receiveBuffer = new byte[1024];
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            result.Status = IPStatus.TimedOut;

            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < _timeout)
            {
                try
                {
                    lock (_obj)
                    {
                        var received = _socket.ReceiveFrom(receiveBuffer, SocketFlags.Peek, ref remoteEP);

                        int idPos = (receiveBuffer[20] != 0 && receiveBuffer[20] != 8) ? 52 : 24;
                        int idSeq = (receiveBuffer[20] != 0 && receiveBuffer[20] != 8) ? 54 : 26;

                        ushort replyId = BitConverter.ToUInt16(receiveBuffer, idPos);
                        ushort replySeq = BitConverter.ToUInt16(receiveBuffer, idSeq);

                        if (replyId == currentIdentifier && replySeq == currentSequenceNumber)
                        {
                            _socket.ReceiveFrom(receiveBuffer, SocketFlags.None, ref remoteEP);
                            var receiveTimestamp = Stopwatch.GetTimestamp();
                            result.RoundTripTime = (receiveTimestamp - sendTimestamp) * 1000.0 / Stopwatch.Frequency;

                            IPAddress responderAddress = ((IPEndPoint)remoteEP).Address;
                            result.Address = responderAddress;

                            if (receiveBuffer[20] == 0 || receiveBuffer[20] == 8)
                            {
                                result.Status = IPStatus.Success;
                                break;
                            }
                            else if (receiveBuffer[20] == 11)
                            {
                                result.Status = IPStatus.TtlExpired;
                                break;
                            }
                            else if (receiveBuffer[20] == 3)
                            {
                                result.Status = IPStatus.DestinationUnreachable;
                                break;
                            }
                        }

                        continue;
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    continue;
                }
            }
        }
        catch
        {
            result.Status = IPStatus.Unknown;
        }

        ClearSocketBuffer();
        return result;
    }

    private byte[] CreateFullIpPacket(byte[] icmpData, int icmpLength, IPAddress sourceIp, IPAddress destIp, int ttl = 128)
    {
        uint source = sourceIp != null ?
            BitConverter.ToUInt32(sourceIp.GetAddressBytes(), 0) :
            BitConverter.ToUInt32(IPAddress.Any.GetAddressBytes(), 0);

        uint dest = BitConverter.ToUInt32(destIp.GetAddressBytes(), 0);

        RawIPHeader ipHeader = new RawIPHeader(
            source,
            dest,
            (ushort)(20 + icmpLength),
            1,
            (byte)ttl
        );

        byte[] fullPacket = new byte[20 + icmpLength];

        using (var ms = new MemoryStream(fullPacket))
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(ipHeader.VersionAndHeaderLength);
            writer.Write(ipHeader.ToS);
            writer.Write(IPAddress.HostToNetworkOrder((short)ipHeader.Length));
            writer.Write(IPAddress.HostToNetworkOrder((short)ipHeader.Identification));
            writer.Write(IPAddress.HostToNetworkOrder((short)ipHeader.FlagsAndFragmentOffset));
            writer.Write(ipHeader.Ttl);
            writer.Write(ipHeader.Protocol);
            writer.Write(IPAddress.HostToNetworkOrder((short)ipHeader.Checksum));
            writer.Write(ipHeader.Source);
            writer.Write(ipHeader.Destination);

            writer.Write(icmpData, 0, icmpLength);
        }

        return fullPacket;
    }

    private void ClearSocketBuffer()
    {
        try
        {
            byte[] tempBuffer = new byte[1024];
            EndPoint tempEp = new IPEndPoint(IPAddress.Any, 0);

            while (_socket.Available > 0)
            {
                _socket.ReceiveFrom(tempBuffer, SocketFlags.None, ref tempEp);
            }
        }
        catch { }
    }

    private ushort CalculateChecksum(byte[] data, int index, int size)
    {
        uint checksum = 0;
        int i = index;

        if (size > data.Length - index)
        {
            size = data.Length - index;
        }

        while (size > 1)
        {
            checksum += BitConverter.ToUInt16(data, i);
            i += 2;
            size -= 2;
        }

        if (size > 0)
        {
            checksum += data[i];
        }

        checksum = (checksum >> 16) + (checksum & 0xffff);
        checksum += (checksum >> 16);
        return (ushort)(~checksum);
    }
}