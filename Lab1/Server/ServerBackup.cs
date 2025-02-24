using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    internal record LastOpData(string FilePath = "", string Ipv4 = "", 
        bool HasCorruptedData = false, long CorruptedPos = 0, double Time = 0);
    
    internal class ServerBackup
    {
        internal LastOpData LastSendData { get; set; } = new LastOpData();

        internal LastOpData LastReceiveData { get; set; } = new LastOpData();
    }
}
