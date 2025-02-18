using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Commands
{
    public class РазрабЕбаныйЛентяый : ApplicationException
    {
        public РазрабЕбаныйЛентяый(string message = "Иди работай даун") : base(message) { }
    }

    internal class EchoCommand : ServerCommand
    {
        internal override string Name => "ECHO";

        internal override bool Execute(object[]? parameters = null)
        {
            throw new NotImplementedException();
        }
    }
}
