using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Server;

public class UdpServer : Server
{
    internal UdpServer() 
            : base(SocketType.Dgram, ProtocolType.Udp)
        { }

        public const string ConnectSignal = "CONNECT";
        
        public const string AcceptSignal = "ACCEPT";
    
        
        public async Task<ServerStatusEnum> Run()
        {
            Console.Write($"{Colors.GREEN}Server is start.{Colors.RESET}\n> ");

            var commandString = new StringBuilder(50);
            while (true)
            {
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
                            IsConnected = true;
                            Console.Write($"{Colors.GREEN}Server is connected to {_clientIp}.{Colors.RESET}\n> ");
                        }
                        
                        Socket.ReceiveBufferSize = ServerConfig.ReceiveBufferSize;
                        Socket.SendBufferSize = ServerConfig.SendBufferSize;
                    }
                    else
                    {
                        ClientHandler();
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

        private void ClientHandler()
        {
            var commandBytes = new byte[ServerConfig.ServingSize];
            Socket.ReceiveFrom(commandBytes, ref _clientIp);

            var commandLength = BitConverter.ToInt32(commandBytes, 0);
            var clientCommand = Encoding.Unicode.GetString(commandBytes.AsSpan(sizeof(int), commandLength));

            var parameters = clientCommand.Split(' ').Skip(1).ToArray();
            if (parameters.Length == 0)
                return;

            var filePath = Path.Combine(Settings.CurrentDirectory, Path.GetFileName(parameters[0]));
            
            var res = ServerStatusEnum.Fail;
            if (clientCommand.StartsWith("UPLOAD"))
                res = ReceiveFile(filePath);
            /*else if (clientCommand.StartsWith("DOWNLOAD"))
                res = SendFile(filePath);*/
            
            if (res == ServerStatusEnum.Success)
                Console.Write($"{Colors.BLUE}The file was successfully transferred{Colors.RESET}\n> ");
            else if (res == ServerStatusEnum.LostConnection)
                Console.Write($"{Colors.RED}The file operation was not completed completely.{Colors.RESET}\n> ");
        }

        protected ServerStatusEnum ReceiveFile(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Create);

            var fileSizeBytes = new byte[sizeof(long)];
            while (!Socket.Poll(50, SelectMode.SelectRead)) ;
            Socket.ReceiveFrom(fileSizeBytes, SocketFlags.None, ref _clientIp);
            
            var fileSize = BitConverter.ToInt64(fileSizeBytes);
            Console.WriteLine($"File size: {fileSize} Bytes");
            
            var buffer = new byte[ServerConfig.ServingSize + sizeof(long)]; var bytePortion = 0;
            var confirmedAck = new List<long>(); var amountReceived = 0L;

            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch();
            timer.Start();
            var clientACK = 0L;
            int amountPackets = 0;
            while (true)
            {
                if (Socket.Poll(50_000, SelectMode.SelectRead))
                {
                    bytePortion = Socket.ReceiveFrom(buffer, buffer.Length, SocketFlags.None, ref _clientIp) - sizeof(long);
                    
                    clientACK = BitConverter.ToInt64(buffer, 0);
                    if (clientACK == -1L)
                        break;
                    
                    confirmedAck.Add(clientACK);

                    stream.Seek(clientACK, SeekOrigin.Begin);
                    stream.Write(buffer, sizeof(long), bytePortion);
                }
                else if (confirmedAck.Count != 0)
                {
                    confirmedAck = confirmedAck.Distinct().OrderBy(ack => ack).ToList();

                    var confirmedAckBytes = BitConverter.GetBytes(confirmedAck.Count)
                        .Concat(confirmedAck.SelectMany(BitConverter.GetBytes))
                        .ToArray();
                    amountPackets += confirmedAck.Count;
                    Console.WriteLine(confirmedAck.Count + " " + amountPackets + " " + stream.Length);
                    
                    Socket.SendTo(confirmedAckBytes, _clientIp);
                    
                    //fll.Report(clientACK, timer.Elapsed.TotalSeconds);
                    confirmedAck.Clear();
                }
            }
            timer.Stop();   
            
            Console.WriteLine(stream.Length);
            stream.Close();
            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                          $"{Colors.GREEN}{amountReceived}{Colors.RESET}/{fileSize} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(amountReceived,
                              timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");
            
            return ServerStatusEnum.Success;
        }
}