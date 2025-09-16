using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using RoboScanner.Models;

namespace RoboScanner.Services
{
    public sealed class LogService
    {
        public static LogService Instance { get; } = new LogService();

        public ObservableCollection<LogEntry> Entries { get; } = new(); // для страницы "Лог"
        public string LogDirectory { get; }
        public string LogPath { get; }
        public int MaxEntries { get; set; } = 2000; // ограничим размер в UI

        private readonly object _fileLock = new();

        private LogService()
        {
            LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(LogDirectory);
            LogPath = Path.Combine(LogDirectory, "app.log");
        }

        // базовый метод
        public void Write(string level, string source, string message, object? payload = null)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Source = source,
                Message = message,
                DataJson = payload == null ? null : JsonSerializer.Serialize(payload)
            };

            // в файл (потокобезопасно)
            lock (_fileLock)
            {
                using var sw = new StreamWriter(LogPath, append: true);
                sw.WriteLine(JsonSerializer.Serialize(entry));
            }

            // в UI (с безопасностью к другим потокам)
            if (System.Windows.Application.Current?.Dispatcher != null &&
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AddToUi(entry);
                });
            }
            else
            {
                AddToUi(entry);
            }
        }

        private void AddToUi(LogEntry entry)
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }

        public void Info(string source, string message, object? payload = null) =>
            Write("INFO", source, message, payload);
        public void Warn(string source, string message, object? payload = null) =>
            Write("WARN", source, message, payload);
        public void Error(string source, string message, object? payload = null) =>
            Write("ERROR", source, message, payload);

        // открыть файл лога проводником
        public void OpenLogFolder()
        {
            try { Process.Start(new ProcessStartInfo { FileName = LogDirectory, UseShellExecute = true }); }
            catch { /* ignore */ }
        }

        public void OpenLogFile()
        {
            try { Process.Start(new ProcessStartInfo { FileName = LogPath, UseShellExecute = true }); }
            catch { /* ignore */ }
        }
    }
}
