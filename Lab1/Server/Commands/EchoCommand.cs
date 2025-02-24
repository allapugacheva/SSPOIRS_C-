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

        internal override bool Execute(object[]? parameters = null)
        {
            if (parameters != null && parameters[0] is string msg)
            {
                Console.Write($"{msg}\n> ");
            }

            return true;
        }
    }
}
