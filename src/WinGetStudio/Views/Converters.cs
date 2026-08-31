using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WinGetStudio.Services;
using WinGetStudio.ViewModels;

namespace WinGetStudio.Views;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            System.Collections.ICollection c => c.Count > 0,
            null => false,
            _ => true
        };
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Faerbt Ausgabezeilen: Fehler rot, Zwischenueberschriften eines Stapellaufs blau.</summary>
public sealed class LineKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            LineKind.Error => "State.Error",
            LineKind.Info => "Accent",
            _ => "Fg.Console"
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RunStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            RunState.Succeeded => "State.Ok",
            RunState.Failed => "State.Error",
            RunState.Canceled => "State.Warn",
            RunState.Running => "Accent",
            _ => "Fg.Secondary"
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hebt den in der Navigation aktiven Befehl hervor.</summary>
public sealed class SelectedCommandConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string id || values[1] is not string current)
            return DependencyProperty.UnsetValue;

        var key = id == current ? "Bg.Selected" : "Transparent";
        if (key == "Transparent") return Brushes.Transparent;
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Waehlt aus einem zweisprachigen Schema-Text den passenden aus.</summary>
public sealed class LocTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Models.Loc loc ? loc.Get(Localizer.Instance.Language) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not bool b || !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not bool b || !b;
}

/// <summary>
/// Faerbt Protokolleintraege. Nimmt sowohl eine Stufe als auch ein bool entgegen, damit
/// dieselbe Regel fuer die Anzeige in der Kopfleiste gilt.
/// </summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            LogLevel.Error => "State.Error",
            LogLevel.Warning => "State.Warn",
            LogLevel.Info => "Fg.Muted",
            true => "State.Error",
            false => "Fg.Secondary",
            _ => "Fg.Secondary"
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
