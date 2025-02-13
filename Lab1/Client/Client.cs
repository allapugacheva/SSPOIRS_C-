using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Serilog;

namespace Client
{
    internal class Client
    {
        private Socket? _socket;

        private readonly IPAddress _serverAddress;

        private string _lastSentFile = string.Empty;

        private string _lastReveiveFile = string.Empty;


        internal Client(IPAddress serverIp)
        {
            _serverAddress = serverIp;
            StartUp();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug() 
                .WriteTo.File("logs/log-.txt",
                    rollingInterval: RollingInterval.Day) 
                .CreateLogger();
        }

        internal ClientStatusEnum StartUp()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            return ClientStatusEnum.Success;
        }

        internal ClientStatusEnum Run()
        {
            if (_socket == null)
                return ClientStatusEnum.Fail;

            try
            {
                _socket.Connect(new IPEndPoint(_serverAddress, ClientConfig.DefaultPort));
            }
            catch (SocketException e) 
            {
                Console.WriteLine($"{Colors.RED}Check logs.{Colors.RESET}");
                Log.Error(e.Message);
                return ClientStatusEnum.Fail;
            }

            Console.WriteLine($"{Colors.GREEN}Client start.{Colors.RESET}");
            while (true) { }

            return ClientStatusEnum.Success;
        }
    }
}
