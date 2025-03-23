using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Server;

public abstract class Server
{
    protected Socket Socket { get; }

    protected List<ClientOperationContext> Clients;

    protected ServerSettings Settings { get; }
    
    
    protected Server(SocketType socketType, ProtocolType protocolType)
    {
        Socket = new Socket(AddressFamily.InterNetwork, socketType, protocolType);
        Socket.Bind(new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort));
        Socket.Listen(ServerConfig.AmountListeners);

        Clients = new List<ClientOperationContext>();
        Settings = new ServerSettings();
    }
    
    //__ Need to overriding methods __
    
    public abstract void Run();
    
    
    //__ Base methods __
    
    protected IPAddress? ConnectClient()
    {
        Socket newClient;
        newClient = Socket.Accept();
        newClient.Blocking = false;
        newClient.SendBufferSize = ServerConfig.SendBufferSize;
        newClient.ReceiveBufferSize = ServerConfig.ReceiveBufferSize;    

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var keepAliveValues = new byte[12];
            BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);  
            BitConverter.GetBytes(10_000).CopyTo(keepAliveValues, 4); //10 s
            BitConverter.GetBytes(5_000).CopyTo(keepAliveValues, 8);   //5 s
                
            newClient.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
        }
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            newClient.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            newClient.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,
                ServerConfig.KeepAliveTime);
            newClient.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,
                ServerConfig.KeepAliveInterval);
            newClient.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 
                ServerConfig.KeepAliveAttempts);
        }
            
        var clientIp = (IPEndPoint?)newClient.RemoteEndPoint;
        if(clientIp != null) 
        {
            ClientOperationContext? client = Clients.FirstOrDefault(c => c.ClientIp.Equals(clientIp.Address));

            if (client != null)
            {
                client.ClientSocket = newClient;
                client.IsConnected = true;
            }
            else
            {
                Clients.Add(new ClientOperationContext(newClient, new ServerBackup(), clientIp.Address, true));
            }
        }
        
        return clientIp?.Address;
    }
    
    private bool IsDisconnected(Socket ClientSocket) => ClientSocket.Poll(0, SelectMode.SelectRead) 
                                   && ClientSocket.Available == 0;
    
    protected int SendData(ClientOperationContext Client, byte[] data, int size = 0, int microseconds = 100_000)
    {
        if (size == 0)
            size = data.Length;

        if (IsDisconnected(Client.ClientSocket))
        {
            Client.IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return Client.ClientSocket.Poll(microseconds, SelectMode.SelectWrite)
            ? Client.ClientSocket.Send(data, size, SocketFlags.None)
            : 0;
    }

    protected int GetData(ClientOperationContext Client, byte[] buffer, int size = 0, int microseconds = 100_000)
    {
        if (!Client.ClientSocket.Poll(microseconds, SelectMode.SelectRead))
            return 0;

        if (Client.ClientSocket.Available == 0)
        {
            Client.IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return Client.ClientSocket.Receive(buffer, 0,
            size == 0 ? buffer.Length : size, SocketFlags.None);
    }
}