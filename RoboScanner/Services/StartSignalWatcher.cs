using System;
using System.Threading.Tasks;
using RoboScanner.Models;

namespace RoboScanner.Services
{
    /// Глобальный «входящий триггер» (группа 17). Живёт независимо от UI.
    public sealed class StartSignalWatcher : IDisposable, IAsyncDisposable
    {
        public static StartSignalWatcher Instance { get; } = new();

        private ModbusInputWatcher? _watcher;
        private bool _running;

        private StartSignalWatcher() { }

        public event EventHandler? Triggered;

        public void Start()
        {
            if (_running) return;

            // Берём настройки группы 17 («Входящий триггер»)
            var g = RobotGroups.Get(17);
            if (string.IsNullOrWhiteSpace(g.Host) || !g.PrimaryCoilAddress.HasValue)
                throw new InvalidOperationException("Group 17 is not configured (Host/Coil).");

            _watcher = new ModbusInputWatcher(
                host: g.Host,
                port: g.Port,                // у FluentModbus Connect использует 502, это ок
                unitId: g.UnitId,
                coilAddressOneBased: g.PrimaryCoilAddress.Value, // M17 -> 8273 (если так настроено)
                pollMs: 80
            );

            _watcher.RisingEdge += (_, __) => Triggered?.Invoke(this, EventArgs.Empty);
            _watcher.Start();
            _running = true;

            LogService.Instance.Info("InputWatcher", "Started (Group17)", new { g.Host, g.Port, g.UnitId, g.PrimaryCoilAddress });
        }

        public async Task StopAsync()
        {
            if (!_running) return;
            try { if (_watcher != null) await _watcher.StopAsync(); }
            finally
            {
                _watcher = null;
                _running = false;
                LogService.Instance.Info("InputWatcher", "Stopped");
            }
        }

        public void Dispose() => StopAsync().GetAwaiter().GetResult();
        public ValueTask DisposeAsync() => new ValueTask(StopAsync());
    }
}
