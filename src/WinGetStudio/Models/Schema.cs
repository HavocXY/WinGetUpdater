using System.Text.Json.Serialization;

namespace WinGetStudio.Models;

/// <summary>Zweisprachiger Text aus dem Schema.</summary>
public sealed class Loc
{
    public string De { get; set; } = "";
    public string En { get; set; } = "";

    public string Get(string language) => language == "de" ? De : (En.Length > 0 ? En : De);
}

public enum OptionKind
{
    Flag,
    Text,
    Int,
    Enum,
    FilePath,
    FolderPath,
    SaveFilePath
}

/// <summary>Eine einzelne winget-Option, unabhaengig vom Befehl.</summary>
public sealed class OptionSpec
{
    /// <summary>Schluessel im Schema. Wird beim Laden nachgetragen.</summary>
    [JsonIgnore] public string Id { get; set; } = "";

    /// <summary>Die lange Schreibweise, die WinGet Studio erzeugt, z. B. <c>--scope</c>.</summary>
    public string Cli { get; set; } = "";

    /// <summary>Kurzform, z. B. <c>-s</c>. Nur zur Anzeige; erzeugt wird immer die Langform.</summary>
    public string? Alias { get; set; }

    /// <summary>Weitere gleichwertige Langformen, die winget ebenfalls akzeptiert.</summary>
    public List<string>? Aliases { get; set; }

    public string Kind { get; set; } = "flag";
    public List<string>? Values { get; set; }
    public bool Positional { get; set; }
    public bool Repeatable { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public string? Placeholder { get; set; }

    /// <summary>Dateifilter-Kennung fuer den Durchsuchen-Dialog: yaml, json, log, config.</summary>
    public string? Filter { get; set; }

    /// <summary>"high" markiert Optionen, die Schutzmechanismen aushebeln.</summary>
    public string? Risk { get; set; }

    public Loc Label { get; set; } = new();
    public Loc Desc { get; set; } = new();

    [JsonIgnore] public bool IsRisky => Risk == "high";

    [JsonIgnore]
    public OptionKind ParsedKind => Kind switch
    {
        "text" => OptionKind.Text,
        "int" => OptionKind.Int,
        "enum" => OptionKind.Enum,
        "filePath" => OptionKind.FilePath,
        "folderPath" => OptionKind.FolderPath,
        "saveFilePath" => OptionKind.SaveFilePath,
        _ => OptionKind.Flag
    };

    /// <summary>Alle Schreibweisen, die winget fuer diese Option kennt.</summary>
    public IEnumerable<string> AllCliForms()
    {
        yield return Cli;
        if (Aliases is not null)
            foreach (var a in Aliases) yield return a;
        if (!string.IsNullOrEmpty(Alias)) yield return Alias;
    }
}

public sealed class GroupSpec
{
    public string Id { get; set; } = "";
    public Loc Label { get; set; } = new();
}

/// <summary>Wann der Befehl erhoehte Rechte braucht.</summary>
public enum ElevationNeed
{
    /// <summary>Nie.</summary>
    Never,
    /// <summary>Abhaengig von den gesetzten Optionen, z. B. --scope machine.</summary>
    Auto,
    /// <summary>Immer.</summary>
    Always
}

/// <summary>Wie das Ergebnis dargestellt wird.</summary>
public enum OutputKind
{
    /// <summary>Fortlaufende Ausgabe eines laenger laufenden Vorgangs.</summary>
    Stream,
    /// <summary>Spaltentabelle, die sich in ein Gitter uebersetzen laesst.</summary>
    Table,
    /// <summary>Freier Text oder JSON.</summary>
    Text
}

public sealed class CommandSpec
{
    public string Id { get; set; } = "";
    public List<string> Path { get; set; } = new();
    public string Group { get; set; } = "";
    public string Docs { get; set; } = "";
    public List<string>? Aliases { get; set; }
    public Loc Title { get; set; } = new();
    public Loc Desc { get; set; } = new();

    /// <summary>Options-Id, die winget auch ohne Flag als erstes Argument akzeptiert.</summary>
    public string? Positional { get; set; }

    public string Output { get; set; } = "stream";

    [JsonPropertyName("elevation")]
    public string ElevationValue { get; set; } = "never";
    public bool Danger { get; set; }
    public List<string> Primary { get; set; } = new();
    public List<string> Advanced { get; set; } = new();

    [JsonIgnore] public string CommandLine => "winget " + string.Join(' ', Path);

    [JsonIgnore]
    public OutputKind ParsedOutput => Output switch
    {
        "table" => OutputKind.Table,
        "text" => OutputKind.Text,
        _ => OutputKind.Stream
    };

    [JsonIgnore]
    public ElevationNeed ParsedElevation => ElevationValue switch
    {
        "always" => ElevationNeed.Always,
        "auto" => ElevationNeed.Auto,
        _ => ElevationNeed.Never
    };
}

public sealed class WingetSchema
{
    public int SchemaVersion { get; set; }
    public string WingetVersion { get; set; } = "";
    public string DocsBase { get; set; } = "";
    public Dictionary<string, OptionSpec> Options { get; set; } = new();
    public List<string> Globals { get; set; } = new();
    public List<GroupSpec> Groups { get; set; } = new();
    public List<CommandSpec> Commands { get; set; } = new();
}
