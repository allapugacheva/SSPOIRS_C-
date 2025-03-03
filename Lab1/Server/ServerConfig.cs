using System.Runtime.InteropServices;

namespace Server
{
    internal static class ServerConfig
    {
        internal static int AmountListeners = 1;

        internal static int DefaultPort = 8080;

        internal static string DateFormat = "dd.MM.yyyy HH:mm";

        internal const int ServingSize = 64 * 1024;

        internal const int MaxClientCommnadLength = 9 * 2;

        internal const int RecoveryTryTIme = 5;

        internal const int CommandCapacity = 10;
        
        internal const int KeepAliveTime = 10;
        internal const int KeepAliveInterval = 5;  
        internal const int KeepAliveAttempts = 3; 
        internal static int MaxFileNameLength
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
