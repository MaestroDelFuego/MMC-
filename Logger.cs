using System;
using System.IO;

public static class Logger
{
    private static readonly string LogFilePath = "logs/server.log";
    private static readonly object Lock = new();

    static Logger()
    {
        Directory.CreateDirectory("logs");
        File.WriteAllText(LogFilePath, "=== Server Log Started ===\n");
    }

    public static void Log(string message)
    {
        string timestamp = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]";
        string fullMessage = $"{timestamp} {message}";

        Console.WriteLine(fullMessage);
        lock (Lock)
        {
            File.AppendAllText(LogFilePath, fullMessage + Environment.NewLine);
        }
    }

    public static void Error(string message)
    {
        Log($"[ERROR] {message}");
    }
}
