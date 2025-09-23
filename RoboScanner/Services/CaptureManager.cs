using System.Threading.Tasks;
using OpenCvSharp;
using AppSettings = RoboScanner.Properties.Settings;

namespace RoboScanner.Services
{
    public sealed class CaptureManager
    {
        private static readonly System.Lazy<CaptureManager> _lazy = new(() => new CaptureManager());
        public static CaptureManager Instance => _lazy.Value;

        private readonly CameraService _camera = CameraService.Instance;
        private CaptureManager() { }

        // Снять с камеры 1 (ID берём из настроек)
        public Task<Mat> CaptureCam1Async(int w = 1920, int h = 1080, int fps = 30)
            => _camera.CaptureByMonikerAsync(AppSettings.Default.Camera1Id, w, h, fps);

        // Снять с камеры 2
        public Task<Mat> CaptureCam2Async(int w = 1920, int h = 1080, int fps = 30)
            => _camera.CaptureByMonikerAsync(AppSettings.Default.Camera2Id, w, h, fps);

        // (Опционально) универсальный метод по moniker
        public Task<Mat> CaptureByMonikerAsync(string moniker, int w = 1920, int h = 1080, int fps = 30)
            => _camera.CaptureByMonikerAsync(moniker, w, h, fps);
    }
}
