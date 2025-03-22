using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server;

public class UdpServer : Server
{
    private const string ConnectSignal = "CONNECT";
        
    private const string AcceptSignal = "ACCEPT";

    public bool IsDisconected { get; private set; } = false;
        
        
    private readonly Stopwatch _timer = new Stopwatch();
    
    
    internal UdpServer() 
            : base(SocketType.Dgram, ProtocolType.Udp)
        { }

        private bool KeepAlive()
        {
            if (_timer.Elapsed.TotalSeconds < 20.0)
                return true;
            
            while (!Socket.Poll(100, SelectMode.SelectWrite)) ;
            Socket.SendTo(Encoding.Unicode.GetBytes("PING"), ClientIp);

            for (var i = 0; i < ServerConfig.KeepAliveRetryCount; i++)
            {
                if (Socket.Poll(ServerConfig.KeepAliveTimeout * 1000, SelectMode.SelectRead))
                {
                    var bytes = new byte[Encoding.Unicode.GetByteCount("PING")];
                    Socket.ReceiveFrom(bytes, ref _clientIp);
                    _timer.Restart();
                    return true;
                }
                Thread.Sleep(ServerConfig.KeepAliveInterval);
            }

            _timer.Stop(); _timer.Reset();
            return false;
        }
        
        public ServerStatusEnum Run()
        {
            Console.Write($"{Colors.GREEN}Server is start.{Colors.RESET}\n> ");
            
            var commandString = new StringBuilder(50);
            while (true)
            {
                if (IsConnected)
                {
                    IsDisconected = !KeepAlive();
                    IsConnected = !IsDisconected;
                }

                if (IsDisconected)
                {
                    Console.Write($"Client is disconnecting: {Colors.RED}{_clientIp}{Colors.RESET}\n> ");
                    IsDisconected = false;
                }
                
                if (Socket.Poll(0, SelectMode.SelectRead))
                {
                    if (!IsConnected)
                    {
                        var buffer = new byte[Encoding.Unicode.GetByteCount(ConnectSignal)];
                        _clientIp = new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort);
                        Socket.ReceiveFrom(buffer, ref _clientIp);

                        if (Encoding.Unicode.GetString(buffer).Equals(ConnectSignal))
                        {
                            Socket.SendTo(Encoding.Unicode.GetBytes(AcceptSignal), _clientIp);
                            IsConnected = true; IsDisconected = false;
                            Console.Write($"{Colors.GREEN}Server is connected to {_clientIp}.{Colors.RESET}\n> ");
                        }
                        
                        Socket.ReceiveBufferSize = ServerConfig.ReceiveBufferSize;
                        Socket.SendBufferSize = ServerConfig.SendBufferSize;
                        _timer.Start();
                    }
                    else 
                    {
                        var clientRes = ClientHandler();
                        if (clientRes == ServerStatusEnum.LostConnection)
                            IsConnected = false;
                    }
                        
                }

                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        var commandParts = commandString.ToString().Split(' ');
                        var commandName = commandParts[0];
                        var commandValues = commandParts.Length > 1 ? commandParts.Skip(1).ToArray() : null;
                        if (commandName.StartsWith("SETTING"))
                        {
                            if (commandName.Contains(".path") && Settings.SetDir(commandValues?.Last()))
                                Console.WriteLine($"{Colors.BLUE}{Settings.CurrentDirectory}{Colors.RESET}");
                            else
                                Console.WriteLine(Settings);
                        }

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

           
            
            return ServerStatusEnum.Success;
        }

        private ServerStatusEnum ClientHandler()
        {
            var res = ServerStatusEnum.Fail;
            var commandBytes = new byte[ServerConfig.ServingSize];
            Socket.ReceiveFrom(commandBytes, ref _clientIp);

            var commandLength = BitConverter.ToInt32(commandBytes, 0);
            var clientCommand = Encoding.Unicode.GetString(commandBytes.AsSpan(sizeof(int), commandLength));

            if (clientCommand.StartsWith("PING"))
            {
                while (!Socket.Poll(1000, SelectMode.SelectWrite)) ;
                Socket.SendTo(Encoding.Unicode.GetBytes("PING"), _clientIp);
            }
            else if (clientCommand.StartsWith("TIME"))
            {
                while (!Socket.Poll(1000, SelectMode.SelectWrite)) ;
                Socket.SendTo(Encoding.Unicode.GetBytes(DateTime.UtcNow.ToString(ServerConfig.DateFormat)), _clientIp);
            }
            else if (clientCommand.StartsWith("ECHO"))
            {
                var parameters = clientCommand.Split(' ').Skip(1).ToArray();
                var str = "";
                if (parameters.Length != 0)
                {
                    str = parameters[0];
                }
                
                while (!Socket.Poll(1000, SelectMode.SelectWrite)) ;
                Socket.SendTo(Encoding.Unicode.GetBytes(str), _clientIp);
            }
            else
            {
                var parameters = clientCommand.Split(' ').Skip(1).ToArray();
                if (parameters.Length == 0)
                    return ServerStatusEnum.Fail;
                
                var filePath = Path.Combine(Settings.CurrentDirectory, Path.GetFileName(parameters[0]));
                
                if (clientCommand.StartsWith("UPLOAD"))
                    res = ReceiveFile(filePath);
                else if (clientCommand.StartsWith("DOWNLOAD"))
                    res = SendFile(filePath);
                
                if (res == ServerStatusEnum.Success)
                    Console.Write($"{Colors.BLUE}The file was successfully transferred{Colors.RESET}\n> ");
                else if (res == ServerStatusEnum.LostConnection)
                    Console.Write($"{Colors.RED}The file operation was not completed completely.{Colors.RESET}\n> ");   
            }
            
            return res;
        }

        private ServerStatusEnum ReceiveFile(string filePath)
        {
            FileStream? stream;

            var fileSizeBytes = new byte[sizeof(long)];
            while (!Socket.Poll(50, SelectMode.SelectRead)) ;
            Socket.ReceiveFrom(fileSizeBytes, SocketFlags.None, ref _clientIp);
            
            var fileSize = BitConverter.ToInt64(fileSizeBytes);
            Console.WriteLine($"File size: {fileSize} Bytes");

            var startPos = 0L;
            if (Backup.LastReceiveData.CanRecovery(filePath, _clientIp.ToString().Split(':')[0]))
            {
                startPos = Backup.LastReceiveData.CorruptedPos;
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Write);
                Console.WriteLine($"Transfer was {Colors.BLUE}recovered{Colors.RESET} from pos: {startPos}");
            }
            else
            {
                stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            }
            
            Socket.SendTo(BitConverter.GetBytes(startPos), _clientIp);
            stream.Seek(startPos, SeekOrigin.Begin);
            
            var buffer = new byte[ServerConfig.ServingSize + sizeof(long)]; var bytePortion = 0;
            var confirmedAck = new List<long>();

            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch();
            timer.Start();
            var clientACK = 0L; var lastConfirmedACK = startPos;

            var attemps = 0;
            while (lastConfirmedACK != fileSize)
            {
                if (Socket.Poll(50_000, SelectMode.SelectRead))
                {
                    bytePortion = Socket.ReceiveFrom(buffer, buffer.Length, SocketFlags.None, ref _clientIp) - sizeof(long);
                    
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
                        if (confirmedAck[i + 1] - confirmedAck[i] <= ServerConfig.ServingSize)
                            lastConfirmedACK = confirmedAck[i + 1];
                        else break;
                    }

                    Socket.SendTo(BitConverter.GetBytes(lastConfirmedACK), _clientIp);
                    
                    fll.Report(lastConfirmedACK, timer.Elapsed.TotalSeconds);
                    confirmedAck.Clear();
                }
                else if(++attemps == 20)
                {
                    IsDisconected = true;
                    break;
                }
            }
            timer.Stop();   
            stream.Close();
            
            Backup.LastReceiveData = new LastOpData(filePath, _clientIp.ToString().Split(':')[0], 
                IsDisconected, lastConfirmedACK, timer.Elapsed.TotalSeconds);
            
            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                          $"{Colors.GREEN}{lastConfirmedACK}{Colors.RESET}/{fileSize} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(lastConfirmedACK,
                              timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            while (Socket.Available > 0)
            {
                Socket.ReceiveFrom(buffer, SocketFlags.None, ref _clientIp);
            }
            
            return !IsDisconected 
                ? ServerStatusEnum.Success 
                : ServerStatusEnum.LostConnection;
        }
        
    private ServerStatusEnum SendFile(string filePath)
    {
        long fileSize = 0; long startPos = 0;
        FileStream? stream = null;

        if (!File.Exists(filePath))
            fileSize = startPos = -1;
        else
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            if(Backup.LastSendData.CanRecovery(filePath, _clientIp.ToString().Split(':')[0]))
                startPos = Backup.LastSendData.CorruptedPos;

            Console.WriteLine("start pos " + startPos);
            
            fileSize = stream.Length;
        }
        
        Socket.SendTo(BitConverter.GetBytes(fileSize).Concat(BitConverter.GetBytes(startPos)).ToArray(), _clientIp);
        if (stream == null)
            return ServerStatusEnum.Fail;
        
        Console.WriteLine($"File size: {fileSize} Bytes");
        if (startPos != 0)
        {
            Console.WriteLine($"Transfer was {Colors.BLUE}recovered{Colors.RESET} from pos: {startPos}");
            stream.Seek(startPos, SeekOrigin.Begin);
        }

        var buffer = new byte[ServerConfig.ServingSize + sizeof(long)];
        var lastConfirmedACK = startPos;
        var clientACK = lastConfirmedACK;
        var bytePortion = 0;

        var attemps = 0;

        var fll = new FileLoadingLine(fileSize);
        var timer = new Stopwatch();
        timer.Start();
        while (lastConfirmedACK != fileSize)
        {
            if (clientACK < lastConfirmedACK + ServerConfig.WindowSize * ServerConfig.ServingSize
                && clientACK != -1L)
            {
                if (Socket.Poll(50, SelectMode.SelectWrite))
                {
                    bytePortion = stream.Read(buffer, sizeof(long), ServerConfig.ServingSize);
                    clientACK += bytePortion;
                    if (bytePortion == 0)
                        clientACK = -1L;

                    BitConverter.GetBytes(clientACK).CopyTo(buffer, 0);
                    Socket.SendTo(buffer, bytePortion + sizeof(long), SocketFlags.None, _clientIp);
                }
            }
            else if (Socket.Poll(2_000_000, SelectMode.SelectRead))
            {
                Socket.ReceiveFrom(buffer, sizeof(long), SocketFlags.None, ref _clientIp);
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

        Backup.LastSendData = new LastOpData(filePath, _clientIp.ToString().Split(':')[0], 
            IsDisconected, lastConfirmedACK, timer.Elapsed.TotalSeconds);
        
        timer.Stop();
        stream.Close();

        Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                      $"{Colors.GREEN}{lastConfirmedACK}{Colors.RESET}/{fileSize} Bytes; " +
                      $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(lastConfirmedACK,
                          timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                      $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

        return !IsDisconected
            ? ServerStatusEnum.Success
            : ServerStatusEnum.LostConnection;
    }
}