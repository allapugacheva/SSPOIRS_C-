using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    internal static class ClientConfig
    {
        internal static IPAddress DefaultIp = IPAddress.Loopback;

        internal static int DefaultPort = 8000;
    }
}
