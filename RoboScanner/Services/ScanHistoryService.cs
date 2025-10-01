using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RoboScanner.Services
{
    public record ScanRecord(DateTime At, int GroupIndex, string GroupName, double X, double Y, double Z);

    public sealed class ScanHistoryService
    {
        public static ScanHistoryService Instance { get; } = new();

        private readonly List<ScanRecord> _records = new();
        private readonly string _filePath;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = false };

        public event EventHandler? Changed;

        private ScanHistoryService()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RoboScanner");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "scan_history.jsonl"); // по строке JSON на запись

            LoadFromDisk();
        }

        public IReadOnlyList<ScanRecord> All => _records;

        public void Add(ScanRecord rec)
        {
            _records.Add(rec);
            AppendToDisk(rec);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public int CountBetween(DateTime from, DateTime to) =>
            _records.Count(r => r.At >= from && r.At <= to);

        private void LoadFromDisk()
        {
            if (!File.Exists(_filePath)) return;

            foreach (var line in File.ReadLines(_filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var rec = JsonSerializer.Deserialize<ScanRecord>(line, _json);
                    if (rec != null) _records.Add(rec);
                }
                catch
                {
                    // пропускаем битые строки
                }
            }
        }

        private void AppendToDisk(ScanRecord rec)
        {
            try
            {
                var s = JsonSerializer.Serialize(rec, _json);
                File.AppendAllText(_filePath, s + Environment.NewLine);
            }
            catch
            {
                // не валим приложение из-за IO — можно добавить лог предупреждения
            }
        }
    }
}
