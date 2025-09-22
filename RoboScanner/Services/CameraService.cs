using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace RoboScanner.Services
{
    /// <summary>
    /// Ленивая обёртка над OpenCV VideoCapture с прогревом и сбросом буфера.
    /// Решает проблему: первый кадр чёрный, далее приходит предыдущий кадр.
    /// </summary>
    public sealed class CameraService : IDisposable
    {
        private static readonly Lazy<CameraService> _lazy = new(() => new CameraService());
        public static CameraService Instance => _lazy.Value;

        private readonly object _sync = new();

        private VideoCapture? _cap;
        private int _deviceIndex = 0;
        private int _width = 1920, _height = 1080, _fps = 30;

        private CameraService() { }

        public void Configure(int deviceIndex = 0, int width = 1920, int height = 1080, int fps = 30)
        {
            lock (_sync)
            {
                _deviceIndex = deviceIndex;
                _width = width; _height = height; _fps = fps;

                _cap?.Release();
                _cap?.Dispose();
                _cap = null; // переоткроется на следующем захвате
            }
        }

        private static void TrySet(VideoCapture cap, VideoCaptureProperties prop, double value)
        {
            try { cap.Set(prop, value); } catch { /* ignore */ }
        }

        /// <summary>Открыть камеру (DSHOW→MSMF→ANY), задать параметры, уменьшить буфер, прогреть.</summary>
        private void EnsureOpen()
        {
            if (_cap != null && _cap.IsOpened()) return;

            lock (_sync)
            {
                _cap?.Release();
                _cap?.Dispose();

                VideoCapture? cap = null;
                foreach (var api in new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY })
                {
                    var c = new VideoCapture(_deviceIndex, api);
                    if (c.IsOpened()) { cap = c; break; }
                    c.Dispose();
                }
                if (cap is null)
                    throw new Exception($"Unable to open camera (deviceIndex={_deviceIndex})");

                // Параметры
                cap.Set(VideoCaptureProperties.FrameWidth, _width);
                cap.Set(VideoCaptureProperties.FrameHeight, _height);
                cap.Set(VideoCaptureProperties.Fps, _fps);

                // Свести задержку к минимуму
                TrySet(cap, VideoCaptureProperties.BufferSize, 1);   // уменьшить глубину буфера (если поддерживается)
                TrySet(cap, VideoCaptureProperties.ConvertRgb, 1);

                // Прогрев: выбрасываем несколько первых кадров, даём автоэкспозиции стабилизироваться
                using var warm = new Mat();
                for (int i = 0; i < 8; i++)
                {
                    cap.Read(warm);
                    Thread.Sleep(10);
                }

                _cap = cap;
            }
        }

        /// <summary>
        /// Считать один актуальный кадр (BGR). Перед возвратом “промываем” очередь,
        /// читаем несколько раз и возвращаем последний (клон).
        /// </summary>
        public Mat GrabFrame()
        {
            EnsureOpen();

            // Сбросить возможный хвост очереди (на некоторых драйверах это критично)
            using var tmp = new Mat();
            Mat? last = null;

            // Делаем короткий дренаж ~40–60 мс: берём всё, что есть “сейчас”
            var t0 = Environment.TickCount;
            while (Environment.TickCount - t0 < 50)
            {
                if (!_cap!.Read(tmp) || tmp.Empty())
                {
                    Thread.Sleep(5);
                    continue;
                }
                last?.Dispose();
                last = tmp.Clone(); // клон — чтобы не зависеть от внутреннего буфера
                // небольшой yield, чтобы драйвер подкинул следующий кадр, если есть
                Thread.Sleep(1);
            }

            // страховка: если по какой-то причине last пуст — читаем ещё раз
            if (last is null || last.Empty())
            {
                for (int i = 0; i < 3; i++)
                {
                    if (_cap!.Read(tmp) && !tmp.Empty())
                    {
                        last?.Dispose();
                        last = tmp.Clone();
                        break;
                    }
                    Thread.Sleep(10);
                }
            }

            if (last is null || last.Empty())
                throw new Exception("Unable to get a frame from the camera");

            return last; // не Dispose — отдаём вызвавшему
        }

        public Task<Mat> GrabFrameAsync() => Task.Run(() => GrabFrame());

        public void Dispose()
        {
            lock (_sync)
            {
                _cap?.Release();
                _cap?.Dispose();
                _cap = null;
            }
        }
    }
}
