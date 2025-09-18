using System;
using System.IO;

namespace RoboScanner.Infrastructure
{
    public static class AppPaths
    {
        public static string BaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoboScanner");

        public static string GroupsJson => Path.Combine(BaseDir, "groups.json");

        public static void EnsureBase()
        {
            if (!Directory.Exists(BaseDir))
                Directory.CreateDirectory(BaseDir);
        }
    }
}
