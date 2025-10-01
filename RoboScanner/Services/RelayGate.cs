using System;

namespace RoboScanner.Services
{
    /// Блокирует запуск скана на время активных импульсов реле.
    public static class RelayGate
    {
        private static readonly object _lock = new();
        private static DateTime _busyUntilUtc = DateTime.MinValue;

        public static bool IsBusy
        {
            get { lock (_lock) return DateTime.UtcNow < _busyUntilUtc; }
        }

        public static TimeSpan Remaining
        {
            get
            {
                lock (_lock)
                {
                    var left = _busyUntilUtc - DateTime.UtcNow;
                    return left > TimeSpan.Zero ? left : TimeSpan.Zero;
                }
            }
        }

        /// Заблокировать на указанный срок (берём максимум, если уже занято).
        public static void BlockFor(TimeSpan span)
        {
            if (span <= TimeSpan.Zero) return;
            lock (_lock)
            {
                var until = DateTime.UtcNow + span;
                if (until > _busyUntilUtc)
                    _busyUntilUtc = until;
            }
        }

        /// Снять блокировку немедленно (на всякий случай).
        public static void Clear()
        {
            lock (_lock) _busyUntilUtc = DateTime.MinValue;
        }
    }
}
