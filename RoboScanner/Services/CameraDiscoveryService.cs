using System.Collections.Generic;
using System.Linq;
using DirectShowLib;

namespace RoboScanner.Services
{
    public sealed class CameraInfo
    {
        public string Name { get; init; } = "";
        public string Moniker { get; init; } = ""; // стабильный ID (PnP path)
        public override string ToString() => Name;
    }

    public sealed class CameraDiscoveryService
    {
        private static readonly System.Lazy<CameraDiscoveryService> _lazy =
            new(() => new CameraDiscoveryService());
        public static CameraDiscoveryService Instance => _lazy.Value;
        private CameraDiscoveryService() { }

        public List<CameraInfo> ListVideoDevices()
        {
            var devs = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)
                               ?? System.Array.Empty<DsDevice>();
            return devs.Select(d => new CameraInfo
            {
                Name = d.Name ?? "Camera",
                Moniker = d.DevicePath ?? d.Name   // было: d.DevicePath ?? d.MonikerString ?? d.Name
            }).ToList();
        }

        public CameraInfo? FindByMoniker(IEnumerable<CameraInfo> list, string? moniker)
            => string.IsNullOrWhiteSpace(moniker) ? null
               : list.FirstOrDefault(c => c.Moniker == moniker);
    }
}
