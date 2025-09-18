using System.ComponentModel;

namespace RoboScanner.Models
{
    public class GroupRule : INotifyPropertyChanged
    {
        public int Index { get; set; }                   // 1..15

        private string _name = "Group";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(nameof(Description)); } }
        }

        // Максимально допустимые размеры (мм). null => ось не ограничиваем.
        private double? _maxX;
        public double? MaxX
        {
            get => _maxX;
            set { if (_maxX != value) { _maxX = value; OnPropertyChanged(nameof(MaxX)); } }
        }

        private double? _maxY;
        public double? MaxY
        {
            get => _maxY;
            set { if (_maxY != value) { _maxY = value; OnPropertyChanged(nameof(MaxY)); } }
        }

        private double? _maxZ;
        public double? MaxZ
        {
            get => _maxZ;
            set { if (_maxZ != value) { _maxZ = value; OnPropertyChanged(nameof(MaxZ)); } }
        }

        // НОВОЕ: активна ли группа в нашем приложении
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
        }

        // НОВОЕ: номер физической "группы робота" (бункера/катушки реле)
        private int? _robotGroup;
        public int? RobotGroup
        {
            get => _robotGroup;
            set { if (_robotGroup != value) { _robotGroup = value; OnPropertyChanged(nameof(RobotGroup)); } }
        }

        public GroupRule Clone() => new GroupRule
        {
            Index = Index,
            Name = Name,
            Description = Description,
            MaxX = MaxX,
            MaxY = MaxY,
            MaxZ = MaxZ,
            IsActive = IsActive,
            RobotGroup = RobotGroup
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
