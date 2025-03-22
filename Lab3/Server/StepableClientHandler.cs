using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Server {

    public class ClientOperationContext {

        public Socket? ClientSocket { get; set; }

        public ServerBackup Backup { get; }

        public IPAddress ClientIp { get; set; }

        public bool IsConnected { get; set; }

        public int Step { get; set; }

        public string? Command { get; set; }

        public string FilePath { get; set; }

        public long StartPos { get; set; }

        public long FileSize { get; set; }

        public long BytesRead { get; set; }

        public FileStream? File { get; set; }

        public ClientOperationContext(Socket client, ServerBackup backup, IPAddress clientIp, bool isConnected) {

            ClientSocket = client;
            Backup = backup;
            ClientIp = clientIp;
            IsConnected = isConnected;
            Step = 0;
            FilePath = "";
        }
    }
}