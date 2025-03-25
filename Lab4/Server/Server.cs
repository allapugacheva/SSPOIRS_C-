using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace Server;

#region Client Information

public enum OperationStatus
{
    OperationReady,
    OperationRunning,
    OperationWaiting,
}

public class ClientContext
{
    public OperationStatus OperationStatus { get; set; } = OperationStatus.OperationReady;

    public bool IsConnected { get; set; } = true;

    public bool IsDisconnected { get; set; } = false;

    public ServerBackup Backup { get; set; } = new();

    public ServerStatusEnum OperationResult = ServerStatusEnum.Fail;

    public double OperationPercent { get; set; } = 0;


    public Socket Socket { get; }

    public string IP { get; private set; }
    
    public string FullAddress => Socket.RemoteEndPoint.ToString();
    
    public string LastCommand { get; set; } = string.Empty;
    
    
    public ClientContext(Socket socket)
    {
        Socket = socket;
        IP = socket.RemoteEndPoint.ToString().Split(':')[0];
    }
}

#endregion


public class Server
{
    private Socket Socket { get; }

    private ServerSettings Settings { get; }

    //___Clients___
    
    private ConcurrentDictionary<Socket, ClientContext> Clients { get; } = [];


    //____TPL____

    private readonly DynamicThreadPool _tpl; 

    public Server()
    {
        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Socket.Bind(new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort));
        Socket.Listen(ServerConfig.AmountListeners);

        Settings = new ServerSettings();

        _tpl = new DynamicThreadPool(ServerConfig.MinAmountThread, ServerConfig.MaxAmountThread);
    }


    public ServerStatusEnum Run()
    {
        Console.WriteLine("Server is running.");
        Console.CursorVisible = false;

        var amountClient = 0;
        while (true)
        {
            List<Socket> listenSockets = [Socket];
            listenSockets.AddRange(Clients.Values
                .Where(c => c.OperationStatus == OperationStatus.OperationReady && c.IsConnected)
                .Select(c => c.Socket));

            Socket.Select(listenSockets, null, null, 1000);

            foreach (var socket in listenSockets)
            {
                if (socket == Socket)
                {
                    var client = ConnectClient();
                    Clients.TryAdd(client, new ClientContext(client));
                }
                else
                {
                    var clientContext = Clients.GetValueOrDefault(socket);

                    if (socket.Available > 0)
                    {
                        var commandSizeBytes = new byte[sizeof(int)];
                        GetData(clientContext.Socket, commandSizeBytes);
                        var commandSize = BitConverter.ToInt32(commandSizeBytes, 0);

                        var commandBytes = new byte[commandSize];
                        GetData(clientContext.Socket, commandBytes);
                        var command = Encoding.Unicode.GetString(commandBytes);

                        clientContext.OperationStatus = OperationStatus.OperationWaiting;
                        _tpl.EnqueueTask(() => ClientHandler(clientContext, command));
                    }
                    else
                    {
                        Clients.TryRemove(socket, out _);
                        socket.Close();
                    }
                }
            }
            
            Console.Clear();
            Console.SetCursorPosition(0, 0);
            Console.Write($"Amount {Colors.GREEN}connected{Colors.RESET} clients: {Colors.GREEN}{Clients.Count}{Colors.RESET}\n");
            Console.Write($"Amount {Colors.GREEN}active{Colors.RESET} thread: {Colors.GREEN}{_tpl.AmountActiveThreads}{Colors.RESET}\n");
            Console.Write($"Current {Colors.BLUE}amount{Colors.RESET} threads: {Colors.GREEN}{_tpl.CurrentAmountThread}{Colors.RESET}");
            Console.WriteLine($"\tMIN: {Colors.RED}{ServerConfig.MinAmountThread}{Colors.RESET} MAX: {Colors.GREEN}{ServerConfig.MaxAmountThread}{Colors.RESET}\n\n");
            Console.WriteLine($"Amount request {Colors.GREEN}: {_tpl.AmountRequests} {Colors.RESET}");

            var i = 0;
            Console.SetCursorPosition(0, 9);
            Console.WriteLine("List of clients:");
            foreach (var client in Clients.Values)
            {
                Console.Write(new String(' ', Console.WindowWidth) + '\r');
                
                var statusColor = Colors.RED; var command = string.Empty; var percent = string.Empty;
                if (client.OperationStatus == OperationStatus.OperationRunning)
                {statusColor = Colors.GREEN; command = client.LastCommand; percent = client.OperationPercent.ToString("00.00");}
                else if(client.OperationStatus == OperationStatus.OperationWaiting)
                {statusColor = Colors.YELLOW; command = client.LastCommand;}

                Console.WriteLine($"[{i++}] {statusColor}{client.FullAddress}{Colors.RESET}: " +
                                  $"{client.LastCommand} " +
                                  $"{percent}");

            }
            
            Thread.Sleep(200);
        }
    }

    private void ClientHandler(ClientContext clientContext, string command)
    {
        clientContext.OperationStatus = OperationStatus.OperationRunning;
        var res = ServerStatusEnum.Fail;

        var commandParameters = command.Split(' ');
        var filePath = string.Empty;
        if (commandParameters.Length > 1)
            filePath = Path.Combine(Settings.CurrentDirectory, commandParameters[1]);

        if (command.StartsWith("UPLOAD"))
        {
            clientContext.LastCommand = "UPLOAD " + Path.GetFileName(filePath);
            res = ReceiveFile(clientContext, filePath);
        }
        else if (command.StartsWith("DOWNLOAD"))
        {
            clientContext.LastCommand = "DOWNLOAD " + Path.GetFileName(filePath);
            res = SendFile(clientContext, filePath);
        }
        else if (command.StartsWith("TIME"))
        {
            clientContext.LastCommand = "TIME";

            while (SendData(clientContext.Socket, 
                       Encoding.Unicode.GetBytes(DateTime.Now.ToString(ServerConfig.DateFormat))) == 0) ;
        }
        else if (command.StartsWith("ECHO") && commandParameters.Length > 1)
        {
            clientContext.LastCommand = "ECHO " + commandParameters[1];
            
            while (SendData(clientContext.Socket, Encoding.Unicode.GetBytes(command)) == 0) ;
        }

        clientContext.OperationStatus = OperationStatus.OperationReady;
    }

    
    private ServerStatusEnum ReceiveFile(ClientContext context, string filePath)
    {
        var res = ServerStatusEnum.Fail;

        var fileSizeBytes = new byte[sizeof(long)];
        try
        {
            while (GetData(context.Socket, fileSizeBytes, sizeof(long)) != sizeof(long)) ;
        }
        catch (SocketException)
        {
            return ServerStatusEnum.LostConnection;
        }

        var fileSize = BitConverter.ToInt64(fileSizeBytes);

        context.OperationStatus = OperationStatus.OperationWaiting;
        SendData(context.Socket, BitConverter.GetBytes(1L), sizeof(long));
        
        FileStream? writer;
        long startPos = 0;
        if (context.Backup.LastReceiveData.CanRecovery(filePath, context.IP))
        {
            while((writer = GetUnlockedFileStream(filePath, FileMode.Open, FileAccess.Write)) == null)
            {
                Thread.Sleep(1000);

                if (IsDisconnected(context.Socket))
                {
                    throw new SocketException((int)SocketError.Disconnecting);
                }
            }
                
            startPos = context.Backup.LastReceiveData.CorruptedPos;
            writer.Seek(startPos, SeekOrigin.Begin);
        }
        else
        {
            while((writer = GetUnlockedFileStream(filePath, FileMode.Create, FileAccess.Write)) == null)
                Thread.Sleep(1000);
        }
        
        context.OperationStatus = OperationStatus.OperationRunning;

        while(SendData(context.Socket,BitConverter.GetBytes(startPos), sizeof(long)) != sizeof(long));

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
                if ((bytePortion = GetData(context.Socket, buffer, ServerConfig.ServingSize, 500_000)) != 0)
                {
                    writer.Write(buffer, 0, bytePortion);
                    bytesRead += bytePortion;

                    context.OperationPercent = bytesRead / (double)fileSize * 100.0;
                }
            }

            res = ServerStatusEnum.Success;
        }
        catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
        {
            context.IsDisconnected = true;
            res = ServerStatusEnum.LostConnection;
        }
        finally
        {
            writer.Close();
            timer.Stop();
            context.Backup.LastReceiveData = new LastOpData(filePath, context.IP,
                context.IsDisconnected, bytesRead, timer.Elapsed.TotalSeconds);
        }

        return res;
    }

    private ServerStatusEnum SendFile(ClientContext context, string filePath)
    {
        var res = ServerStatusEnum.Fail;

        FileStream? reader = null;
        long fileSize, startPos = 0;
        
        context.OperationStatus = OperationStatus.OperationWaiting;
        if (!Path.Exists(filePath))
            fileSize = startPos = -1;
        else
        {
            try
            {
                while ((reader = GetUnlockedFileStream(filePath, FileMode.Open, FileAccess.Read)) == null)
                {
                    Thread.Sleep(1000);

                    if (IsDisconnected(context.Socket))
                    {
                        throw new SocketException((int)SocketError.Disconnecting);
                    }
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine(e);
                return res;
            }

            if (context.Backup.LastSendData.CanRecovery(filePath, context.IP))
            {
                startPos = context.Backup.LastSendData.CorruptedPos;
                reader.Seek(startPos, SeekOrigin.Begin);
            }

            fileSize = reader.Length;
        }
        if (reader != null)
            SendData(context.Socket, BitConverter.GetBytes(1L), sizeof(long));
        
        context.OperationStatus = OperationStatus.OperationRunning;
        
        var fileSizeBytes = BitConverter.GetBytes(fileSize);
        var startPosBytes = BitConverter.GetBytes(startPos);
        SendData(context.Socket, fileSizeBytes.Concat(startPosBytes).ToArray(), 2 * sizeof(long));

        if (fileSize == -1 && startPos == -1
            || reader == null)
            return res;

        var bytesSent = startPos;
        int bytePortion;
        
        var timer = new Stopwatch();
        var buffer = new byte[ServerConfig.ServingSize];
        try
        {
            timer.Start();
            while (bytesSent < fileSize)
            {
                if ((bytePortion = reader.Read(buffer)) == 0)
                    break;

                while (SendData(context.Socket, buffer, bytePortion, 500_000) == 0) ;

                bytesSent += bytePortion;
                context.OperationPercent = bytesSent / (double)fileSize * 100.0;
            }

            res = ServerStatusEnum.Success;
        }
        catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
        {
            context.IsDisconnected = true;
            res = ServerStatusEnum.LostConnection;
        }
        finally
        {
            reader.Close();
            timer.Stop();
            context.Backup.LastSendData = new LastOpData(filePath, context.IP,
                context.IsDisconnected, bytesSent, timer.Elapsed.TotalSeconds);
        }

        return res;
    }
    
    private Socket ConnectClient()
    {
        var clientSocket = Socket.Accept();
        clientSocket.SendBufferSize = ServerConfig.SendBufferSize;
        clientSocket.ReceiveBufferSize = ServerConfig.ReceiveBufferSize;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var keepAliveValues = new byte[12];
            BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);
            BitConverter.GetBytes(10_000).CopyTo(keepAliveValues, 4); //10 s
            BitConverter.GetBytes(5_000).CopyTo(keepAliveValues, 8); //5 s

            clientSocket.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,
                ServerConfig.KeepAliveTimeout);
            clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,
                ServerConfig.KeepAliveInterval);
            clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount,
                ServerConfig.KeepAliveRetryCount);
        }

        return clientSocket;
    }

    private bool IsDisconnected(Socket socket) => socket.Poll(0, SelectMode.SelectRead)
                                                  && socket.Available == 0;

    private int SendData(Socket socket, byte[] data, int size = 0, int microseconds = 100_000)
    {
        if (size == 0)
            size = data.Length;

        if (IsDisconnected(socket))
            throw new SocketException((int)SocketError.Disconnecting);

        return socket.Poll(microseconds, SelectMode.SelectWrite)
            ? socket.Send(data, size, SocketFlags.None)
            : 0;
    }

    private int GetData(Socket socket, byte[] buffer, int size = 0, int microseconds = 100_000)
    {
        if (!socket.Poll(microseconds, SelectMode.SelectRead))
            return 0;

        if (socket.Available == 0)
            throw new SocketException((int)SocketError.Disconnecting);

        return socket.Receive(buffer, 0,
            size == 0 ? buffer.Length : size, SocketFlags.None);
    }
    
    private readonly Lock _lock = new();

    FileStream? GetUnlockedFileStream(string filePath, FileMode mode, FileAccess access)
    {
        try
        {
           return new FileStream(filePath, mode, access, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}