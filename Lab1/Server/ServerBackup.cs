using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    internal class ServerBackup
    {
        internal string LastSentFilePath {  get; set; } = string.Empty;

        internal string LastReceivedFilePath { get; set; } = string.Empty;

        internal bool IsDisconnected { get; set; }

        internal bool HasCorruptedData { get; set; }

        internal long CorruptedPos { get; set; }

        internal IPAddress? ClientIp { get; set; }
    }
}
