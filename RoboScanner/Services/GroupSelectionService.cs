using System.Collections.Generic;
using System.Linq;
using RoboScanner.Models; // GroupRule

namespace RoboScanner.Services
{
    /// <summary>
    /// Выбор группы по размерам детали.
    /// Возвращает непосредственно GroupRule.
    /// Участвуют только активные правила с RobotGroup и заданными MaxX/MaxY/MaxZ.
    /// </summary>
    public static class GroupSelectionService
    {
        /// <summary>
        /// Основной режим: выбор по ВЕРХНИМ пределам.
        /// Деталь подходит, если L <= MaxX && W <= MaxY && H <= MaxZ.
        /// Идём снизу-вверх по потолкам и берём первую подходящую.
        /// Если не нашли — возвращаем минимальную из существующих (fallback).
        /// </summary>
        public static GroupRule? SelectRuleByMax(IEnumerable<GroupRule> allRules, double L, double W, double H)
        {
            if (allRules == null) return null;

            var rules = allRules
                .Where(r => r.IsActive)
                .Where(r => r.RobotGroup.HasValue)
                .Where(r => r.MaxX.HasValue && r.MaxY.HasValue && r.MaxZ.HasValue)
                .OrderBy(r => r.MaxX ?? double.MaxValue)
                .ThenBy(r => r.MaxY ?? double.MaxValue)
                .ThenBy(r => r.MaxZ ?? double.MaxValue)
                .ToList();

            foreach (var r in rules)
            {
                if (L <= r.MaxX!.Value && W <= r.MaxY!.Value && H <= r.MaxZ!.Value)
                    return r; // первая (самая «малая») подходящая
            }

            // fallback: самая малая существующая
            return rules.FirstOrDefault();
        }

        /// <summary>
        /// Вариант по МИНИМУМАМ (если понадобится где-то отдельно).
        /// </summary>
        public static GroupRule? SelectRuleByMin(IEnumerable<GroupRule> allRules, double L, double W, double H)
        {
            if (allRules == null) return null;

            var rules = allRules
                .Where(r => r.IsActive)
                .Where(r => r.RobotGroup.HasValue)
                .Where(r => r.MaxX.HasValue && r.MaxY.HasValue && r.MaxZ.HasValue)
                .OrderBy(r => r.MaxX ?? double.MaxValue)
                .ThenBy(r => r.MaxY ?? double.MaxValue)
                .ThenBy(r => r.MaxZ ?? double.MaxValue)
                .ToList();

            foreach (var r in rules)
            {
                if (L > r.MaxX!.Value && W > r.MaxY!.Value && H > r.MaxZ!.Value)
                    return r;
            }

            return rules.FirstOrDefault();
        }
    }
}
