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

        public Mat CaptureOnce() => _camera.GrabFrame();
        public Task<Mat> CaptureOnceAsync() => _camera.GrabFrameAsync();
    }
}
