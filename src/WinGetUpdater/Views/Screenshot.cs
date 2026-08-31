using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinGetUpdater.Views;

/// <summary>
/// Nimmt das Fenster als PNG auf. Gedacht fuer die Pruefung des Layouts und fuer
/// Bilder in der Dokumentation, ohne dass jemand danebensitzen und klicken muss.
/// Aufruf: WinGetUpdater.exe --screenshot &lt;datei.png&gt; [--command &lt;id&gt;] [--run] [--light]
/// </summary>
internal static class Screenshot
{
    public static async Task CaptureAsync(Window window, string path)
    {
        // Zweimal bis Leerlauf warten: einmal fuer das Layout, einmal fuer die
        // Bindungen, die erst danach ausgewertet werden.
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(400);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        if (width <= 0 || height <= 0) return;

        const double scale = 1.5; // schaerfer als 96 dpi, damit Text lesbar bleibt
        var bitmap = new RenderTargetBitmap(
            (int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        var content = (Visual)window.Content;
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
