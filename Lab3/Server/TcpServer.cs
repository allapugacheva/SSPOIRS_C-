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
            Console.WriteLine($"{Colors.GREEN}Server is start.{Colors.RESET}");

            while (true)
            {

                if (Socket.Poll(0, SelectMode.SelectRead))
                {
                    IPAddress? ip;
                    if ((ip = ConnectClient()) != null)
                        Console.WriteLine($"Client {Colors.YELLOW}{ip.ToString()} {Colors.GREEN}connected{Colors.RESET}.");
                }

                for (int i = 0; i < Clients.Count; i++) {
                
                    if (Clients[i].CanMakeStep())
                        MakeStep(Clients[i]);
                    
                    if (Clients[i].IsDisconnected())
                    {
                        Console.WriteLine($"Client {Colors.YELLOW}{Clients[i].ClientIp} {Colors.RED}disconnected{Colors.RESET}.");
                        if (Clients[i].Backup.AnyBackup())
                        {
                            Clients[i].ClientSocket.Close();                       
                            Clients[i].ClientSocket = null;
                        }
                        else
                            Clients.RemoveAt(i--);
                    }
                }
            }
        }

        private void MakeStep(ClientOperationContext client) 
        {
            try 
            {
                if (client.Step == 0)
                    ParseCommand(client);
                else if (client.Step == 1)
                    GetFileSize(client);
                else if (client.Step == 2)
                    GetFilePart(client);
                else if (client.Step == 3)
                    SendFileSize(client);
                else if (client.Step == 4)
                    SendFilePart(client);
                else if (client.Step == 5)
                    ExecuteTime(client);
                else if (client.Step == 6)
                    ExecuteEcho(client);
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                if (client.Step != 0) 
                {
                    if (client.Step == 2)
                        client.Backup.LastReceiveData.CorruptedPos = client.BytesRead;
                    else if (client.Step == 4)
                        client.Backup.LastSendData.CorruptedPos = client.BytesRead;
                    
                    client.Step = 0;
                    client.File?.Close();
                    client.IsConnected = false;

                    Console.Write($"File operation {Colors.RED}failed{Colors.RESET} for {Colors.YELLOW}{client.ClientIp}{Colors.RESET}\n> ");
                }
            }
        }

        private void ParseCommand(ClientOperationContext client) 
        {
            var commandLengthBytes = new byte[sizeof(int)];
            while (GetData(client, commandLengthBytes, sizeof(int)) == 0);

            var commandLength = BitConverter.ToInt32(commandLengthBytes);
            var commandBytes = new byte[commandLength];
            while (GetData(client, commandBytes, commandLength) == 0);

            client.Command = Encoding.Unicode.GetString(commandBytes);
            var parameters = client.Command.Split(' ').Skip(1).ToArray();

            if (client.Command.StartsWith("UPLOAD") && parameters.Length != 0)
            {
                client.FilePath = Path.GetFileName(parameters[0]);
                client.Step = 1;
            }
            else if (client.Command.StartsWith("DOWNLOAD") && parameters.Length != 0)
            {
                client.FilePath = Path.GetFileName(parameters[0]);
                client.Step = 3;
            }
            else if (client.Command.StartsWith("TIME"))
                client.Step = 5;
            else if (client.Command.StartsWith("ECHO") && parameters.Length != 0)
                client.Step = 6;
        }

        private void GetFileSize(ClientOperationContext client) 
        {
            client.File = null;
            var fileSizeBytes = new byte[sizeof(long)];
            while (GetData(client, fileSizeBytes, sizeof(long)) == 0); 
            
            client.FileSize = BitConverter.ToInt64(fileSizeBytes);

            client.StartPos = 0;
            if (client.Backup.LastReceiveData.CanRecovery(client.FilePath, client.ClientIp))
            {
                client.File = new FileStream(client.FilePath, FileMode.Open, FileAccess.Write);
                client.StartPos = client.Backup.LastReceiveData.CorruptedPos;
                client.File.Seek(client.StartPos, SeekOrigin.Begin);
            }
            else
                client.File = new FileStream(client.FilePath, FileMode.Create, FileAccess.Write);
            
            client.Backup.LastReceiveData = new LastOpData(client.FilePath, client.ClientIp);
            
            while (SendData(client, BitConverter.GetBytes(client.StartPos), sizeof(long)) == 0);
            
            client.Step = 2;
            client.BytesRead = client.StartPos;
        }

        private void GetFilePart(ClientOperationContext client)
        {
            int bytePortion;
            var buffer = new byte[ServerConfig.ServingSize];
            while ((bytePortion = GetData(client, buffer, ServerConfig.ServingSize)) == 0);

            client.File?.Write(buffer, 0, bytePortion);
            client.BytesRead += bytePortion;

            if (client.BytesRead >= client.FileSize) 
            {
                client.Step = 0;
                client.File?.Close();
                Console.WriteLine($"File {client.FilePath} {Colors.GREEN}successfully{Colors.RESET} transferred for {Colors.YELLOW}{client.ClientIp}{Colors.RESET}");
            }
        }

        private void SendFileSize(ClientOperationContext client)
        {
            client.File = null;
            client.StartPos = 0;
            if (!Path.Exists(client.FilePath))
                client.FileSize = client.StartPos = -1;
            else
            {
                client.File = new FileStream(client.FilePath, FileMode.Open, FileAccess.Read);

                if (client.Backup.LastSendData.CanRecovery(client.FilePath, client.ClientIp))
                {
                    client.StartPos = client.Backup.LastSendData.CorruptedPos;
                    client.File.Seek(client.StartPos, SeekOrigin.Begin);
                }
                client.FileSize = client.File.Length;
            }
            
            client.Backup.LastSendData = new LastOpData(client.FilePath, client.ClientIp);
            
            var fileSizeBytes = BitConverter.GetBytes(client.FileSize);
            var startPosBytes = BitConverter.GetBytes(client.StartPos);
            while (SendData(client, fileSizeBytes.Concat(startPosBytes).ToArray(), 2 * sizeof(long)) == 0);
            
            if ((client.FileSize == -1 && client.StartPos == -1) || client.File == null) 
            {
                client.Step = 0;
                Console.WriteLine($"File operation {Colors.RED}failed{Colors.RESET} for {Colors.YELLOW}{client.ClientIp}{Colors.RESET}");
                return;
            }

            client.Step = 4;
            client.BytesRead = client.StartPos;
        }

        private void SendFilePart(ClientOperationContext client)
        {
            int bytePortion;
            var buffer = new byte[ServerConfig.ServingSize];
            if ((bytePortion = client.File.Read(buffer)) != 0) 
            {
                while (SendData(client, buffer, bytePortion) == 0);
                client.BytesRead += bytePortion;
            }
            
            if (client.BytesRead >= client.FileSize || bytePortion == 0) 
            {
                client.Step = 0;
                client.File.Close();
                Console.WriteLine($"File {client.FilePath} {Colors.GREEN}successfully{Colors.RESET} transferred for {Colors.YELLOW}{client.ClientIp}{Colors.RESET}");
            }
        }

        private void ExecuteTime(ClientOperationContext client)
        {
            var buffer = Encoding.Unicode.GetBytes(DateTime.UtcNow.ToString(ServerConfig.DateFormat));
            var bufferBytes = BitConverter.GetBytes(buffer.Length).Concat(buffer).ToArray();
            while (SendData(client, bufferBytes) == 0);
            client.Step = 0;
        }

        private void ExecuteEcho(ClientOperationContext client)
        {
            var buffer = Encoding.Unicode.GetBytes(client.Command.Substring(client.Command.IndexOf(' ') + 1));
            var bufferBytes = BitConverter.GetBytes(buffer.Length).Concat(buffer).ToArray();
            while (SendData(client, bufferBytes) == 0);
            client.Step = 0;
        }
    }
}