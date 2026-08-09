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
        private const string AppDataFolderName = "FluxDB";
        private const string LogFileName = "logs.txt";

        public static bool IsDebugMode { get; private set; }

        public static void SetDebugMode(bool value) => IsDebugMode = value;

        static LoggingService()
        {
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName);
                if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
                _logFilePath = Path.Combine(appData, LogFileName);
            }
            catch
            {
                _logFilePath = Path.Combine(Directory.GetCurrentDirectory(), LogFileName);
            }
        }

        private static readonly Queue<string> _writeQueue = new Queue<string>();
        private static bool _isProcessingQueue = false;
        private static StreamWriter _logWriter;
        private static readonly object _writerLock = new object();

        private static StreamWriter GetWriter()
        {
            if (_logWriter == null)
            {
                _logWriter = new StreamWriter(_logFilePath, true, Encoding.UTF8) { AutoFlush = true };
            }
            return _logWriter;
        }

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
            try
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
                        var sb = new StringBuilder();
                        foreach (var line in linesToWrite)
                            sb.AppendLine(line);

                        lock (_writerLock)
                        {
                            GetWriter().Write(sb.ToString());
                        }
                    }
                    catch
                    {
                        // swallow file write errors
                    }
                }
            }
            catch
            {
                lock (_lock)
                {
                    _isProcessingQueue = false;
                }
            }
        }

        public static void LogDebug(string message)
        {
            if (!IsDebugMode) return;
            Log($"[DEBUG] {message}");
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
                try { File.WriteAllText(_logFilePath, string.Empty); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoggingService.Clear: {ex.Message}"); }
            }
        }

        public static string LogFilePath => _logFilePath;

        public static void Shutdown()
        {
            lock (_lock)
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }
    }
}
