using System;
namespace RoboScanner.Services
{
    public static class Calibration
    {
        /// mm на один пиксель по горизонтали.
        public static double MmPerPixel(int imageWidthPx, double distanceMm, double fovDeg)
        {
            // ширина сцены на расстоянии D: 2*D*tan(FOV/2)
            // делим на число пикселей по ширине
            if (imageWidthPx <= 0) return 0;
            double fovRad = fovDeg * Math.PI / 180.0;
            double sceneWidthMm = 2.0 * distanceMm * Math.Tan(fovRad / 2.0);
            return sceneWidthMm / imageWidthPx;
        }
    }
}
