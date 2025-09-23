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

        // 1) Открытие камеры по moniker + базовая настройка и короткий прогрев
        private VideoCapture EnsureOpen(string moniker, int width, int height, int fps)
        {
            lock (_sync)
            {
                if (_open.TryGetValue(moniker, out var entry) && entry.cap.IsOpened())
                    return entry.cap;

                var idx = ResolveIndex(moniker);
                if (idx < 0)
                    throw new Exception($"Камера не найдена: {moniker}");

                // backend: DSHOW -> MSMF -> ANY
                VideoCapture cap = new(idx, VideoCaptureAPIs.DSHOW);
                if (!cap.IsOpened()) { cap.Dispose(); cap = new VideoCapture(idx, VideoCaptureAPIs.MSMF); }
                if (!cap.IsOpened()) { cap.Dispose(); cap = new VideoCapture(idx, VideoCaptureAPIs.ANY); }
                if (!cap.IsOpened())
                    throw new Exception($"Не удалось открыть камеру (index={idx})");

                // Базовые параметры + MJPG (часто обязателен на Windows)
                TrySet(cap, VideoCaptureProperties.FrameWidth, width);
                TrySet(cap, VideoCaptureProperties.FrameHeight, height);
                TrySet(cap, VideoCaptureProperties.Fps, fps);
                TrySet(cap, VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                TrySet(cap, VideoCaptureProperties.ConvertRgb, 1);
                TrySet(cap, VideoCaptureProperties.BufferSize, 1);

                // Короткий прогрев только при открытии
                using (var warm = new Mat())
                {
                    for (int i = 0; i < 4; i++) { cap.Read(warm); System.Threading.Thread.Sleep(5); }
                }

                // Если драйвер вернул ерунду (0x0) — деградация до 1280x720 + MJPG
                var aw = cap.Get(VideoCaptureProperties.FrameWidth);
                var ah = cap.Get(VideoCaptureProperties.FrameHeight);
                if (aw < 64 || ah < 64)
                {
                    TrySet(cap, VideoCaptureProperties.FrameWidth, 1280);
                    TrySet(cap, VideoCaptureProperties.FrameHeight, 720);
                    TrySet(cap, VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));

                    using var warm2 = new Mat();
                    for (int i = 0; i < 4; i++) { cap.Read(warm2); System.Threading.Thread.Sleep(5); }
                }

                _open[moniker] = (cap, idx);
                return cap;
            }
        }



        private static void TrySet(VideoCapture cap, VideoCaptureProperties p, double v)
        { try { cap.Set(p, v); } catch { /* ignore */ } }

        // ======== получить свежий кадр (промываем очередь ~50 мс) ========

        // 2) Быстрый «свежий» кадр: короткий дренаж (≈120 мс) + страховочный read
        private Mat GrabFresh(VideoCapture cap)
        {
            using var tmp = new Mat();
            Mat? last = null;

            const int windowMs = 120;                     // было долго — теперь коротко
            int t0 = Environment.TickCount;
            while (Environment.TickCount - t0 < windowMs)
            {
                if (!cap.Read(tmp) || tmp.Empty())
                {
                    System.Threading.Thread.Sleep(2);
                    continue;
                }
                last?.Dispose();
                last = tmp.Clone();                       // берём самый свежий в окне
            }

            // Страховка: одна попытка чтения, если окно ничего не дало
            if (last is null || last.Empty())
            {
                if (!cap.Read(tmp) || tmp.Empty())
                    throw new Exception("Не удалось получить кадр с камеры");
                last = tmp.Clone();
            }

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
