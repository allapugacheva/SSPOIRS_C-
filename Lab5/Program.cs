using System.Net;

namespace PingUtility;

class Program
{
    internal static async Task<int> Main(string[] argv)
    {

        // Traceroute
        // string host = "google.com";
        // var results = await PingUtility.TraceRoute(host);
        // foreach (var result in results)
        // {
        //     if (result.Hostname != "*")
        //         Console.WriteLine($"{result.Hostname}: {result.RoundTripTime}ms");
        // }

        // Single ping
        // string host = "google.com";
        // var result = await PingUtility.Ping(host);
        // Console.WriteLine($"{result.Host}: {result.RoundTripTime}ms - {result.Status}");

        // Multiple ping
        // string[] hosts = { "google.com", "yandex.ru", "github.com", "stackoverflow.com", "fuck.by", "127.0.0.1" };

        // for (int i = 0; i < hosts.Length; i++)
        // {
        //     await Task.Run(async () =>
        //     {
        //         var result = await PingUtility.Ping(hosts[i]);
        //         Console.WriteLine($"{result.Host}: {result.RoundTripTime}ms - {result.Status}");
        //     });
        // }

        // Smurfiki
        string host = "192.168.1.255";
        var result = await PingUtility.Ping(host, 1000, 0, IPAddress.Parse("192.168.1.72"));
        Console.WriteLine(result.Status);

        return 1;
    }
}