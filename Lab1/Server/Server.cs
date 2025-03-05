using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Server;

public abstract class Server
{
    protected Socket Socket { get; }

    protected Socket ClientSocket { get; set; }

    protected ServerSettings Settings { get; }

    protected ServerBackup Backup { get; }

    protected IPAddress? ClientIp { get; set; }

    protected bool IsConnected { get; set; }
    
    
    protected Server(SocketType socketType, ProtocolType protocolType)
    {
        IsConnected = false;
        Socket = new Socket(AddressFamily.InterNetwork, socketType, protocolType);
        Socket.Bind(new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort));
        Socket.Listen(ServerConfig.AmountListeners);

        Settings = new ServerSettings();
        Backup = new ServerBackup();
    }
    
    //__ Need to overriding methods __
    
    public abstract ServerStatusEnum Run();

    protected abstract ServerStatusEnum SendFile(string filePath);
    protected abstract ServerStatusEnum ReceiveFile(string filePath);
    
    
    //__ Base methods __
    
    protected IPAddress? ConnectClient()
    {
        ClientSocket = Socket.Accept();
        ClientSocket.Blocking = false;
        ClientSocket.SendBufferSize = ServerConfig.SendBufferSize;
        ClientSocket.ReceiveBufferSize = ServerConfig.ReceiveBufferSize;    

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var keepAliveValues = new byte[12];
            BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);  
            BitConverter.GetBytes(10_000).CopyTo(keepAliveValues, 4); //10 s
            BitConverter.GetBytes(5_000).CopyTo(keepAliveValues, 8);   //5 s
                
            ClientSocket.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
        }
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ClientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            ClientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,
                ServerConfig.KeepAliveTime);
            ClientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,
                ServerConfig.KeepAliveInterval);
            ClientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 
                ServerConfig.KeepAliveAttempts);
        }
            
        var clientIp = (IPEndPoint?)ClientSocket.RemoteEndPoint;
        if(clientIp != null)
            IsConnected = true;
        
        return clientIp?.Address;
    }
    
    private bool IsDisconnected => ClientSocket.Poll(0, SelectMode.SelectRead) 
                                   && ClientSocket.Available == 0;
    
    protected int SendData(byte[] data, int size = 0, int microseconds = 100_000)
    {
        if (size == 0)
            size = data.Length;

        if (IsDisconnected)
        {
            IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return ClientSocket.Poll(microseconds, SelectMode.SelectWrite)
            ? ClientSocket.Send(data, size, SocketFlags.None)
            : 0;
    }

    protected int GetData(byte[] buffer, int size = 0, int microseconds = 100_000)
    {
        if (!ClientSocket.Poll(microseconds, SelectMode.SelectRead))
            return 0;

        if (ClientSocket.Available == 0)
        {
            IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return ClientSocket.Receive(buffer, 0,
            size == 0 ? buffer.Length : size, SocketFlags.None);
    }
}