using System.Net;
using System.Runtime.InteropServices;


namespace Client
{
    public static class ClientConfig
    {
        public static IPAddress DefaultIp = IPAddress.Loopback;

        public static int DefaultPort = 8080;

        public const int ServingSize = 8 * 1024;
        
        public const int ReceiveBufferSize = 1024 * 1024 * 32;
        
        public const int SendBufferSize = 1024 * 1024 * 32;
        
        public const string DateFormat = "dd.MM.yyyy HH:mm";
        
        public const int KeepAliveInterval = 1000;
        public const int KeepAliveTimeout = 1000;
        public const int KeepAliveRetryCount = 10;

        public const int WindowSize = 60;
        
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
