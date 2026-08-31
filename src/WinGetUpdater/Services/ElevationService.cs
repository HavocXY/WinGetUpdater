using System.Security.Principal;
using WinGetUpdater.Models;

namespace WinGetUpdater.Services;

public sealed record ElevationAdvice(bool Recommended, string ReasonKey);

/// <summary>
/// Entscheidet, ob ein Aufruf Administratorrechte braucht - und begruendet es.
/// Die Empfehlung setzt den Schalter in der Oberflaeche vor, ueberstimmt ihn aber nie:
/// die letzte Entscheidung trifft der Benutzer.
/// </summary>
public static class ElevationService
{
    private static bool? _isElevated;

    public static bool IsProcessElevated
    {
        get
        {
            if (_isElevated is not null) return _isElevated.Value;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                _isElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                ErrorLog.Instance.Warn(nameof(ElevationService),
                    "Die Rechtestufe des eigenen Prozesses ließ sich nicht ermitteln; " +
                    "es wird von eingeschränkten Rechten ausgegangen.", ex);
                _isElevated = false;
            }
            return _isElevated.Value;
        }
    }

    public static ElevationAdvice Advise(CommandSpec command, IReadOnlyDictionary<string, object?> values)
    {
        if (command.ParsedElevation == ElevationNeed.Always)
            return new ElevationAdvice(true, "Elevation.Always");

        if (command.ParsedElevation == ElevationNeed.Never)
            return new ElevationAdvice(false, "");

        // Auto: haengt an den gesetzten Optionen.
        if (values.TryGetValue("scope", out var scope) && scope as string == "machine")
            return new ElevationAdvice(true, "Elevation.MachineScope");

        if (command.Id == "settings" && (HasText(values, "adminEnable") || HasText(values, "adminDisable")))
            return new ElevationAdvice(true, "Elevation.AdminSetting");

        if (command.Id == "mcp" && (IsFlagSet(values, "featureEnable") || IsFlagSet(values, "featureDisable")))
            return new ElevationAdvice(true, "Elevation.Feature");

        if (command.Id.StartsWith("dscv3.", StringComparison.Ordinal) && IsFlagSet(values, "dscSet"))
            return new ElevationAdvice(true, "Elevation.DscSet");

        if (command.Id is "configure" or "configure.test")
            return new ElevationAdvice(true, "Elevation.Configuration");

        return new ElevationAdvice(false, "Elevation.MaybeNeeded");
    }

    private static bool HasText(IReadOnlyDictionary<string, object?> values, string id) =>
        values.TryGetValue(id, out var v) && v is string s && !string.IsNullOrWhiteSpace(s);

    private static bool IsFlagSet(IReadOnlyDictionary<string, object?> values, string id) =>
        values.TryGetValue(id, out var v) && v is true;
}
