namespace Server;

class Program
{
    internal static int Main()
    {
        var server = new Server();  
        server.Run();

        return 1;
    }
}