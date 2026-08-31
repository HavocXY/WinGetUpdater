using System.Text;
using WinGetUpdater.Models;

namespace WinGetUpdater.Services;

/// <summary>
/// Baut aus einem Befehl und den gesetzten Optionen das Argumentfeld fuer winget.exe.
///
/// Zwei Festlegungen, die bewusst so getroffen sind:
/// - Es wird immer die Langform erzeugt (--source statt -s). Das macht die Vorschau lesbar
///   und schliesst Verwechslungen aus, denn winget belegt manche Kurzform doppelt
///   (-h ist bei install "silent", bei configure "history").
/// - Auch Positionsargumente werden mit ihrem Flag geschrieben. winget akzeptiert beides,
///   und mit Flag bleibt die Zeile eindeutig.
/// </summary>
public sealed class CommandLineBuilder
{
    private readonly SchemaStore _store;

    public CommandLineBuilder(SchemaStore store) => _store = store;

    /// <summary>
    /// Erzeugt die Argumente. <paramref name="values"/> bildet Options-Id auf den Wert ab:
    /// bool fuer Schalter, string fuer Werte, IEnumerable&lt;string&gt; fuer wiederholbare Optionen.
    /// </summary>
    public List<string> Build(CommandSpec command, IReadOnlyDictionary<string, object?> values,
                              IEnumerable<string>? extraArguments = null)
    {
        var args = new List<string>(command.Path);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in OrderedOptionIds(command))
        {
            if (!emitted.Add(id)) continue;
            if (!values.TryGetValue(id, out var value) || value is null) continue;
            if (!_store.TryGetOption(id, out var spec)) continue;

            AppendOption(args, spec, value);
        }

        if (extraArguments is not null)
            args.AddRange(extraArguments.Where(a => !string.IsNullOrWhiteSpace(a)));

        return args;
    }

    /// <summary>Reihenfolge: Positionsargument, dann haeufige, dann erweiterte, dann globale Optionen.</summary>
    private IEnumerable<string> OrderedOptionIds(CommandSpec command)
    {
        if (!string.IsNullOrEmpty(command.Positional))
            yield return command.Positional!;
        foreach (var id in command.Primary) yield return id;
        foreach (var id in command.Advanced) yield return id;
        foreach (var id in _store.Globals) yield return id;
    }

    private static void AppendOption(List<string> args, OptionSpec spec, object value)
    {
        switch (value)
        {
            case bool flag:
                if (flag) args.Add(spec.Cli);
                return;

            case string text:
                if (!string.IsNullOrWhiteSpace(text))
                {
                    args.Add(spec.Cli);
                    args.Add(text.Trim());
                }
                return;

            case IEnumerable<string> items:
                foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i)))
                {
                    args.Add(spec.Cli);
                    args.Add(item.Trim());
                }
                return;
        }
    }

    /// <summary>Die Zeile, wie sie in der Vorschau steht und in PowerShell eingefuegt werden kann.</summary>
    public static string ToDisplayLine(IReadOnlyList<string> args) =>
        "winget " + string.Join(' ', args.Select(QuoteForPowerShell));

    /// <summary>
    /// PowerShell-Quoting: nur wenn noetig, und eingebettete Anfuehrungszeichen werden
    /// verdoppelt, wie PowerShell es innerhalb doppelter Anfuehrungszeichen erwartet.
    /// </summary>
    public static string QuoteForPowerShell(string argument)
    {
        if (argument.Length == 0) return "\"\"";

        var needsQuotes = argument.Any(c =>
            char.IsWhiteSpace(c) || c is '"' or '\'' or '`' or '$' or ';' or ',' or '&' or '|' or '(' or ')' or '{' or '}');

        if (!needsQuotes) return argument;
        return "\"" + argument.Replace("\"", "\"\"") + "\"";
    }

    public static string ToPowerShellScript(IReadOnlyList<string> args, string? comment = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Erzeugt von WinGetUpdater");
        if (!string.IsNullOrWhiteSpace(comment))
            builder.AppendLine("# " + comment);
        builder.AppendLine();
        builder.AppendLine(ToDisplayLine(args));
        builder.AppendLine("exit $LASTEXITCODE");
        return builder.ToString();
    }
}
