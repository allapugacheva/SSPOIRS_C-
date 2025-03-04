using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Client
{
    internal class TcpClient
    {
        private readonly Socket _socket;

        private readonly IPAddress _serverAddress;

        private readonly ClientSetting _settings;

        private bool _isConnected;
        
        private readonly Stopwatch _checkTimer;

        private bool IsDisconnected => _socket.Poll(0, SelectMode.SelectRead) 
                                       && _socket.Available == 0;
        
        
        internal TcpClient(IPAddress serverIp)
        {
            _serverAddress = serverIp;
            _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _settings = new ClientSetting();
            _checkTimer = new Stopwatch();
        }
        
        private bool TryConnect()
        {
            try
            {
                _socket.Connect(new IPEndPoint(_serverAddress, ClientConfig.DefaultPort));
                _isConnected = true;
                _socket.Blocking = false;
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var keepAliveValues = new byte[12];
                    BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);  
                    BitConverter.GetBytes(10_000).CopyTo(keepAliveValues, 4); //10 s
                    BitConverter.GetBytes(5_000).CopyTo(keepAliveValues, 8);   //5 s
                
                    _socket.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
                }
                else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,
                        ClientConfig.KeepAliveTime);
                    _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,
                        ClientConfig.KeepAliveInterval);
                    _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 
                        ClientConfig.KeepAliveAttempts);
                }
            }
            catch (SocketException)
            {
                Console.WriteLine("Penis");
                return false;
            }

            return true;
        }
        
        internal ClientStatusEnum Run()
        {
            var commandString = new StringBuilder();
            while (true)
            {
                if (!_isConnected)
                {

                    if (!TryConnect())
                        return ClientStatusEnum.ConnectionError;
                    
                    Console.Write($"{Colors.GREEN}Client start.{Colors.RESET}\n> ");
                    _checkTimer.Start();
                }
                
                if (_socket.Connected && IsDisconnected)
                    return ClientStatusEnum.LostConnection;
                
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
                            if (commandName.Contains(".path") && _settings.SetDir(commandValues.Last()))
                                Console.WriteLine($"{Colors.BLUE}{_settings.CurrentDirectory}{Colors.RESET}");
                            else
                                Console.WriteLine(_settings);
                        } 
                        else if (commandName.StartsWith("CLOSE"))
                        {
                            _socket.Close();
                            break;
                        }
                        else if (ServerHandler(commandName, commandValues) == ClientStatusEnum.LostConnection)
                                _isConnected = false;

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
            var res = ClientStatusEnum.BadCommand;
            var filePath = ".";
            if (parameters.Length > 0)
                filePath = Path.Combine(_settings.CurrentDirectory, parameters[0]);

            var commandBytes = Encoding.Unicode.GetBytes($"{command} {Path.GetFileName(filePath)}");
            var bytes = BitConverter.GetBytes(commandBytes.Length).Concat(commandBytes).ToArray();
            if (command.StartsWith("UPLOAD") && File.Exists(filePath))
            {
                SendData(bytes);
                res = SendFile(filePath);
            }
            else if (command.StartsWith("DOWNLOAD"))
            {
                SendData(bytes);
                res = ReceiveFile(filePath);
            }
            
            if (res == ClientStatusEnum.Success)
                Console.WriteLine($"{Colors.BLUE}The file was successfully transferred{Colors.RESET}");
            else if (res == ClientStatusEnum.LostConnection)
                Console.WriteLine($"{Colors.RED}The file operation was not completed completely.{Colors.RESET}");
            else if(res == ClientStatusEnum.Fail)
                Console.WriteLine($"{Colors.RED}File dont exists.{Colors.RESET}");

            return res;
        }


        private ClientStatusEnum SendFile(string filePath)
        {
            var res = ClientStatusEnum.Success;
            using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var fileSize = reader.Length;
			Console.WriteLine("File size: " + fileSize);

            SendData(BitConverter.GetBytes(fileSize));

            var startPosBytes = new byte[sizeof(long)];
            try
            {
                while (GetData(startPosBytes,+ sizeof(long)) != sizeof(long)) ;
            }
            catch (SocketException)
            {
                return ClientStatusEnum.LostConnection;
            }
            
            var startPos = BitConverter.ToInt64(startPosBytes);
            if (startPos != 0)
            {
                reader.Seek(startPos, SeekOrigin.Begin);
                Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET} " +
                                  $"from pos: {startPos}.");   
            }
            
            var fll = new FileLoadingLine(new FileInfo(filePath).Length);
            var timer = new Stopwatch();
            var buffer = new byte[ClientConfig.ServingSize];
            var bytesSent = startPos;
            try
            {
                int bytePortion;
                timer.Start();
                while (true)
                {
                    if ((bytePortion = reader.Read(buffer)) == 0)
                        break;

                    bytesSent += SendData(buffer, bytePortion, 500_000);

                    fll.Report(bytesSent, timer.Elapsed.TotalSeconds
                        , bytesSent - startPos);
                }
            }
            catch (SocketException)
            {
                res = ClientStatusEnum.LostConnection;
            }
            finally
            {
                timer.Stop();
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " + 
                          $"{Colors.GREEN}{bytesSent - startPos}{Colors.RESET}/{fileSize - startPos} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesSent - startPos, 
                              timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return res;
        }

        private ClientStatusEnum ReceiveFile(string filePath)
        {
            var res = ClientStatusEnum.Fail;

            var fileInfoBytes = new byte[2 * sizeof(long)];
            try
            {
                while (GetData(fileInfoBytes, 2 * sizeof(long)) != 2 * sizeof(long));
            }
            catch (SocketException)
            {
                _isConnected = false;
                return ClientStatusEnum.LostConnection;
            }

            var fileSize = BitConverter.ToInt64(fileInfoBytes.AsSpan(0, sizeof(long)));
            Console.WriteLine("File size: " + fileSize);

            var startPos = BitConverter.ToInt64(fileInfoBytes.AsSpan(sizeof(long), sizeof(long)));

            if (fileSize == -1 && startPos == -1)
                return res;
            
            using var writer = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            if (startPos != 0)
            {
                startPos = writer.Length;
                writer.Seek(startPos, SeekOrigin.Begin);
                Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET} " +
                                  $"from pos: {startPos}.");
            }
            
            SendData(BitConverter.GetBytes(startPos));
            
            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch();
            var bytesRead = startPos; int bytePortion;
            try
            {
                var buffer = new byte[ClientConfig.ServingSize];
                timer.Start();
                while (bytesRead != fileSize)
                { 
                    if ((bytePortion = GetData(buffer, ClientConfig.ServingSize)) != 0)
                    {
                        writer.Write(buffer, 0, bytePortion);
                        bytesRead += bytePortion;
                        
                        fll.Report(bytesRead, timer.Elapsed.TotalSeconds
                            , bytesRead - startPos);
                    }
                }

                res = ClientStatusEnum.Success;
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                _isConnected = false;
                res = ClientStatusEnum.LostConnection;
            }
            finally
            {
                timer.Stop();
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} received " +
                          $"{Colors.GREEN}{bytesRead - startPos}{Colors.RESET}/{fileSize - startPos} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesRead - startPos, 
                              timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return res;
        }

        private int SendData(byte[] data, int size = 0, int microseconds = 100_000)
        {
            if (size == 0)
                size = data.Length;
            
            if(IsDisconnected)
                throw new SocketException((int)SocketError.Disconnecting);

            return _socket.Poll(microseconds, SelectMode.SelectWrite)
                ? _socket.Send(data, size, SocketFlags.None)
                : 0;
        }

        private int GetData(byte[] buffer, int size = 0, int microseconds = 100_000)
        {
            if (!_socket.Poll(microseconds, SelectMode.SelectRead))
                return 0;

            if (_socket.Available == 0)
                throw new SocketException((int)SocketError.Disconnecting);

            return _socket.Receive(buffer, 0,
                size == 0 ? buffer.Length : size, SocketFlags.None);
        }
    }
}