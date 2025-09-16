using Microsoft.Win32;
using RoboScanner.Models;
using RoboScanner.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;                  // для .Cast<T>() и Count()
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;      // для TextBlock и SelectionChangedEventArgs
using System.Windows.Data;

namespace RoboScanner.Views
{
    public partial class LogViewerWindow : Window
    {
        private readonly ObservableCollection<LogEntry> _all = new();
        private ICollectionView? _view;
        private string _currentPath = "";
        private string S(string key) => (string)FindResource(key);

        public LogViewerWindow() : this(LogService.Instance.LogPath) { }

        public LogViewerWindow(string path)
        {
            InitializeComponent();

            // Привязка источника к таблице
            LogGrid.ItemsSource = _all;

            // Представление для сортировки/фильтрации
            _view = CollectionViewSource.GetDefaultView(LogGrid.ItemsSource);
            _view.SortDescriptions.Add(new SortDescription(nameof(LogEntry.Timestamp), ListSortDirection.Descending));
            _view.Filter = Filter;

            // Загружаем файл после построения визуального дерева
            Loaded += (_, __) => LoadFile(path);
        }

        private void LoadFile(string? path)
        {
            _all.Clear();
            _currentPath = path ?? "";

            // Безопасно обновляем путь в статус-баре (если элемент уже создан)
            TxtPath?.SetCurrentValue(TextBlock.TextProperty,
                string.IsNullOrEmpty(_currentPath) ? S("LogViewer.Status.FileNotSelected") : _currentPath);

            if (!string.IsNullOrEmpty(_currentPath) && File.Exists(_currentPath))
            {
                try
                {
                    foreach (var line in File.ReadLines(_currentPath))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var entry = JsonSerializer.Deserialize<LogEntry>(line);
                            if (entry != null) _all.Add(entry);
                        }
                        catch
                        {
                            // невалидная строка — пропускаем
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(S("LogViewer.Error.ReadBody") + "\n" + ex.Message,
                            S("LogViewer.Error.ReadTitle"),
                            MessageBoxButton.OK, MessageBoxImage.Error);

                }
            }

            _view?.Refresh();
            UpdateCount();
        }

        private bool Filter(object obj)
        {
            if (obj is not LogEntry e) return false;

            // Если фильтров ещё нет в визуальном дереве, пропускаем
            if (CbType == null || DpFrom == null || DpTo == null) return true;

            // Тип события
            var type = (CbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            if (!string.Equals(type, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(e.EventType, type, StringComparison.OrdinalIgnoreCase))
                return false;

            // Диапазон дат (включительно)
            if (DpFrom.SelectedDate is DateTime from && e.Timestamp < from.Date)
                return false;
            if (DpTo.SelectedDate is DateTime to &&
                e.Timestamp > to.Date.AddDays(1).AddTicks(-1))
                return false;

            return true;
        }

        // Один обработчик для ComboBox.SelectionChanged и DatePicker.SelectedDateChanged
        private void FilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            _view?.Refresh();
            UpdateCount();
        }

        private void UpdateCount()
        {
            if (TxtCount == null) return; // защита от раннего вызова
            var shown = _view?.Cast<LogEntry>().Count() ?? 0;
            TxtCount.Text = string.Format(S("LogViewer.Status.CountFormat"), shown, _all.Count);
        }

        private void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите файл лога",
                Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(LogService.Instance.LogDirectory)
                    ? LogService.Instance.LogDirectory
                    : AppContext.BaseDirectory
            };
            if (dlg.ShowDialog(this) == true)
                LoadFile(dlg.FileName);
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            LoadFile(_currentPath);
        }
    }
}
