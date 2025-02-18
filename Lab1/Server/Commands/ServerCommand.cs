using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Commands
{
    internal abstract class ServerCommand
    {
        internal abstract string Name { get; }

        internal abstract bool Execute(object[]? parameters = null);

        internal bool IsContain(string name)
        {
            return Name.Equals(name);
        }
    }
}
