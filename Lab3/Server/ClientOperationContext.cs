using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Server {

    public class ClientOperationContext {

        public Socket? ClientSocket { get; set; }

        public ServerBackup Backup { get; }

        public string ClientIp { get; private set; }

        public bool IsConnected { get; set; }

        public int Step { get; set; }

        public string? Command { get; set; }

        public string FilePath { get; set; }

        public long StartPos { get; set; }

        public long FileSize { get; set; }

        public long BytesRead { get; set; }

        public FileStream? File { get; set; }

        public ClientOperationContext(Socket client) {

            ClientSocket = client;
            Backup = new ServerBackup();
            ClientIp = client.RemoteEndPoint.ToString().Split(':')[0];
            IsConnected = true;
            Step = 0;
            FilePath = "";
        }

        public bool CanMakeStep() => IsConnected 
                    && ((Step == 0 && ClientSocket.Poll(0, SelectMode.SelectRead)) || Step > 0);

        public bool IsDisconnected() => !IsConnected && ClientSocket != null;
    }
}