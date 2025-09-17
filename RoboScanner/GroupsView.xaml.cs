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
                    $"Сбросить счётчик группы «{stat.Name}» (#{stat.Index}) до 0?",
                    "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (ans == MessageBoxResult.Yes)
                {
                    _groups.ResetGroup(idx, clearLast: false); // только счётчик
                    _log.Info("Application", $"Счётчик группы «{stat.Name}» сброшен до 0",
                              new { Group = stat.Index });
                    MessageBox.Show($"Группа «{stat.Name}» сброшена. Можно продолжать сканирование.",
                                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show("Нельзя очистить группу во время работы. Поставьте сканирование на паузу.",
                                "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                _log.Warn("Application", "Попытка очистить группу при активном сканировании", new { Group = idx });
                return;
            }

            var ans = MessageBox.Show(
                $"Очистить группу «{stat.Name}» (#{stat.Index})?\nБудут обнулены счётчик и последние замеры.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ans == MessageBoxResult.Yes)
            {
                _groups.ResetGroup(idx, clearLast: true);
                _log.Info("Application", "Группа очищена", new { Group = stat.Index, ClearedLast = true });
                MessageBox.Show($"Группа «{stat.Name}» очищена.", "Готово",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
