using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Avalonia.DrmKit
{
    internal static class SoftCursorInstaller
    {
        public static void TryAttach(TopLevel tl, SoftCursorOptions options)
        {
            if (!OperatingSystem.IsLinux()) { Console.WriteLine("[DrmKit] SoftCursorInstaller: not Linux"); return; }
            var sys = SmartPlatform.DetectSystemType(Array.Empty<string>());
            if (!sys.IsDrm()) { Console.WriteLine("[DrmKit] SoftCursorInstaller: not DRM"); return; }

            Panel? host = tl.GetVisualDescendants().OfType<Panel>().FirstOrDefault();
            if (host is null) { Console.WriteLine("[DrmKit] SoftCursorInstaller: no host panel found"); return; }

            var overlay = new Input.SoftCursorLayer(options.Size, options.StrokeThickness)
            {
                Width = double.NaN,
                Height = double.NaN,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            //Panel.SetZIndex(overlay, 10_000);

            host.Children.Add(overlay);
            Input.SoftCursorHub.Register(tl, overlay);
            Console.WriteLine("[DrmKit] SoftCursorInstaller: overlay added");

            tl.PointerMoved += (_, e) =>
            {
                // Для LibInput: реагируем только на мышь, тач-скрин игнорируем
                if (e.Pointer.Type != PointerType.Mouse)
                    return;

                overlay.UpdatePosition(e.GetPosition(tl));
            };

            tl.PointerPressed += (_, e) =>
            {
                if (e.Pointer.Type != PointerType.Mouse)
                    return;

                overlay.SetPressed(true);
            };

            tl.PointerReleased += (_, e) =>
            {
                if (e.Pointer.Type != PointerType.Mouse)
                    return;

                overlay.SetPressed(false);
            };
        }
    }
}
