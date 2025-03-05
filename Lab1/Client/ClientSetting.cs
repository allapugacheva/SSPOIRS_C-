using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public class ClientSetting
    {
        public string CurrentDirectory { get; private set; } = Directory.GetCurrentDirectory();

        public bool SetDir(string? dir = null)
        {
            if (string.IsNullOrEmpty(dir))
                CurrentDirectory = Directory.GetCurrentDirectory();
            else if (Directory.Exists(dir))
                CurrentDirectory = dir;
            else
                return false;

            return true;
        }

        public override string ToString()
        {
            return string.Format($"DIRECOTIRY: {CurrentDirectory}");
        }
    }
}
