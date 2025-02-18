using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Commands
{
    internal class TimeCommand : ServerCommand
    {
        internal override string Name => "TIME";

        internal override bool Execute(object[]? parameters = null)
        {
            Console.WriteLine($"Current time: {Colors.BLUE}{DateTime.Now.ToString(ServerConfig.DateFormat)}{Colors.RESET}");
            return true;
        }
    }
}
