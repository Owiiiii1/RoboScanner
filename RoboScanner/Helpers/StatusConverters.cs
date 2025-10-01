using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RoboScanner.Services;   // нужен для OperationState

namespace RoboScanner.Helpers
{
    public class IsRunningToTextConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool running = v is bool b && b;
            var key = running ? "Scan.Program.Running" : "Scan.Program.Stopped";
            var fromRes = Application.Current.TryFindResource(key) as string;
            return fromRes ?? (running ? "Program: Launched" : "Program: Stopped");
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class OpStateToTextConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v is OperationState os ? os : OperationState.Wait;
            string key = s switch
            {
                OperationState.Scanning => "Scan.State.Scanning",
                OperationState.Done => "Scan.State.Done",
                _ => "Scan.State.Wait"
            };
            var fromRes = Application.Current.TryFindResource(key) as string;
            if (fromRes != null) return fromRes;

            return s switch
            {
                OperationState.Scanning => "Scanning in progress",
                OperationState.Done => "Scanned",
                _ => "Waiting"
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class IsRunningToBrushConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool running = v is bool b && b;
            return new SolidColorBrush(running ? Color.FromRgb(209, 250, 229) : Color.FromRgb(239, 246, 255));
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class OpStateToBrushConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v is OperationState os ? os : OperationState.Wait;
            return new SolidColorBrush(s switch
            {
                OperationState.Scanning => Color.FromRgb(254, 249, 195), // #FEF9C3
                OperationState.Done => Color.FromRgb(209, 250, 229), // #D1FAE5
                _ => Color.FromRgb(239, 246, 255), // #EFF6FF
            });
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
