namespace PingUtility;

class Program
{
    internal static async Task<int> Main(string[] argv)
    {
        int timeout = argv.Length != 0 ? Convert.ToInt32(argv[0]) : 1000;

        PingUtility util = new PingUtility(timeout);

        string? input;
        while (true)
        {
            Console.Write("> ");
            input = Console.ReadLine();

            if (input.Contains("ping"))
            {
                string[] hosts = input.Split(' ').Skip(1).ToArray();

                for (int i = 0; i < hosts.Length; i++)
                {
                    await Task.Run(() =>
                    {
                        var result = util.Ping(hosts[i]);
                        Console.WriteLine($"{result.Host}: {result.RoundTripTime}ms - {result.Status}");
                    });
                }
            }
            else if (input.Contains("traceroute"))
            {
                string host = input.Split(' ').Last();

                var results = util.TraceRoute(host);
                if (results.Count == 1 && results[0].Hop == 0)
                    Console.WriteLine($"{results[0].Hostname} - {results[0].Status}");
                else
                {
                    foreach (var result in results)
                    {
                        if (result.Hostname != "*")
                            Console.WriteLine($"{result.Hostname}: {result.RoundTripTime}ms");
                    }
                }
            }
            else if (input.Contains("spoof"))
            {
                string[] sourceDest = input.Split(' ').Skip(1).ToArray();

                var result = util.Ping(sourceDest[0], spoofSource: sourceDest[1]);
                Console.WriteLine(result.Status);
            }
            else if (input.Contains("q"))
                break;
        }

        return 1;
    }
}