// Localization/Loc.cs
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace RoboScanner.Localization
{
    public static class Loc
    {
        private const string MarkerKey = "Sidebar.Title";
        public static string CurrentLanguage { get; private set; } = "ru";

        public static void SetLanguage(string langCode)
        {
            // 1) подменяем словарь строк
            var uri = new Uri($"pack://application:,,,/Resources/Strings.{langCode}.xaml", UriKind.Absolute);
            var newDict = new ResourceDictionary { Source = uri };
            var appRes = Application.Current.Resources;
            var oldDict = appRes.MergedDictionaries.FirstOrDefault(d => d.Contains(MarkerKey));
            if (oldDict != null) appRes.MergedDictionaries.Remove(oldDict);
            appRes.MergedDictionaries.Insert(0, newDict);
            CurrentLanguage = langCode;

            // 2) культуры
            string cultureTag = langCode switch { "ru" => "ru-RU", "it" => "it-IT", _ => "en-US" };
            var ci = new CultureInfo(cultureTag);
            Thread.CurrentThread.CurrentCulture = ci;
            Thread.CurrentThread.CurrentUICulture = ci;

            // 3) обновляем Language у уже ОТКРЫТЫХ окон (унаследуется дочерним элементам)
            var xml = XmlLanguage.GetLanguage(ci.IetfLanguageTag);
            foreach (Window w in Application.Current.Windows)
                w.Language = xml;
        }
    }
}
