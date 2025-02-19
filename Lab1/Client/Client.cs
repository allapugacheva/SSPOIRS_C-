using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using Luna.ConsoleProgressBar;

namespace Client
{
    internal class TcpClient
    {
        private readonly Socket _socket;

        private readonly IPAddress _serverAddress;

        private readonly ClientSetting _settings;


        internal TcpClient(IPAddress serverIp)
        {
            _serverAddress = serverIp;
            _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _settings = new ClientSetting();
        }


        internal ClientStatusEnum Run()
        {
            try { _socket.Connect(new IPEndPoint(_serverAddress, ClientConfig.DefaultPort));}
            catch (SocketException) { return ClientStatusEnum.ConnectionError; }
            _socket.Blocking = false;

            Console.Write($"{Colors.GREEN}Client start.{Colors.RESET}\n> ");

            var commandString = new StringBuilder();
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        var commandParts = commandString.ToString().Split(' ');
                        var commandName = commandParts[0];
                        var commandValues = commandParts.Length > 1 ? commandParts.Skip(1).ToArray() : null;

                        if (commandName.StartsWith("SETTING"))
                        {
                            if (commandName.Contains(".path") && _settings.SetDir(commandValues?.Last()))
                                Console.WriteLine($"{Colors.BLUE}{_settings.CurrentDirectory}{Colors.RESET}");
                            else
                                Console.WriteLine(_settings);
                        }
                        else
                        {
                            ClientHandler(commandName, commandValues);
                        }
                        commandString.Clear();
                        Console.Write("\n> ");
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

            return ClientStatusEnum.Success;
        }

        internal void ClientHandler(string commnad, string[]? parameters = null)
        {
            string filePath = ".";
            if (parameters != null && parameters.Length > 0)
                filePath = Path.Combine(_settings.CurrentDirectory, parameters[0]);

            if (!File.Exists(filePath))
                return;

            SendData(Encoding.Unicode.GetBytes(commnad + " " + Path.GetFileName(filePath)));
            if (commnad.StartsWith("UPLOAD"))
            {
                SendFile(filePath);
            }
        }


        internal ClientStatusEnum SendFile(string filePath)
        {
            long bytesSent = 0; int bytePortion;
            using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            long fileSize = reader.Length;

            SendData(BitConverter.GetBytes(fileSize));

            if (GetData(out var data, 300_000) != 0)
            {
                bytesSent = BitConverter.ToInt64(data);
                reader.Seek(bytesSent, SeekOrigin.Begin);
            }

            byte[] buffer = new byte[ClientConfig.ServingSize];

            var fll = new FileLoadingLine(new FileInfo(filePath).Length);
            var timer = new Stopwatch(); 
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
            catch (SocketException ex)
            {
                Console.Write(ex.Message);
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                $"{Colors.GREEN}{bytesSent}{Colors.RESET} Byte; " +
                $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesSent, timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s");

            return ClientStatusEnum.Success;
        }

        internal int SendData(byte[] data, int size = 0, int usec = 100_000)
        {
            if (data.Length == 0)
                return 0;

            if (size == 0)
                size = data.Length;

            if (_socket.Poll(usec, SelectMode.SelectWrite))
            {
                return _socket.Send(data, size, SocketFlags.None);         
            }
            return 0;
        }

        internal int GetData(out byte[]? data, int usec = 100_000)
        {
            var buffer = new byte[ClientConfig.ServingSize];
            data = null;

            int read = 0;
            if (_socket.Poll(usec, SelectMode.SelectRead))
            {
                if ((read = _socket.Receive(buffer)) != 0)
                    data = buffer.SkipLast(buffer.Length - read).ToArray();
            }

            return read;
        }
    }
}
