using System;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;
using System.Net;

namespace RoboScanner.Services
{
    public sealed class ModbusInputWatcher : IAsyncDisposable, IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly byte _unitId;
        private readonly ushort _coilAddr0; // 0-based
        private readonly int _pollMs;

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private ModbusTcpClient? _client;

        public event EventHandler? RisingEdge;

        public ModbusInputWatcher(string host, int port, byte unitId, int coilAddressOneBased, int pollMs = 100)
        {
            if (coilAddressOneBased <= 0) throw new ArgumentOutOfRangeException(nameof(coilAddressOneBased));
            _host = host;
            _port = port;
            _unitId = unitId;
            _coilAddr0 = (ushort)(coilAddressOneBased - 1); // 8272 -> 8271
            _pollMs = pollMs;
        }

        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            _cts.Cancel();
            try { if (_loop != null) await _loop; } catch { }
            _loop = null;
            _cts.Dispose(); _cts = null;

            try { _client?.Dispose(); } catch { }
            _client = null;
        }

        private async Task RunAsync(CancellationToken ct)
        {
            bool prev = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_client == null)
                    {
                        _client = new ModbusTcpClient();
                        _client.Connect(_host, ModbusEndianness.BigEndian); // ← ровно 2 аргумента
                    }

                    bool curr = _client.ReadCoils(_unitId, _coilAddr0, 1)[0] != 0; // <-- фикc

                    if (curr && !prev)
                        RisingEdge?.Invoke(this, EventArgs.Empty);

                    prev = curr;
                    await Task.Delay(_pollMs, ct);
                }
                catch
                {
                    prev = false;
                    try { _client?.Dispose(); } catch { }
                    _client = null;
                    await Task.Delay(500, ct);
                }

            }
        }

        public void Dispose() => StopAsync().GetAwaiter().GetResult();
        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
