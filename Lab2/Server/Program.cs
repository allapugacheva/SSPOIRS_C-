namespace Server;

class Program
{
    internal static int Main()
    {
        var server = new UdpServer();  
        server.Run();

        return 1;
    }
}