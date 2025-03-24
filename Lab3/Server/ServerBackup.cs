using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public record LastOpData(
        string filePath = "",
        string ipV4 = "",
        long corruptedPos = 0)
    {
        public string FilePath { get; init; } = filePath;

        public string Ipv4 { get; init; } = ipV4;

        public long CorruptedPos { get; set; } = corruptedPos;

        public bool CanRecovery(string filePath, string ipV4)
        {
            return CorruptedPos != 0
                   && FilePath.Equals(filePath)
                   && Ipv4 == ipV4;
        }
    }
    
    public class ServerBackup
    {
        internal LastOpData LastSendData { get; set; } = new();

        internal LastOpData LastReceiveData { get; set; } = new();

        public bool AnyBackup() => LastSendData.CorruptedPos !=0 || LastReceiveData.CorruptedPos != 0;
    }
}
