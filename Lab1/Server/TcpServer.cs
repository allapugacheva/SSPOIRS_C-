using Client;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Server
{
    internal class TcpServer : Server
    {
        internal TcpServer() 
            : base(SocketType.Stream, ProtocolType.Tcp)
        { }

        public override ServerStatusEnum Run()
        {
            Console.Write($"{Colors.GREEN}Server is start.{Colors.RESET}\n> ");

            var commandString = new StringBuilder(50);
            while (true)
            {
                if (!IsConnected && Socket.Poll(0, SelectMode.SelectRead))
                {
                    if ((ClientIp = ConnectClient()) != null)
                        Console.Write(
                            $"The client is connected. Ip: {Colors.GREEN}{ClientIp}{Colors.RESET}\n> ");
                }
                else if (ClientIp != null && ClientSocket.Poll(0, SelectMode.SelectRead))
                {
                    if (ClientSocket.Available != 0)
                        ClientHandler();
                    else
                        IsConnected = false;
                    
                    if(!IsConnected)
                        Console.Write(
                            $"The client is unconnected. Ip: {Colors.RED}{ClientIp}{Colors.RESET}\n> ");
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
                        if (commandName.StartsWith("ECHO"))
                            Console.WriteLine($"{Backup.LastReceiveData.FilePath}" +
                                              $"/{Backup.LastSendData.FilePath}");
                        else if (commandName.StartsWith("TIME"))
                            Console.WriteLine($"Current time: " +
                                              $"{Colors.BLUE}{DateTime.Now.ToString(ServerConfig.DateFormat)}{Colors.RESET}");
                        else if (commandName.StartsWith("CLOSE"))
                        {
                            ClientSocket.Close();
                            Socket.Close();
                            break;
                        }
                        else if (commandName.StartsWith("SETTING"))
                        {
                            if (commandName.Contains(".path") && Settings.SetDir(commandValues?.Last()))
                                Console.WriteLine($"{Colors.BLUE}{Settings.CurrentDirectory}{Colors.RESET}");
                            else
                                Console.WriteLine(Settings);
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
                    else if (!char.IsControl(keyInfo.KeyChar) && !char.IsSurrogate(keyInfo.KeyChar))
                    {
                        commandString.Append(keyInfo.KeyChar);
                        Console.Write(keyInfo.KeyChar);
                    }
                }
            }
            
            return ServerStatusEnum.Success;
        }

        private void ClientHandler()
        {
            var commandLengthBytes = new byte[sizeof(long)];
            if (GetData(commandLengthBytes, sizeof(int), 15_000_000) != sizeof(int))
                return;

            var commandLength = BitConverter.ToInt32(commandLengthBytes);
            var commandBytes = new byte[commandLength];
            if (GetData(commandBytes, commandLength, 15_000_000) != commandLength)
                return;

            var clientCommand = Encoding.Unicode.GetString(commandBytes);

            var parameters = clientCommand.Split(' ').Skip(1).ToArray();
            if (parameters.Length == 0)
                return;

            var filePath = Path.Combine(Settings.CurrentDirectory, Path.GetFileName(parameters[0]));

            var res = ServerStatusEnum.Fail;
            if (clientCommand.StartsWith("UPLOAD"))
                res = ReceiveFile(filePath);
            else if (clientCommand.StartsWith("DOWNLOAD"))
                res = SendFile(filePath);
            
            if (res == ServerStatusEnum.Success)
                Console.Write($"{Colors.BLUE}The file was successfully transferred{Colors.RESET}\n> ");
            else if (res == ServerStatusEnum.LostConnection)
                Console.Write($"{Colors.RED}The file operation was not completed completely.{Colors.RESET}\n> ");
        }

        protected override ServerStatusEnum ReceiveFile(string filePath)
        {
            var res = ServerStatusEnum.Fail;
            
            var fileSizeBytes = new byte[sizeof(long)];
            try { while (GetData(fileSizeBytes, sizeof(long)) != sizeof(long)); }
            catch (SocketException) { return ServerStatusEnum.LostConnection; }
            
            var fileSize = BitConverter.ToInt64(fileSizeBytes);
            Console.WriteLine("File size: " + fileSize);

            FileStream? writer; long startPos = 0;
            if (Backup.LastReceiveData.CanRecovery(filePath, ClientIp.ToString()))
            {
                writer = new FileStream(filePath, FileMode.Open, FileAccess.Write);
                startPos = Backup.LastReceiveData.CorruptedPos;
                writer.Seek(startPos, SeekOrigin.Begin);
                Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET} " +
                                  $"from pos: {startPos}.");
            }
            else
            {
                writer = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            }
            
            SendData(BitConverter.GetBytes(startPos), sizeof(long));

            var bytesRead = startPos;
            int bytePortion;
            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch();
            try
            {
                var buffer = new byte[ServerConfig.ServingSize];
                timer.Start();
                while (bytesRead < fileSize)
                {
                    if ((bytePortion = GetData(buffer, ServerConfig.ServingSize, 500_000)) != 0)
                    {
                        writer.Write(buffer, 0, bytePortion);
                        bytesRead += bytePortion;
                        
                        fll.Report(bytesRead, timer.Elapsed.TotalSeconds,
                            bytesRead - startPos); 
                    }
                }
                res = ServerStatusEnum.Success;
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                res = ServerStatusEnum.LostConnection;
            }
            finally
            {
                writer.Close();
                timer.Stop();
                Backup.LastReceiveData = new LastOpData(filePath, ClientIp.ToString(),
                    !IsConnected, bytesRead, timer.Elapsed.TotalSeconds);
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} received " +
                          $"{Colors.GREEN}{bytesRead - startPos}{Colors.RESET}/{fileSize - startPos} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesRead - startPos, 
                              timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return res;
        }

        protected override ServerStatusEnum SendFile(string filePath)
        {
            var res = ServerStatusEnum.Fail;
            
            FileStream? reader = null; long fileSize, startPos = 0;
            if (!Path.Exists(filePath))
                fileSize = startPos = -1;
            else
            {
                reader = new FileStream(filePath, FileMode.Open, FileAccess.Read);

                if (Backup.LastSendData.CanRecovery(filePath, ClientIp.ToString()))
                {
                    startPos = Backup.LastSendData.CorruptedPos;
                    reader.Seek(startPos, SeekOrigin.Begin);
                    Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET} " +
                                      $"from pos: {startPos}.");
                }
                fileSize = reader.Length;
            }
            
            var fileSizeBytes = BitConverter.GetBytes(fileSize); var startPosBytes = BitConverter.GetBytes(startPos);
            SendData(fileSizeBytes.Concat(startPosBytes).ToArray(), 2 * sizeof(long));
            
            if(fileSize == -1 && startPos == -1)
                return res;
            
            if (reader == null)
                return res;

            Console.WriteLine($"File size: {fileSize}");
            
            var bytesSent = startPos; int bytePortion;
            
            var fll = new FileLoadingLine(new FileInfo(filePath).Length);
            var timer = new Stopwatch();
            var buffer = new byte[ServerConfig.ServingSize];
            try
            {
                timer.Start();
                while (bytesSent < fileSize)
                {
                    if ((bytePortion = reader.Read(buffer)) == 0)
                        break;
                    
                    while (SendData(buffer, bytePortion, 500_000) == 0);
                    
                    bytesSent += bytePortion;
                    fll.Report(bytesSent, timer.Elapsed.TotalSeconds, 
                        bytesSent - startPos); 
                }
                res = ServerStatusEnum.Success; 
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                res = ServerStatusEnum.LostConnection;
            }
            finally
            {
                reader.Close();
                timer.Stop();
                Backup.LastSendData = new LastOpData(filePath, ClientIp.ToString(),
                    !IsConnected, bytesSent, timer.Elapsed.TotalSeconds);
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " + 
                          $"{Colors.GREEN}{bytesSent - startPos}{Colors.RESET}/{fileSize - startPos} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesSent - startPos, 
                              timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return res;
        }
    }
}