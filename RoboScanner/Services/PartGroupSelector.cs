using System.Collections.Generic;
using System.Linq;
using RoboScanner.Models; // GroupRule

namespace RoboScanner.Services
{
    public static class PartGroupSelector
    {
        /// <summary>
        /// Выбор группы: "группа помещается в деталь".
        /// Условие: L > MaxX && W > MaxY && H > MaxZ (строго больше).
        /// Если подходит несколько — берём самую "крупную" (лексикографически по MaxX, MaxY, MaxZ).
        /// Если не подошла ни одна — возвращаем минимальную из валидных (fallback).
        /// В выбор попадают только активные правила с RobotGroup и всеми тремя размерами.
        /// </summary>
        public static GroupRule? SelectByMax(IEnumerable<GroupRule> allRules, double L, double W, double H)
        {
            if (allRules == null) return null;

            var rules = allRules
                .Where(r => r.IsActive)
                .Where(r => r.RobotGroup.HasValue)
                .Where(r => r.MaxX.HasValue && r.MaxY.HasValue && r.MaxZ.HasValue)
                .OrderBy(r => r.MaxX!.Value)
                .ThenBy(r => r.MaxY!.Value)
                .ThenBy(r => r.MaxZ!.Value)
                .ToList();

            // Ищем «лучшую подгонку»: самую большую тройку, которая всё ещё помещается
            GroupRule? best = null;
            foreach (var r in rules)
            {
                if (L > r.MaxX!.Value && W > r.MaxY!.Value && H > r.MaxZ!.Value)
                {
                    best = r; // продолжаем — найдём максимально близкую сверху
                }
            }

            if (best != null)
                return best;

            // Фоллбек: самая малая из валидных (чтобы было что выбрать всегда;
            // позже её роль возьмёт твоя "1-1-1")
            return rules.FirstOrDefault();
        }
    }
}
