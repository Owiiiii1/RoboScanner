// App.xaml.cs
using RoboScanner.Localization;
using RoboScanner.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using AppSettings = RoboScanner.Properties.Settings;

namespace RoboScanner
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _ = ScanHistoryService.Instance; // форсируем загрузку истории с диска

            // читаем сохранённый язык или авто-выбор
            var saved = AppSettings.Default.UILang;
            if (string.IsNullOrWhiteSpace(saved))
            {
                var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                saved = two switch { "ru" => "ru", "it" => "it", _ => "en" };
            }

            // вычисляем культуру заранее
            var tag = saved switch { "ru" => "ru-RU", "it" => "it-IT", _ => "en-US" };
            var ci = new CultureInfo(tag);

            // ВАЖНО: OverrideMetadata — только один раз за всё время работы
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(ci.IetfLanguageTag)));

            // применяем ресурсы и культуры, проставляем Language окнам
            Loc.SetLanguage(saved);

            LogService.Instance.Info("App", "Application started", new { UILang = saved });

            var w = new MainWindow();
            this.MainWindow = w;
            w.Show();
        }
    }
}
