using System.IO;
using WinGetStudio.Services;

namespace WinGetStudio.Tests;

internal static class TestSchema
{
    private static SchemaStore? _cached;

    /// <summary>
    /// Laedt das ausgelieferte Schema. Der Pfad wird von der Testassembly aus gesucht,
    /// damit die Tests unabhaengig vom Arbeitsverzeichnis laufen.
    /// </summary>
    public static SchemaStore Load()
    {
        if (_cached is not null) return _cached;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "winget-schema.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                         "src", "WinGetStudio", "Resources", "winget-schema.json")
        };

        var path = candidates.FirstOrDefault(File.Exists)
                   ?? throw new FileNotFoundException(
                       "winget-schema.json nicht gefunden. Gesucht in: " + string.Join(" | ", candidates));

        return _cached = SchemaStore.Load(Path.GetFullPath(path));
    }
}
