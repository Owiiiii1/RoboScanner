using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RoboScanner.Services;
using RoboScanner.Models; // если у тебя GroupRule в другом неймспейсе — поправь

namespace RoboScanner.Views
{
    public partial class GroupSetupView : UserControl
    {
        private readonly RulesService _rules = RulesService.Instance;
        private string? _currentFilePath; // путь текущего выбранного файла настроек
        private string S(string key) => (string)FindResource(key);

        private string L(string key, string fallback)
        {
            return TryFindResource(key) as string ?? fallback;
        }

        private static string FileDisplayName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }


        public GroupSetupView()
        {
            InitializeComponent();
            DataContext = _rules;

            // По умолчанию работаем как раньше с дефолтным rules.json,
            // но явного "текущего файла" ещё нет — покажем "файл не выбран".
            UpdateCurrentFileLabel();
        }

        // ===== ВСПОМОГАТЕЛЬНОЕ =====
        private void UpdateCurrentFileLabel()
        {
            if (TxtCurrentFile == null) return;

            var prefix = L("GroupSetup.CurrentFilePrefix", "current file:");
            var none = L("GroupSetup.FileNotSelected", "—");
            var name = string.IsNullOrWhiteSpace(_currentFilePath) ? none : FileDisplayName(_currentFilePath);

            TxtCurrentFile.Text = $"{prefix} {name}";
        }


        private static void CommitGridEdits(DataGrid grid)
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // ===== КНОПКИ =====

        // ОТКРЫТЬ: проводник -> _rules.Load(path) -> запомнить путь -> показать в подписи
        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = L("GroupSetup.Dialog.Open.Title", "Open settings file"),
                Filter = $"{L("GroupSetup.Dialog.Filter.Json", "Rules files (*.json)")}|*.json|{L("GroupSetup.Dialog.Filter.All", "All files (*.*)")}|*.*"

            };
            if (dlg.ShowDialog() == true)
            {
                _rules.Load(dlg.FileName);
                _currentFilePath = dlg.FileName;
                GroupsService.Instance.RefreshFromRules();
                UpdateCurrentFileLabel();
                MessageBox.Show(S("GroupSetup.LoadOk.Body"), S("GroupSetup.LoadOk.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // СОХРАНИТЬ: только в текущий файл; если файл не выбран — попросим сначала открыть или "сохранить новый"
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                MessageBox.Show(
            L("GroupSetup.NoFileSelected.Body", "Open a file first or use “Save as new…”."),
            L("GroupSetup.NoFileSelected.Title", "No file selected"),
            MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CommitGridEdits(GridRules);
            _rules.Save(_currentFilePath);
            GroupsService.Instance.RefreshFromRules();
            MessageBox.Show(S("GroupSetup.SaveOk.Body"), S("GroupSetup.SaveOk.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // СОХРАНИТЬ НОВЫЙ: проводник -> сохраняем → делаем его текущим
        private void BtnSaveNew_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = L("GroupSetup.Dialog.SaveNew.Title", "Save settings as…"),
                Filter = $"{L("GroupSetup.Dialog.Filter.Json", "Rules files (*.json)")}|*.json|{L("GroupSetup.Dialog.Filter.All", "All files (*.*)")}|*.*",
                FileName = "rules.json"
            };
            if (dlg.ShowDialog() == true)
            {
                CommitGridEdits(GridRules);
                _rules.Save(dlg.FileName);
                _currentFilePath = dlg.FileName;
                GroupsService.Instance.RefreshFromRules();
                UpdateCurrentFileLabel();
                MessageBox.Show(S("GroupSetup.SaveOk.Body"), S("GroupSetup.SaveOk.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ОСТАВЛЯЕМ БЕЗ ИЗМЕНЕНИЙ
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(S("GroupSetup.ResetConfirm.Body"), S("GroupSetup.ResetConfirm.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _rules.ResetDefaults();
                GroupsService.Instance.RefreshFromRules();
            }
        }

        private void ToggleActive_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GroupRule rule)
            {
                rule.IsActive = !rule.IsActive;
            }
        }
    }
}
