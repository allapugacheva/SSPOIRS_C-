namespace Server;

class Program
{
    internal static int Main()
    {
        var server = new TcpServer();  
        server.Run();

        return 1;
    }
}