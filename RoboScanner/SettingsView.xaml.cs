using System.Windows;
using System.Windows.Controls;
using RoboScanner.Localization;
using AppSettings = RoboScanner.Properties.Settings;
using System.Linq;
using RoboScanner.Models;
using RoboScanner.Services;
using System.Collections.Generic;
using System;
using System.Windows.Threading;
using AppSettingsStatic = RoboScanner.Properties.Settings;

namespace RoboScanner.Views
{
    public partial class SettingsView : UserControl
    {

        private List<CameraInfo> _cams = new();
        private readonly DispatcherTimer _laserTimer;

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

            // Лазеры
            LoadLaserSettings();
            LaserService.Instance.Start();

            _laserTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _laserTimer.Tick += LaserTimer_Tick;
            _laserTimer.Start();
        }

        private void LoadLaserSettings()
        {
            var s = AppSettingsStatic.Default;

            if (s.LaserAxisXSensor == 0 &&
                s.LaserAxisYSensor == 0 &&
                s.LaserAxisZSensor == 0)
            {
                s.LaserAxisXSensor = 1; // датчик 1 → X
                s.LaserAxisYSensor = 2; // датчик 2 → Y
                s.LaserAxisZSensor = 3; // датчик 3 → Z
                s.Save();
            }

            if (TbLaserIp != null) TbLaserIp.Text = s.LaserHost;
            if (TbLaserPollMs != null) TbLaserPollMs.Text = s.LaserPollMs.ToString();

            TbLaser1Inst.Text = s.LaserSensor1Inst.ToString();
            TbLaser2Inst.Text = s.LaserSensor2Inst.ToString();
            TbLaser3Inst.Text = s.LaserSensor3Inst.ToString();

            // Оффсеты датчиков: если <=0, можно показывать пусто
            if (TbLaser1Offset != null)
                TbLaser1Offset.Text = s.LaserSensor1Offset > 0 ? s.LaserSensor1Offset.ToString() : "";
            if (TbLaser2Offset != null)
                TbLaser2Offset.Text = s.LaserSensor2Offset > 0 ? s.LaserSensor2Offset.ToString() : "";
            if (TbLaser3Offset != null)
                TbLaser3Offset.Text = s.LaserSensor3Offset > 0 ? s.LaserSensor3Offset.ToString() : "";

            SetAxisCombo(CbLaser1Axis, s.LaserAxisXSensor, s.LaserAxisYSensor, s.LaserAxisZSensor, 1);
            SetAxisCombo(CbLaser2Axis, s.LaserAxisXSensor, s.LaserAxisYSensor, s.LaserAxisZSensor, 2);
            SetAxisCombo(CbLaser3Axis, s.LaserAxisXSensor, s.LaserAxisYSensor, s.LaserAxisZSensor, 3);

            
            TbAxisXOffset.Text = s.LaserAxisXOffsetMm.ToString("0.###");
            TbAxisYOffset.Text = s.LaserAxisYOffsetMm.ToString("0.###");
            TbAxisZOffset.Text = s.LaserAxisZOffsetMm.ToString("0.###");
        }


        private void SetAxisCombo(ComboBox cb, int axX, int axY, int axZ, int sensorIndex)
        {
            if (cb == null) return;
            int tag = 0;
            if (axX == sensorIndex) tag = 1;
            else if (axY == sensorIndex) tag = 2;
            else if (axZ == sensorIndex) tag = 3;

            foreach (ComboBoxItem item in cb.Items)
            {
                if (item.Tag is string sTag && int.TryParse(sTag, out int val) && val == tag)
                {
                    cb.SelectedItem = item;
                    return;
                }
                if (item.Tag is int iTag && iTag == tag)
                {
                    cb.SelectedItem = item;
                    return;
                }
            }
        }

        private void LaserTimer_Tick(object? sender, EventArgs e)
        {
            var laser = LaserService.Instance;

            double? v1 = laser.GetRawMm(0);
            double? v2 = laser.GetRawMm(1);
            double? v3 = laser.GetRawMm(2);

            LblLaser1Value.Text = v1.HasValue ? $"{v1.Value:0.##}" : "—";
            LblLaser2Value.Text = v2.HasValue ? $"{v2.Value:0.##}" : "—";
            LblLaser3Value.Text = v3.HasValue ? $"{v3.Value:0.##}" : "—";

            // --- Новый блок: расчет результата измерения ---
            var s = AppSettingsStatic.Default;

            // Определяем какие датчики привязаны к осям
            double?[] vals = { v1, v2, v3 };

            double? axisX = (s.LaserAxisXSensor >= 1 && s.LaserAxisXSensor <= 3)
                ? vals[s.LaserAxisXSensor - 1]
                : null;
            double? axisY = (s.LaserAxisYSensor >= 1 && s.LaserAxisYSensor <= 3)
                ? vals[s.LaserAxisYSensor - 1]
                : null;
            double? axisZ = (s.LaserAxisZSensor >= 1 && s.LaserAxisZSensor <= 3)
                ? vals[s.LaserAxisZSensor - 1]
                : null;

            // Берем смещения, введенные вручную
            double.TryParse(TbAxisXOffset.Text, out double offX);
            double.TryParse(TbAxisYOffset.Text, out double offY);
            double.TryParse(TbAxisZOffset.Text, out double offZ);

            // Вычисляем результат: смещение - измерение
            double? resX = axisX.HasValue ? offX - axisX : null;
            double? resY = axisY.HasValue ? offY - axisY : null;
            double? resZ = axisZ.HasValue ? offZ - axisZ : null;

            LblAxisXResult.Text = resX.HasValue ? $"{resX.Value:0.###}" : "—";
            LblAxisYResult.Text = resY.HasValue ? $"{resY.Value:0.###}" : "—";
            LblAxisZResult.Text = resZ.HasValue ? $"{resZ.Value:0.###}" : "—";
        }



        private void OnLaserRestartClick(object sender, RoutedEventArgs e)
        {
            var s = AppSettingsStatic.Default;

            if (int.TryParse(TbLaserPollMs.Text, out int poll))
                s.LaserPollMs = poll;

            s.LaserHost = TbLaserIp.Text?.Trim() ?? "";

            if (int.TryParse(TbLaser1Inst.Text, out int inst1)) s.LaserSensor1Inst = inst1;
            if (int.TryParse(TbLaser2Inst.Text, out int inst2)) s.LaserSensor2Inst = inst2;
            if (int.TryParse(TbLaser3Inst.Text, out int inst3)) s.LaserSensor3Inst = inst3;

            // <<< НОВОЕ: оффсеты датчиков >>>
            if (int.TryParse(TbLaser1Offset.Text, out int off1)) s.LaserSensor1Offset = off1;
            if (int.TryParse(TbLaser2Offset.Text, out int off2)) s.LaserSensor2Offset = off2;
            if (int.TryParse(TbLaser3Offset.Text, out int off3)) s.LaserSensor3Offset = off3;

            
            if (double.TryParse(TbAxisXOffset.Text, out double offX)) s.LaserAxisXOffsetMm = offX;
            if (double.TryParse(TbAxisYOffset.Text, out double offY)) s.LaserAxisYOffsetMm = offY;
            if (double.TryParse(TbAxisZOffset.Text, out double offZ)) s.LaserAxisZOffsetMm = offZ;

            // Назначение датчиков по осям
            ApplyAxisMappingFromCombos();

            s.Save();

           
        }


        private void ApplyAxisMappingFromCombos()
        {
            int axX = 0, axY = 0, axZ = 0;

            ReadAxisFromCombo(CbLaser1Axis, 1, ref axX, ref axY, ref axZ);
            ReadAxisFromCombo(CbLaser2Axis, 2, ref axX, ref axY, ref axZ);
            ReadAxisFromCombo(CbLaser3Axis, 3, ref axX, ref axY, ref axZ);

            var s = AppSettingsStatic.Default;
            s.LaserAxisXSensor = axX;
            s.LaserAxisYSensor = axY;
            s.LaserAxisZSensor = axZ;
        }

        private static void ReadAxisFromCombo(ComboBox cb, int sensorIndex,
            ref int axX, ref int axY, ref int axZ)
        {
            if (cb.SelectedItem is ComboBoxItem item)
            {
                int tag = 0;
                if (item.Tag is int i) tag = i;
                else if (item.Tag is string s && int.TryParse(s, out int i2)) tag = i2;

                switch (tag)
                {
                    case 1: axX = sensorIndex; break;
                    case 2: axY = sensorIndex; break;
                    case 3: axZ = sensorIndex; break;
                }
            }
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

        
    }
}
