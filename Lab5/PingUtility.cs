using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace PingUtility;

public class PingUtility
{
    private static int _sequenceNumber = 0;

    public static async Task<List<TracerouteResult>> TraceRoute(string host, int maxHops = 30, int timeout = 1000)
    {
        var results = new List<TracerouteResult>();

        IPAddress targetAddress;
        try
        {
            targetAddress = (await Dns.GetHostAddressesAsync(host))[0];
        }
        catch
        {
            throw new ArgumentException($"Cannot resolve host: {host}");
        }

        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            var result = new TracerouteResult { Hop = ttl };

            try
            {
                var pingResult = await Ping(host, timeout, ttl);

                result.Status = pingResult.Status;
                result.RoundTripTime = (long)pingResult.RoundTripTime;

                if (pingResult.Status == IPStatus.Success || pingResult.Status == IPStatus.TtlExpired)
                {
                    result.Address = pingResult.Address;

                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(pingResult.Address);
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
            catch (Exception ex)
            {
                result.Status = IPStatus.Unknown;
                result.ErrorMessage = ex.Message;
                result.Hostname = "*";
            }

            results.Add(result);

            if (ShouldStopTracing(results, targetAddress))
            {
                break;
            }

            await Task.Delay(100);
        }

        return results;
    }

    private static bool ShouldStopTracing(List<TracerouteResult> results, IPAddress targetAddress)
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

    public static async Task<PingResult> Ping(string host, int timeout = 1000, int ttl = 0, IPAddress? spoofSource = null)
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
                ipAddress = (await Dns.GetHostAddressesAsync(host))[0];
            }
            catch
            {
                ipAddress = IPAddress.Parse(host);
            }
            result.Address = ipAddress;
            // Используем сырые сокеты с доступом к IP заголовку
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
            {
                // Разрешаем ручное управление IP заголовком
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);
                if (ttl != 0)
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
                socket.ReceiveBufferSize = 1024;
                socket.EnableBroadcast = true;

                // if (spoofSource != null)
                // {
                //     // Привязываем сокет к поддельному адресу
                //     socket.Bind(new IPEndPoint(spoofSource, 0));
                // }

                // Подменяем исходный адрес если указан
                var sourceIp = spoofSource != null ? spoofSource : GetLocalIpAddress();

                // Создаем ICMP пакет
                var buf = new byte[1024];
                int ind = 0;

                IcmpEchoPacket request = new IcmpEchoPacket();
                request.Identifier = currentIdentifier;
                request.SequenceNumber = currentSequenceNumber;
                request.Data = "abcdefghijklmnopqrstuvwabcdefghi";
                request.Build(buf, ref ind);
                if (request.Checksum == 0)
                {
                    ushort checksum = CalculateChecksum(buf, 0, ind);
                    Buffer.BlockCopy(BitConverter.GetBytes(checksum), 0, buf, 2, 2);
                }

                // Комбинируем IP header + ICMP data
                byte[] fullPacket = CreateFullIpPacket(buf, ind, sourceIp, ipAddress, ttl);

                var sendTimestamp = Stopwatch.GetTimestamp();
                // Отправляем пакет с поддельным source IP
                await socket.SendToAsync(fullPacket, SocketFlags.None, new IPEndPoint(ipAddress, 0));
                // Прием ответа (обычным способом)
                byte[] receiveBuffer = new byte[1024];

                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                var startTime = DateTime.UtcNow;
                while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeout)
                {
                    try
                    {
                        var received = socket.ReceiveFrom(receiveBuffer, SocketFlags.None, ref remoteEP);
                        Console.WriteLine("1");

                        var receiveTimestamp = Stopwatch.GetTimestamp();
                        result.RoundTripTime = (receiveTimestamp - sendTimestamp) * 1000.0 / Stopwatch.Frequency;

                        // Пропускаем IP header (20 bytes) и парсим ICMP
                        //int parseIndex = 20;
                        int parseIndex = 0;
                        IcmpResponse response = new IcmpResponse();
                        response.Parse(receiveBuffer, ref parseIndex);

                        IPAddress responderAddress = ((IPEndPoint)remoteEP).Address;
                        result.Address = responderAddress;

                        if (response.IcmpPacket is IcmpEchoPacket echoResponse)
                        {
                            if (echoResponse.Identifier == currentIdentifier &&
                                echoResponse.SequenceNumber == currentSequenceNumber)
                            {
                                result.Status = IPStatus.Success;
                                return result;
                            }
                        }
                        else if (response.IcmpPacket is IcmpTimeExceededPacket timeExceeded)
                        {
                            result.Status = IPStatus.TtlExpired;
                            return result;
                        }
                        else if (response.IcmpPacket is IcmpDestinationUnreachablePacket unreachable)
                        {
                            result.Status = IPStatus.DestinationUnreachable;
                            return result;
                        }

                        continue;
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

                result.Status = IPStatus.TimedOut;
            }
        }
        catch (SocketException ex)
        {
            result.Status = IPStatus.Unknown;
            result.ErrorMessage = ex.Message;
            Console.WriteLine(ex.Message);
        }
        catch (Exception ex)
        {
            result.Status = IPStatus.Unknown;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static IPAddress GetLocalIpAddress()
    {
        var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
        return hostEntry.AddressList.FirstOrDefault(ip =>
            ip.AddressFamily == AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(ip));
    }

    private static byte[] CreateFullIpPacket(byte[] icmpData, int icmpLength, IPAddress sourceIp, IPAddress destIp, int ttl)
    {
        uint source = sourceIp != null ?
            BitConverter.ToUInt32(sourceIp.GetAddressBytes(), 0) :
            BitConverter.ToUInt32(IPAddress.Any.GetAddressBytes(), 0);

        uint dest = BitConverter.ToUInt32(destIp.GetAddressBytes(), 0);

        // Создаем IP заголовок
        RawIPHeader ipHeader = new RawIPHeader(
            source, dest, (ushort)(20 + icmpLength), 1, (byte)(ttl == 0 ? 128 : ttl)
        );

        // Комбинируем IP header + ICMP data
        byte[] fullPacket = new byte[20 + icmpLength];

        using (var ms = new MemoryStream(fullPacket))
        using (var writer = new BinaryWriter(ms))
        {
            // Записываем IP заголовок
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

            // Записываем ICMP данные
            writer.Write(icmpData, 0, icmpLength);
        }

        return fullPacket;
    }

    // public static async Task<PingResult> Ping(string host, int timeout = 1000, int ttl = 0)
    // {
    //     ushort currentSequenceNumber = (ushort)_sequenceNumber;
    //     Interlocked.Increment(ref _sequenceNumber);

    //     ushort currentIdentifier = (ushort)Process.GetCurrentProcess().Id;

    //     var result = new PingResult { Host = host };

    //     try
    //     {
    //         IPAddress ipAddress;
    //         try
    //         {
    //             ipAddress = (await Dns.GetHostAddressesAsync(host))[0];
    //         }
    //         catch
    //         {
    //             throw new ArgumentException($"Cannot resolve host: {host}");
    //         }
    //         result.Address = ipAddress;

    //         using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
    //         {
    //             socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);
    //             if (ttl != 0)
    //                 socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
    //             socket.ReceiveBufferSize = 1024;

    //             var buf = new byte[1024];
    //             int ind = 0;

    //             IcmpEchoPacket request = new IcmpEchoPacket();
    //             request.Identifier = currentIdentifier;
    //             request.SequenceNumber = currentSequenceNumber;
    //             request.Data = "abcdefghijklmnopqrstuvwabcdefghi";
    //             request.Build(buf, ref ind);

    //             if (request.Checksum == 0)
    //             {
    //                 ushort checksum = CalculateChecksum(buf, 0, ind);
    //                 Buffer.BlockCopy(BitConverter.GetBytes(checksum), 0, buf, 2, 2);
    //             }

    //             var sendTimestamp = Stopwatch.GetTimestamp();
    //             await socket.SendToAsync(buf, SocketFlags.None, new IPEndPoint(ipAddress, 0));

    //             byte[] receiveBuffer = new byte[1024];
    //             EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

    //             var startTime = DateTime.UtcNow;
    //             while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeout)
    //             {
    //                 try
    //                 {
    //                     var received = socket.ReceiveFrom(receiveBuffer, SocketFlags.None, ref remoteEP);
    //                     var receiveTimestamp = Stopwatch.GetTimestamp();

    //                     int parseIndex = 0;
    //                     IcmpResponse response = new IcmpResponse();
    //                     response.Parse(receiveBuffer, ref parseIndex);

    //                     IPAddress responderAddress = ((IPEndPoint)remoteEP).Address;

    //                     if (response.IcmpPacket is IcmpEchoPacket echoResponse)
    //                     {
    //                         if (echoResponse.Identifier == currentIdentifier &&
    //                             echoResponse.SequenceNumber == currentSequenceNumber)
    //                         {
    //                             result.Status = IPStatus.Success;
    //                             result.RoundTripTime = (receiveTimestamp - sendTimestamp) * 1000.0 / Stopwatch.Frequency;
    //                             result.Address = responderAddress;
    //                             return result;
    //                         }
    //                     }
    //                     else if (response.IcmpPacket is IcmpTimeExceededPacket timeExceeded)
    //                     {
    //                         result.Status = IPStatus.TtlExpired;
    //                         result.RoundTripTime = (receiveTimestamp - sendTimestamp) * 1000.0 / Stopwatch.Frequency;
    //                         result.Address = responderAddress;
    //                         return result;
    //                     }
    //                     else if (response.IcmpPacket is IcmpDestinationUnreachablePacket unreachable)
    //                     {
    //                         result.Status = IPStatus.DestinationUnreachable;
    //                         result.RoundTripTime = (receiveTimestamp - sendTimestamp) * 1000.0 / Stopwatch.Frequency;
    //                         result.Address = responderAddress;
    //                         return result;
    //                     }

    //                     continue;
    //                 }
    //                 catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
    //                 {
    //                     break;
    //                 }
    //                 catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
    //                 {
    //                     continue;
    //                 }
    //             }

    //             result.Status = IPStatus.TimedOut;
    //         }
    //     }
    //     catch (SocketException ex)
    //     {
    //         result.Status = IPStatus.Unknown;
    //         result.ErrorMessage = ex.Message;
    //     }
    //     catch (Exception ex)
    //     {
    //         result.Status = IPStatus.Unknown;
    //         result.ErrorMessage = ex.Message;
    //     }

    //     return result;
    // }

    private static ushort CalculateChecksum(byte[] data, int index, int size)
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