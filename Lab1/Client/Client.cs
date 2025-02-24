﻿using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;

namespace Client
{
    internal class TcpClient
    {
        private readonly Socket _socket;

        private readonly IPAddress _serverAddress;

        private readonly ClientSetting _settings;

        private bool _isConnected;
        
        private readonly Stopwatch _checkTimer;

        private const double CheckTimeout = 29.9999999999;

        
        internal TcpClient(IPAddress serverIp)
        {
            _serverAddress = serverIp;
            _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _settings = new ClientSetting();
            _checkTimer = new Stopwatch();
        }
        
        private bool CheckConnection() => _socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0;
        
        private bool TryConnect()
        {
            try
            {
                _socket.Connect(new IPEndPoint(_serverAddress, ClientConfig.DefaultPort));
                _isConnected = true;
                _socket.Blocking = false;
            }
            catch (SocketException)
            {
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
                    {
                        Console.WriteLine("Соси хуй сервер сдох");
                        break;
                    }
                    Console.Write($"{Colors.GREEN}Client start.{Colors.RESET}\n> ");
                    _checkTimer.Start();
                }
                
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
                        else if (ClientHandler(commandName, commandValues) == ClientStatusEnum.LostConnection)
                                _isConnected = false;

                        commandString.Clear();
                        Console.Write("\n> ");
                    }
                    else if (commandString.Length > 0 && keyInfo.Key == ConsoleKey.Backspace)
                    {
                        commandString.Remove(commandString.Length - 1, 1);
                        Console.Write("\b \b");
                    }
                    else
                    {
                        commandString.Append(keyInfo.KeyChar);
                        Console.Write(keyInfo.KeyChar);
                    }
                }
                
                if(CheckConnection())
                {
                    Console.WriteLine("Соси хуй сервер сдох");
                    Console.ReadLine();
                    break;
                }
                
            }

            return ClientStatusEnum.Success;
        }

        private ClientStatusEnum ClientHandler(string command, string[]? parameters = null)
        {
            var res = ClientStatusEnum.Fail;
            var filePath = ".";
            if (parameters != null && parameters.Length > 0)
                filePath = Path.Combine(_settings.CurrentDirectory, parameters[0]);

            if (!File.Exists(filePath))
                return res;

            var bytes = Encoding.Unicode.GetBytes($"{command} {Path.GetFileName(filePath)}");
            SendData(BitConverter.GetBytes(bytes.Length));
            SendData(bytes);
            if (command.StartsWith("UPLOAD"))
                res = SendFile(filePath);
            /*else if (command.StartsWith("DOWNLOAD"))
                res = ReceiveFile(filePath);*/
            
            if(res == ClientStatusEnum.LostConnection)
                Console.WriteLine("Операция с файлом не была завершена полностью");

            return res;
        }


        private ClientStatusEnum SendFile(string filePath)
        {
            var res = ClientStatusEnum.Success;
            long bytesSent = 0; int bytePortion;
            using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var fileSize = reader.Length;

            SendData(BitConverter.GetBytes(fileSize));

            var startPosBytes = new byte[sizeof(long)];
            if (GetData(startPosBytes, sizeof(long), 300_000) != 0)
            {
                bytesSent = BitConverter.ToInt64(startPosBytes);
                reader.Seek(bytesSent, SeekOrigin.Begin);
                Console.WriteLine($"Transfer was {Colors.BLUE}recovery{Colors.RESET} " +
                                  $"from pos: {bytesSent}.");
            }

            var fll = new FileLoadingLine(new FileInfo(filePath).Length);
            var timer = new Stopwatch();
            var buffer = new byte[ClientConfig.ServingSize];
            try
            {
                while (true)
                {
                    timer.Start();
                    if ((bytePortion = reader.Read(buffer)) == 0)
                        break;

                    bytesSent += SendData(buffer, bytePortion, 400_000);
                    if (bytesSent == 0)
                        throw new SocketException((int)SocketError.Disconnecting);
                    
                    timer.Stop();
                    fll.Report(bytesSent, timer.Elapsed.TotalSeconds);
                }
            }
            catch (SocketException) 
            {
                res = ClientStatusEnum.LostConnection;
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                          $"{Colors.GREEN}{bytesSent}{Colors.RESET}/{fileSize} Bytes; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesSent, timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s");

            return res;
        }

        /*
        private ClientStatusEnum ReceiveFile(string filePath)
        {
            using var writer = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);

            long fileSize = 0;
            if (GetData(out var fileSizeBuffer, sizeof(long), 500_000) != 0)
                fileSize = BitConverter.ToInt64(fileSizeBuffer);

            Console.WriteLine("File size: " + fileSize);

            long bytesRead = 0;
            if (GetData(out var startPositionBuffer, sizeof(long), 500_000) != 0)
            {
                bytesRead = BitConverter.ToInt64(startPositionBuffer);
                if (bytesRead != 0)
                    Console.WriteLine($"Transferred was {Colors.GREEN}recovery{Colors.RESET} from pos: " + bytesRead);
                writer.Seek(bytesRead, SeekOrigin.Begin);
            }
            
            int bytePortion;
            var fll = new FileLoadingLine(fileSize);
            var timer = new Stopwatch();
            try
            {
                while (true)
                {
                    timer.Start();
                    if ((bytePortion = GetData(out var buffer, microseconds: 500_000)) != 0)
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
                return ClientStatusEnum.LostConnection;
            }

            Console.Write($"\r{Colors.GREEN}Success{Colors.RESET} sent " +
                          $"{Colors.GREEN}{bytesRead}{Colors.RESET} Byte; " +
                          $"Speed: {Colors.BLUE}{FileLoadingLine.GetSpeed(bytesRead, timer.Elapsed.TotalSeconds)}{Colors.RESET}; " +
                          $"Time: {Colors.YELLOW}{timer.Elapsed.TotalSeconds:F3}{Colors.RESET} s\n");

            return ClientStatusEnum.Success;
        }
        */

        private int SendData(byte[] data, int size = 0, int microseconds = 100_000)
        {
            if (size == 0)
                size = data.Length;

            return _socket.Poll(microseconds, SelectMode.SelectWrite)
                ? _socket.Send(data, size, SocketFlags.None)
                : 0;
        }

        private int GetData(byte[] buffer, int size = 0, int microseconds = 100_000)
        {
            if (!_socket.Poll(microseconds, SelectMode.SelectRead))
                return 0;
            
            if(_socket.Available == 0)
                throw new SocketException((int)SocketError.Disconnecting);

            return _socket.Receive(buffer, 0, 
                size == 0 ? buffer.Length : size, SocketFlags.None);
        }
    }
}