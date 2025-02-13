using Client;
using System.Net;

namespace Client;

class Program
{
    internal static int Main(string[] argv)
    {
        IPAddress ip = ClientConfig.DefaultIp;
        if(argv.Length != 0)
            ip = IPAddress.Parse(argv[0]);

        var client = new Client(ip);
        client.Run();

        return 1;
    }
}