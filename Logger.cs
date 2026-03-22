using System;
using System.IO;

namespace WindowsOptimizer
{
    public sealed class Logger
    {
        private readonly Action<string> logToUi;
        private readonly string logFilePath;

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
