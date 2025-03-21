using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Client;

public abstract class Client 
{
    protected Socket Socket { get; }

    protected ClientSetting Settings { get; }

    protected EndPoint _serverAddress;

    public EndPoint ServerAddress => _serverAddress;

    protected bool IsConnected { get; set; }
    
    protected bool IsDisconnected => Socket.Poll(0, SelectMode.SelectRead) 
                                   && Socket.Available == 0;

    
    protected Client(IPAddress serverIp, SocketType socketType, ProtocolType protocolType)
    {
        _serverAddress = new IPEndPoint(serverIp, ClientConfig.DefaultPort);
        Socket = new Socket(AddressFamily.InterNetwork, socketType, protocolType);

        Settings = new ClientSetting();
    }
    
    protected int SendData(byte[] data, int size = 0, int microseconds = 100_000)
    {
        if (size == 0)
            size = data.Length;

        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), ClientConfig.DefaultPort);
        
        return Socket.Poll(microseconds, SelectMode.SelectWrite)
            ? Socket.SendTo(data, 0, size, SocketFlags.None, remoteEndPoint)
            : 0;
    }

    protected int GetData(byte[] buffer, int size = 0, int microseconds = 100_000)
    {
        if (!Socket.Poll(microseconds, SelectMode.SelectRead))
            return 0;
        

        return Socket.Receive(buffer, 0,
            size == 0 ? buffer.Length : size, SocketFlags.None);
    }
}