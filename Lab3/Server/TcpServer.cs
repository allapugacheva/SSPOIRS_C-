using Client;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server
{
    internal class TcpServer : Server
    {

        internal TcpServer() : base(SocketType.Stream, ProtocolType.Tcp) { }

        public override void Run()
        {
            Console.Write($"{Colors.GREEN}Server is start.{Colors.RESET}\n> ");

            while (true)
            {

                if (Socket.Poll(0, SelectMode.SelectRead))
                {
                    if (ConnectClient() != null)
                        Console.Write($"Client connected. Ip: {Colors.GREEN}{Clients[^1].ClientIp}{Colors.RESET}\n> ");
                }
                
                for (int i = 0; i < Clients.Count; i++) {
                
                    if (Clients[i].IsConnected) 
                    {
                        MakeStep(Clients[i]);
                    }
                    
                    if(!Clients[i].IsConnected && Clients[i].ClientSocket != null)
                    {
                        Console.Write($"Client disconnected. Ip: {Colors.RED}{Clients[i].ClientIp}{Colors.RESET}\n> ");
                        Clients[i].ClientSocket = null;
                    }
                }
            }
        }

        private void MakeStep(ClientOperationContext Client) {

            try {
                switch (Client.Step)
                {
                    case 0: {

                        var commandLengthBytes = new byte[sizeof(int)];
                        if (GetData(Client, commandLengthBytes, sizeof(int), 15_000_000) != sizeof(int))
                            return;

                        var commandLength = BitConverter.ToInt32(commandLengthBytes);
                        var commandBytes = new byte[commandLength];
                        if (GetData(Client, commandBytes, commandLength, 15_000_000) != commandLength)
                            return;

                        Client.Command = Encoding.Unicode.GetString(commandBytes);

                        var parameters = Client.Command.Split(' ').Skip(1).ToArray();
                        if (parameters.Length == 0 && !Client.Command.Equals("TIME"))
                            return;

                        if (Client.Command.StartsWith("UPLOAD") || Client.Command.StartsWith("DOWNLOAD"))
                            Client.FilePath = Path.GetFileName(parameters[0]);

                        if (Client.Command.StartsWith("UPLOAD"))
                            Client.Step = 1;
                        else if (Client.Command.StartsWith("DOWNLOAD"))
                            Client.Step = 3;
                        else if (Client.Command.Equals("TIME"))
                            Client.Step = 5;
                        else if (Client.Command.StartsWith("ECHO"))
                            Client.Step = 6;

                        break;
                    }
                    case 1: {
                        
                        Client.File = null;
                        var fileSizeBytes = new byte[sizeof(long)];
                        while (GetData(Client, fileSizeBytes, sizeof(long)) != sizeof(long)); 
                        
                        Client.FileSize = BitConverter.ToInt64(fileSizeBytes);

                        Client.StartPos = 0;
                        if (Client.Backup.LastReceiveData.CanRecovery(Client.FilePath, Client.ClientIp.ToString()))
                        {
                            Client.File = new FileStream(Client.FilePath, FileMode.Open, FileAccess.Write);
                            Client.StartPos = Client.Backup.LastReceiveData.CorruptedPos;
                            Client.File.Seek(Client.StartPos, SeekOrigin.Begin);
                        }
                        else
                        {
                            Client.File = new FileStream(Client.FilePath, FileMode.Create, FileAccess.Write);
                        }
                        
                        Client.Backup.LastReceiveData = new LastOpData(Client.FilePath, Client.ClientIp.ToString(), 0);
                        
                        SendData(Client, BitConverter.GetBytes(Client.StartPos), sizeof(long));
                        
                        Client.Step = 2;
                        Client.BytesRead = Client.StartPos;

                        break;
                    }
                    case 2: {

                        int bytePortion;
                        var buffer = new byte[ServerConfig.ServingSize];
                        if ((bytePortion = GetData(Client, buffer, ServerConfig.ServingSize, 500_000)) != 0)
                        {
                            Client.File?.Write(buffer, 0, bytePortion);
                            Client.BytesRead += bytePortion;
                        }
                        if (Client.BytesRead >= Client.FileSize) {
                            Client.Step = 0;
                            Client.File?.Close();
                            Console.Write($"{Colors.BLUE}File successfully transferred for {Colors.GREEN}{Client.ClientIp}{Colors.RESET}\n> ");
                        }

                        break;
                    }
                    case 3: {

                        Client.File = null;
                        Client.StartPos = 0;
                        if (!Path.Exists(Client.FilePath))
                            Client.FileSize = Client.StartPos = -1;
                        else
                        {
                            Client.File = new FileStream(Client.FilePath, FileMode.Open, FileAccess.Read);

                            if (Client.Backup.LastSendData.CanRecovery(Client.FilePath, Client.ClientIp.ToString()))
                            {
                                Client.StartPos = Client.Backup.LastSendData.CorruptedPos;
                                Client.File.Seek(Client.StartPos, SeekOrigin.Begin);
                            }
                            Client.FileSize = Client.File.Length;
                        }
                        
                        Client.Backup.LastSendData = new LastOpData(Client.FilePath, Client.ClientIp.ToString(), 0);
                        
                        var fileSizeBytes = BitConverter.GetBytes(Client.FileSize); var startPosBytes = BitConverter.GetBytes(Client.StartPos);
                        while (SendData(Client, fileSizeBytes.Concat(startPosBytes).ToArray(), 2 * sizeof(long)) == 0);
                        
                        if((Client.FileSize == -1 && Client.StartPos == -1) || Client.File == null) {
                            Client.Step = 0;
                            Console.Write($"{Colors.RED}File operation failed for {Colors.GREEN}{Client.ClientIp}{Colors.RESET}\n> ");
                            return;
                        }

                        Client.Step = 4;
                        Client.BytesRead = Client.StartPos;

                        break;

                    }
                    case 4: {

                        int bytePortion;
                        var buffer = new byte[ServerConfig.ServingSize];
                        if ((bytePortion = Client.File.Read(buffer)) != 0) 
                        {
                            while (SendData(Client, buffer, bytePortion, 500_000) == 0);
                            
                            Client.BytesRead += bytePortion;
                        }
                        
                        if (Client.BytesRead >= Client.FileSize || bytePortion == 0) {
                            Client.Step = 0;
                            Client.File.Close();
                            Console.Write($"{Colors.BLUE}File successfully transferred for {Colors.GREEN}{Client.ClientIp}{Colors.RESET}\n> ");
                        }

                        break;                 
                    }
                    case 5: {

                        var buffer = Encoding.Unicode.GetBytes(DateTime.UtcNow.ToString(ServerConfig.DateFormat));
                        var bufferBytes = BitConverter.GetBytes(buffer.Length).Concat(buffer).ToArray();
                        while (SendData(Client, bufferBytes) == 0);
                        Client.Step = 0;

                        break;
                    }
                    case 6: {

                        var buffer = Encoding.Unicode.GetBytes(Client.Command.Substring(Client.Command.IndexOf(' ') + 1));
                        var bufferBytes = BitConverter.GetBytes(buffer.Length).Concat(buffer).ToArray();
                        while (SendData(Client, bufferBytes) == 0);
                        Client.Step = 0;

                        break;
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                if (Client.Step != 0) 
                {
                    if (Client.Step == 2)
                        Client.Backup.LastReceiveData.CorruptedPos = Client.BytesRead;
                    else if (Client.Step == 4)
                        Client.Backup.LastSendData.CorruptedPos = Client.BytesRead;
                    
                    Client.Step = 0;
                    Client.File?.Close();
                    Client.IsConnected = false;

                    Console.Write($"{Colors.RED}File operation failed for {Colors.GREEN}{Client.ClientIp}{Colors.RESET}\n> ");
                }
            }
        }
    }
}