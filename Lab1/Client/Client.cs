using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Client;

public abstract class Client 
{
    protected Socket Socket { get; }

    protected ClientSetting Settings { get; }

    protected IPAddress? ServerAddress { get; }

    protected bool IsConnected { get; set; }
    
    protected bool IsDisconnected => Socket.Poll(0, SelectMode.SelectRead) 
                                   && Socket.Available == 0;

    
    protected Client(IPAddress serverIp, SocketType socketType, ProtocolType protocolType)
    {
        ServerAddress = serverIp;
        Socket = new Socket(AddressFamily.InterNetwork, socketType, protocolType);

        Settings = new ClientSetting();
    }
    
    
    protected bool TryConnect()
    {
        try
        {
            Socket.Connect(new IPEndPoint(ServerAddress, ClientConfig.DefaultPort));
            IsConnected = true;
            Socket.Blocking = false;
            Socket.SendBufferSize = ClientConfig.SendBufferSize;
            Socket.ReceiveBufferSize = ClientConfig.ReceiveBufferSize;
                
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var keepAliveValues = new byte[12];
                BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);  
                BitConverter.GetBytes(10_000).CopyTo(keepAliveValues, 4); //10 s
                BitConverter.GetBytes(5_000).CopyTo(keepAliveValues, 8);   //5 s
                
                Socket.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
            }
            else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,
                    ClientConfig.KeepAliveTime);
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,
                    ClientConfig.KeepAliveInterval);
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 
                    ClientConfig.KeepAliveAttempts);
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
    
    public abstract ClientStatusEnum Run();

    protected abstract ClientStatusEnum ReceiveFile(string filePath);
    
    protected abstract ClientStatusEnum SendFile(string filePath);
}