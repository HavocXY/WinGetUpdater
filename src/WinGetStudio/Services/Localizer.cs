using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinGetStudio.Services;

/// <summary>
/// Texte der Oberflaeche in Deutsch und Englisch.
///
/// Bewusst eine Tabelle im Code statt Satellitenassemblys aus .resx: die Sprache laesst
/// sich damit im laufenden Programm umschalten, ohne Neustart und ohne dass Bindungen
/// neu aufgebaut werden muessen. Die winget-eigenen Texte - Optionsnamen und deren
/// Beschreibungen - stehen dagegen im Schema, weil sie dorthin gehoeren.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Instance { get; } = new();

    private string _language = "de";

    private Localizer() { }

    public string Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(IsGerman));
            OnPropertyChanged("Item[]");   // laesst alle Indexer-Bindungen neu auswerten
        }
    }

    public bool IsGerman => _language == "de";

    public void Toggle() => Language = IsGerman ? "en" : "de";

    public string this[string key] =>
        Table.TryGetValue(key, out var pair) ? (IsGerman ? pair.De : pair.En) : key;

    public string Format(string key, params object[] args) => string.Format(this[key], args);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static readonly Dictionary<string, (string De, string En)> Table = new()
    {
        ["App.Subtitle"] = ("Oberfläche für den Windows-Paket-Manager", "A front end for the Windows Package Manager"),

        // ---------------------------------------------------------- Hauptansicht: Updates
        ["Update.HeadlineStart"] = ("Programme aktuell halten", "Keep your programs up to date"),
        ["Update.HeadlineChecking"] = ("Wird geprüft …", "Checking …"),
        ["Update.HeadlineUpToDate"] = ("Alles ist aktuell", "Everything is up to date"),
        ["Update.HeadlineUpdating"] = ("Wird aktualisiert …", "Updating …"),
        ["Update.HeadlineFinished"] = ("Fertig", "Done"),
        ["Update.HeadlineReadyOne"] = ("1 Programm kann aktualisiert werden",
                                       "1 program can be updated"),
        ["Update.HeadlineReadyMany"] = ("{0} Programme können aktualisiert werden",
                                        "{0} programs can be updated"),

        ["Update.SubStart"] = ("Diese Anwendung prüft, für welche installierten Programme es neuere " +
                               "Versionen gibt, und installiert sie auf Wunsch. Sie können jederzeit " +
                               "auswählen, was aktualisiert wird.",
                               "This application checks which of your installed programs have newer " +
                               "versions available and installs them on request. You choose what gets " +
                               "updated."),
        ["Update.SubChecking"] = ("Der Paketmanager vergleicht Ihre installierten Programme mit den Quellen.",
                                  "The package manager is comparing your installed programs against the sources."),
        ["Update.SubUpToDate"] = ("Für Ihre installierten Programme gibt es derzeit keine neueren Versionen. ",
                                  "None of your installed programs have a newer version right now. "),
        ["Update.SubUpdating"] = ("Bitte warten. Sie können den Vorgang jederzeit abbrechen.",
                                  "Please wait. You can cancel at any time."),
        ["Update.SubReady"] = ("Wählen Sie aus, was aktualisiert werden soll. ",
                               "Choose what should be updated. "),
        ["Update.SubFinishedOkOne"] = ("Ein Programm wurde aktualisiert.", "One program was updated."),
        ["Update.SubFinishedOkMany"] = ("{0} Programme wurden aktualisiert.", "{0} programs were updated."),
        ["Update.SubFinishedMixed"] = ("{0} aktualisiert, {1} fehlgeschlagen.", "{0} updated, {1} failed."),
        ["Update.LastCheck"] = ("Zuletzt geprüft um {0} Uhr.", "Last checked at {0}."),

        ["Update.Check"] = ("Jetzt nach Updates suchen", "Check for updates now"),
        ["Update.CheckAgain"] = ("Erneut prüfen", "Check again"),
        ["Update.RunNone"] = ("Nichts ausgewählt", "Nothing selected"),
        ["Update.RunOne"] = ("1 Programm aktualisieren", "Update 1 program"),
        ["Update.RunMany"] = ("{0} Programme aktualisieren", "Update {0} programs"),
        ["Update.SelectAll"] = ("Alle auswählen", "Select all"),
        ["Update.SelectedOf"] = ("{0} von {1} ausgewählt", "{0} of {1} selected"),
        ["Update.SelectNone"] = ("Auswahl aufheben", "Clear selection"),

        ["Update.ColumnProgram"] = ("Programm", "Program"),
        ["Update.ColumnVersion"] = ("Version", "Version"),
        ["Update.ColumnSource"] = ("Quelle", "Source"),

        ["Update.Options"] = ("Optionen", "Options"),
        ["Update.OptSilent"] = ("Ohne Rückfragen installieren", "Install without prompts"),
        ["Update.OptSilentHelp"] = ("Das Installationsprogramm läuft unsichtbar durch. Abschalten, wenn " +
                                    "Sie die Dialoge des Herstellers sehen möchten.",
                                    "The installer runs invisibly. Turn off if you want to see the " +
                                    "vendor's own dialogs."),
        ["Update.OptAgreements"] = ("Lizenzvereinbarungen annehmen", "Accept licence agreements"),
        ["Update.OptAgreementsHelp"] = ("Nötig für Pakete, die vor der Installation eine Zustimmung " +
                                        "verlangen – etwa aus dem Microsoft Store.",
                                        "Required for packages that ask for consent before installing, " +
                                        "such as those from the Microsoft Store."),
        ["Update.OptUnknown"] = ("Programme mit unbekannter Version einbeziehen",
                                 "Include programs with unknown version"),
        ["Update.OptUnknownHelp"] = ("Bei manchen Programmen lässt sich die installierte Version nicht " +
                                     "ermitteln. Sie werden sonst übersprungen.",
                                     "For some programs the installed version cannot be determined. " +
                                     "They are skipped otherwise."),
        ["Update.OptPinned"] = ("Angeheftete Programme einbeziehen", "Include pinned programs"),
        ["Update.OptPinnedHelp"] = ("Anheftungen halten ein Programm bewusst auf einer Version.",
                                    "A pin deliberately holds a program at one version."),
        ["Update.OptAdmin"] = ("Als Administrator ausführen", "Run as administrator"),
        ["Update.OptAdminHelp"] = ("Nötig für Programme, die für alle Benutzer installiert sind.",
                                   "Required for programs installed for all users."),

        ["Update.ChipSilent"] = ("ohne Rückfragen", "no prompts"),
        ["Update.ChipInteractive"] = ("mit Dialogen", "with dialogs"),
        ["Update.ChipAgreements"] = ("Lizenzen angenommen", "licences accepted"),
        ["Update.ChipUnknown"] = ("inkl. unbekannter Versionen", "incl. unknown versions"),
        ["Update.ChipPinned"] = ("inkl. angehefteter", "incl. pinned"),
        ["Update.ChipAdmin"] = ("als Administrator", "as administrator"),

        ["Update.ShowOutput"] = ("Verlauf anzeigen", "Show log"),
        ["Update.HideOutput"] = ("Verlauf ausblenden", "Hide log"),
        ["Update.ShowCommand"] = ("Befehl anzeigen", "Show command"),

        ["Update.ItemDone"] = ("aktualisiert", "updated"),
        ["Update.ItemFailed"] = ("fehlgeschlagen (Code {0})", "failed (code {0})"),
        ["Update.ItemCanceled"] = ("abgebrochen", "cancelled"),
        ["Update.CheckFailed"] = ("Die Suche nach Updates ist fehlgeschlagen (Exitcode {0}).",
                                  "Checking for updates failed (exit code {0})."),
        ["Update.SomeFailed"] = ("{0} Programm(e) konnten nicht aktualisiert werden: {1}",
                                 "{0} program(s) could not be updated: {1}"),
        ["Update.SeeLog"] = ("Einzelheiten stehen im Verlauf und im Fehlerprotokoll.",
                             "Details are in the log below and in the error log."),

        // ---------------------------------------------------------- Fehlerprotokoll
        ["Log.Title"] = ("Fehlerprotokoll", "Error log"),
        ["Log.Subtitle"] = ("Jeder abgefangene Fehler landet hier – auch die, die den Ablauf nicht gestört haben.",
                            "Every caught error ends up here – including those that did not disrupt anything."),
        ["Log.Empty"] = ("Noch nichts vorgefallen.", "Nothing has happened yet."),
        ["Log.Open"] = ("Protokoll öffnen", "Open log"),
        ["Log.OpenFile"] = ("Datei öffnen", "Open file"),
        ["Log.Copy"] = ("Kopieren", "Copy"),
        ["Log.Clear"] = ("Leeren", "Clear"),
        ["Log.Close"] = ("Schließen", "Close"),
        ["Log.NoProblems"] = ("Keine Fehler", "No errors"),

        // ---------------------------------------------------------- Schale
        ["Shell.Updates"] = ("Updates", "Updates"),
        ["Shell.AllCommands"] = ("Alle Befehle", "All commands"),
        ["Shell.AllCommandsHint"] = ("Der vollständige Paketmanager: suchen, installieren, deinstallieren, " +
                                     "Quellen, Pins, Konfiguration.",
                                     "The complete package manager: search, install, uninstall, sources, " +
                                     "pins, configuration."),
        ["Shell.FatalTitle"] = ("WinGet Studio konnte nicht starten", "WinGet Studio could not start"),

        ["Nav.Search"] = ("Befehl suchen", "Search command"),
        ["Nav.NoResults"] = ("Kein Befehl passt zur Suche.", "No command matches the search."),
        ["Nav.CommandCount"] = ("{0} Befehle", "{0} commands"),

        ["Header.Docs"] = ("Dokumentation", "Documentation"),
        ["Header.NeedsAdmin"] = ("Administrator", "Administrator"),
        ["Header.Danger"] = ("Verändert das System", "Changes the system"),

        ["Options.Primary"] = ("Häufig verwendet", "Commonly used"),
        ["Options.Advanced"] = ("Erweitert", "Advanced"),
        ["Options.Global"] = ("Global", "Global"),
        ["Options.Extra"] = ("Zusätzliche Argumente", "Extra arguments"),
        ["Options.ExtraHint"] = ("Wird unverändert ans Ende der Befehlszeile gehängt.",
                                "Appended to the command line unchanged."),
        ["Options.None"] = ("Dieser Befehl hat keine eigenen Optionen.", "This command has no options of its own."),
        ["Options.Count"] = ("{0} Optionen", "{0} options"),
        ["Options.Reset"] = ("Zurücksetzen", "Reset"),
        ["Options.Risky"] = ("Hebt eine Schutzfunktion auf.", "Disables a safeguard."),
        ["Options.Add"] = ("Hinzufügen", "Add"),
        ["Options.Browse"] = ("Durchsuchen", "Browse"),
        ["Options.NotSet"] = ("nicht gesetzt", "not set"),

        ["Preview.Title"] = ("Befehlszeile", "Command line"),
        ["Preview.Copy"] = ("Kopieren", "Copy"),
        ["Preview.Copied"] = ("Kopiert", "Copied"),
        ["Preview.Save"] = ("Als .ps1 speichern", "Save as .ps1"),

        ["Run.Execute"] = ("Ausführen", "Run"),
        ["Run.Cancel"] = ("Abbrechen", "Cancel"),
        ["Run.Elevated"] = ("Als Administrator ausführen", "Run as administrator"),
        ["Run.Running"] = ("Läuft…", "Running…"),

        ["Result.Table"] = ("Ergebnisse", "Results"),
        ["Result.Output"] = ("Ausgabe", "Output"),
        ["Result.Rows"] = ("{0} Zeilen", "{0} rows"),
        ["Result.Empty"] = ("Noch nichts ausgeführt.", "Nothing has been run yet."),
        ["Result.NoTable"] = ("Die Ausgabe ließ sich nicht als Tabelle lesen – siehe Ausgabe.",
                              "The output could not be read as a table – see Output."),
        ["Result.Filter"] = ("Ergebnisse filtern", "Filter results"),

        ["Status.Success"] = ("Erfolgreich", "Succeeded"),
        ["Status.Failed"] = ("Fehlgeschlagen", "Failed"),
        ["Status.Canceled"] = ("Abgebrochen", "Cancelled"),
        ["Status.ExitCode"] = ("Exitcode {0}", "Exit code {0}"),
        ["Status.Duration"] = ("{0:N1} s", "{0:N1} s"),

        ["Elevation.Always"] = ("Dieser Befehl erfordert immer Administratorrechte.",
                                "This command always requires administrator rights."),
        ["Elevation.MachineScope"] = ("Bereich „machine“ erfordert Administratorrechte.",
                                      "Scope \"machine\" requires administrator rights."),
        ["Elevation.AdminSetting"] = ("Administratoreinstellungen zu ändern erfordert erhöhte Rechte.",
                                      "Changing administrator settings requires elevation."),
        ["Elevation.Feature"] = ("Das Ein- und Ausschalten dieses Features erfordert erhöhte Rechte.",
                                 "Toggling this feature requires elevation."),
        ["Elevation.DscSet"] = ("„set“ verändert den Systemzustand und erfordert meist erhöhte Rechte.",
                                "\"set\" changes system state and usually requires elevation."),
        ["Elevation.Configuration"] = ("Konfigurationen greifen in der Regel systemweit ein.",
                                       "Configurations usually apply system-wide."),
        ["Elevation.MaybeNeeded"] = ("Je nach Paket können Administratorrechte nötig sein.",
                                     "Depending on the package, administrator rights may be required."),
        ["Elevation.AlreadyElevated"] = ("Die Anwendung läuft bereits mit Administratorrechten.",
                                         "The application is already running elevated."),

        ["Grid.Install"] = ("Installieren", "Install"),
        ["Grid.Upgrade"] = ("Aktualisieren", "Upgrade"),
        ["Grid.Uninstall"] = ("Deinstallieren", "Uninstall"),
        ["Grid.Details"] = ("Details", "Details"),
        ["Grid.Pin"] = ("Pin setzen", "Add pin"),
        ["Grid.Selected"] = ("{0} ausgewählt", "{0} selected"),

        ["Shell.Theme"] = ("Design wechseln", "Switch theme"),
        ["Shell.Language"] = ("Sprache wechseln", "Switch language"),
        ["Shell.Elevated"] = ("Mit Administratorrechten gestartet", "Started with administrator rights"),

        ["Winget.NotFound"] = ("winget wurde auf diesem System nicht gefunden.",
                               "winget was not found on this system."),
        ["Winget.NotFoundHint"] = ("WinGet Studio steuert das Programm winget.exe. Es gehört zum " +
                                   "App-Installer aus dem Microsoft Store. Installieren Sie den " +
                                   "App-Installer und starten Sie die Anwendung neu.",
                                   "WinGet Studio drives winget.exe, which ships with the App Installer " +
                                   "from the Microsoft Store. Install the App Installer and restart the application."),
        ["Winget.FoundVia"] = ("gefunden über {0}", "found via {0}")
    };
}
