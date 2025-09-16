using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using RoboScanner.Models;

namespace RoboScanner.Services
{
    public sealed class RulesService
    {
        public static RulesService Instance { get; } = new RulesService();
        public ObservableCollection<GroupRule> Rules { get; } = new();

        private readonly string _path = Path.Combine(AppContext.BaseDirectory, "rules.json");

        private RulesService() { Load(); }

        public void ResetDefaults()
        {
            Rules.Clear();
            // Пример дефолтов: растущие MaxX через 10 мм; Y/Z не ограничиваем (null)
            for (int i = 0; i < 15; i++)
            {
                Rules.Add(new GroupRule
                {
                    Index = i + 1,
                    Name = $"Группа {i + 1}",
                    Description = "",
                    MaxX = (i + 1) * 10.0,
                    MaxY = null,
                    MaxZ = null
                });
            }
        }

        public void Save(string? path = null)
        {
            var p = path ?? _path;
            var list = Rules.OrderBy(r => r.Index).ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(p, json);
        }

        public void Load(string? path = null)
        {
            var p = path ?? _path;
            if (!File.Exists(p)) { ResetDefaults(); return; }

            var json = File.ReadAllText(p);
            var list = JsonSerializer.Deserialize<GroupRule[]>(json) ?? Array.Empty<GroupRule>();
            Rules.Clear();
            foreach (var r in list.OrderBy(r => r.Index)) Rules.Add(r);
        }
    }
}
