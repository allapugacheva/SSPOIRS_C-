using System.Net;
using System.Runtime.InteropServices;


namespace Client
{
    public static class ClientConfig
    {
        public static IPAddress DefaultIp = IPAddress.Loopback;

        public static int DefaultPort = 8080;

        public const int ServingSize = 8 * 1024;
        
        public const int ReceiveBufferSize = 1024 * 1024 * 10;
        
        public const int SendBufferSize = 1024 * 1024 * 10;
        
        public const int KeepAliveTime = 10;
        public const int KeepAliveInterval = 5;  
        public const int KeepAliveAttempts = 3;

        public const int WindowSize = 50;
        
        public static int MaxFileSize
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
