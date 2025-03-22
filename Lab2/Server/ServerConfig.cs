using System.Runtime.InteropServices;

namespace Server
{
    public static class ServerConfig
    {
        public const int AmountListeners = 1;

        public const int DefaultPort = 8080;

        public const string DateFormat = "dd.MM.yyyy HH:mm";

        public const int ServingSize = 1024 * 8;

        public const int ReceiveBufferSize = 1024 * 1024 * 10;
        
        public const int SendBufferSize = 1024 * 1024 * 10;
        
        public const int KeepAliveInterval = 1000;
        public const int KeepAliveTimeout = 1000;
        public const int KeepAliveRetryCount = 10;
        
        public const int WindowSize = 60;
        
        public static int MaxFileNameLength
        {
            get 
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return 260 * 2;

                return 255 * 2;
            }
        }
    }
}
