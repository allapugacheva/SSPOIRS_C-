using System.Collections;

namespace Server
{
    internal class CommandList(int capacity)
    {
        private readonly List<string> _commands = [];

        private readonly int _capacity = capacity;

        private int _currentCommand = 0;


        public void Add(string command) 
        {
            if(_commands.Count == _capacity)
                _commands.RemoveAt(0);

            _commands.Add(command);
        }

        public string GetNext() => _currentCommand == _capacity || _currentCommand == _commands.Count ? _commands[0] : _commands[_currentCommand++];

        public string GetPrev() => _currentCommand == 0 ? _commands[--_currentCommand] : string.Empty;

        public void Reset() => _currentCommand = 0;
    }
}
