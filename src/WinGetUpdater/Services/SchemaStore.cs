using System.IO;
using System.Text.Json;
using WinGetUpdater.Models;

namespace WinGetUpdater.Services;

/// <summary>
/// Laedt winget-schema.json und stellt Befehle und Optionen bereit.
/// Das Schema ist die einzige Quelle dafuer, welche Optionen die Oberflaeche anbietet;
/// tools\Check-Schema.ps1 haelt es gegen "winget --help" gruen.
/// </summary>
public sealed class SchemaStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public WingetSchema Schema { get; }

    public IReadOnlyList<CommandSpec> Commands => Schema.Commands;
    public IReadOnlyList<GroupSpec> Groups => Schema.Groups;
    public IReadOnlyList<string> Globals => Schema.Globals;

    private SchemaStore(WingetSchema schema)
    {
        Schema = schema;
        foreach (var (id, option) in schema.Options)
            option.Id = id;
    }

    /// <summary>
    /// Laedt das Schema. Ohne Pfadangabe gilt: eine Datei neben der Anwendung hat Vorrang,
    /// sonst wird die eingebettete Fassung verwendet. So bleibt die Anwendung eine einzelne
    /// Datei, laesst sich aber an eine neuere winget-Version anpassen, ohne neu zu bauen.
    /// </summary>
    public static SchemaStore Load(string? path = null)
    {
        var json = ReadJson(path);
        var schema = JsonSerializer.Deserialize<WingetSchema>(json, JsonOptions)
                     ?? throw new InvalidDataException("Schemadatei konnte nicht gelesen werden.");
        return new SchemaStore(schema);
    }

    private static string ReadJson(string? path)
    {
        if (path is not null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Schemadatei nicht gefunden: {path}", path);
            return File.ReadAllText(path);
        }

        var external = Path.Combine(AppContext.BaseDirectory, "Resources", "winget-schema.json");
        if (File.Exists(external)) return File.ReadAllText(external);

        using var stream = typeof(SchemaStore).Assembly.GetManifestResourceStream("winget-schema.json")
                           ?? throw new InvalidOperationException(
                               "Das eingebettete Schema fehlt. Die Anwendung ist unvollständig gebaut.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public OptionSpec Option(string id) =>
        Schema.Options.TryGetValue(id, out var spec)
            ? spec
            : throw new KeyNotFoundException($"Option '{id}' ist im Schema nicht definiert.");

    public bool TryGetOption(string id, out OptionSpec spec) => Schema.Options.TryGetValue(id, out spec!);

    public CommandSpec? Find(string commandId) => Schema.Commands.FirstOrDefault(c => c.Id == commandId);

    public string DocsUrl(CommandSpec command) => Schema.DocsBase + command.Docs;
}
