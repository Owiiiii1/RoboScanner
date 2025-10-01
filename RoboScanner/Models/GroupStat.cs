using System;
using System.ComponentModel;

namespace RoboScanner.Models
{
    public class GroupStat : INotifyPropertyChanged
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";

        private int _count;
        public int Count { get => _count; set { _count = value; OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(IsFull)); } }

        public int Limit { get; set; } = 150;
        public bool IsFull => Count >= Limit;

        public double? LastX { get; set; }
        public double? LastY { get; set; }
        public double? LastZ { get; set; }
        public DateTime? LastTime { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
