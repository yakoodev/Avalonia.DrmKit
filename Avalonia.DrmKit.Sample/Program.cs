using Avalonia;
using Avalonia.DrmKit;
using Avalonia.DrmKit.Input;
using ReactiveUI.Avalonia;

namespace Avalonia.DrmKit.Sample;

internal static class Program
{
    public static int Main(string[] args)
    {
        var keyboardOptions = new DrmKeyboardOptions
        {
            Layout = "us,ru",
            Options = "grp:alt_shift_toggle"
        };

        var options = new SmartPlatformOptions
        {
            // Example: explicit evdev input backend, DRM card and output mode.
            InputBackend = new DrmUnifiedInputBackend(keyboardOptions),
            DrmCard = "/dev/dri/card1",
            Resolution = new PixelSize(1920, 1080),
            Scaling = 1.0,
            ConnectorsForceProbe = true,
        };

        return SmartPlatform.Start(BuildAvaloniaApp(), args, options);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ => { })
            .UseDrmSoftCursor();
}
