using Server.Commands;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server
{
    internal class Server
    {
        private Socket? _socket;

        private Socket? _clientSocket;

        private readonly Dictionary<string, ServerCommand> _commands;

        private readonly ServerSettings _settings;

        internal Server() 
        {
            StartUp();

            _commands = new Dictionary<string, ServerCommand>()
                {
                    { "ECHO", new EchoCommand() },
                    { "TIME", new TimeCommand() }
                };

            _settings = new ServerSettings();
        }

        internal ServerStatusEnum StartUp()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, ServerConfig.DefaultPort));

            _socket.Listen(ServerConfig.AmountListeners);

            return ServerStatusEnum.Success;
        }

        internal ServerStatusEnum Run()
        {
            if(_socket == null)
                return ServerStatusEnum.Fail;

            Console.Write($"{Colors.GREEN}Server is start.{Colors.RESET}\n> ");

            var commandString = new StringBuilder(50);
            while(true)
            {
                if(_socket.Poll(0, SelectMode.SelectRead))
                {
                    _clientSocket = _socket.Accept();
                    var clientIp = (IPEndPoint?)_clientSocket.RemoteEndPoint;
                    var ipString = clientIp?.Address.ToString() ?? "Undefinded";
                    Console.Write($"The client is connected. Ip: {Colors.GREEN}{ipString}{Colors.RESET}\n> ");
                }

                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    if(keyInfo.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        var commandParts = commandString.ToString().Split(' ');
                        var commandName = commandParts[0];
                        var commandValue = commandParts.Length > 1 ? commandParts[1] : null;

                        if (_commands.TryGetValue(commandName, out var serverCommand))
                        {
                            serverCommand.Execute(commandValue);
                        }
                        else if(commandName.StartsWith("SETTING"))
                        {
                            if(commandName.Contains(".path"))
                                _settings.SetDir(commandName);
                            else
                                Console.WriteLine(_settings);
                        }
                        commandString.Clear();
                        Console.Write("> ");
                    } 
                    else if(keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if(commandString.Length > 0)
                        {
                            commandString.Remove(commandString.Length - 1, 1);
                            Console.Write("\b \b");
                        }
                    }
                    else
                    {
                        commandString.Append(keyInfo.KeyChar);
                        Console.Write(keyInfo.KeyChar);
                    }          
                }
            }
        }
    }
}
