using WinGetUpdater.Services;
using WinGetUpdater.ViewModels;
using Xunit;

namespace WinGetUpdater.Tests;

/// <summary>
/// Prüft den generischen [Localized]/RegisterLocalized-Mechanismus direkt, unabhängig von
/// den konkreten ViewModels. Vor diesem Umbau musste jede lokalisierte Eigenschaft von Hand
/// in einer Namensliste an der Aufrufstelle aufgezählt werden - das lässt sich vergessen.
/// Jetzt genügt das Attribut an der Eigenschaft selbst.
/// </summary>
public class ObservableObjectTests
{
    private sealed class Probe : ObservableObject
    {
        public Probe() => RegisterLocalized();

        public void RegisterAgain() => RegisterLocalized();

        [Localized] public string Localized1 => "a";
        [Localized] public string Localized2 => "b";

        // Absichtlich nicht markiert - darf beim Sprachwechsel nicht mitgemeldet werden.
        public string NotLocalized => "c";
    }

    [Fact]
    public void Sprachwechsel_meldet_nur_die_mit_Localized_markierten_Eigenschaften()
    {
        var probe = new Probe();
        var original = Localizer.Instance.Language;
        try
        {
            var raised = new List<string>();
            probe.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

            Localizer.Instance.Language = Localizer.Instance.IsGerman ? "en" : "de";

            Assert.Contains(nameof(Probe.Localized1), raised);
            Assert.Contains(nameof(Probe.Localized2), raised);
            Assert.DoesNotContain(nameof(Probe.NotLocalized), raised);
        }
        finally
        {
            Localizer.Instance.Language = original;
        }
    }

    [Fact]
    public void Mehrfacher_RegisterLocalized_Aufruf_abonniert_nur_einmal()
    {
        // Verteidigt den _subscribedToLanguage-Schutz: ohne ihn wuerde jeder weitere Aufruf
        // eine zusaetzliche Abo-Closure anhaengen und jede Eigenschaft mehrfach pro
        // Sprachwechsel feuern.
        var probe = new Probe();
        probe.RegisterAgain();
        probe.RegisterAgain();

        var original = Localizer.Instance.Language;
        try
        {
            var count = 0;
            probe.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Probe.Localized1)) count++; };

            Localizer.Instance.Language = Localizer.Instance.IsGerman ? "en" : "de";

            Assert.Equal(1, count);
        }
        finally
        {
            Localizer.Instance.Language = original;
        }
    }
}
