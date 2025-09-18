using RoboScanner.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RoboScanner.Services
{
    public sealed class GroupsService
    {
        public static GroupsService Instance { get; } = new GroupsService();

        public ObservableCollection<GroupStat> ActiveGroups { get; } = new();
        private readonly Dictionary<int, GroupStat> _all = new();
        private readonly string _path = Path.Combine(AppContext.BaseDirectory, "group_counts.json");

        public int LimitPerGroup { get; set; } = 150;
        public bool IsPaused { get; private set; } = false;

        private GroupsService()
        {
            LoadCounts();
            RefreshFromRules(); // первичная синхронизация
        }

        public void RefreshFromRules()
        {
            var rules = RulesService.Instance.Rules;

            // ТЕПЕРЬ: активность только по чекбоксу IsActive
            var activeIndexes = rules
                .Where(r => r.IsActive)
                .Select(r => r.Index)
                .ToHashSet();

            // обновляем/создаём все группы из правил
            foreach (var r in rules)
            {
                if (!_all.TryGetValue(r.Index, out var gs))
                {
                    gs = new GroupStat { Index = r.Index, Name = r.Name, Limit = LimitPerGroup };
                    _all[r.Index] = gs;
                }
                else
                {
                    gs.Name = r.Name;           // имя могло измениться
                    gs.Limit = LimitPerGroup;
                }
            }

            // пересобираем коллекцию активных
            ActiveGroups.Clear();
            foreach (var idx in activeIndexes.OrderBy(i => i))
                ActiveGroups.Add(_all[idx]);

            RecomputePause();
            SaveCounts();
        }


        public (GroupStat stat, bool justReachedLimit) AddItemToGroup(int index, double? x, double? y, double? z, DateTime? at = null)
        {
            if (!_all.TryGetValue(index, out var gs))
            {
                gs = new GroupStat { Index = index, Name = $"Group {index}", Limit = LimitPerGroup };
                _all[index] = gs;
            }

            gs.LastX = x; gs.LastY = y; gs.LastZ = z; gs.LastTime = at ?? DateTime.Now;
            var wasFull = gs.IsFull;
            gs.Count++;

            var justReached = !wasFull && gs.IsFull;

            SaveCounts();
            RecomputePause();

            return (gs, justReached);
        }

        public void ResetGroup(int index, bool clearLast = true)
        {
            if (_all.TryGetValue(index, out var gs))
            {
                gs.Count = 0;
                if (clearLast)
                {
                    gs.LastX = null;
                    gs.LastY = null;
                    gs.LastZ = null;
                    gs.LastTime = null;
                }
                SaveCounts();
                RecomputePause();
            }
        }


        public void ResetAllCounts()
        {
            foreach (var g in _all.Values) g.Count = 0;
            SaveCounts();
            RecomputePause();
        }

        private void RecomputePause()
        {
            IsPaused = _all.Values.Any(g => g.IsFull);
        }

        private class Persist
        {
            public int Index { get; set; }
            public int Count { get; set; }
            public double? LastX { get; set; }
            public double? LastY { get; set; }
            public double? LastZ { get; set; }
            public DateTime? LastTime { get; set; }
        }

        private void SaveCounts()
        {
            try
            {
                var arr = _all.Values.Select(g => new Persist
                {
                    Index = g.Index,
                    Count = g.Count,
                    LastX = g.LastX,
                    LastY = g.LastY,
                    LastZ = g.LastZ,
                    LastTime = g.LastTime
                }).OrderBy(p => p.Index).ToArray();

                File.WriteAllText(_path, JsonSerializer.Serialize(arr, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore IO errors */ }
        }

        private void LoadCounts()
        {
            if (!File.Exists(_path)) return;
            try
            {
                var arr = JsonSerializer.Deserialize<Persist[]>(File.ReadAllText(_path)) ?? Array.Empty<Persist>();
                _all.Clear();
                foreach (var p in arr)
                {
                    _all[p.Index] = new GroupStat
                    {
                        Index = p.Index,
                        Name = $"Group {p.Index}",
                        Count = p.Count,
                        LastX = p.LastX,
                        LastY = p.LastY,
                        LastZ = p.LastZ,
                        LastTime = p.LastTime,
                        Limit = LimitPerGroup
                    };
                }
            }
            catch { /* ignore */ }
        }
    }
}
