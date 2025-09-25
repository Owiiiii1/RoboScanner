using System.Threading.Tasks;
using OpenCvSharp;

namespace RoboScanner.Services
{
    public sealed class CaptureManager
    {
        private static readonly System.Lazy<CaptureManager> _lazy = new(() => new CaptureManager());
        public static CaptureManager Instance => _lazy.Value;

        private readonly CameraService _camera = CameraService.Instance;
        private CaptureManager() { }

        // старые
        public Mat CaptureOnce() => _camera.GrabFrame();
        public Task<Mat> CaptureOnceAsync() => _camera.GrabFrameAsync();

        // новые — по Moniker (DevicePath)
        public Mat CaptureOnce(string moniker, int w = 1920, int h = 1080, int fps = 30)
            => _camera.GrabFrameByMoniker(moniker, w, h, fps);

        public Task<Mat> CaptureOnceAsync(string moniker, int w = 1920, int h = 1080, int fps = 30)
            => _camera.GrabFrameByMonikerAsync(moniker, w, h, fps);
    }
}
