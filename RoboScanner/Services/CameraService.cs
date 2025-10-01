using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace RoboScanner.Services
{
    /// <summary>
    /// Пул VideoCapture по Moniker (DevicePath). Каждая камера имеет свой капчер и буфер.
    /// Открытие по текущему индексу устройства, найденному через CameraDiscoveryService.
    /// </summary>
    public sealed class CameraService : IDisposable
    {
        private static readonly Lazy<CameraService> _lazy = new(() => new CameraService());
        public static CameraService Instance => _lazy.Value;

        private readonly object _sync = new();
        private readonly Dictionary<string, Handle> _pool = new(StringComparer.OrdinalIgnoreCase);

        private CameraService() { }

        private sealed class Handle : IDisposable
        {
            public string Moniker { get; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Fps { get; set; }

            private readonly object _lock = new();
            private VideoCapture? _cap;
            private int? _lastIndex;

            public Handle(string moniker, int width, int height, int fps)
            {
                Moniker = moniker;
                Width = width; Height = height; Fps = fps;
            }

            private static void TrySet(VideoCapture cap, VideoCaptureProperties p, double v)
            {
                try { cap.Set(p, v); } catch { /* ignore */ }
            }

            private void EnsureOpen()
            {
                if (_cap != null && _cap.IsOpened()) return;

                lock (_lock)
                {
                    _cap?.Release();
                    _cap?.Dispose();
                    _cap = null;

                    // Находим текущий индекс для этого Moniker
                    var devs = CameraDiscoveryService.Instance.ListVideoDevices();
                    var idx = devs
                        .Select((d, i) => (d, i))
                        .FirstOrDefault(t => t.d.Moniker.Equals(Moniker, StringComparison.OrdinalIgnoreCase)).i;

                    if (idx < 0 && !devs.Any()) throw new Exception("No video devices found");
                    if (idx < 0) throw new Exception($"Camera not found by moniker: {Moniker}");
                    _lastIndex = idx;

                    // Пытаемся открыть через DSHOW → MSMF → ANY
                    VideoCapture? cap = null;
                    foreach (var api in new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY })
                    {
                        var c = new VideoCapture(idx, api);
                        if (c.IsOpened()) { cap = c; break; }
                        c.Dispose();
                    }
                    if (cap is null)
                        throw new Exception($"Unable to open camera index {idx} for moniker: {Moniker}");

                    // Параметры
                    cap.Set(VideoCaptureProperties.FrameWidth, Width);
                    cap.Set(VideoCaptureProperties.FrameHeight, Height);
                    cap.Set(VideoCaptureProperties.Fps, Fps);
                    TrySet(cap, VideoCaptureProperties.BufferSize, 1);
                    TrySet(cap, VideoCaptureProperties.ConvertRgb, 1);

                    // Прогрев
                    using var warm = new Mat();
                    for (int i = 0; i < 8; i++) { cap.Read(warm); Thread.Sleep(10); }

                    _cap = cap;
                }
            }

            public Mat GrabFrame()
            {
                EnsureOpen();

                using var tmp = new Mat();
                Mat? last = null;

                var t0 = Environment.TickCount;
                while (Environment.TickCount - t0 < 50)
                {
                    if (!_cap!.Read(tmp) || tmp.Empty()) { Thread.Sleep(5); continue; }
                    last?.Dispose();
                    last = tmp.Clone();
                    Thread.Sleep(1);
                }

                if (last is null || last.Empty())
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (_cap!.Read(tmp) && !tmp.Empty()) { last?.Dispose(); last = tmp.Clone(); break; }
                        Thread.Sleep(10);
                    }
                }

                if (last is null || last.Empty())
                    throw new Exception($"Unable to get a frame (moniker={Moniker}, index={_lastIndex})");

                return last;
            }

            public Task<Mat> GrabFrameAsync() => Task.Run(GrabFrame);

            public void Dispose()
            {
                lock (_lock)
                {
                    _cap?.Release();
                    _cap?.Dispose();
                    _cap = null;
                }
            }
        }

        private Handle GetHandle(string moniker, int width, int height, int fps)
        {
            lock (_sync)
            {
                if (!_pool.TryGetValue(moniker, out var h))
                {
                    h = new Handle(moniker, width, height, fps);
                    _pool[moniker] = h;
                }
                else
                {
                    // обновим параметры (на следующем открытии применятся)
                    h.Width = width; h.Height = height; h.Fps = fps;
                }
                return h;
            }
        }

        // === Публичные методы ===

        public Mat GrabFrameByMoniker(string moniker, int width = 1920, int height = 1080, int fps = 30)
            => GetHandle(moniker, width, height, fps).GrabFrame();

        public Task<Mat> GrabFrameByMonikerAsync(string moniker, int width = 1920, int height = 1080, int fps = 30)
            => GetHandle(moniker, width, height, fps).GrabFrameAsync();

        // Оставляем старые сигнатуры для совместимости (возьмём первый девайс)
        public Mat GrabFrame()
        {
            var devs = CameraDiscoveryService.Instance.ListVideoDevices();
            var mon = devs.FirstOrDefault()?.Moniker ?? throw new Exception("No video devices");
            return GrabFrameByMoniker(mon);
        }

        public Task<Mat> GrabFrameAsync() => Task.Run(GrabFrame);

        public void Dispose()
        {
            lock (_sync)
            {
                foreach (var h in _pool.Values) h.Dispose();
                _pool.Clear();
            }
        }
    }
}
