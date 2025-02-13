using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    internal enum ServerStatusEnum : byte
    {
        Fail,
        Success,
        Error,
        LostConnection
    }
}
