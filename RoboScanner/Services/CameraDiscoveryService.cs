using DirectShowLib;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace RoboScanner.Services
{
    public sealed class CameraInfo
    {
        public string Name { get; init; } = "";
        public string Moniker { get; init; } = "";     // ключ/DevicePath
        public string? PortInfo { get; init; }         // "Hub 4 / Port 3" или null

        // Готовая подпись для UI
        public string Display => string.IsNullOrWhiteSpace(PortInfo)
            ? Name
            : $"{Name} ({PortInfo})";
    }



    public sealed class CameraDiscoveryService
    {
        private static readonly System.Lazy<CameraDiscoveryService> _lazy =
            new(() => new CameraDiscoveryService());
        public static CameraDiscoveryService Instance => _lazy.Value;
        private CameraDiscoveryService() { }

        private static string? TryGetPortInfo(string? devicePath)
        {
            var pnp = NormalizeToPnp(devicePath);
            if (pnp is null) return null;

            string? current = pnp;
            for (int hop = 0; hop < 5 && !string.IsNullOrWhiteSpace(current); hop++)
            {
                // 1) WMI
                var loc = QueryWmiLocationInfo(current!);
                var pretty = ParseLocationInformation(loc);
                if (!string.IsNullOrWhiteSpace(pretty))
                    return pretty;

                // 2) Реестр: LocationInformation
                loc = ReadRegString($@"SYSTEM\CurrentControlSet\Enum\{current}", "LocationInformation")
                   ?? ReadRegString($@"SYSTEM\CurrentControlSet\Enum\{current}\Device Parameters", "LocationInformation");
                pretty = ParseLocationInformation(loc);
                if (!string.IsNullOrWhiteSpace(pretty))
                    return pretty;

                // 3) Реестр: LocationPaths
                var paths = ReadRegMulti($@"SYSTEM\CurrentControlSet\Enum\{current}", "LocationPaths")
                         ?? ReadRegMulti($@"SYSTEM\CurrentControlSet\Enum\{current}\Device Parameters", "LocationPaths");
                if (paths is { Length: > 0 })
                {
                    var formatted = FormatUsbLocationPath(paths[0]);
                    if (!string.IsNullOrWhiteSpace(formatted))
                        return formatted;
                }

                // 4) вверх по дереву
                current = ReadRegString($@"SYSTEM\CurrentControlSet\Enum\{current}", "Parent");
            }

            // Фолбэк: короткий хвост PNP ID
            var tail = pnp.Split('\\').LastOrDefault();
            return string.IsNullOrWhiteSpace(tail) ? null : $"Path {tail}";
        }

        private static string? NormalizeToPnp(string? devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath)) return null;
            string s = devicePath;

            if (s.StartsWith("@device:pnp:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("@device:pnp:".Length);
            if (s.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(4);

            var i = s.IndexOf("#{", StringComparison.Ordinal);
            if (i >= 0) s = s.Substring(0, i);

            return s.Replace('#', '\\').ToUpperInvariant(); // PNPDeviceID
        }

        private static string? QueryWmiLocationInfo(string pnp)
        {
            var key = pnp.Replace(@"\", @"\\").Replace("'", "''");
            try
            {
                using var q = new ManagementObjectSearcher(
                    $"SELECT LocationInformation FROM Win32_PnPEntity WHERE PNPDeviceID='{key}'");
                foreach (ManagementObject mo in q.Get())
                    return mo["LocationInformation"] as string;
            }
            catch { }
            return null;
        }

        private static string? ReadRegString(string subkey, string name)
        {
            try { using var k = Registry.LocalMachine.OpenSubKey(subkey, false); return k?.GetValue(name) as string; }
            catch { return null; }
        }

        private static string[]? ReadRegMulti(string subkey, string name)
        {
            try { using var k = Registry.LocalMachine.OpenSubKey(subkey, false); return k?.GetValue(name) as string[]; }
            catch { return null; }
        }

        private static string? ParseLocationInformation(string? loc)
        {
            if (string.IsNullOrWhiteSpace(loc)) return null;
            // Примеры: "Port_#0003.Hub_#0004", "Port_#0001"
            var m = Regex.Match(loc, @"Port_#0*(\d+)(?:\.Hub_#0*(\d+))?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var port = m.Groups[1].Value;
                var hub = m.Groups[2].Success ? m.Groups[2].Value : null;
                return hub is null ? $"Port {port}" : $"Hub {hub} / Port {port}";
            }
            return null;
        }

        private static string? FormatUsbLocationPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            // Нормальная цепочка: USB(3)#USB(2)#...
            var usb = Regex.Matches(path, @"USB\((\d+)\)", RegexOptions.IgnoreCase)
                           .Cast<Match>()
                           .Select(m => m.Groups[1].Value)
                           .ToArray();
            if (usb.Length > 0)
                return usb.Length == 1 ? $"Port {usb[0]}" : $"Ports {string.Join("→", usb)}";

            // Альтернативный «точечный» формат: 0000.001a.0000.001.001...
            var m2 = Regex.Match(path, @"(\d+(?:\.\d+){2,})$");
            if (m2.Success)
                return $"Path {m2.Groups[1].Value}";

            return $"Path {path}";
        }





        public List<CameraInfo> ListVideoDevices()
        {
            var devs = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice) ?? Array.Empty<DsDevice>();
            var list = new List<CameraInfo>(devs.Length);
            foreach (var d in devs)
            {
                var mon = d.DevicePath ?? d.Name ?? string.Empty;
                list.Add(new CameraInfo
                {
                    Name = d.Name ?? "Camera",
                    Moniker = mon,
                    PortInfo = TryGetPortInfo(mon)
                });

            }
            return list;
        }

        public CameraInfo? FindByMoniker(IEnumerable<CameraInfo> list, string? moniker)
            => string.IsNullOrWhiteSpace(moniker) ? null
               : list.FirstOrDefault(c => c.Moniker == moniker);
    }
}
