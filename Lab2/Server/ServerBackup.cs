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
        bool hasCorruptedData = false,
        long corruptedPos = 0,
        double time = 0)
    {
        public string FilePath { get; init; } = filePath;

        public string Ipv4 { get; init; } = ipV4;
        
        public bool HasCorruptedData { get; init; } = hasCorruptedData;

        public long CorruptedPos { get; init; } = corruptedPos;

        public double Time { get; init; } = time;

        public bool CanRecovery(string filePath, string ipV4)
        {
            return HasCorruptedData
                   && FilePath.Equals(filePath)
                   && Ipv4 == ipV4;
        }
    }
    
    public class ServerBackup
    {
        internal LastOpData LastSendData { get; set; } = new();

        internal LastOpData LastReceiveData { get; set; } = new();
    }
}
