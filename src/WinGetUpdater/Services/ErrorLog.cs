using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;

namespace WinGetUpdater.Services;

public enum LogLevel { Info, Warning, Error }

public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Source, string Message, string? Detail)
{
    public string TimeText => Time.ToString("HH:mm:ss");
    public string LevelText => Level switch
    {
        LogLevel.Error => "FEHLER",
        LogLevel.Warning => "WARNUNG",
        _ => "INFO"
    };

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string ToFileLine() =>
        $"{Time:yyyy-MM-dd HH:mm:ss} [{LevelText}] {Source}: {Message}" +
        (HasDetail ? Environment.NewLine + "    " + Detail!.Replace("\n", "\n    ") : "");
}

/// <summary>
/// Sammelstelle fuer alles, was schiefgeht.
///
/// Der Grundsatz: kein leerer catch-Block im Programm. Jeder abgefangene Fehler landet hier -
/// auch die harmlosen, die den Ablauf nicht stoeren. Sichtbar wird das ueber die Anzeige in der
/// Kopfzeile; nachlesbar bleibt es in der Protokolldatei, auch nach einem Absturz.
/// </summary>
public sealed class ErrorLog : INotifyPropertyChanged
{
    private const int MaxEntriesInMemory = 500;
    private const long MaxFileBytes = 1024 * 1024;

    public static ErrorLog Instance { get; } = new();

    private readonly object _fileLock = new();
    private int _errorCount;
    private int _warningCount;

    private ErrorLog()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGetUpdater");
        try
        {
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, "wingetupdater.log");
        }
        catch
        {
            // Selbst das Anlegen des Ordners kann fehlschlagen - dann bleibt die Anzeige
            // in der Oberflaeche, nur die Datei entfaellt.
            FilePath = "";
        }
    }

    /// <summary>Leer, wenn keine Datei geschrieben werden konnte.</summary>
    public string FilePath { get; }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public int ErrorCount
    {
        get => _errorCount;
        private set { _errorCount = value; Raise(nameof(ErrorCount)); Raise(nameof(HasProblems)); Raise(nameof(BadgeText)); }
    }

    public int WarningCount
    {
        get => _warningCount;
        private set { _warningCount = value; Raise(nameof(WarningCount)); Raise(nameof(HasProblems)); Raise(nameof(BadgeText)); }
    }

    public bool HasProblems => _errorCount > 0 || _warningCount > 0;
    public bool HasErrors => _errorCount > 0;
    public string BadgeText => (_errorCount + _warningCount).ToString();

    public void Info(string source, string message) => Add(LogLevel.Info, source, message, null);

    public void Warn(string source, string message, Exception? exception = null) =>
        Add(LogLevel.Warning, source, message, Describe(exception));

    public void Error(string source, string message, Exception? exception = null) =>
        Add(LogLevel.Error, source, message, Describe(exception));

    // Zwei Ueberladungen je Stufe: nicht jeder Fehler kommt als Ausnahme. Ein Exitcode
    // ungleich null etwa hat nur eine Ausgabe, die es festzuhalten lohnt.
    public void Warn(string source, string message, string? detail) =>
        Add(LogLevel.Warning, source, message, detail);

    public void Error(string source, string message, string? detail) =>
        Add(LogLevel.Error, source, message, detail);

    private static string? Describe(Exception? exception) => exception?.ToString();

    private void Add(LogLevel level, string source, string message, string? detail)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, source, message, detail);

        WriteToFile(entry);

        // Kann aus jedem Thread kommen; die Sammlung gehoert dem Oberflaechen-Thread.
        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess()) Append(entry);
        else app.Dispatcher.BeginInvoke(new Action(() => Append(entry)));
    }

    private void Append(LogEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntriesInMemory) Entries.RemoveAt(Entries.Count - 1);

        if (entry.Level == LogLevel.Error) ErrorCount++;
        else if (entry.Level == LogLevel.Warning) WarningCount++;
    }

    private void WriteToFile(LogEntry entry)
    {
        if (FilePath.Length == 0) return;
        try
        {
            lock (_fileLock)
            {
                Rotate();
                File.AppendAllText(FilePath, entry.ToFileLine() + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Ein Fehler beim Protokollieren darf niemals den Ablauf stoeren; die Anzeige
            // in der Oberflaeche zeigt den Eintrag trotzdem.
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxFileBytes) return;

        var previous = FilePath + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(FilePath, previous);
    }

    public void Clear()
    {
        Entries.Clear();
        ErrorCount = 0;
        WarningCount = 0;
    }

    /// <summary>Oeffnet die Protokolldatei im Standardeditor.</summary>
    public void OpenFile()
    {
        if (FilePath.Length == 0 || !File.Exists(FilePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn(nameof(ErrorLog), "Die Protokolldatei konnte nicht geöffnet werden.", ex);
        }
    }

    public string CopyText() =>
        string.Join(Environment.NewLine, Entries.Reverse().Select(e => e.ToFileLine()));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
