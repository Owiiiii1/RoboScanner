using System;
using System.Linq;
using System.Windows;

namespace RoboScanner.Localization
{
    public static class Loc
    {
        private const string MarkerKey = "Sidebar.Title"; // ключ, который точно есть в словаре
        public static string CurrentLanguage { get; private set; } = "ru";

        public static void SetLanguage(string langCode)
        {
            var uri = new Uri($"pack://application:,,,/Resources/Strings.{langCode}.xaml", UriKind.Absolute);
            var newDict = new ResourceDictionary { Source = uri };

            var appRes = Application.Current.Resources;
            // найти старый словарь по наличию маркера
            var oldDict = appRes.MergedDictionaries
                .FirstOrDefault(d => d.Contains(MarkerKey));

            if (oldDict != null)
                appRes.MergedDictionaries.Remove(oldDict);

            appRes.MergedDictionaries.Insert(0, newDict);
            CurrentLanguage = langCode;
        }
    }
}

