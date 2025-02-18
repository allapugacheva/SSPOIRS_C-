namespace Client
{
    internal class FileLoadingLine
    {
        public const byte BaseAmountStages = 20;

        private byte _amountStages;

        public byte AmountStages 
        {
            get => _amountStages;
            set
            {
                if (value <= 0)
                    _amountStages = BaseAmountStages;
                else 
                    _amountStages = value;
            }
        }

        private byte _amountInputStages;

        private long _fileSize;

        private double _currentPercent;

        public double PercentDif;

        public FileLoadingLine(long fileSize)
        {
            _fileSize = fileSize;
            _currentPercent = 0;
            _amountInputStages = 0;
            PercentDif = 1.0;
            AmountStages = BaseAmountStages;
        }

        public void Report(long amountProcessedBytes, double second = 0)
        {
            double newPercent = (double)amountProcessedBytes / _fileSize;
            double percentDifferent = (newPercent - _currentPercent) * 100.0;

            if (percentDifferent < PercentDif)
                return;

            _amountInputStages += (byte)((newPercent * AmountStages) - _amountInputStages);

            Console.CursorVisible = false;
            Console.Write("\r[");
            for (int i = 1; i <= _amountInputStages; i++)
            {
                var color = GetColor((double)i / AmountStages * 100.0);
                Console.Write($"{color}#{Colors.RESET}");
            }
            for (int i = 0; i < AmountStages - _amountInputStages; i++)
            {
                Console.Write(" ");
            }
            Console.Write($"] {newPercent * 100.0:###0.00}");
            if (second != 0)
                Console.Write($" {GetSpeed(amountProcessedBytes, second)}     ");

            Console.CursorVisible = true;
            _currentPercent = newPercent;
        }

        string GetColor(double percent)
        {
            if (percent >= 67.0)
                return Colors.GREEN;
            if (percent >= 34.0)
                return Colors.YELLOW;

            return Colors.RED;
        }

        public static string GetSpeed(long processedByte, double second)
        {
            var bytePerSecond = processedByte / second;
            if (bytePerSecond < 100)
                return $"{bytePerSecond:F2}Б/c";

            bytePerSecond /= 1024;
            if(bytePerSecond < 100)
                return $"{bytePerSecond:F2}КБ/c";

            bytePerSecond /= 1024;
            if (bytePerSecond < 100)
                return $"{bytePerSecond:F2}МБ/c";

            bytePerSecond /= 1024;
            if (bytePerSecond < 100)
                return $"{bytePerSecond:F2}ГБ/c";

            bytePerSecond /= 1024;
            if (bytePerSecond < 100)
                return $"{bytePerSecond:F2}ТБ/c";

            return "Fast";
        }
    }
}
