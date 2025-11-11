using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RoboScanner.Properties;
using System.IO;

namespace RoboScanner.Services
{
    /// <summary>
    /// Глобальный сервис опроса трёх лазерных датчиков через AL1120 (ENIP).
    /// Берёт настройки из Properties.Settings и крутится в фоне.
    /// </summary>
    public sealed class LaserService : IDisposable, IAsyncDisposable
    {
        public static LaserService Instance { get; } = new();

        private EnipClient? _client;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly object _sync = new();
        private readonly double?[] _rawMm = new double?[3]; // сырое значение каждого датчика (мм)

        // Скользящее среднее по 10 последним измерениям для каждого датчика
        const int SmoothWindow = 10;
        double?[,] _smoothBuf = new double?[3, SmoothWindow];
        int[] _smoothCount = new int[3];   // сколько реально значений есть (до 10)
        int[] _smoothIndex = new int[3];   // куда писать следующее значение

        private LaserService() { }

        /// <summary>Сырой миллиметровый результат конкретного датчика (0..2), без поправки, или null если нет данных.</summary>
        public double? GetRawMm(int index)
        {
            lock (_sync)
                return _rawMm[index];
        }

        /// <summary>Получить значения по осям X/Y/Z с учётом назначения датчиков (без оффсетов).</summary>
        public (double? x, double? y, double? z) GetAxesRaw()
        {
            var s = Settings.Default;

            int sx = s.LaserAxisXSensor - 1; // 1..3 -> 0..2
            int sy = s.LaserAxisYSensor - 1;
            int sz = s.LaserAxisZSensor - 1;

            double? x = (sx is >= 0 and < 3) ? GetRawMm(sx) : null;
            double? y = (sy is >= 0 and < 3) ? GetRawMm(sy) : null;
            double? z = (sz is >= 0 and < 3) ? GetRawMm(sz) : null;

            return (x, y, z);
        }

        /// <summary>Значения X/Y/Z с учётом ручных оффсетов (то, что будем использовать как размер детали).</summary>
        public (double? x, double? y, double? z) GetAxesWithOffset()
        {
            var s = Settings.Default;
            var (rx, ry, rz) = GetAxesRaw();

            double? x = rx.HasValue ? s.LaserAxisXOffsetMm - rx : null;
            double? y = ry.HasValue ? s.LaserAxisYOffsetMm - ry : null;
            double? z = rz.HasValue ? s.LaserAxisZOffsetMm - rz : null;

            return (x, y, z);
        }

        public void ResetAveraging()
        {
            lock (_sync)
            {
                // сбрасываем усреднённые значения
                _rawMm[0] = _rawMm[1] = _rawMm[2] = null;

                // сбрасываем окна усреднения
                for (int i = 0; i < 3; i++)
                {
                    _smoothCount[i] = 0;
                    _smoothIndex[i] = 0;
                    for (int k = 0; k < SmoothWindow; k++)
                        _smoothBuf[i, k] = null;
                }
            }
        }


        public void Start()
        {
            if (_loopTask != null && !_loopTask.IsCompleted) return;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public async Task RestartAsync()
        {
            await StopAsync();
            Start();
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;

            try
            {
                _cts.Cancel();
                if (_loopTask != null)
                    await _loopTask;
            }
            catch { /* ignore */ }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _loopTask = null;

                _client?.Dispose();
                _client = null;

                lock (_sync)
                {
                    _rawMm[0] = _rawMm[1] = _rawMm[2] = null;
                }
            }
        }

        public void Dispose() => StopAsync().GetAwaiter().GetResult();
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        // === основной цикл опроса ===
        private async Task LoopAsync(CancellationToken ct)
        {
            // Хост берём один раз (если его поменять — проще перезапустить программу)
            string host = Settings.Default.LaserHost?.Trim() ?? "";
            if (string.IsNullOrEmpty(host))
                return;

            try
            {
                _client = new EnipClient(host);
                await _client.ConnectAsync();

                while (!ct.IsCancellationRequested)
                {
                    var s = Settings.Default;

                    // Период опроса
                    int pollMs = s.LaserPollMs;
                    if (pollMs < 50) pollMs = 50;

                    // Оффсеты датчиков
                    int o1 = s.LaserSensor1Offset;
                    if (o1 <= 0) o1 = 118; // дефолт для первого, как раньше

                    int[] offsets =
                    {
        o1,
        s.LaserSensor2Offset,
        s.LaserSensor3Offset
    };

                    // 🔴 ОДИН ОБЩИЙ INSTANCE ДЛЯ ВСЕХ СЕНСОРОВ
                    ushort inst = (ushort)s.LaserSensor1Inst;
                    if (inst == 0)
                    {
                        // если инстанс не задан — данных нет по всем трём
                        lock (_sync)
                        {
                            _rawMm[0] = _rawMm[1] = _rawMm[2] = null;
                        }
                        await Task.Delay(pollMs, ct);
                        continue;
                    }

                    try
                    {
                        // один раз получаем массив raw для общего инстанса
                        byte[] raw = await _client.GetAssemblyDataAsync(inst);

                        for (int i = 0; i < 3; i++)
                        {
                            int offset = offsets[i];

                            double? mm = ParseDistanceMm(raw, offset, i);
                            lock (_sync)
                            {
                                if (mm.HasValue)
                                {
                                    // кладём новое значение в кольцевой буфер
                                    int idx = _smoothIndex[i];
                                    _smoothBuf[i, idx] = mm.Value;

                                    if (_smoothCount[i] < SmoothWindow)
                                        _smoothCount[i]++;

                                    _smoothIndex[i] = (idx + 1) % SmoothWindow;

                                    // считаем среднее по имеющимся значениям
                                    double sum = 0;
                                    int cnt = 0;
                                    for (int k = 0; k < _smoothCount[i]; k++)
                                    {
                                        var v = _smoothBuf[i, k];
                                        if (v.HasValue)
                                        {
                                            sum += v.Value;
                                            cnt++;
                                        }
                                    }

                                    _rawMm[i] = cnt > 0 ? sum / cnt : (double?)null;
                                }
                                else
                                {
                                    // если текущее чтение невалидно — чистим буфер и считаем, что данных нет
                                    for (int k = 0; k < SmoothWindow; k++)
                                        _smoothBuf[i, k] = null;

                                    _smoothCount[i] = 0;
                                    _smoothIndex[i] = 0;
                                    _rawMm[i] = null;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error("Laser",
                            $"Failed to read common instance {inst}", ex);

                        lock (_sync)
                        {
                            _rawMm[0] = _rawMm[1] = _rawMm[2] = null;

                            for (int i = 0; i < 3; i++)
                            {
                                _smoothCount[i] = 0;
                                _smoothIndex[i] = 0;
                                for (int k = 0; k < SmoothWindow; k++)
                                    _smoothBuf[i, k] = null;
                            }
                        }
                    }

                    await Task.Delay(pollMs, ct);
                }

            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Laser", "LaserService main loop error", ex);
            }
        }


        // Вытаскиваем расстояние из массива байтов RAW для AL1120+O1D102
        // offset берём из настроек (для каждого датчика свой)
        private static double? ParseDistanceMm(byte[] raw, int offset, int sensorIndex)
        {
            if (raw == null || raw.Length < 2) return null;

            if (offset >= 0 && raw.Length >= offset + 2)
            {
                byte hi = raw[offset];
                byte lo = raw[offset + 1];
                ushort value = (ushort)((hi << 8) | lo); // BigEndian

                // «разумный» диапазон расстояний, можно подправить при желании
                if (value >= 5 && value <= 10000)
                    return value;
            }

            // если offset ещё не задан (например, = -1) или там мусор — просто вернём null
            // автоопределение будем делать отдельным сервисом по кнопке в настройках
            LogService.Instance.Warn("Laser",
                $"No valid offset for sensor {sensorIndex + 1} (offset={offset}), rawLen={raw.Length}");

            return null;
        }

    }

    // ====== MINIMAL EnipClient (из тестовой проги) ======
    // ====== EtherNet/IP EnipClient для AL1120 ======
    public sealed class EnipClient : IDisposable
    {
        private readonly string ip;
        private TcpClient? tcp;
        private NetworkStream? ns;
        private uint session;

        const ushort CMD_RegisterSession = 0x0065;
        const ushort CMD_SendRRData = 0x006F;
        const ushort CMD_Unregister = 0x0066;

        public EnipClient(string ip)
        {
            this.ip = ip;
        }

        public async Task ConnectAsync()
        {
            tcp = new TcpClient();
            await tcp.ConnectAsync(ip, 44818);
            ns = tcp.GetStream();

            // RegisterSession: отправляем команду с 4 байтами payload (protocol version = 1)
            byte[] payload = new byte[4];
            WriteUInt16LE(payload, 0, 1); // protocol version
            WriteUInt16LE(payload, 2, 0); // options

            byte[] header = new byte[24];
            WriteUInt16LE(header, 0, CMD_RegisterSession);
            WriteUInt16LE(header, 2, (ushort)payload.Length);
            WriteUInt32LE(header, 4, 0); // session = 0

            await ns.WriteAsync(header, 0, header.Length);
            await ns.WriteAsync(payload, 0, payload.Length);

            // ответ
            byte[] respHeader = new byte[24];
            await ReadExactAsync(ns, respHeader, 0, 24);
            ushort cmd = ReadUInt16LE(respHeader, 0);
            ushort len = ReadUInt16LE(respHeader, 2);

            if (cmd != CMD_RegisterSession)
                throw new Exception($"Unexpected ENIP cmd {cmd} on RegisterSession");

            byte[] respPayload = new byte[len];
            if (len > 0)
                await ReadExactAsync(ns, respPayload, 0, len);

            session = ReadUInt32LE(respHeader, 4);
            if (session == 0)
                throw new Exception("Failed to register ENIP session (session=0)");
        }

        /// <summary>
        /// Читает Assembly Data (Class 0x04, Instance=<inst>, Attribute 3) через SendRRData.
        /// Возвращает чистые процессные данные (PD), без CIP-заголовка.
        /// </summary>
        public async Task<byte[]> GetAssemblyDataAsync(ushort instance)
        {
            if (ns == null) throw new InvalidOperationException("Not connected");

            // CIP: Get_Attribute_Single(0x0E), path: 20 04 24 <inst> 30 03
            byte service = 0x0E;
            byte[] path =
            {
            0x20, 0x04,            // Class 0x04 (Assembly)
            0x24, (byte)instance,  // Instance
            0x30, 0x03             // Attribute 3 (Data)
        };
            byte pathWords = (byte)(path.Length / 2);

            byte[] cip = new byte[2 + path.Length];
            cip[0] = service;
            cip[1] = pathWords;
            Buffer.BlockCopy(path, 0, cip, 2, path.Length);

            // Заворачиваем CIP в CPF (unconnected)
            byte[] cpf = BuildCpfUnconnected(cip);

            // Отправляем как SendRRData
            byte[] encapReply = await SendRecvEncapAsync(CMD_SendRRData, cpf);

            // Извлекаем CIP-ответ
            byte[] cipReply = ExtractCipFromRrData(encapReply);

            if (cipReply.Length < 4)
                throw new Exception("Bad CIP reply (len < 4)");

            byte genStatus = cipReply[2];
            if (genStatus != 0)
                throw new Exception($"CIP general status = {genStatus}");

            // После первых 4 байт CIP-ответа идут данные атрибута
            int dataLen = cipReply.Length - 4;
            byte[] data = new byte[dataLen];
            Buffer.BlockCopy(cipReply, 4, data, 0, dataLen);
            return data;
        }

        public void Dispose()
        {
            try
            {
                if (ns != null && session != 0)
                {
                    // UnregisterSession
                    SendRecvEncapAsync(CMD_Unregister, Array.Empty<byte>())
                        .GetAwaiter().GetResult();
                }
            }
            catch { /* ignore */ }

            try { ns?.Dispose(); } catch { }
            try { tcp?.Close(); } catch { }

            ns = null;
            tcp = null;
            session = 0;
        }

        // ================= ВНУТРЕННИЕ МЕТОДЫ =================

        private async Task<byte[]> SendRecvEncapAsync(ushort cmd, byte[] payload)
        {
            if (ns == null) throw new InvalidOperationException("Not connected");

            // Заголовок ENIP
            byte[] header = new byte[24];
            WriteUInt16LE(header, 0, cmd);
            WriteUInt16LE(header, 2, (ushort)payload.Length);
            WriteUInt32LE(header, 4, session);
            // остальные поля — 0

            await ns.WriteAsync(header, 0, header.Length);
            if (payload.Length > 0)
                await ns.WriteAsync(payload, 0, payload.Length);

            // читаем ответ
            byte[] respHeader = new byte[24];
            await ReadExactAsync(ns, respHeader, 0, 24);

            ushort rCmd = ReadUInt16LE(respHeader, 0);
            ushort rLen = ReadUInt16LE(respHeader, 2);
            if (rCmd != cmd)
                throw new Exception($"Unexpected ENIP cmd {rCmd} (expected {cmd})");

            byte[] all = new byte[24 + rLen];
            Buffer.BlockCopy(respHeader, 0, all, 0, 24);
            if (rLen > 0)
                await ReadExactAsync(ns, all, 24, rLen);

            return all; // header + payload
        }

        private static byte[] BuildCpfUnconnected(byte[] cip)
        {
            using var ms = new MemoryStream();

            // Interface handle (UINT, 4 bytes) = 0 (CIP)
            WriteUInt32LE(ms, 0);

            // Timeout (UINT, 2 bytes) = 0
            WriteUInt16LE(ms, 0);

            // Item count (UINT, 2 bytes) = 2
            WriteUInt16LE(ms, 2);

            // Item 1: Null Address (Type=0x0000, Length=0)
            WriteUInt16LE(ms, 0x0000);
            WriteUInt16LE(ms, 0x0000);

            // Item 2: Unconnected Data Item (Type=0x00B2, Length=cip.Length)
            WriteUInt16LE(ms, 0x00B2);
            WriteUInt16LE(ms, (ushort)cip.Length);
            ms.Write(cip, 0, cip.Length);

            return ms.ToArray();
        }

        private static byte[] ExtractCipFromRrData(byte[] encapReply)
        {
            int ofs = 24; // пропускаем ENIP-заголовок

            uint ifaceHandle = ReadUInt32LE(encapReply, ofs); ofs += 4;
            ushort timeout = ReadUInt16LE(encapReply, ofs); ofs += 2;
            ushort itemCount = ReadUInt16LE(encapReply, ofs); ofs += 2;

            byte[] cip = Array.Empty<byte>();

            for (int i = 0; i < itemCount; i++)
            {
                ushort type = ReadUInt16LE(encapReply, ofs); ofs += 2;
                ushort len = ReadUInt16LE(encapReply, ofs); ofs += 2;

                if (type == 0x00B2)
                {
                    cip = new byte[len];
                    Buffer.BlockCopy(encapReply, ofs, cip, 0, len);
                }

                ofs += len;
            }

            if (cip.Length == 0)
                throw new Exception("No CIP item (0x00B2) in RRData reply");

            return cip;
        }

        // ==== общие helper-ы ====

        private static async Task ReadExactAsync(NetworkStream ns, byte[] buf, int off, int len)
        {
            int read = 0;
            while (read < len)
            {
                int r = await ns.ReadAsync(buf, off + read, len - read);
                if (r <= 0) throw new Exception("Connection closed");
                read += r;
            }
        }

        // little-endian helpers для массивов
        private static void WriteUInt16LE(byte[] b, int off, ushort v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)(v >> 8);
        }
        private static void WriteUInt32LE(byte[] b, int off, uint v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
            b[off + 2] = (byte)((v >> 16) & 0xFF);
            b[off + 3] = (byte)(v >> 24);
        }

        // перегрузки для MemoryStream
        private static void WriteUInt16LE(MemoryStream ms, ushort v)
        {
            ms.WriteByte((byte)(v & 0xFF));
            ms.WriteByte((byte)(v >> 8));
        }
        private static void WriteUInt32LE(MemoryStream ms, uint v)
        {
            ms.WriteByte((byte)(v & 0xFF));
            ms.WriteByte((byte)((v >> 8) & 0xFF));
            ms.WriteByte((byte)((v >> 16) & 0xFF));
            ms.WriteByte((byte)(v >> 24));
        }

        private static ushort ReadUInt16LE(byte[] b, int off) =>
            (ushort)(b[off] | (b[off + 1] << 8));

        private static uint ReadUInt32LE(byte[] b, int off) =>
            (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
    }
}
