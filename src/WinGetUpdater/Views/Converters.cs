using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WinGetUpdater.Services;

namespace WinGetUpdater.Views;

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

/// <summary>
/// Hebt den in der Navigation aktiven Befehl hervor. Liefert bewusst nur noch bool statt
/// eines fertig aufgeloesten Brush: ein Brush, den
/// Application.Current.TryFindResource(...) einmalig zurueckgibt, ist danach ein einfaches
/// Objekt ohne jede Verbindung zur Ressource - bei einem Themenwechsel bleibt der
/// vorausgewaehlte Eintrag dann auf der alten Farbe haengen, bis Id/CurrentCommandId sich
/// erneut aendern und das Binding neu auswertet. Der Aufrufer (ShellWindow.xaml) bindet
/// dieses bool an einen DataTrigger, dessen Setter {DynamicResource Bg.Selected} verwendet -
/// das bleibt live am Theme haengen, unabhaengig davon, wann der Trigger zuletzt gefeuert hat.
/// </summary>
public sealed class SelectedCommandConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string id || values[1] is not string current)
            return DependencyProperty.UnsetValue;

        return id == current;
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
/// Rechnet eine Hoehe in einen Anteil davon um und begrenzt das Ergebnis.
///
/// Damit richtet sich die Hoehe des Optionsbereichs nach dem Fenster: auf einem grossen
/// Bildschirm darf er wachsen, auf einem kleinen bleibt trotzdem Platz fuer die
/// Ergebnisliste. Eine feste Hoehe kann das nicht - sie ist entweder oben zu knapp
/// oder unten zu grosszuegig.
/// </summary>
public sealed class FractionOfHeightConverter : IValueConverter
{
    public double Fraction { get; set; } = 0.3;
    public double Min { get; set; } = 120;
    public double Max { get; set; } = 480;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Beim ersten Messdurchlauf steht die Hoehe noch nicht fest; dann gilt das Minimum.
        if (value is not double height || double.IsNaN(height) || height <= 0) return Min;
        return Math.Clamp(height * Fraction, Min, Max);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
