using System.IO;

namespace Server
{
    public class ServerSettings
    {
        public string CurrentDirectory { get; private set; } = Directory.GetCurrentDirectory();

        public bool SetDir(string? dir = null)
        {
            if(string.IsNullOrEmpty(dir))
                CurrentDirectory = Directory.GetCurrentDirectory();
            else if(Directory.Exists(dir))
                CurrentDirectory = dir;
            else
                return false;

            return true;
        }

        public override string ToString()
        {
            return string.Format($"DIRECTORY: {CurrentDirectory}");
        }
    }
}
