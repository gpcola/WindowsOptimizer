using System;
using System.IO;

namespace WindowsOptimizer
{
    public sealed class Logger
    {
        private readonly Action<string> logToUi;
        private readonly string logFilePath;
        private readonly object sync = new object();
        private string lastMessage = string.Empty;
        private int repeatedCount;

        public Logger(Action<string> uiLogger)
        {
            logToUi = uiLogger;
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsOptimizer");
            Directory.CreateDirectory(folder);
            logFilePath = Path.Combine(folder, "optimizer.log");
        }

        public string LogFilePath => logFilePath;

        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            lock (sync)
            {
                string trimmed = message.Trim();
                if (string.Equals(trimmed, lastMessage, StringComparison.Ordinal))
                {
                    repeatedCount++;
                    return;
                }

                FlushRepeatNotice();
                lastMessage = trimmed;
                WriteLine(trimmed);
            }
        }

        private void FlushRepeatNotice()
        {
            if (repeatedCount <= 0)
                return;

            WriteLine($"Previous message repeated {repeatedCount} more time(s).");
            repeatedCount = 0;
        }

        private void WriteLine(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
            logToUi(line);

            try
            {
                File.AppendAllText(logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // ignore file logging errors
            }
        }
    }
}
