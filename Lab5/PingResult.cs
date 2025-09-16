using System.Net;
using System.Net.NetworkInformation;

namespace PingUtility;

public class PingResult
{
    public string Host { get; set; }
    public IPAddress Address { get; set; }
    public double RoundTripTime { get; set; }
    public IPStatus Status { get; set; }
}