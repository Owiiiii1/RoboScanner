using System;
using System.Windows;
using System.Windows.Controls;
using RoboScanner.Services;

namespace RoboScanner.Views
{
    public partial class StatsView : UserControl
    {
        private readonly AppStateService _app = AppStateService.Instance;
        private readonly ScanHistoryService _hist = ScanHistoryService.Instance;

        private bool _isReady = false; // блокируем Apply/AnyChanged до окончания инициализации

        private string L(string key, string fallback) =>
    (TryFindResource(key) as string) ?? fallback;

        public StatsView()
        {
            InitializeComponent();
            InitTimePickers();          // заполняем и выставляем дефолты (события пока игнорируются)
            _isReady = true;            // теперь можно реагировать на изменения

            // автопересчет при появлении новых сканов
            _hist.Changed += (_, __) => Dispatcher.Invoke(Apply);

            // по умолчанию — с начала сеанса
            ChkSession.IsChecked = true; // вызовет AnyChanged, но уже безопасно
            Apply();
        }

        private void InitTimePickers()
        {
            // Часы 0..23, минуты 0..59
            for (int h = 0; h < 24; h++) { CbFromH.Items.Add(h.ToString("00")); CbToH.Items.Add(h.ToString("00")); }
            for (int m = 0; m < 60; m++) { CbFromM.Items.Add(m.ToString("00")); CbToM.Items.Add(m.ToString("00")); }

            var now = DateTime.Now;
            DpFrom.SelectedDate = now.Date;
            DpTo.SelectedDate = now.Date;

            CbFromH.SelectedIndex = 0;
            CbFromM.SelectedIndex = 0;

            CbToH.SelectedItem = now.Hour.ToString("00");
            CbToM.SelectedItem = now.Minute.ToString("00");
        }

        private void AnyChanged(object? sender, RoutedEventArgs e)
        {
            if (!_isReady) return;

            bool fromSession = ChkSession.IsChecked == true;

            // Весь блок фильтров выключаем, если выбран "с начала сеанса"
            RowFilters.IsEnabled = !fromSession;

            Apply();
        }

        private int ParseCombo(ComboBox cb, int fallback)
        {
            // Пытаемся взять SelectedItem как строку, если пусто — берем Text
            var s = cb.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(s)) s = cb.Text;

            return int.TryParse(s, out var v) ? v : fallback;
        }

        private void Apply()
        {
            DateTime now = DateTime.Now;
            DateTime from = _app.SessionStarted;
            DateTime to = now;

            if (ChkSession.IsChecked != true)
            {
                if (DpFrom.SelectedDate == null || DpTo.SelectedDate == null)
                {
                    TxtTotal.Text = "0";
                    TxtRange.Text = L("Stats.Range.NotSelected", "Select dates «From» and «To»");
                    return;
                }

                // без NullReference/ArgumentNull — с запасными значениями
                int fh = ParseCombo(CbFromH, 0);
                int fm = ParseCombo(CbFromM, 0);
                int th = ParseCombo(CbToH, now.Hour);
                int tm = ParseCombo(CbToM, now.Minute);

                from = DpFrom.SelectedDate.Value.Date.AddHours(fh).AddMinutes(fm);
                to = DpTo.SelectedDate.Value.Date.AddHours(th).AddMinutes(tm);
            }

            if (from > to)
            {
                TxtTotal.Text = "0";
                TxtRange.Text = L("Stats.Range.Invalid", "Wrong interval");
                return;
            }

            var toExclusive = to.AddMinutes(1);

            int count = _hist.CountBetween(from, toExclusive);
            TxtTotal.Text = count.ToString();
            TxtRange.Text = $"{from:yyyy-MM-dd HH:mm} → {to:yyyy-MM-dd HH:mm}";
        }
    }
}
