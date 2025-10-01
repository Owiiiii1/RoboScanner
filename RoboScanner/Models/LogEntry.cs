using System;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;

namespace RoboScanner.Models
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "INFO";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string? DataJson { get; set; }

        [JsonIgnore]
        public string EventType =>
            string.Equals(Source, "App", StringComparison.OrdinalIgnoreCase) ? "Application" :
            (Source?.IndexOf("scan", StringComparison.OrdinalIgnoreCase) >= 0 ? "Scan" : "Other");

        [JsonIgnore]
        public string DisplayData
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DataJson)) return "";
                try
                {
                    using var doc = JsonDocument.Parse(DataJson);
                    var root = doc.RootElement;

                    // есть ли ключ "group"?
                    if (!TryGet(root, "group", out var gEl)) return DataJson;

                    var group = gEl.ValueKind == JsonValueKind.Number
                        ? gEl.GetInt32()
                        : (int.TryParse(gEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var gi) ? gi : 0);

                    double? x = GetDouble(root, "x");
                    double? y = GetDouble(root, "y");
                    double? z = GetDouble(root, "z");

                    // время берём из поля "at" (ISO), если есть; иначе — Timestamp
                    DateTime at = Timestamp;
                    if (TryGet(root, "at", out var atEl) && atEl.ValueKind == JsonValueKind.String)
                    {
                        var s = atEl.GetString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            // сначала пробуем ISO, затем общий парс
                            if (!DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out at))
                                DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at);
                        }
                    }

                    string sx = x.HasValue ? x.Value.ToString("F2", CultureInfo.InvariantCulture) : "--";
                    string sy = y.HasValue ? y.Value.ToString("F2", CultureInfo.InvariantCulture) : "--";
                    string sz = z.HasValue ? z.Value.ToString("F2", CultureInfo.InvariantCulture) : "--";

                    string date = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string time = at.ToString("HH.mm", CultureInfo.InvariantCulture); // точка вместо двоеточия

                    return $"Group - {group} - x ( {sx} ) - y ( {sy} ) - z ( {sz} ) - {date} ( {time} )";
                }
                catch
                {
                    return DataJson; // если JSON битый — показываем как есть
                }
            }
        }

        private static bool TryGet(JsonElement root, string key, out JsonElement value)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static double? GetDouble(JsonElement root, string key)
        {
            if (!TryGet(root, key, out var el)) return null;

            if (el.ValueKind == JsonValueKind.Number)
                return el.GetDouble();

            if (el.ValueKind == JsonValueKind.String &&
                double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;

            return null;
        }
    }
}
