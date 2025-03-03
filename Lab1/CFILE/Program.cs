using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        long fileSizeInBytes = 10L * 1024 * 1024 * 1024; // 10 ГБ
        if(args.Length == 1)
            fileSizeInBytes = long.Parse(args[0]) * 1024 * 1024;

        string filePath = "test.bin";

        byte[] buffer = new byte[1024 * 1024]; // 1 МБ

        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                long totalWritten = 0;

                while (totalWritten < fileSizeInBytes)
                {
                    fs.Write(buffer, 0, buffer.Length);
                    totalWritten += buffer.Length;
                    Console.WriteLine($"Записано {totalWritten / (1024 * 1024)} МБ из {fileSizeInBytes / (1024 * 1024)} МБ...");
                }
            }

            Console.WriteLine("Файл успешно создан!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Произошла ошибка: {ex.Message}");
        }
    }
}