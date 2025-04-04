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

    protected EndPoint _clientIp;
    
    public EndPoint ClientIp => _clientIp;

    protected bool IsConnected { get; set; }
    
    
    protected Server(SocketType socketType, ProtocolType protocolType)
    {
        IsConnected = false;
        Socket = new Socket(AddressFamily.InterNetwork, socketType, protocolType);
        Socket.Bind(new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort));
        Socket.Blocking = false;
        //Socket.Listen(ServerConfig.AmountListeners);

        Settings = new ServerSettings();
        Backup = new ServerBackup();
    }
    
    //__ Need to overriding methods __
    
    //__ Base methods __
    
    private bool IsDisconnected => ClientSocket.Poll(0, SelectMode.SelectRead) 
                                   && ClientSocket.Available == 0;
    
    protected int SendData(byte[] data, int size = 0, int microseconds = 100_000)
    {
        if (size == 0)
            size = data.Length;

        return ClientSocket.Poll(microseconds, SelectMode.SelectWrite)
            ? ClientSocket.Send(data, size, SocketFlags.None)
            : 0;
    }

    protected int GetData(byte[] buffer, int size = 0, int microseconds = 100_000)
    {
        if (!Socket.Poll(microseconds, SelectMode.SelectRead))
            return 0;

        EndPoint localEndPoint = new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort);
        
        return Socket.ReceiveFrom(buffer, 0,
            size == 0 ? buffer.Length : size,SocketFlags.None, ref localEndPoint);
    }
}