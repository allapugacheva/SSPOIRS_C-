using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client;

public class UdpClient : Client
{
    private const string ConnectSignal = "CONNECT";

    private const string AcceptSignal = "ACCEPT";

    private readonly Stopwatch _timer = new Stopwatch();


    public UdpClient(IPAddress serverIp)
        : base(serverIp, SocketType.Dgram, ProtocolType.Udp)
    {
    }


    private bool KeepAlive()
    {
        if (Socket.Poll(0, SelectMode.SelectRead))
        {
            var bytes = new byte[Encoding.Unicode.GetByteCount("PING")];
            Socket.ReceiveFrom(bytes, ref _serverAddress);

            Socket.SendTo(Encoding.Unicode.GetBytes("PING"), _serverAddress);
        }

        if (_timer.Elapsed.TotalSeconds < 20.0)
            return true;

        while (!Socket.Poll(100, SelectMode.SelectWrite)) ;
        Socket.SendTo(BitConverter.GetBytes(Encoding.Unicode.GetByteCount("PING"))
            .Concat(Encoding.Unicode.GetBytes("PING")).ToArray(), _serverAddress);

        for (var i = 0; i < ClientConfig.KeepAliveRetryCount; i++)
        {
            if (Socket.Poll(ClientConfig.KeepAliveTimeout * 1000, SelectMode.SelectRead))
            {
                var bytes = new byte[Encoding.Unicode.GetByteCount("PING")];
                Socket.ReceiveFrom(bytes, ref _serverAddress);
                _timer.Restart();
                return true;
            }

            Thread.Sleep(ClientConfig.KeepAliveInterval);
        }

        return false;
    }


    public bool TryConnect()
    {
        for (var a = 0; a < 4 && !IsConnected; a++)
        {
            Socket.ReceiveBufferSize = ClientConfig.ReceiveBufferSize;
            Socket.SendBufferSize = ClientConfig.SendBufferSize;
            Socket.Blocking = false;

            Socket.SendTo(Encoding.Unicode.GetBytes(ConnectSignal), _serverAddress);

            var buffer = new byte[Encoding.Unicode.GetByteCount(AcceptSignal)];
            if (Socket.Poll(2_000_000, SelectMode.SelectRead)
                && Socket.ReceiveFrom(buffer, ref _serverAddress) == buffer.Length
                && Encoding.Unicode.GetString(buffer).Equals(AcceptSignal))
                IsConnected = true;
        }

        _timer.Start();
        return IsConnected;
    }

    public ClientStatusEnum Run()
    {
        if (!TryConnect())
            Console.WriteLine($"{Colors.RED}Failed to connect to server.{Colors.RESET}");
        else
            Console.Write($"{Colors.GREEN}Connected to server.{Colors.RESET}\n> ");

        var commandString = new StringBuilder();
        while (IsConnected)
        {
            if (!KeepAlive())
            {
                IsConnected = false;
                Console.WriteLine($"{Colors.RED}Disconnect from server.{Colors.RESET}");
            }

            if (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    var commandParts = commandString.ToString().Split(' ');
                    var commandName = commandParts[0];
                    var commandValues = commandParts.Length > 1 ? commandParts.Skip(1).ToArray() : [];

                    if (commandName.StartsWith("SETTING"))
                    {
                        if (commandName.Contains(".path") && Settings.SetDir(commandValues.Last()))
                            Console.WriteLine($"{Colors.BLUE}{Settings.CurrentDirectory}{Colors.RESET}");
                        else
                            Console.WriteLine(Settings);
                    }
                    else if (commandName.StartsWith("CLOSE"))
                    {
                        Socket.Close();
                        break;
                    }
                    else if (ServerHandler(commandName, commandValues) == ClientStatusEnum.LostConnection)
                        IsConnected = false;

                    commandString.Clear();
                    Console.Write("> ");
                }
                else if (keyInfo.Key == ConsoleKey.Backspace
                         && commandString.Length > 0)
                {
                    commandString.Remove(commandString.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(keyInfo.KeyChar) && !char.IsSurrogate(keyInfo.KeyChar))
                {
                    commandString.Append(keyInfo.KeyChar);
                    Console.Write(keyInfo.KeyChar);
                }
            }
        }

        return ClientStatusEnum.Success;
    }

    private ClientStatusEnum ServerHandler(string command, string[] parameters)
    {
        var res = ClientStatusEnum.Success;
        if (command.StartsWith("TIME"))
        {
            while (!Socket.Poll(1000, SelectMode.SelectWrite)) ;
            Socket.SendTo(BitConverter.GetBytes(Encoding.Unicode.GetByteCount("TIME"))
                .Concat(Encoding.Unicode.GetBytes("TIME")).ToArray(), _serverAddress);

            var timeBytes = new byte[Encoding.Unicode.GetByteCount(ClientConfig.DateFormat)];
            while (!Socket.Poll(1000, SelectMode.SelectRead)) ;
            Socket.ReceiveFrom(timeBytes, ref _serverAddress);

            Console.WriteLine($"{Colors.BLUE}Server time: {Encoding.Unicode.GetString(timeBytes)}{Colors.RESET}");
        }
        else if (command.StartsWith("ECHO"))
        {
            var filePath = "";
            if (parameters.Length > 0)
                filePath = parameters[0];
            
            while (!Socket.Poll(1000, SelectMode.SelectWrite)) ;
            Socket.SendTo(BitConverter.GetBytes(Encoding.Unicode.GetByteCount("ECHO " + filePath))
                .Concat(Encoding.Unicode.GetBytes("ECHO " + filePath)).ToArray(), _serverAddress);

            var timeBytes = new byte[Encoding.Unicode.GetByteCount(filePath)];
            while (!Socket.Poll(1000, SelectMode.SelectRead)) ;
            Socket.ReceiveFrom(timeBytes, ref _serverAddress);

            Console.WriteLine($"{Encoding.Unicode.GetString(timeBytes)}");
        }
        else
        {
            var filePath = ".";
            if (parameters.Length > 0)
                filePath = Path.Combine(Settings.CurrentDirectory, parameters[0]);

            var commandBytes = Encoding.Unicode.GetBytes($"{command} {Path.GetFileName(filePath)}");
            var bytes = BitConverter.GetBytes(commandBytes.Length).Concat(commandBytes).ToArray();
            if (command.StartsWith("UPLOAD") && File.Exists(filePath))
            {
                Socket.SendTo(bytes, _serverAddress);
                res = SendFile(filePath);
            }
            else if (command.StartsWith("DOWNLOAD"))
            {
                Socket.SendTo(bytes, _serverAddress);
                res = ReceiveFile(filePath);
            }
        }

        return res;
    }

    private ClientStatusEnum SendFile(string filePath)
    {
        if (!File.Exists(filePath))
            return ClientStatusEnum.Fail;

        using var stream = new FileStream(filePath, FileMode.Open);
        var fileSize = new FileInfo(filePath).Length;
        Console.WriteLine($"File size: {fileSize} Bytes");

        Socket.SendTo(BitConverter.GetBytes(fileSize), _serverAddress);

        var buffer = new byte[ClientConfig.ServingSize + sizeof(long)];

        long startPos;
        while (!Socket.Poll(1000, SelectMode.SelectRead)) ;
        Socket.ReceiveFrom(buffer, sizeof(long), SocketFlags.None, ref _serverAddress);

        startPos = BitConverter.ToInt64(buffer, 0);
        if (startPos != 0L)
            Console.WriteLine($"Transfer was {Colors.BLUE}recovered{Colors.RESET} from pos: {startPos}");

        stream.Seek(startPos, SeekOrigin.Begin);

        var lastConfirmedACK = startPos;
        var bytesSend = 0L;
        var clientACK = lastConfirmedACK;
        var bytePortion = 0;

        var attemps = 0;

        var fll = new FileLoadingLine(fileSize);
        var timer = new Stopwatch();
        timer.Start();
        while (lastConfirmedACK != fileSize)
        {
            if (clientACK < lastConfirmedACK + ClientConfig.WindowSize * ClientConfig.ServingSize
                && clientACK != -1L)
            {
                if (Socket.Poll(50, SelectMode.SelectWrite))
                {
                    bytePortion = stream.Read(buffer, sizeof(long), ClientConfig.ServingSize);
                    clientACK += bytePortion;
                    if (bytePortion == 0)
                        clientACK = -1L;

                    BitConverter.GetBytes(clientACK).CopyTo(buffer, 0);
                    Socket.SendTo(buffer, bytePortion + sizeof(long), SocketFlags.None, _serverAddress);
                }
            }
            else if (Socket.Poll(2_000_000, SelectMode.SelectRead))
            {
                Socket.ReceiveFrom(buffer, sizeof(long), SocketFlags.None, ref _serverAddress);
                clientACK = lastConfirmedACK = BitConverter.ToInt32(buffer, 0);
                stream.Seek(clientACK, SeekOrigin.Begin);
                
                fll.Report(lastConfirmedACK, timer.Elapsed.TotalSeconds);
                attemps = 0;
            }
            else
            {
                if (attemps == 5 && !Socket.Poll(500_000, SelectMode.SelectRead))
                {
                    IsConnected = false;
                    break;
                }

                attemps++;
                clientACK = lastConfirmedACK;
                stream.Seek(clientACK, SeekOrigin.Begin);
            }
        }

        timer.Stop();

        Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                      $"{Colors.GREEN}{lastConfirmedACK}{Colors.RESET}/{fileSize} Bytes; " +
                      $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(lastConfirmedACK,
                          timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                      $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

        return IsConnected
            ? ClientStatusEnum.Success
            : ClientStatusEnum.LostConnection;
    }


    private ClientStatusEnum ReceiveFile(string filePath)
    {
        FileStream? stream;

        var fileInfoBytes = new byte[2 * sizeof(long)];
        while (!Socket.Poll(1000, SelectMode.SelectRead)) ;
        Socket.ReceiveFrom(fileInfoBytes, 2 * sizeof(long), SocketFlags.None, ref _serverAddress);

        var fileSize = BitConverter.ToInt64(fileInfoBytes, 0);
        Console.WriteLine($"File size: {fileSize} Bytes");

        var startPos = BitConverter.ToInt64(fileInfoBytes, sizeof(long));

        Console.WriteLine(startPos + " " + fileSize);
        
        if (startPos == -1L && fileSize != -1L)
        {
            Console.WriteLine($"File don't exists: {Colors.RED}{filePath} Bytes{Colors.RESET}");
            return ClientStatusEnum.Fail;
        }

        if (startPos != 0L)
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Write);
            Console.WriteLine($"Transfer was {Colors.BLUE}recovered{Colors.RESET} from pos: {startPos}");
        }
        else
        {
            stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        }

        stream.Seek(startPos, SeekOrigin.Begin);

        var buffer = new byte[ClientConfig.ServingSize + sizeof(long)];
        var bytePortion = 0;
        var confirmedAck = new List<long>();

        var fll = new FileLoadingLine(fileSize);
        var timer = new Stopwatch();
        timer.Start();
        var clientACK = 0L;
        var lastConfirmedACK = startPos;

        var attemps = 0;
        while (lastConfirmedACK != fileSize)
        {
            if (Socket.Poll(100_000, SelectMode.SelectRead))
            {
                bytePortion = Socket.ReceiveFrom(buffer, buffer.Length, SocketFlags.None, ref _serverAddress) -
                              sizeof(long);

                clientACK = BitConverter.ToInt64(buffer, 0);
                if (clientACK == -1L)
                {
                    if (confirmedAck.Count == 0)
                        break;
                        
                    continue;
                }

                confirmedAck.Add(clientACK);

                stream.Seek(clientACK - bytePortion, SeekOrigin.Begin);
                stream.Write(buffer, sizeof(long), bytePortion);
                attemps = 0;
            }
            else if (confirmedAck.Count != 0)
            {
                confirmedAck = confirmedAck.Distinct().OrderBy(ack => ack).ToList();

                for (var i = 0; i < confirmedAck.Count - 1; i++)
                {
                    if (confirmedAck[i + 1] - confirmedAck[i] <= ClientConfig.ServingSize)
                        lastConfirmedACK = confirmedAck[i + 1];
                    else break;
                }

                Socket.SendTo(BitConverter.GetBytes(lastConfirmedACK), _serverAddress);
                
                fll.Report(lastConfirmedACK, timer.Elapsed.TotalSeconds);
                confirmedAck.Clear();
            }
            else if (++attemps == 20)
            {
                IsConnected = true;
                break;
            }
        }

        timer.Stop();
        stream.Close();

        Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                      $"{Colors.GREEN}{lastConfirmedACK}{Colors.RESET}/{fileSize} Bytes; " +
                      $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(lastConfirmedACK,
                          timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                      $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

        return IsConnected
            ? ClientStatusEnum.Success
            : ClientStatusEnum.LostConnection;
    }
}