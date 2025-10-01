using System;
using System.ComponentModel;

namespace RoboScanner.Services
{
    public enum OperationState { Wait, Scanning, Done }

    public sealed class AppStateService : INotifyPropertyChanged
    {
        public static AppStateService Instance { get; } = new();

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { if (_isRunning != value) { _isRunning = value; OnChanged(nameof(IsRunning)); } }
        }

        private OperationState _opState = OperationState.Wait;
        public OperationState OpState
        {
            get => _opState;
            set { if (_opState != value) { _opState = value; OnChanged(nameof(OpState)); } }
        }

        // (как было) — последние результаты сканирования
        private int? _lastGroup;
        public int? LastGroup { get => _lastGroup; set { _lastGroup = value; OnChanged(nameof(LastGroup)); } }
        private double? _lastX, _lastY, _lastZ;
        public double? LastX { get => _lastX; set { _lastX = value; OnChanged(nameof(LastX)); } }
        public double? LastY { get => _lastY; set { _lastY = value; OnChanged(nameof(LastY)); } }
        public double? LastZ { get => _lastZ; set { _lastZ = value; OnChanged(nameof(LastZ)); } }
        private DateTime? _lastTime;
        public DateTime? LastTime { get => _lastTime; set { _lastTime = value; OnChanged(nameof(LastTime)); } }

        public void SetLastScan(int group, double x, double y, double z, DateTime at)
        {
            LastGroup = group; LastX = x; LastY = y; LastZ = z; LastTime = at;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public DateTime SessionStarted { get; } = DateTime.Now;

    }
}
