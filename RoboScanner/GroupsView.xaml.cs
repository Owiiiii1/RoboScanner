using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RoboScanner.Services;

namespace RoboScanner.Views
{
    public partial class GroupsView : UserControl
    {
        private readonly GroupsService _groups = GroupsService.Instance;
        private readonly LogService _log = LogService.Instance;
        private readonly AppStateService _app = AppStateService.Instance;
        private string S(string key) => (string)FindResource(key);

        public GroupsView()
        {
            InitializeComponent();
            DataContext = _groups;
            _groups.RefreshFromRules(); // синхронизируем список активных групп
        }

        // Клик по большой карточке: если переполнена — предложим сбросить счётчик
        private void GroupCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not int idx) return;

            var stat = _groups.ActiveGroups.FirstOrDefault(g => g.Index == idx);
            if (stat == null) return;

            if (stat.IsFull)
            {
                var ans = MessageBox.Show(
                             string.Format(S("Groups.Confirm.ResetCount.Body"), stat.Name, stat.Index),
                                S("Groups.Confirm.ResetCount.Title"),
                                MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (ans == MessageBoxResult.Yes)
                {
                    _groups.ResetGroup(idx, clearLast: false);
                    _log.Info("Application", $"Counter of group “{stat.Name}” reset to 0", new { Group = stat.Index }); // лог можно оставить на одном языке
                    MessageBox.Show(
                        string.Format(S("Groups.Alert.ResetDone.Body"), stat.Name),
                        S("Groups.Alert.ResetDone.Title"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        // Клик по корзине: можно только когда сканирование на паузе
        private void TinyDelete_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // не даём событию “пробулькать” на карточку

            if (sender is not Button b || b.Tag is not int idx) return;

            var stat = _groups.ActiveGroups.FirstOrDefault(g => g.Index == idx);
            if (stat == null) return;

            if (_app.IsRunning)
            {
                MessageBox.Show(S("Groups.Alert.CannotClearWhileRunning.Body"),
                                S("Groups.Alert.CannotClearWhileRunning.Title"),
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                _log.Warn("Application", "Attempt to clear group while running", new { Group = idx });
                return;
            }

            var ans = MessageBox.Show(
                string.Format(S("Groups.Confirm.ClearGroup.Body"), stat.Name, stat.Index),
                S("Groups.Confirm.ClearGroup.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ans == MessageBoxResult.Yes)
            {
                _groups.ResetGroup(idx, clearLast: true);
                _log.Info("Application", "Group cleared", new { Group = stat.Index, ClearedLast = true });
                MessageBox.Show(
                    string.Format(S("Groups.Alert.Cleared.Body"), stat.Name),
                    S("Groups.Alert.Cleared.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
