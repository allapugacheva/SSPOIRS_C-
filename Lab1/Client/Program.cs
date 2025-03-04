using Client;
using System.Net;

namespace Client;

class Program
{
    internal static int Main(string[] argv)
    {
        var ip = ClientConfig.DefaultIp;
        if(argv.Length != 0)
            ip = IPAddress.Parse(argv[0]);

        var client = new TcpClient(ip);
        var status = client.Run();
        if(status == ClientStatusEnum.ConnectionError)
            Console.WriteLine($"{Colors.RED}Couldn't connect to the server{Colors.RESET}");
        else if(status == ClientStatusEnum.LostConnection)
            Console.WriteLine($"{Colors.RED}Lost connection with server{Colors.RESET}");
        
        Console.ReadLine();
        return 1;
    }
}