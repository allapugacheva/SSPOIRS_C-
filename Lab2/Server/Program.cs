namespace Server;

class Program
{
    internal static async Task<int> Main()
    {
        var server = new UdpServer();  
        await server.Run();

        return 1;
    }
}