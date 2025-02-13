using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Commands
{
    internal class EchoCommand : ServerCommand
    {
        internal override string Name => "ECHO";

        internal override bool Execute(string? parameters = null)
        {
            throw new NotImplementedException();
        }
    }
}
