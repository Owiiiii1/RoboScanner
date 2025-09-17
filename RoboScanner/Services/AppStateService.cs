using System.ComponentModel;

namespace RoboScanner.Services
{
    public sealed class AppStateService : INotifyPropertyChanged
    {
        public static AppStateService Instance { get; } = new AppStateService();

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { if (_isRunning != value) { _isRunning = value; PropertyChanged?.Invoke(this, new(nameof(IsRunning))); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private AppStateService() { }
    }
}
