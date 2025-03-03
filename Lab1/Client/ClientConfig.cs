using System.Net;
using System.Runtime.InteropServices;


namespace Client
{
    internal static class ClientConfig
    {
        internal static IPAddress DefaultIp = IPAddress.Loopback;

        internal static int DefaultPort = 8080;

        internal const int ServingSize = 64 * 1024;
        
        internal const int KeepAliveTime = 10;
        internal const int KeepAliveInterval = 5;  
        internal const int KeepAliveAttempts = 3; 

        internal static int MaxFileSize
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return 260;

                return 255;
            }
        }
    }
}
