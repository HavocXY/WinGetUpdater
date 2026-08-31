using System.Text;
using System.Text.RegularExpressions;

namespace WinGetUpdater.Services;

public sealed class TableResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<string[]> Rows { get; init; }

    /// <summary>Zeilen, die nicht zur Tabelle gehoerten - etwa "1 Aktualisierungen verfügbar."</summary>
    public required IReadOnlyList<string> Trailer { get; init; }

    public int NameColumn { get; init; } = -1;
    public int IdColumn { get; init; } = -1;
    public int VersionColumn { get; init; } = -1;
    public int AvailableColumn { get; init; } = -1;
    public int SourceColumn { get; init; } = -1;

    public string Cell(int row, int column) =>
        column >= 0 && row < Rows.Count && column < Rows[row].Length ? Rows[row][column] : "";
}

/// <summary>
/// Liest die Spaltentabellen von search, list, upgrade, pin list und source list.
///
/// Drei Eigenheiten der winget-Ausgabe bestimmen den Aufbau:
///
/// 1. Die Spaltenkoepfe sind lokalisiert ("Übereinstimmung" statt "Match"). Die Spalten werden
///    deshalb ueber Positionen bestimmt, nicht ueber Namen.
/// 2. winget fuellt die Spalten nach *Darstellungsbreite*. Ein ostasiatisches Zeichen belegt zwei
///    Spalten, aber nur ein char - wer in Zeichen rechnet, verschiebt die ganze Zeile. Ein
///    installiertes Paket mit einem solchen Zeichen im Namen genuegt, damit eine Zeile falsch
///    zerlegt wird.
/// 3. "winget upgrade" haengt seine Zusammenfassung ohne Leerzeile direkt an die Tabelle. Diese
///    Zeile ignoriert das Spaltenraster und wird daran auch erkannt.
/// </summary>
public static class TableParser
{
    private static readonly Regex ColumnSplitter = new(@"\S(?:.*?\S)?(?=\s{2,}|$)", RegexOptions.Compiled);
    private static readonly Regex PackageIdShape = new(@"^[^\s]+\.[^\s]+$", RegexOptions.Compiled);

    public static TableResult? Parse(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');

        var headerIndex = FindHeaderIndex(lines);
        if (headerIndex < 0) return null;

        var header = lines[headerIndex];

        var candidates = new List<string>();
        for (var i = headerIndex + 2; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) break;
            candidates.Add(lines[i]);
        }

        var starts = ColumnStarts(header, candidates);
        if (starts.Count < 2) return null;

        var rows = new List<string[]>();
        var trailer = new List<string>();

        foreach (var line in candidates)
        {
            if (!FitsGrid(line, starts))
            {
                trailer.Add(line.Trim());
                continue;
            }

            var cells = Slice(line, starts);
            if (cells.All(string.IsNullOrEmpty)) continue;
            rows.Add(cells);
        }

        var columns = Slice(header, starts);

        return new TableResult
        {
            Columns = columns,
            Rows = rows,
            Trailer = trailer,
            NameColumn = columns.Length > 0 ? 0 : -1,
            IdColumn = FindIdColumn(columns, rows),
            VersionColumn = FindColumn(columns, "version"),
            AvailableColumn = FindColumn(columns, "verfügbar", "verfuegbar", "available"),
            SourceColumn = FindColumn(columns, "quelle", "source")
        };
    }

    /// <summary>Die Kopfzeile ist die Zeile direkt ueber der durchgezogenen Trennlinie.</summary>
    private static int FindHeaderIndex(string[] lines)
    {
        for (var i = 0; i < lines.Length - 1; i++)
        {
            var separator = lines[i + 1].Trim();
            if (separator.Length >= 8 && separator.All(c => c == '-'))
            {
                if (!string.IsNullOrWhiteSpace(lines[i])) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Spaltenanfaenge als Darstellungsspalten. Normalerweise trennen zwei oder mehr Leerzeichen
    /// die Spalten. Bei "winget upgrade" und "winget list" schreibt winget den Kopf jedoch als
    /// "Version Verfügbar Quelle" mit nur einem Leerzeichen, wenn die Spalte genau so breit ist
    /// wie ihr eigener Name. Ein einzelnes Leerzeichen gilt deshalb zusaetzlich als Grenze, wenn
    /// an derselben Stelle in jeder Datenzeile ebenfalls ein Leerzeichen steht.
    /// </summary>
    private static List<int> ColumnStarts(string header, IReadOnlyList<string> rows)
    {
        var starts = new List<int>();
        var headerColumns = ColumnOf(header);

        foreach (Match match in ColumnSplitter.Matches(header))
        {
            starts.Add(headerColumns[match.Index]);

            var end = match.Index + match.Length;
            for (var j = match.Index + 1; j < end - 1; j++)
            {
                if (header[j] != ' ' || header[j - 1] == ' ' || header[j + 1] == ' ') continue;

                var column = headerColumns[j];
                if (rows.All(r => IsSpaceAt(r, column))) starts.Add(column + 1);
            }
        }

        starts.Sort();
        return starts;
    }

    /// <summary>
    /// Eine Datenzeile folgt dem Spaltenraster: unmittelbar vor jedem Spaltenanfang steht ein
    /// Leerzeichen, weil winget jede Spalte breiter macht als ihren laengsten Wert. Zeilen, die
    /// das verletzen, sind keine Daten - etwa die Zusammenfassung am Ende von "winget upgrade".
    /// </summary>
    private static bool FitsGrid(string line, List<int> starts)
    {
        for (var i = 1; i < starts.Count; i++)
            if (!IsSpaceAt(line, starts[i] - 1)) return false;

        return true;
    }

    private static string[] Slice(string line, List<int> starts)
    {
        var cells = new string[starts.Count];
        for (var i = 0; i < starts.Count; i++)
        {
            var from = CharIndexOfColumn(line, starts[i]);
            if (from >= line.Length) { cells[i] = ""; continue; }

            var to = i + 1 < starts.Count
                ? Math.Min(CharIndexOfColumn(line, starts[i + 1]), line.Length)
                : line.Length;
            cells[i] = line[from..to].Trim();
        }
        return cells;
    }

    private static bool IsSpaceAt(string line, int column)
    {
        var index = CharIndexOfColumn(line, column);
        return index >= line.Length || line[index] == ' ';
    }

    /// <summary>Erstes Zeichen, das an oder hinter der gegebenen Darstellungsspalte beginnt.</summary>
    private static int CharIndexOfColumn(string line, int column)
    {
        var width = 0;
        var index = 0;

        while (index < line.Length)
        {
            if (width >= column) return index;
            width += RuneWidth(line, index, out var consumed);
            index += consumed;
        }
        return line.Length;
    }

    /// <summary>Darstellungsspalte jedes Zeichens einer Zeile.</summary>
    private static int[] ColumnOf(string line)
    {
        var columns = new int[line.Length + 1];
        var width = 0;
        var index = 0;

        while (index < line.Length)
        {
            columns[index] = width;
            width += RuneWidth(line, index, out var consumed);
            for (var k = 1; k < consumed && index + k < columns.Length; k++)
                columns[index + k] = columns[index];
            index += consumed;
        }
        columns[line.Length] = width;
        return columns;
    }

    /// <summary>
    /// Breite eines Zeichens in Konsolenspalten. Ostasiatische Schriftzeichen und Emoji belegen
    /// zwei Spalten, kombinierende Zeichen keine.
    /// </summary>
    private static int RuneWidth(string line, int index, out int consumed)
    {
        if (!Rune.TryGetRuneAt(line, index, out var rune))
        {
            consumed = 1;
            return 1;
        }

        consumed = rune.Utf16SequenceLength;

        var category = Rune.GetUnicodeCategory(rune);
        if (category is System.Globalization.UnicodeCategory.NonSpacingMark
                     or System.Globalization.UnicodeCategory.EnclosingMark
                     or System.Globalization.UnicodeCategory.Format)
            return 0;

        return IsWide(rune.Value) ? 2 : 1;
    }

    private static bool IsWide(int cp) =>
        (cp >= 0x1100 && cp <= 0x115F) ||    // Hangul-Jamo
        (cp >= 0x2E80 && cp <= 0x303E) ||    // CJK-Radikale, Kangxi, CJK-Symbole
        (cp >= 0x3041 && cp <= 0x33FF) ||    // Kana, Bopomofo, CJK-Kompatibilitaet
        (cp >= 0x3400 && cp <= 0x4DBF) ||    // CJK Ext. A
        (cp >= 0x4E00 && cp <= 0x9FFF) ||    // CJK
        (cp >= 0xA000 && cp <= 0xA4CF) ||    // Yi
        (cp >= 0xA960 && cp <= 0xA97F) ||    // Hangul-Jamo Ext. A
        (cp >= 0xAC00 && cp <= 0xD7A3) ||    // Hangul-Silben
        (cp >= 0xF900 && cp <= 0xFAFF) ||    // CJK-Kompatibilitaetsideogramme
        (cp >= 0xFE10 && cp <= 0xFE19) ||
        (cp >= 0xFE30 && cp <= 0xFE6F) ||
        (cp >= 0xFF00 && cp <= 0xFF60) ||    // Vollbreite Formen
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||
        (cp >= 0x1F300 && cp <= 0x1F64F) ||  // Emoji
        (cp >= 0x1F680 && cp <= 0x1F6FF) ||
        (cp >= 0x1F900 && cp <= 0x1F9FF) ||
        (cp >= 0x20000 && cp <= 0x2FFFD) ||  // CJK Ext. B und weiter
        (cp >= 0x30000 && cp <= 0x3FFFD);

    private static int FindColumn(string[] columns, params string[] names)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            var text = columns[i].Trim();
            if (names.Any(n => string.Equals(text, n, StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// "ID" heisst in beiden Sprachen gleich. Falls der Kopf doch abweicht, entscheidet
    /// die Form der Werte: Paket-Ids enthalten einen Punkt und keine Leerzeichen.
    /// </summary>
    private static int FindIdColumn(string[] columns, List<string[]> rows)
    {
        var byHeader = FindColumn(columns, "id");
        if (byHeader >= 0) return byHeader;
        if (rows.Count == 0) return -1;

        var best = -1;
        var bestScore = 0;
        for (var column = 1; column < columns.Length; column++)
        {
            var score = rows.Count(r => column < r.Length && PackageIdShape.IsMatch(r[column]));
            if (score > bestScore) { bestScore = score; best = column; }
        }
        return bestScore >= Math.Max(1, rows.Count / 2) ? best : -1;
    }
}
