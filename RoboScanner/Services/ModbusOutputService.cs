using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;

namespace RoboScanner.Services
{
    public static class ModbusOutputService
    {
        // один общий семафор – чтобы записи не накладывались
        private static readonly SemaphoreSlim _gate = new(1, 1);

        public static async Task PulseAsync(string host, int port, byte unitId, int coilAddressOneBased, int? pulseSeconds)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host is empty");
            if (coilAddressOneBased <= 0) throw new ArgumentOutOfRangeException(nameof(coilAddressOneBased));

            await _gate.WaitAsync();
            try
            {
                // временно приостанавливаем опрос, чтобы LOGO! не рвал соединение
                await StartSignalWatcher.Instance.StopAsync();
                await Task.Delay(150); // дать LOGO! «освободить» соединение опроса

                await WriteCoilOnce(host, unitId, (ushort)(coilAddressOneBased - 1), true);

                // фоновое выключение (как раньше)
                if (pulseSeconds.HasValue && pulseSeconds.Value > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(pulseSeconds.Value));
                            await WriteCoilOnce(host, unitId, (ushort)(coilAddressOneBased - 1), false);
                        }
                        catch { /* опционально лог */ }
                    });
                }
            }
            finally
            {
                // возобновляем опрос
                try { StartSignalWatcher.Instance.Start(); } catch { /* опционально лог */ }
                _gate.Release();
            }
        }

        private static async Task WriteCoilOnce(string host, byte unitId, ushort coilAddr0, bool value)
        {
            // одна попытка + один повтор на случай «форсированного закрытия»
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var client = new ModbusTcpClient();
                    client.Connect(host, ModbusEndianness.BigEndian); // LOGO! порт 502
                    client.WriteSingleCoil(unitId, coilAddr0, value);
                    return; // успех
                }
                catch (IOException)
                {
                    if (attempt == 2) throw;     // после повтора – отдаём наверх
                    await Task.Delay(200);        // короткая пауза и повтор
                }
            }
        }

        public static async Task SetAsync(string host, int port, byte unitId, int coilAddressOneBased, bool value)
        {
            await _gate.WaitAsync();
            try
            {
                await StartSignalWatcher.Instance.StopAsync();
                await Task.Delay(150);
                await WriteCoilOnce(host, unitId, (ushort)(coilAddressOneBased - 1), value);
            }
            finally
            {
                try { StartSignalWatcher.Instance.Start(); } catch { }
                _gate.Release();
            }
        }
    }
}
