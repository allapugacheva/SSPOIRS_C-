using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public enum ClientStatusEnum : byte
    {
        Fail,
        Success,
        Error,
        LostConnection,
        ConnectionError,
        BadCommand
    }
}
