using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;

namespace RoboScanner.Services
{
    public static class ModbusOutputService
    {
        private static readonly SemaphoreSlim _gate = new(1, 1);

        public static async Task PulseAsync(string host, int port, byte unitId, int coilAddressOneBased, int? pulseSeconds)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host is empty");
            if (coilAddressOneBased <= 0) throw new ArgumentOutOfRangeException(nameof(coilAddressOneBased));

            ushort addr0 = (ushort)(coilAddressOneBased - 1);

            // ON с паузой вотчера
            await ExecWithWatcherPauseAsync(async () =>
            {
                await WriteCoilOnceAsync(host, unitId, addr0, true);
                LogService.Instance.Info("Relay", "Coil ON", new { host, unitId, Coil = coilAddressOneBased });
            });

            // OFF в фоне (если нужен), тоже с паузой вотчера
            if (pulseSeconds.HasValue && pulseSeconds.Value > 0)
            {
                int delaySec = pulseSeconds.Value;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySec));
                        await ExecWithWatcherPauseAsync(async () =>
                        {
                            await WriteCoilOnceAsync(host, unitId, addr0, false);
                            LogService.Instance.Info("Relay", "Coil OFF", new { host, unitId, Coil = coilAddressOneBased });
                        });
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error("Relay", "Coil OFF failed", ex);
                    }
                });
            }
        }

        public static async Task SetAsync(string host, int port, byte unitId, int coilAddressOneBased, bool value)
        {
            ushort addr0 = (ushort)(coilAddressOneBased - 1);
            await ExecWithWatcherPauseAsync(async () =>
            {
                await WriteCoilOnceAsync(host, unitId, addr0, value);
                LogService.Instance.Info("Relay", "Coil SET", new { host, unitId, Coil = coilAddressOneBased, Value = value });
            });
        }

        private static async Task WriteCoilOnceAsync(string host, byte unitId, ushort coilAddr0, bool value)
        {
            await _gate.WaitAsync();
            try
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        using var client = new ModbusTcpClient();
                        client.Connect(host, ModbusEndianness.BigEndian); // LOGO!: 502
                        client.WriteSingleCoil(unitId, coilAddr0, value);
                        return;
                    }
                    catch (IOException)
                    {
                        if (attempt == 2) throw;
                        await Task.Delay(200);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private static async Task ExecWithWatcherPauseAsync(Func<Task> action)
        {
            try
            {
                await StartSignalWatcher.Instance.StopAsync();
                await Task.Delay(150);
            }
            catch { }

            try
            {
                await action();
            }
            finally
            {
                try { StartSignalWatcher.Instance.Start(); } catch { }
            }
        }
    }
}
