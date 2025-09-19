using RoboScanner.Localization;
using RoboScanner.Models;
using System;
using System.Windows;

namespace RoboScanner
{
    public partial class GroupSettingsWindow : Window
    {
        private RobotGroup _model;

        private class Vm
        {
            public string Header { get; set; } = "";
            public string Name { get; set; } = "";

            public string Host { get; set; } = "";
            public int Port { get; set; } = 502;
            public byte UnitId { get; set; } = 1;
            public int TimeoutMs { get; set; } = 1500;

            public ushort? PrimaryCoilAddress { get; set; } // 0..29
            public int? PulseSeconds { get; set; }           // null/0 => без авто-выключения
        }

        private readonly Vm _vm;

        public GroupSettingsWindow(int groupIndex)
        {
            InitializeComponent();

            _model = RobotGroups.Get(groupIndex);
            _vm = new Vm
            {
                Header = $"{_model.DisplayName} (#{_model.Index})",
                Name = _model.Name,
                Host = _model.Host,
                Port = _model.Port,
                UnitId = _model.UnitId,
                TimeoutMs = _model.TimeoutMs,
                PrimaryCoilAddress = _model.PrimaryCoilAddress,
                PulseSeconds = _model.PulseSeconds
            };

            DataContext = _vm;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Простая валидация
            if (!string.IsNullOrWhiteSpace(_vm.Host) && _vm.Port <= 0)
            {
                MessageBox.Show(Loc.Get("Msg.PortInvalid"), Loc.Get("Group.Window.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            //if (_vm.PrimaryCoilAddress is ushort coil && coil > 29)
            //{
            //    MessageBox.Show(Loc.Get("Msg.CoilRange"), Loc.Get("Group.Window.Title"),
            //        MessageBoxButton.OK, MessageBoxImage.Warning);
            //    return;
            //}

            if (_vm.PulseSeconds is int sec && sec < 0)
            {
                MessageBox.Show(Loc.Get("Msg.PulseNegative"), Loc.Get("Group.Window.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Сохраняем в модель
            _model.Name = string.IsNullOrWhiteSpace(_vm.Name)
                ? $"Group {_model.Index}"
                : _vm.Name.Trim();

            _model.Host = _vm.Host?.Trim() ?? "";
            _model.Port = _vm.Port <= 0 ? 502 : _vm.Port;
            _model.UnitId = _vm.UnitId == 0 ? (byte)1 : _vm.UnitId;
            _model.TimeoutMs = _vm.TimeoutMs <= 0 ? 1500 : _vm.TimeoutMs;

            _model.PrimaryCoilAddress = _vm.PrimaryCoilAddress;
            _model.PulseSeconds = (_vm.PulseSeconds is int p && p > 0) ? p : null;

            RobotGroups.Update(_model);

            DialogResult = true;
            Close();
        }
    }
}
