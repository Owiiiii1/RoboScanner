using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;

namespace RoboScanner.Services
{
    /// <summary>
    /// Управление выходами ПЛК по Modbus/TCP.
    /// - Подключение строго к host:port
    /// - Таймауты: ожидание семафора и выполнение операции (без «вечных» зависаний)
    /// - Ретрай по сетевым исключениям
    /// </summary>
    public static class ModbusOutputService
    {
        private static readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>Короткий «пульс»: coil=1 → выдержка → coil=0.</summary>
        public static async Task PulseAsync(string host, int port, byte unitId, int coilAddressOneBased, int? pulseSeconds)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host is empty", nameof(host));
            if (coilAddressOneBased <= 0) throw new ArgumentOutOfRangeException(nameof(coilAddressOneBased));

            ushort coil0 = checked((ushort)(coilAddressOneBased - 1));
            int holdSec = pulseSeconds.GetValueOrDefault(1);
            if (holdSec < 0) holdSec = 0;

            await ExecWithGateAsync(async () =>
            {
                await WriteCoilWithRetryAsync(host, port, unitId, coil0, true);
                if (holdSec > 0)
                    await Task.Delay(TimeSpan.FromSeconds(holdSec));
                await WriteCoilWithRetryAsync(host, port, unitId, coil0, false);
            });
        }

        /// <summary>Принудительно установить значение катушки.</summary>
        public static async Task SetAsync(string host, int port, byte unitId, int coilAddressOneBased, bool value)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host is empty", nameof(host));
            if (coilAddressOneBased <= 0) throw new ArgumentOutOfRangeException(nameof(coilAddressOneBased));

            ushort coil0 = checked((ushort)(coilAddressOneBased - 1));

            await ExecWithGateAsync(async () =>
            {
                await WriteCoilWithRetryAsync(host, port, unitId, coil0, value);
            });
        }

        // ================== internal ==================

        /// <summary>
        /// Захват «ворот» с таймаутом и общий таймаут операции,
        /// чтобы исключить бесконечные зависания.
        /// </summary>
        private static async Task ExecWithGateAsync(Func<Task> action)
        {
            if (!await _gate.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Modbus gate wait timed out.");

            try
            {
                await action().WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Запись катушки с коротким ретраем (3 попытки).</summary>
        private static async Task WriteCoilWithRetryAsync(string host, int port, byte unitId, ushort coil0, bool value)
        {
            Exception? last = null;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await WriteCoilOnceAsync(host, port, unitId, coil0, value);
                    return;
                }
                catch (Exception ex) when (ex is SocketException ||
                                           ex is TimeoutException ||
                                           ex is IOException ||
                                           ex is ObjectDisposedException)
                {
                    last = ex;
                    await Task.Delay(200);
                }
            }

            throw new Exception($"Modbus write failed after retries (host={host}, port={port}, unit={unitId}, coil0={coil0}, value={value}).", last);
        }

        /// <summary>
        /// Разовая запись катушки: быстрый probe-connect (2s), затем Modbus Connect (2s) и Write.
        /// </summary>
        private static async Task WriteCoilOnceAsync(string host, int port, byte unitId, ushort coil0, bool value)
        {
            // 1) Resolve host → IPv4 предпочтительно
            IPAddress ip;
            if (!IPAddress.TryParse(host, out ip))
            {
                var all = await Dns.GetHostAddressesAsync(host);
                ip = Array.Find(all, a => a.AddressFamily == AddressFamily.InterNetwork) ?? all[0];
            }
            var endpoint = new IPEndPoint(ip, port);

            // 2) Быстрый probe TCP (2s), чтобы не залипнуть
            using (var probe = new TcpClient())
            {
                var probeTask = probe.ConnectAsync(ip, port);
                if (!probeTask.Wait(TimeSpan.FromSeconds(2)))
                    throw new TimeoutException($"Could not connect to {endpoint} within 2s.");
                // закрываем пробный коннект — дальше коннектится сам Modbus клиент
            }

            // 3) Modbus Connect (2s) + Write
            using var client = new ModbusTcpClient();
            await Task.Run(() => client.Connect(endpoint, ModbusEndianness.BigEndian))
                      .WaitAsync(TimeSpan.FromSeconds(2));

            client.WriteSingleCoil(unitId, coil0, value);
        }
    }
}
