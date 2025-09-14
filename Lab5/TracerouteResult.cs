using System.Net;
using System.Net.NetworkInformation;

namespace PingUtility;

public class TracerouteResult
{
    public int Hop { get; set; }
    public IPAddress Address { get; set; }
    public string Hostname { get; set; }
    public long RoundTripTime { get; set; }
    public IPStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}