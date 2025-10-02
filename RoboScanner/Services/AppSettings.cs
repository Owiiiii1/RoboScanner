namespace RoboScanner.Services
{
    /// <summary>
    /// Простейшие настройки приложения (в памяти).
    /// При желании можно добавить загрузку/сохранение в файл.
    /// </summary>
    public static class AppSettings
    {
        // Идентификаторы камер (если уже есть — оставь как у тебя)
        public static string? Camera1Id { get; set; }
        public static string? Camera2Id { get; set; }

        // Калибровка: расстояния и FOV для двух камер
        public static double Cam1DistanceMm { get; set; } = 1000;  // мм
        public static double Cam2DistanceMm { get; set; } = 1000;  // мм
        public static double Cam1FovDeg { get; set; } = 60;    // градусы (горизонтальный FOV)
        public static double Cam2FovDeg { get; set; } = 60;    // градусы

        // Если хочешь — добавь методы Save/Load (например, в json)
        // public static void Load() { ... }
        // public static void Save() { ... }
    }
}
