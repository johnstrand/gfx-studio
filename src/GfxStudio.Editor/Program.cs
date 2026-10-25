using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace GfxStudio.Editor;

/// <summary>Entry point for the GfxStudio editor application.</summary>
public static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
