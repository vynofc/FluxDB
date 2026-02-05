using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace FluxDB.Services
{
    public static class LoggingService
    {
        private static readonly object _lock = new object();
        private static readonly List<string> _buffer = new List<string>();
        private const int MaxBufferLines = 2000;
        private static readonly string _logFilePath;

        static LoggingService()
        {
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxDB");
                if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
                _logFilePath = Path.Combine(appData, "logs.txt");
            }
            catch
            {
                _logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "logs.txt");
            }
        }

        private static readonly Queue<string> _writeQueue = new Queue<string>();
        private static bool _isProcessingQueue = false;

        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"[{timestamp}] {message}";
                lock (_lock)
                {
                    _buffer.Add(line);
                    if (_buffer.Count > MaxBufferLines)
                        _buffer.RemoveRange(0, _buffer.Count - MaxBufferLines);

                    _writeQueue.Enqueue(line);
                    if (!_isProcessingQueue)
                    {
                        _isProcessingQueue = true;
                        ThreadPool.QueueUserWorkItem(ProcessWriteQueue);
                    }
                }
            }
            catch
            {
                // ignore logging failures
            }
        }

        private static void ProcessWriteQueue(object state)
        {
            while (true)
            {
                string[] linesToWrite;
                lock (_lock)
                {
                    if (_writeQueue.Count == 0)
                    {
                        _isProcessingQueue = false;
                        return;
                    }
                    linesToWrite = _writeQueue.ToArray();
                    _writeQueue.Clear();
                }

                try
                {
                    File.AppendAllLines(_logFilePath, linesToWrite, Encoding.UTF8);
                }
                catch
                {
                    // swallow file write errors
                }
                
                Thread.Sleep(100); // Small delay to batch more logs
            }
        }

        public static string[] GetLogs()
        {
            lock (_lock)
            {
                return _buffer.ToArray();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _buffer.Clear();
                try { File.WriteAllText(_logFilePath, string.Empty); } catch { }
            }
        }

        public static string LogFilePath => _logFilePath;
    }
}
