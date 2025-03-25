using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Client;

public class Client
{
    protected Socket Socket { get; }

    protected ClientSetting Settings { get; }

    protected IPEndPoint ServerAddress { get; }

    protected bool IsConnected { get; set; }

    protected bool IsDisconnected => Socket.Poll(0, SelectMode.SelectRead)
                                     && Socket.Available == 0;


    public Client(IPEndPoint serverIp)
    {
        ServerAddress = serverIp;
        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        Settings = new ClientSetting();
    }


    public ClientStatusEnum Run()
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
        if (command.StartsWith("TIME"))
        {
            while (SendData(BitConverter.GetBytes(Encoding.Unicode.GetByteCount("TIME"))
                       .Concat(Encoding.Unicode.GetBytes("TIME")).ToArray()) == 0) ;

            var timeBytes = new byte[Encoding.Unicode.GetByteCount(ClientConfig.DateFormat)];
            while (GetData(timeBytes) != timeBytes.Length) ;

            Console.WriteLine($"Fucking time: {Colors.BLUE}{Encoding.Unicode.GetString(timeBytes)}{Colors.RESET}");
            res = ClientStatusEnum.Success;
        }
        else if (command.StartsWith("ECHO"))
        {
            var echoLength = Encoding.Unicode.GetByteCount("ECHO " + parameters.Last());
            while (SendData(BitConverter.GetBytes(echoLength)
                       .Concat(Encoding.Unicode.GetBytes("ECHO " + parameters.Last())).ToArray()) == 0) ;

            var echoBytes = new byte[echoLength];
            while (GetData(echoBytes) != echoBytes.Length) ;

            Console.WriteLine($"Fucking echo: {Colors.BLUE}{Encoding.Unicode.GetString(echoBytes).Split(' ')[1]}{Colors.RESET}");
            res = ClientStatusEnum.Success;
        }
        else
        {
            var filePath = ".";
            if (parameters.Length > 0)
                filePath = Path.Combine(Settings.CurrentDirectory, parameters[0]);

            var commandBytes = Encoding.Unicode.GetBytes($"{command} {Path.GetFileName(filePath)}");
            var bytes = BitConverter.GetBytes(commandBytes.Length).Concat(commandBytes).ToArray();

            if (IsFileBlocked(filePath))
            {
                Console.WriteLine($"{Colors.RED}File is blocked{Colors.RESET}");
                return ClientStatusEnum.Fail;
            }
        
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
            else if (res == ClientStatusEnum.Fail)
                Console.WriteLine($"{Colors.RED}File dont exists.{Colors.RESET}");
        }

        return res;
    }


    private ClientStatusEnum SendFile(string filePath)
    {
        var res = ClientStatusEnum.Success;

        using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var fileSize = reader.Length;
        Console.WriteLine("File size: " + fileSize);

        while (!Socket.Poll(1000, SelectMode.SelectWrite)) ;
        SendData(BitConverter.GetBytes(fileSize));

        Console.WriteLine($"Get access to file. {Colors.YELLOW}Wait...{Colors.RESET}");
        var accessBytes = new byte[sizeof(long)];
        try
        {
            while (GetData(accessBytes, sizeof(long)) != sizeof(long)) ;
            Console.WriteLine("File access: " + BitConverter.ToInt64(accessBytes, 0));
        }
        catch (SocketException)
        {
            return ClientStatusEnum.LostConnection;
        }
        
        var startPosBytes = new byte[sizeof(long)];
        try
        {
            while (GetData(startPosBytes, sizeof(long)) != sizeof(long)) ;
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

        Console.WriteLine("Start pos: " + startPos);
        
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

                while (SendData(buffer, bytePortion, 500_000) == 0) ;

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

    private ClientStatusEnum ReceiveFile(string filePath)
    {
        var res = ClientStatusEnum.Fail;

        Console.WriteLine($"Get access to file. {Colors.YELLOW}Wait...{Colors.RESET}");
        var accessBytes = new byte[sizeof(long)];
        try
        {
            while (GetData(accessBytes, sizeof(long)) != sizeof(long)) ;
            Console.WriteLine("File access: " + BitConverter.ToInt64(accessBytes, 0));
        }
        catch (SocketException)
        {
            return ClientStatusEnum.LostConnection;
        }
        
        var fileInfoBytes = new byte[2 * sizeof(long)];
        try
        {
            while (GetData(fileInfoBytes, 2 * sizeof(long)) != 2 * sizeof(long)) ;
        }
        catch (SocketException)
        {
            return ClientStatusEnum.LostConnection;
        }

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
        var bytesRead = startPos;
        int bytePortion;
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
    
    private bool TryConnect()
    {
        try
        {
            Socket.Connect(ServerAddress);
            IsConnected = true;
            Socket.Blocking = false;
            Socket.SendBufferSize = ClientConfig.SendBufferSize;
            Socket.ReceiveBufferSize = ClientConfig.ReceiveBufferSize;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var keepAliveValues = new byte[12];
                BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);
                BitConverter.GetBytes(10_000).CopyTo(keepAliveValues, 4); //10 s
                BitConverter.GetBytes(5_000).CopyTo(keepAliveValues, 8); //5 s

                Socket.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,
                    ClientConfig.KeepAliveTimeout);
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,
                    ClientConfig.KeepAliveInterval);
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount,
                    ClientConfig.KeepAliveRetryCount);
            }
        }
        catch (SocketException)
        {
            return false;
        }

        return true;
    }

    protected int SendData(byte[] data, int size = 0, int microseconds = 100_000)
    {
        if (size == 0)
            size = data.Length;

        if (IsDisconnected)
        {
            IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return Socket.Poll(microseconds, SelectMode.SelectWrite)
            ? Socket.Send(data, size, SocketFlags.None)
            : 0;
    }

    protected int GetData(byte[] buffer, int size = 0, int microseconds = 100_000)
    {
        if (!Socket.Poll(microseconds, SelectMode.SelectRead))
            return 0;

        if (Socket.Available == 0)
        {
            IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return Socket.Receive(buffer, 0,
            size == 0 ? buffer.Length : size, SocketFlags.None);
    }
    
    FileStream? GetUnlockedFileStream(string filePath, FileMode mode, FileAccess access)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            return new FileStream(filePath, mode, access, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private bool IsFileBlocked(string filePath)
    {
        if (!File.Exists(filePath))
            return false;
        
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                return false;
            }
        }
        catch (IOException)
        {
            return true;
        }
    }
}