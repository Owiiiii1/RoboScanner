using System.Windows;
using System.Windows.Controls;
using RoboScanner.Localization;
using AppSettings = RoboScanner.Properties.Settings;
using System.Linq;
using RoboScanner.Models;
using RoboScanner.Services;
using System.Collections.Generic;

namespace RoboScanner.Views
{
    public partial class SettingsView : UserControl
    {

        private List<CameraInfo> _cams = new();

        public SettingsView()
        {
            InitializeComponent();
            // выставить текущий язык
            switch (Loc.CurrentLanguage)
            {
                case "ru": RbRU.IsChecked = true; break;
                case "it": RbIT.IsChecked = true; break;
                default: RbEN.IsChecked = true; break;
            }

            // Заполнить выпадающий список групп 1..15
            if (CbRobotGroup != null)
            {
                var items = RobotGroups.All.Values.OrderBy(g => g.Index).ToList();
                CbRobotGroup.ItemsSource = items;
                CbRobotGroup.SelectedValue = RobotGroups.SelectedIndex;
                CbRobotGroup.SelectionChanged += CbRobotGroup_SelectionChanged;
            }

            LoadCamerasAndBind();     // заполняем выпадашки
            HookCameraEvents();       // подписываемся на выбор
        }

        private void LoadCamerasAndBind()
        {
            _cams = CameraDiscoveryService.Instance.ListVideoDevices();

            if (CbCamera1 != null)
            {
                CbCamera1.ItemsSource = _cams;
                // восстановить выбор по сохранённому ID
                var saved1 = AppSettings.Default.Camera1Id; // строка в Settings
                var found1 = CameraDiscoveryService.Instance.FindByMoniker(_cams, saved1);
                CbCamera1.SelectedValue = found1?.Moniker ?? _cams.FirstOrDefault()?.Moniker;
            }

            if (CbCamera2 != null)
            {
                CbCamera2.ItemsSource = _cams;
                var saved2 = AppSettings.Default.Camera2Id;
                var found2 = CameraDiscoveryService.Instance.FindByMoniker(_cams, saved2);
                CbCamera2.SelectedValue = found2?.Moniker ?? _cams.Skip(1).FirstOrDefault()?.Moniker
                                          ?? _cams.FirstOrDefault()?.Moniker;
            }
        }

        private void HookCameraEvents()
        {
            if (CbCamera1 != null)
                CbCamera1.SelectionChanged += (s, e) =>
                {
                    if (CbCamera1.SelectedValue is string moniker)
                    {
                        AppSettings.Default.Camera1Id = moniker;
                        AppSettings.Default.Save();
                    }
                };

            if (CbCamera2 != null)
                CbCamera2.SelectionChanged += (s, e) =>
                {
                    if (CbCamera2.SelectedValue is string moniker)
                    {
                        AppSettings.Default.Camera2Id = moniker;
                        AppSettings.Default.Save();
                    }
                };
        }

        private void CbRobotGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbRobotGroup?.SelectedValue is int idx)
                RobotGroups.SetSelected(idx); // централизованно запоминаем выбор
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string lang)
            {
                Loc.SetLanguage(lang);
                AppSettings.Default.UILang = lang;
                AppSettings.Default.Save();   // ← без доп. кнопки, сохраняем сразу
            }
        }

        private void RefreshCameras_Click(object sender, RoutedEventArgs e)
        {
            LoadCamerasAndBind();
        }

        private void OpenGroupSettings_Click(object sender, RoutedEventArgs e)
        {
            // SelectedValuePath = Index, так что SelectedValue — int
            if (CbRobotGroup?.SelectedValue is int idx)
            {
                var dlg = new GroupSettingsWindow(idx)
                {
                    Owner = Window.GetWindow(this)
                };
                dlg.ShowDialog();
                // тут ничего больше не нужно — модель уже обновлена внутри диалога
            }
            else
            {
                MessageBox.Show(Loc.Get("Msg.SelectGroup"), Loc.Get("Group.Window.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnOpenCalibrationClick(object sender, RoutedEventArgs e)
        {
            var win = new CalibrationWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }
    }
}
