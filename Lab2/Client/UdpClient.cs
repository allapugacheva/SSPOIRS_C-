using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Client;

public class UdpClient : Client
{
    public UdpClient(IPAddress serverIp)
        : base(serverIp, SocketType.Dgram, ProtocolType.Udp)
    {
    }

    public const string ConnectSignal = "CONNECT";

    public const string AcceptSignal = "ACCEPT";

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

        return IsConnected;
    }

    public async Task<ClientStatusEnum> Run()
    {
        if (!TryConnect())
            Console.WriteLine($"{Colors.RED}Failed to connect to server.{Colors.RESET}");
        else
            Console.Write($"{Colors.GREEN}Connected to server.{Colors.RESET}\n> ");


        var commandString = new StringBuilder();
        while (true)
        {
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
        var res = ClientStatusEnum.BadCommand;
        var filePath = ".";
        if (parameters.Length > 0)
            filePath = Path.Combine(Settings.CurrentDirectory, parameters[0]);

        var commandBytes = Encoding.Unicode.GetBytes($"{command} {Path.GetFileName(filePath)}");
        var bytes = BitConverter.GetBytes(commandBytes.Length).Concat(commandBytes).ToArray();
        if (command.StartsWith("UPLOAD") /*File.Exists(filePath)*/)
        {
            Socket.SendTo(bytes, _serverAddress);
            SendFile(filePath);
        }
        /*else if (command.StartsWith("DOWNLOAD"))
        {
            SendData(bytes);
            res = ReceiveFile(filePath);
        }*/

        return ClientStatusEnum.Success;
    }

    protected ClientStatusEnum SendFile(string filePath)
    {
        if(!File.Exists(filePath))
            return ClientStatusEnum.Fail;

        Console.WriteLine(filePath);
        using var stream = new FileStream(filePath, FileMode.Open);
        var fileSize = new FileInfo(filePath).Length;
        Console.WriteLine($"File size: {fileSize} Bytes");
        
        Socket.SendTo(BitConverter.GetBytes(fileSize), _serverAddress);
        
        var buffer = new byte[ClientConfig.ServingSize + sizeof(long)];
        
        var serverACKs = new List<long>(); var clientACKs = new List<long>();
        var clientACK = 0L; var bytePortion = 0; 
        var amountPackets = (long)Math.Ceiling(fileSize / (double)ClientConfig.ServingSize);
        var currentAmount = 0L; var bytesSend = 0L;

        Console.WriteLine("Amount packets: " + amountPackets);

        var fll = new FileLoadingLine(fileSize);
        var timer = new Stopwatch(); timer.Start();
        while (true)
        {
            if (clientACKs.Count < ClientConfig.WindowSize)
            {
                if (Socket.Poll(50, SelectMode.SelectWrite))
                {
                    if (serverACKs.Count != 0)
                    {
                        clientACK = serverACKs[0]; 
                        serverACKs.RemoveAt(0);
                    }
                    else
                    {
                        clientACK += bytePortion; 
                    }

                    if (clientACK != fileSize)
                    {
                        stream.Seek(clientACK, SeekOrigin.Begin);
                        bytePortion = stream.Read(buffer, sizeof(long), ClientConfig.ServingSize);
                        BitConverter.GetBytes(clientACK).CopyTo(buffer, 0);
                        clientACKs.Add(clientACK);
                        Socket.SendTo(buffer, bytePortion + sizeof(long), SocketFlags.None, _serverAddress);
                    }
                }
            }
            else if (Socket.Poll(200_000, SelectMode.SelectRead))
            {
                Socket.ReceiveFrom(buffer, ref _serverAddress);
                var countServerACKs = BitConverter.ToInt32(buffer.AsSpan(0, sizeof(int)).ToArray());
                
                var temp = countServerACKs * sizeof(long);

                serverACKs.AddRange(Enumerable.Range(0, temp / sizeof(long))
                    .Select(i => BitConverter.ToInt64(buffer, sizeof(int) + i * sizeof(long)))
                    .ToList());
                bytesSend += serverACKs.Count * ClientConfig.ServingSize;
                currentAmount += serverACKs.Count;
                Console.Write(serverACKs.Count + " " + currentAmount);
                
                serverACKs = clientACKs.Except(serverACKs).ToList();
                Console.WriteLine(" " + serverACKs.Count + " " + bytesSend);
                if (amountPackets <= currentAmount)
                {
                    Socket.SendTo(BitConverter.GetBytes(-1L), _serverAddress);
                    break;
                }

                //fll.Report(bytesSend, timer.Elapsed.TotalSeconds);
                clientACKs.Clear();
            }
            else
            {
                serverACKs.Clear();
                serverACKs.AddRange(clientACKs);
                clientACKs.Clear();
            }
        }
        timer.Stop();

        Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                      $"{Colors.GREEN}{bytesSend}{Colors.RESET}/{fileSize} Bytes; " +
                      $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesSend,
                          timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                      $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

        return ClientStatusEnum.Success;
    }

}