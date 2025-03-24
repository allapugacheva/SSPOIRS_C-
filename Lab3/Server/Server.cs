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
    
    public abstract void Run();
    
    protected IPAddress? ConnectClient()
    {
        var newClient = Socket.Accept();
        newClient.Blocking = false;
        newClient.SendBufferSize = ServerConfig.SendBufferSize;
        newClient.ReceiveBufferSize = ServerConfig.ReceiveBufferSize;    

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var keepAliveValues = new byte[12];
            BitConverter.GetBytes(1).CopyTo(keepAliveValues, 0);  
            BitConverter.GetBytes(10_000).CopyTo(keepAliveValues, 4); 
            BitConverter.GetBytes(5_000).CopyTo(keepAliveValues, 8);   
                
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
        var client = Clients.FirstOrDefault(c => c.ClientIp.Equals(clientIp?.Address) 
                                                && c.ClientSocket == null);

        if (client != null)
        {
            client.ClientSocket = newClient;
            client.IsConnected = true;
        }
        else
            Clients.Add(new ClientOperationContext(newClient));

        return clientIp?.Address;
    }
    
    private bool IsDisconnected(Socket clientSocket) => clientSocket.Poll(0, SelectMode.SelectRead) 
                                                            && clientSocket.Available == 0;
    
    protected int SendData(ClientOperationContext client, byte[] data, int size = 0, int microseconds = 100_000)
    {
        if (size == 0)
            size = data.Length;

        if (IsDisconnected(client.ClientSocket))
        {
            client.IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return client.ClientSocket.Poll(microseconds, SelectMode.SelectWrite)
            ? client.ClientSocket.Send(data, size, SocketFlags.None)
            : 0;
    }

    protected int GetData(ClientOperationContext client, byte[] buffer, int size = 0, int microseconds = 100_000)
    {
        if (!client.ClientSocket.Poll(microseconds, SelectMode.SelectRead))
            return 0;

        if (client.ClientSocket.Available == 0)
        {
            client.IsConnected = false;
            throw new SocketException((int)SocketError.Disconnecting);
        }

        return client.ClientSocket.Receive(buffer, 0,
            size == 0 ? buffer.Length : size, SocketFlags.None);
    }
}