using RoboScanner.Localization;
using RoboScanner.Infrastructure;                 // +++
using System;
using System.Collections.Generic;
using System.IO;                                  // +++
using System.Linq;                                // +++
using System.Text.Json;                           // +++
using System.Text.Json.Serialization;             // +++

namespace RoboScanner.Models
{
    public sealed class RobotGroup
    {
        public int Index { get; set; }              // 1..16 (16 — "Старт робот")
        public string Name { get; set; } = "";

        // Подключение Modbus TCP
        public string Host { get; set; } = "";      // IP Waveshare, напр. 192.168.1.100
        public int Port { get; set; } = 502;        // TCP порт (обычно 502)
        public byte UnitId { get; set; } = 1;       // Slave ID (обычно 1)
        public int TimeoutMs { get; set; } = 1500;  // Таймаут запроса

        // Главный канал группы (реле)
        public ushort? PrimaryCoilAddress { get; set; } // 0..29 для 30-канального Waveshare

        // Сколько держать реле включённым, после чего выключить (секунды).
        // Если 0 или null — "держать включенным" (импульс не применяется).
        public int? PulseSeconds { get; set; }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                // Если это «Старт робот» (Index == 16) и имя не задано явно или стоит одно из дефолтных значений,
                // показываем локализованную строку.
                if (Index == 16)
                {
                    var n = Name?.Trim();
                    if (string.IsNullOrEmpty(n) ||
                        n.Equals("Старт робот", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("Start robot", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("StartRobot", StringComparison.OrdinalIgnoreCase))
                    {
                        return RoboScanner.Localization.Loc.Get("Groups.StartRobot");
                    }
                }
                return Name ?? "";
            }
        }
    }

    // Структура файла JSON
    internal sealed class GroupsStore                    // +++
    {
        public int SelectedIndex { get; set; } = 1;
        public List<RobotGroup> Groups { get; set; } = new();

        // карта привязок: ScanGroupId -> RobotGroupIndex
        public Dictionary<int, int> ScanToRobot { get; set; } = new(); // +++
    }

    public static class RobotGroups
    {
        private static readonly Dictionary<int, RobotGroup> _groups;
        private static Dictionary<int, int> _scanToRobot = new(); // ScanGroupId -> RobotGroupIndex (1..16)

        public static void LinkScanGroup(int scanGroupId, int robotGroupIndex) // +++
        {
            if (scanGroupId < 1) return;
            if (!_groups.ContainsKey(robotGroupIndex)) return;
            _scanToRobot[scanGroupId] = robotGroupIndex;
            Save();
        }

        public static void UnlinkScanGroup(int scanGroupId) // +++
        {
            if (_scanToRobot.Remove(scanGroupId))
                Save();
        }

        public static int? GetRobotIndexForScanGroup(int scanGroupId) // +++
            => _scanToRobot.TryGetValue(scanGroupId, out var idx) ? idx : (int?)null;

        public static RobotGroup GetForScanGroup(int scanGroupId) // +++
        {
            if (_scanToRobot.TryGetValue(scanGroupId, out var idx) && _groups.ContainsKey(idx))
                return _groups[idx];
            // fallback: использовать глобально выбранную, если привязки нет
            return Selected;
        }

        private static readonly object _sync = new();     // +++

        // Текущее выбранное значение (по умолчанию 1)
        public static int SelectedIndex { get; private set; } = 1;

        // Удобный доступ к объекту текущей группы
        public static RobotGroup Selected => _groups.TryGetValue(SelectedIndex, out var g) ? g : _groups[1];

        // Событие, чтобы другие вью могли подхватывать смену выбора
        public static event EventHandler<int>? SelectedChanged;

        private static readonly JsonSerializerOptions _jsonOptions = new()   // +++
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        static RobotGroups()
        {
            AppPaths.EnsureBase();                    // +++

            _groups = new Dictionary<int, RobotGroup>();

            // Попробуем загрузить с диска
            if (TryLoadFromDisk())                   // +++
                return;

            // Иначе — дефолтная инициализация
            for (int i = 1; i <= 15; i++)
            {
                _groups[i] = new RobotGroup
                {
                    Index = i,
                    Name = $"Group {i}",
                    Port = 502,
                    UnitId = 1,
                    TimeoutMs = 1500
                };
            }

            // Доп. группа №16 — "Старт робот"
            var startName = Loc.Get("Groups.StartRobot");   // как у тебя
            if (string.IsNullOrWhiteSpace(startName) || startName == "Groups.StartRobot")
                startName = "Start robot";                  // безопасный фолбэк на случай ранней инициализации

            _groups[16] = new RobotGroup
            {
                Index = 16,
                Name = startName,
                Port = 502,
                UnitId = 1,
                TimeoutMs = 1500
            };

            // после создания 1..15 и 16-й
            _groups[17] = new RobotGroup
            {
                Index = 17,
                Name = "InputTrigger",   // (или Loc.Get("Groups.InputTrigger") если хочешь локализацию)
                Port = 502,
                UnitId = 1,
                TimeoutMs = 1500
            };


            SelectedIndex = 1;
            Save();                                     // +++
        }

        public static IReadOnlyDictionary<int, RobotGroup> All => _groups;

        public static RobotGroup Get(int index) => _groups[index];

        public static void Update(RobotGroup g)
        {
            if (g.Index < 1 || g.Index > 17) return;
            lock (_sync)                                   // +++
            {
                _groups[g.Index] = g;
                Save();                                    // +++
            }
        }

        public static void SetSelected(int index)
        {
            if (!_groups.ContainsKey(index)) return;
            if (SelectedIndex == index) return;
            SelectedIndex = index;
            SelectedChanged?.Invoke(null, index);

            lock (_sync) Save();                           // +++
        }

        // Публично: принудительно перечитать файл (если сделали импорт)
        public static void ReloadFromDisk()               // +++
        {
            lock (_sync) TryLoadFromDisk();
        }

        // ====== приватные помощники JSON ======

        private static bool TryLoadFromDisk()             // +++
        {
            try
            {
                var path = AppPaths.GroupsJson;
                if (!File.Exists(path)) return false;

                var json = File.ReadAllText(path);
                var store = JsonSerializer.Deserialize<GroupsStore>(json, _jsonOptions);
                if (store == null || store.Groups == null || store.Groups.Count == 0)
                    return false;

                // нормализуем 1..16 (защита от ручного редактирования)
                _groups.Clear();
                foreach (var g in store.Groups.Where(x => x.Index >= 1 && x.Index <= 17)
                                               .GroupBy(x => x.Index)
                                               .Select(x => x.First()))
                {
                    _groups[g.Index] = g;
                }

                for (int i = 1; i <= 17; i++)
                {
                    if (!_groups.ContainsKey(i))
                    {
                        var name =
                            i == 16 ? "Start robot" :
                            i == 17 ? "InputTrigger" :
                            $"Group {i}";

                        _groups[i] = new RobotGroup
                        {
                            Index = i,
                            Name = name,
                            Port = 502,
                            UnitId = 1,
                            TimeoutMs = 1500
                        };
                    }
                }

                if (_groups.TryGetValue(17, out var g17) &&
    string.Equals(g17.Name, "Group 17", StringComparison.OrdinalIgnoreCase))
                {
                    g17.Name = "InputTrigger";
                }

                SelectedIndex = (store.SelectedIndex >= 1 && store.SelectedIndex <= 17)
                                ? store.SelectedIndex : 1;

                // Прочитаем карту привязок(может отсутствовать в старом файле)
                _scanToRobot = store.ScanToRobot ?? new Dictionary<int, int>(); // +++

                // зачистим неверные индексы (оставим только 1..16)
                _scanToRobot = _scanToRobot
                    .Where(p => p.Value >= 1 && p.Value <= 17)
                    .ToDictionary(p => p.Key, p => p.Value); // +++

                return true;
            }
            catch
            {
                // тут можно залогировать
                return false;
            }
        }

        private static void Save()                        // +++
        {
            try
            {
                var path = AppPaths.GroupsJson;
                var store = new GroupsStore
                {
                    SelectedIndex = SelectedIndex,
                    Groups = _groups.Values.OrderBy(g => g.Index).ToList(),
                    ScanToRobot = _scanToRobot // +++
                };
                var json = JsonSerializer.Serialize(store, _jsonOptions);

                // на всякий — резервная копия
                if (File.Exists(path))
                {
                    var bak = path + ".bak";
                    File.Copy(path, bak, overwrite: true);
                }

                File.WriteAllText(path, json);
            }
            catch
            {
                // тут можно залогировать
            }
        }
    }
}
