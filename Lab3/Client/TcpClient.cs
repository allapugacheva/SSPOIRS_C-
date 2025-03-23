using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Client
{
    public class TcpClient : Client
    {
        public TcpClient(IPAddress serverIp)
            : base(serverIp, SocketType.Stream, ProtocolType.Tcp)
        { }
        
        public override ClientStatusEnum Run()
        {
            var commandString = new StringBuilder();
            while (true)
            {
                if (!IsConnected)
                {
                    if (!TryConnect())
                        return ClientStatusEnum.ConnectionError;
                    
                    Console.Write($"{Colors.GREEN}Client start.{Colors.RESET}\n> ");
                }
                
                if (Socket.Connected && IsDisconnected)
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
                filePath = Path.Combine(Settings.CurrentDirectory, parameters[0]);

            byte[] commandBytes;
            
            if (command.StartsWith("DOWNLOAD") || command.StartsWith("UPLOAD"))
                commandBytes = Encoding.Unicode.GetBytes($"{command} {Path.GetFileName(filePath)}");
            else if (command.StartsWith("ECHO"))
                commandBytes = Encoding.Unicode.GetBytes($"{command} {string.Join(" ", parameters)}");
            else
                commandBytes = Encoding.Unicode.GetBytes($"{command}");

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
            else if (command.StartsWith("TIME"))
            {
                SendData(bytes);

                var bufferLengthBytes = new byte[sizeof(int)];
                while (GetData(bufferLengthBytes, sizeof(int), 15_000_000) != sizeof(int));

                var bufferLength = BitConverter.ToInt32(bufferLengthBytes);
                var timeBuffer = new byte[bufferLength];
                while (GetData(timeBuffer, bufferLength, 15_000_000) != bufferLength);

                Console.WriteLine($"Server time: {Encoding.Unicode.GetString(timeBuffer)}");
            }
            else if (command.StartsWith("ECHO"))
            {
                SendData(bytes);

                var bufferLengthBytes = new byte[sizeof(int)];
                while (GetData(bufferLengthBytes, sizeof(int), 15_000_000) != sizeof(int));

                var bufferLength = BitConverter.ToInt32(bufferLengthBytes);
                var timeBuffer = new byte[bufferLength];
                while (GetData(timeBuffer, bufferLength, 15_000_000) != bufferLength);

                Console.WriteLine($"{Encoding.Unicode.GetString(timeBuffer)}");
            }
            
            if (res == ClientStatusEnum.Success)
                Console.WriteLine($"{Colors.BLUE}The file was successfully transferred{Colors.RESET}");
            else if (res == ClientStatusEnum.LostConnection)
                Console.WriteLine($"{Colors.RED}The file operation was not completed completely.{Colors.RESET}");
            else if(res == ClientStatusEnum.Fail)
                Console.WriteLine($"{Colors.RED}File dont exists.{Colors.RESET}");

            return res;
        }


        protected override ClientStatusEnum SendFile(string filePath)
        {
            var res = ClientStatusEnum.Success;
            
            using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var fileSize = reader.Length;
			Console.WriteLine("File size: " + fileSize);
            
            SendData(BitConverter.GetBytes(fileSize));
            
            var startPosBytes = new byte[sizeof(long)];
            try { while (GetData(startPosBytes,sizeof(long)) != sizeof(long)) ; }
            catch (SocketException) { return ClientStatusEnum.LostConnection; }
            
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

                    while(SendData(buffer, bytePortion, 500_000) == 0);

                    bytesSent += bytePortion;
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

        protected override ClientStatusEnum ReceiveFile(string filePath)
        {
            var res = ClientStatusEnum.Fail;

            var fileInfoBytes = new byte[2 * sizeof(long)];
            try { while (GetData(fileInfoBytes, 2 * sizeof(long)) != 2 * sizeof(long)); }
            catch (SocketException) { return ClientStatusEnum.LostConnection; }

            var fileSize = BitConverter.ToInt64(fileInfoBytes.AsSpan(0, sizeof(long)));
            Console.WriteLine("File size: " + fileSize);

            var startPos = BitConverter.ToInt64(fileInfoBytes.AsSpan(sizeof(long), sizeof(long)));

            if (fileSize == -1 && startPos == -1)
                return res;

            FileStream? writer;
            if (startPos != 0)
            {
                writer = new FileStream(filePath, FileMode.Open, FileAccess.Write);
                writer.Seek(startPos, SeekOrigin.Begin);
                Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET} " +
                                  $"from pos: {startPos}.");
            }
            else
            {
                writer = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            }
            
            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch();
            var bytesRead = startPos; int bytePortion;
            try
            {
                var buffer = new byte[ClientConfig.ServingSize];
                timer.Start();
                while (bytesRead != fileSize)
                { 
                    if ((bytePortion = GetData(buffer, ClientConfig.ServingSize, 500_000)) != 0)
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
                res = ClientStatusEnum.LostConnection;
            }
            finally
            {
                timer.Stop();
                writer.Close();
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} received " +
                          $"{Colors.GREEN}{bytesRead - startPos}{Colors.RESET}/{fileSize - startPos} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesRead - startPos, 
                              timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return res;
        }
    }
}