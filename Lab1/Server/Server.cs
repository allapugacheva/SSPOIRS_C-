using Client;
using Server.Commands;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server
{
    internal class TcpServer
    {
        private readonly Socket _socket;

        private Socket _clientSocket;

        private readonly Dictionary<string, ServerCommand> _commands;

        private readonly ServerSettings _settings;

        private readonly ServerBackup _backup;
        
        private IPAddress? _clientIp; 
        
        private bool _isConnected;


        internal TcpServer()
        {
            _isConnected = false;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            StartUp();

            _commands = new Dictionary<string, ServerCommand>()
            {
                { "ECHO", new EchoCommand() },
                { "TIME", new TimeCommand() }
            };

            _settings = new ServerSettings();
            _backup = new ServerBackup();
        }

        private ServerStatusEnum StartUp()
        {
            _socket.Bind(new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort));
            _socket.Listen(ServerConfig.AmountListeners);
            return ServerStatusEnum.Success;
        }

        internal ServerStatusEnum Run()
        {
            Console.Write($"{Colors.GREEN}Server is start.{Colors.RESET}\n> ");

            var commandString = new StringBuilder(50);
            while (true)
            {
                if (_socket.Poll(0, SelectMode.SelectRead))
                {
                    if ((_clientIp = ConnectClient()) != null)
                    {
                        _isConnected = true;
                        Console.Write(
                            $"The client is connected. Ip: {Colors.GREEN}{_clientIp}{Colors.RESET}\n> ");   
                    }
                }
                else if (_isConnected && _clientSocket.Poll(0, SelectMode.SelectRead))
                {
                    if (_clientSocket.Available != 0)
                    {
                        ListenClient();   
                    }
                    else
                    {
                        _isConnected = false;
                        Console.Write(
                            $"The client is unconnected. Ip: {Colors.RED}{_clientIp}{Colors.RESET}\n> ");
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
                        if (_commands.TryGetValue(commandName, out var serverCommand))
                        {
                            serverCommand.Execute(commandValues);
                        }
                        else if (commandName.StartsWith("SETTING"))
                        {
                            if (commandName.Contains(".path") && _settings.SetDir(commandValues?.Last()))
                                Console.WriteLine($"{Colors.BLUE}{_settings.CurrentDirectory}{Colors.RESET}");
                            else
                                Console.WriteLine(_settings);
                        }

                        commandString.Clear();
                        Console.Write("> ");
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (commandString.Length > 0)
                        {
                            commandString.Remove(commandString.Length - 1, 1);
                            Console.Write("\b \b");
                        }
                    }
                    else
                    {
                        commandString.Append(keyInfo.KeyChar);
                        Console.Write(keyInfo.KeyChar);
                    }
                }
            }
        }

        private ServerStatusEnum ListenClient()
        {
            var res = ServerStatusEnum.Fail;
            var commandLengthBytes = new byte[sizeof(long)];
            if (GetData(commandLengthBytes , sizeof(int), 200_000) != sizeof(int))
                return res;

            var commandLength = BitConverter.ToInt32(commandLengthBytes);
            var commandBytes = new byte[commandLength];
            if (GetData(commandBytes, commandLength, 200_000) != commandLength)
                return res;

            var clientCommand = Encoding.Unicode.GetString(commandBytes);

            var parameters = clientCommand.Split(' ').Skip(1).ToArray();
            if (parameters.Length == 0)
                return res;

            var filePath = Path.Combine(_settings.CurrentDirectory, Path.GetFileName(parameters[0]));

            if (clientCommand.StartsWith("UPLOAD"))
                res = ReceiveFile(filePath);
            /*else if (clientCommand.StartsWith("DOWNLOAD"))
                res = SendFile(filePath);*/

            if (res == ServerStatusEnum.Success)
                Console.Write($"{Colors.BLUE}The file was successfully transferred{Colors.RESET}\n> ");
            else if (res == ServerStatusEnum.Error)
                Console.Write($"{Colors.RED}The file was not transferred{Colors.RESET}\n> ");
            else if(res == ServerStatusEnum.LostConnection)
                Console.Write($"{Colors.RED}Операция с файлом не была завершена полностью.{Colors.RESET}\n> ");

            return res;
        }

        private ServerStatusEnum ReceiveFile(string filePath)
        {
            var res = ServerStatusEnum.Fail;
            using var writer = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            
            var fileSizeBytes = new byte[sizeof(long)];
            if (GetData(fileSizeBytes, sizeof(long), 500_000) == 0)
                return res;
            
            var fileSize = BitConverter.ToInt64(fileSizeBytes);
            Console.WriteLine("File size: " + fileSize);

            if (_backup.LastReceiveData.HasCorruptedData 
                && _backup.LastReceiveData.FilePath.Equals(filePath))
            {
                if (SendData(BitConverter.GetBytes(_backup.LastReceiveData.CorruptedPos)) != 0)
                {
                    writer.Seek(_backup.LastReceiveData.CorruptedPos, SeekOrigin.Begin);
                    Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET} " +
                                      $"from pos: {_backup.LastReceiveData.CorruptedPos}.");
                }
            }

            long bytesRead = _backup.LastReceiveData.CorruptedPos; int bytePortion;
            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch();
            try
            {
                var buffer = new byte[ServerConfig.ServingSize];
                while (true)
                {
                    timer.Start();
                    if ((bytePortion = GetData(buffer, ServerConfig.ServingSize, 500_000)) != 0)
                    {
                        writer.Write(buffer, 0, bytePortion);
                        bytesRead += bytePortion;

                        timer.Stop();
                        fll.Report(bytesRead, timer.Elapsed.TotalSeconds);
                    }
                    else break;
                }
                res = ServerStatusEnum.Success;
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                _isConnected = false;
                res = ServerStatusEnum.LostConnection;
            }
            finally
            {
                _backup.LastReceiveData = new(filePath, _clientIp.ToString(), 
                    !_isConnected, bytesRead, timer.Elapsed.TotalSeconds);   
            }
            
            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                          $"{Colors.GREEN}{bytesRead}{Colors.RESET}/{fileSize} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesRead, timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return res;
        }

        /*private ServerStatusEnum SendFile(string filePath)
        {
            long bytesSent = 0;
            using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var fileSize = reader.Length;

            SendData(BitConverter.GetBytes(fileSize));

            long startPos = 0;
            if (_backup.HasCorruptedData && _backup.LastReceivedFilePath.Equals(filePath))
            {
                _backup.HasCorruptedData = false;
                bytesSent = _backup.CorruptedPos;
            }

            if (SendData(BitConverter.GetBytes(startPos)) != 0)
            {
                reader.Seek(bytesSent, SeekOrigin.Begin);
                if (bytesSent != 0)
                    Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET}.");
            }

            var buffer = new byte[ServerConfig.ServingSize];

            var fll = new FileLoadingLine(new FileInfo(filePath).Length);
            var timer = new Stopwatch();

            int bytePortion;
            
            _backup.LastReceivedFilePath = filePath;
            try
            {
                while (true)
                {
                    timer.Start();
                    if ((bytePortion = reader.Read(buffer)) == 0)
                        break;

                    bytesSent += SendData(buffer, bytePortion, 400_000);
                    timer.Stop();
                    fll.Report(bytesSent, timer.Elapsed.TotalSeconds);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                _backup.HasCorruptedData = true;
                _backup.CorruptedPos = bytesSent;
                return ServerStatusEnum.LostConnection;
            }
            
            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                          $"{Colors.GREEN}{bytesSent}{Colors.RESET} Byte; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesSent, timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s");

            return ServerStatusEnum.Success;
        }*/

        private int SendData(byte[] data, int size = 0, int microseconds = 100_000)
        {
            if (size == 0)
                size = data.Length;

            return _clientSocket.Poll(microseconds, SelectMode.SelectWrite)
                ? _clientSocket.Send(data, size, SocketFlags.None)
                : 0;
        }

        private int GetData(byte[] buffer, int size = 0, int microseconds = 100_000)
        {
            if (!_clientSocket.Poll(microseconds, SelectMode.SelectRead))
                return 0;
            
            if(_clientSocket.Available == 0)
                throw new SocketException((int)SocketError.Disconnecting);

            return _clientSocket.Receive(buffer, 0, 
                size == 0 ? buffer.Length : size, SocketFlags.None);
        }

        private IPAddress? ConnectClient()
        {
            _clientSocket = _socket.Accept();
            _clientSocket.Blocking = false;

            var clientIp = (IPEndPoint?)_clientSocket.RemoteEndPoint;
            return clientIp?.Address;
        }
    }
}