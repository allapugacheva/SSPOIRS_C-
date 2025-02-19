using Client;
using Server.Commands;
using System.ComponentModel;
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

        private readonly CommandList _commandsList;


        internal TcpServer()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            StartUp();

            _commands = new Dictionary<string, ServerCommand>()
                {
                    { "ECHO", new EchoCommand() },
                    { "TIME", new TimeCommand() }
                };

            _settings = new ServerSettings();
            _backup = new ServerBackup();
            _commandsList = new CommandList(ServerConfig.CommandCapacity);
        }

        internal ServerStatusEnum StartUp()
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
                    _backup.ClientIp = ConnectClient();

                    if(_backup.ClientIp != null)
                    {
                        _backup.IsDisconnected = false;
                        Console.Write($"The client is connected. Ip: {Colors.GREEN}{_backup.ClientIp}{Colors.RESET}\n> ");
                    }
                }
                else if (!_backup.IsDisconnected && _clientSocket != null && _clientSocket.Poll(0, SelectMode.SelectRead))
                {
                    if(_clientSocket.Available != 0)
                    {
                        var res = ListenClient();
                        if(res == ServerStatusEnum.LostConnection)
                            Console.Write($"{Colors.RED}The file could not be processed.{Colors.RESET}\n> ");
                    }
                    else
                    {
                        _backup.IsDisconnected = true;
                        Console.Write($"The client is unconnected. Ip: {Colors.RED}{_backup.ClientIp}{Colors.RESET}\n> ");
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

        internal ServerStatusEnum ListenClient()
        {
            var res = ServerStatusEnum.Fail;
            byte[] bytes = new byte[ServerConfig.MaxFileNameLength + ServerConfig.MaxClientCommnadLength];
            if (_clientSocket.Receive(bytes) != 0)
            {
                string clientCommand = Encoding.Unicode.GetString(bytes).Replace("\0", "");

                var parameters = clientCommand.Split(' ').Skip(1).ToArray();
                string filePath = string.Empty;
                if (parameters != null && parameters.Length > 0)
                {
                    filePath = Path.Combine(_settings.CurrentDirectory, Path.GetFileName(parameters[0]));

                    if (clientCommand.StartsWith("UPLOAD"))
                        res = ReceiveFile(filePath);

                    if (res == ServerStatusEnum.Success)
                        Console.Write($"{Colors.BLUE}The file was successfully transferred{Colors.RESET}\n> ");
                    else if (res == ServerStatusEnum.Error)
                        Console.Write($"{Colors.RED}The file was not transferred{Colors.RESET}\n> ");
                }
            }

            return res;
        }

        internal ServerStatusEnum ReceiveFile(string filePath)
        {
            byte[]? buffer = new byte[ServerConfig.ServingSize];
            long bytesRead = 0; int bytePortion;

            using var writer = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);

            long fileSize = 0;
            if (GetData(out var fileSizeByte, 300_000) != 0 && fileSizeByte != null)
                fileSize = BitConverter.ToInt64(fileSizeByte);
            else
                return ServerStatusEnum.Fail;
            Console.WriteLine("File size: " + fileSize);

            if (_backup.HasCorruptedData && _backup.LastReceivedFilePath.Equals(filePath))
            {
                _backup.HasCorruptedData = false;
                if (SendData(BitConverter.GetBytes(_backup.CorruptedPos)) != 0)
                {
                    writer.Seek(_backup.CorruptedPos, SeekOrigin.Begin);
                    Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET}.");
                }
            }

            _backup.LastReceivedFilePath = filePath;

            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch(); 
            try
            {
                while (true)
                {
                    timer.Start();
                    if((bytePortion = GetData(out buffer, 500_000)) != 0 && buffer != null)
                    {
                        writer.Write(buffer, 0, bytePortion);
                        bytesRead += bytePortion;

                        timer.Stop();
                        fll.Report(bytesRead, timer.Elapsed.TotalSeconds);
                    }
                    else break;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                _backup.HasCorruptedData = true;
                _backup.CorruptedPos = bytesRead;
                return ServerStatusEnum.LostConnection;
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                $"{Colors.GREEN}{bytesRead}{Colors.RESET} Byte; " +
                $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesRead, timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return ServerStatusEnum.Success;
        }

        internal int SendData(byte[] data, int size = 0, int usec = 100_000)
        {
            if (data.Length == 0)
                return 0;

            if (size == 0)
                size = data.Length;

            if (_clientSocket.Poll(usec, SelectMode.SelectWrite))
            {
                return _clientSocket.Send(data, size, SocketFlags.None);
            }
            return 0;
        }

        internal int GetData(out byte[]? data, int usec = 100_000)
        {
            var buffer = new byte[ServerConfig.ServingSize];
            data = null;

            int read = 0;
            if (_clientSocket.Poll(usec, SelectMode.SelectRead))
            {
                if ((read = _clientSocket.Receive(buffer)) != 0)
                    data = buffer.SkipLast(buffer.Length - read).ToArray();
            }

            return read;
        }

        internal IPAddress? ConnectClient()
        {
            _clientSocket = _socket.Accept();
            _clientSocket.Blocking = false;

            var clientIp = (IPEndPoint?)_clientSocket.RemoteEndPoint;
            return clientIp?.Address;
        }
    }
}
