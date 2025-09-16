namespace RoboScanner.Models
{
    public class GroupRule
    {
        public int Index { get; set; }                   // 1..15
        public string Name { get; set; } = "Group";     // отображаемое имя
        public string Description { get; set; } = "";    // описание

        // Максимально допустимые размеры (мм). null => ось не ограничиваем.
        public double? MaxX { get; set; }
        public double? MaxY { get; set; }
        public double? MaxZ { get; set; }

        public GroupRule Clone() => new GroupRule
        {
            Index = Index,
            Name = Name,
            Description = Description,
            MaxX = MaxX,
            MaxY = MaxY,
            MaxZ = MaxZ
        };
    }
}
