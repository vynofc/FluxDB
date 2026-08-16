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

        private static Queue<string> _writeQueue = new Queue<string>();
        private static bool _isProcessingQueue = false;
        private static StreamWriter _logWriter;
        private static readonly object _writerLock = new object();

        private static StreamWriter GetWriter()
        {
            if (_logWriter == null)
            {
                _logWriter = new StreamWriter(_logFilePath, true, Encoding.UTF8) { AutoFlush = false };
            }
            return _logWriter;
        }

        private static DateTime _lastFlush = DateTime.MinValue;

        private static int _cachedMaxBufferLines = -1;

        private static int GetMaxBufferLines()
        {
            if (_cachedMaxBufferLines > 0)
                return _cachedMaxBufferLines;

            try
            {
                var v = new SettingsService().GetDevSettingInt(DevSettingsRegistry.LogBufferLinesKey);
                _cachedMaxBufferLines = v > 0 ? v : 2000;
            }
            catch
            {
                _cachedMaxBufferLines = 2000;
            }
            return _cachedMaxBufferLines;
        }

        public static void InvalidateCachedSettings()
        {
            _cachedMaxBufferLines = -1;
        }

        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"[{timestamp}] {message}";
                lock (_lock)
                {
                    var maxBuffer = GetMaxBufferLines();
                    _buffer.Add(line);
                    if (_buffer.Count > maxBuffer)
                    {
                        _buffer.RemoveRange(0, _buffer.Count - maxBuffer);
                        if (_buffer.Capacity > 4 * _buffer.Count)
                            _buffer.TrimExcess();
                    }

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
                    Queue<string> linesToWrite;
                    lock (_lock)
                    {
                        if (_writeQueue.Count == 0)
                        {
                            _isProcessingQueue = false;
                            return;
                        }
                        linesToWrite = _writeQueue;
                        _writeQueue = new Queue<string>();
                    }

                    try
                    {
                        var sb = new StringBuilder();
                        while (linesToWrite.Count > 0)
                            sb.AppendLine(linesToWrite.Dequeue());

                        lock (_writerLock)
                        {
                            var writer = GetWriter();
                            writer.Write(sb.ToString());

                            var now = DateTime.UtcNow;
                            bool queueEmpty;
                            lock (_lock) { queueEmpty = _writeQueue.Count == 0; }
                            if (queueEmpty || (now - _lastFlush).TotalSeconds >= 2)
                            {
                                writer.Flush();
                                _lastFlush = now;
                            }
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
            // Drain remaining queue
            Queue<string> remaining;
            lock (_lock)
            {
                remaining = _writeQueue;
                _writeQueue = new Queue<string>();
            }
            if (remaining.Count > 0)
            {
                try
                {
                    var sb = new StringBuilder();
                    while (remaining.Count > 0)
                        sb.AppendLine(remaining.Dequeue());
                    lock (_writerLock)
                    {
                        GetWriter().Write(sb.ToString());
                    }
                }
                catch { }
            }

            lock (_writerLock)
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }
    }
}
