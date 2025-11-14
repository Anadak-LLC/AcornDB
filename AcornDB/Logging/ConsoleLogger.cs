using System;

namespace AcornDB.Logging
{
    /// <summary>
    /// Console-based logger implementation
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        public void Info(string message)
        {
            Console.WriteLine(message);
        }

        public void Warning(string message)
        {
            Console.WriteLine($"⚠️  {message}");
        }

        public void Error(string message)
        {
            Console.Error.WriteLine($"❌ {message}");
        }

        public void Error(string message, Exception ex)
        {
            Console.Error.WriteLine($"❌ {message}: {ex.Message}");
        }
    }
}
