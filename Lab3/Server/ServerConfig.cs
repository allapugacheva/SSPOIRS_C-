using System.Runtime.InteropServices;

namespace Server
{
    public static class ServerConfig
    {
        public const int AmountListeners = 100;

        public const int DefaultPort = 8080;

        public const string DateFormat = "dd.MM.yyyy HH:mm";

        public const int ServingSize = 64 * 1024;

        public const int ReceiveBufferSize = 1024 * 1024 * 64;
        
        public const int SendBufferSize = 1024 * 1024 * 64;
        
        public const int KeepAliveTime = 10;
        public const int KeepAliveInterval = 5;  
        public const int KeepAliveAttempts = 3; 
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
