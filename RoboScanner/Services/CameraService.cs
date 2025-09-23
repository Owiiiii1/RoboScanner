using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DirectShowLib;
using OpenCvSharp;

namespace RoboScanner.Services
{
    /// <summary>
    /// UVC-камеры по стабильному идентификатору (DevicePath/Moniker).
    /// Держит кэш открытых VideoCapture, снимает «свежий» кадр (без лагов очереди).
    /// </summary>
    public sealed class CameraService : IDisposable
    {
        private static readonly Lazy<CameraService> _lazy = new(() => new CameraService());
        public static CameraService Instance => _lazy.Value;

        private readonly object _sync = new();

        // moniker(DevicePath) -> (VideoCapture, index)
        private readonly Dictionary<string, (VideoCapture cap, int index)> _open = new();

        private CameraService() { }

        // ======== перечисление устройств и поиск индекса по монникеру ========

        private static DsDevice[] ListDsCams()
            => DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice) ?? Array.Empty<DsDevice>();

        private static int ResolveIndex(string moniker)
        {
            var list = ListDsCams();
            for (int i = 0; i < list.Length; i++)
            {
                var path = list[i].DevicePath ?? list[i].Name;
                if (!string.IsNullOrWhiteSpace(path) &&
                    string.Equals(path, moniker, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // ======== открыть/сконфигурировать, с прогревом ========

        private VideoCapture EnsureOpen(string moniker, int width, int height, int fps)
        {
            lock (_sync)
            {
                if (_open.TryGetValue(moniker, out var entry) && entry.cap.IsOpened())
                    return entry.cap;

                var idx = ResolveIndex(moniker);
                if (idx < 0) throw new Exception($"Камера не найдена: {moniker}");

                // пробуем разные backend'ы
                VideoCapture cap = new(idx, VideoCaptureAPIs.DSHOW);
                if (!cap.IsOpened())
                {
                    cap.Dispose();
                    cap = new VideoCapture(idx, VideoCaptureAPIs.MSMF);
                }
                if (!cap.IsOpened())
                {
                    cap.Dispose();
                    cap = new VideoCapture(idx, VideoCaptureAPIs.ANY);
                }
                if (!cap.IsOpened())
                    throw new Exception($"Не удалось открыть камеру (index={idx})");

                cap.Set(VideoCaptureProperties.FrameWidth, width);
                cap.Set(VideoCaptureProperties.FrameHeight, height);
                cap.Set(VideoCaptureProperties.Fps, fps);
                TrySet(cap, VideoCaptureProperties.BufferSize, 1);
                TrySet(cap, VideoCaptureProperties.ConvertRgb, 1);

                // небольшой прогрев
                using var warm = new Mat();
                for (int i = 0; i < 6; i++) { cap.Read(warm); Thread.Sleep(10); }

                _open[moniker] = (cap, idx);
                return cap;
            }
        }

        private static void TrySet(VideoCapture cap, VideoCaptureProperties p, double v)
        { try { cap.Set(p, v); } catch { /* ignore */ } }

        // ======== получить свежий кадр (промываем очередь ~50 мс) ========

        private static Mat GrabFresh(VideoCapture cap)
        {
            using var tmp = new Mat();
            Mat? last = null;

            var t0 = Environment.TickCount;
            while (Environment.TickCount - t0 < 50)
            {
                if (!cap.Read(tmp) || tmp.Empty()) { Thread.Sleep(2); continue; }
                last?.Dispose();
                last = tmp.Clone();
                Thread.Sleep(1);
            }
            if (last is null || last.Empty())
            {
                for (int i = 0; i < 3; i++)
                {
                    if (cap.Read(tmp) && !tmp.Empty()) { last = tmp.Clone(); break; }
                    Thread.Sleep(5);
                }
            }
            if (last is null || last.Empty())
                throw new Exception("Не удалось получить кадр с камеры");

            return last;
        }

        // ======== публичные методы ========

        /// <summary>Снять кадр с камеры по её стабильному ID (DevicePath/Moniker).</summary>
        public Mat CaptureByMoniker(string moniker, int width = 1920, int height = 1080, int fps = 30)
            => GrabFresh(EnsureOpen(moniker, width, height, fps));

        public Task<Mat> CaptureByMonikerAsync(string moniker, int width = 1920, int height = 1080, int fps = 30)
            => Task.Run(() => CaptureByMoniker(moniker, width, height, fps));

        public void Dispose()
        {
            lock (_sync)
            {
                foreach (var kv in _open.Values) { kv.cap.Release(); kv.cap.Dispose(); }
                _open.Clear();
            }
        }
    }
}
