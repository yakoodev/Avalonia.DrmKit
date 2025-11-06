
using Avalonia;
using Avalonia.LinuxFramebuffer;
using Avalonia.LinuxFramebuffer.Input.LibInput;
using Avalonia.ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Avalonia.DrmKit
{
    public static class SmartPlatform
    {
        [DllImport("libc")] private static extern uint geteuid();

        public static int Start(AppBuilder builder, string[] args)
        {
            var system = DetectSystemType(args);
            Console.WriteLine($"[DrmKit] SystemType={system}");

            // XDG_RUNTIME_DIR for libinput when launched via sudo/tty
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var sudoUid = Environment.GetEnvironmentVariable("SUDO_UID");
            if (string.IsNullOrEmpty(xdg) && OperatingSystem.IsLinux())
            {
                if (!string.IsNullOrEmpty(sudoUid))
                {
                    var sudoPath = $"/run/user/{sudoUid}";
                    if (Directory.Exists(sudoPath))
                    {
                        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", sudoPath);
                        Console.WriteLine($"[DrmKit] XDG_RUNTIME_DIR set to {sudoPath} (sudo).");
                    }
                    else Console.WriteLine($"[DrmKit] {sudoPath} not found; libinput may fail to init seat.");
                }
                else if (geteuid() == 0)
                {
                    var rootPath = "/run/user/0";
                    if (Directory.Exists(rootPath))
                    {
                        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", rootPath);
                        Console.WriteLine("[DrmKit] XDG_RUNTIME_DIR set to /run/user/0 (root).");
                    }
                    else Console.WriteLine("[DrmKit] /run/user/0 not found; libinput may fail to init seat.");
                }
            }

            if (!system.IsLinux() || system.IsDesktopSystem())
                return builder.StartWithClassicDesktopLifetime(args);

            var drmToken = GetAnyToken(args, new[] { "--drm", "-card" });
            var resolution = ParseResolutionAny(args, new[] { "--resolution", "-resolution" }, new PixelSize(1280, 720));

            var cards = EnumerateDrmCards().ToList();
            Console.WriteLine($"[DrmKit] Found DRM cards: {string.Join(", ", cards)}");
            if (cards.Count == 0)
            {
                Console.Error.WriteLine("[DrmKit] /dev/dri/card* was not found. Falling back to Desktop.");
                return FreshDesktop().StartWithClassicDesktopLifetime(args);
            }

            var requestedCard = ParseDrmArgValue(drmToken);
            string chosenCard;
            if (!string.IsNullOrEmpty(requestedCard))
            {
                var normalized = NormalizeCardPath(requestedCard!);
                chosenCard = cards.Contains(normalized) ? normalized : cards[0];
            }
            else
            {
                chosenCard = cards[0];
            }
            Console.WriteLine($"[DrmKit] Selected card: {chosenCard}");

            var drmOpt = new DrmOutputOptions
            {
                Scaling = 1,
                EnableInitialBufferSwapping = true,
                VideoMode = resolution
            };
            var libInputOpt = new LibInputBackendOptions { };

            try
            {
                return builder.StartLinuxDrm(
                    args,
                    chosenCard,
                    true,
                    drmOpt,
                    new LibInputBackend(libInputOpt));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DrmKit] DRM start error on {chosenCard}: {ex}");
                Console.Error.WriteLine("[DrmKit] Falling back to Desktop.");
                try { return FreshDesktop().StartWithClassicDesktopLifetime(args); }
                catch (Exception ex2) { Console.Error.WriteLine($"[DrmKit] Desktop fallback failed: {ex2}"); return -1; }
            }
        }

        private static AppBuilder FreshDesktop()
            => AppBuilder.Configure<Application>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();

        public enum SystemType { DRM, Windows, LinuxDesktop, Android, MacOS }
        public static bool IsDesktopSystem(this SystemType type)
            => type == SystemType.Windows || type == SystemType.LinuxDesktop || type == SystemType.MacOS;
        public static bool IsLinux(this SystemType type)
            => type == SystemType.LinuxDesktop || type == SystemType.DRM;
        public static bool IsDrm(this SystemType type)
            => type == SystemType.DRM;

        public static SystemType DetectSystemType(string[] args)
        {
            if (!OperatingSystem.IsLinux())
            {
                if (OperatingSystem.IsWindows()) return SystemType.Windows;
                if (OperatingSystem.IsMacOS()) return SystemType.MacOS;
                if (OperatingSystem.IsAndroid()) return SystemType.Android;
                return SystemType.Windows;
            }

            var requested = ParseRequestedSystemType(args);
            if (requested is SystemType.LinuxDesktop or SystemType.DRM) return requested.Value;

            return IsDesktopSession() ? SystemType.LinuxDesktop : SystemType.DRM;
        }

        public static SystemType? ParseRequestedSystemType(string[] args)
        {
            static string? Read(string[] a, string key)
            {
                var prefix = key + "=";
                var token = a.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                return token == null ? null : token.Substring(prefix.Length).Trim();
            }

            var raw = Read(args, "--system") ?? Read(args, "--backend");
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.ToLowerInvariant();
            return raw switch
            {
                "auto" => null,
                "desktop" or "linux" or "linuxdesktop" => SystemType.LinuxDesktop,
                "drm" => SystemType.DRM,
                "windows" => SystemType.Windows,
                "macos" or "osx" => SystemType.MacOS,
                "android" => SystemType.Android,
                _ => null
            };
        }

        private static bool IsDesktopSession()
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (!string.IsNullOrEmpty(xdg) &&
                (xdg.Equals("x11", StringComparison.OrdinalIgnoreCase) ||
                 xdg.Equals("wayland", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return true;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return true;
            return false;
        }

        private static string? GetAnyToken(string[] args, string[] keys)
        {
            foreach (var key in keys)
            {
                var token = args.FirstOrDefault(a => a.StartsWith(key, StringComparison.OrdinalIgnoreCase));
                if (token != null) return token;
            }
            return null;
        }

        private static PixelSize ParseResolutionAny(string[] args, string[] keys, PixelSize @default)
        {
            foreach (var key in keys)
            {
                var prefix = key + "=";
                var token = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (token == null) continue;

                var val = token[(token.IndexOf('=') + 1)..].Trim();
                var parts = val.Split(new[] { 'x', 'X', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                if (int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0)
                    return new PixelSize(w, h);
            }
            return @default;
        }

        private static IEnumerable<string> EnumerateDrmCards()
        {
            const string driDir = "/dev/dri";
            if (!Directory.Exists(driDir))
                yield break;

            foreach (var path in Directory.EnumerateFileSystemEntries(driDir, "card*")
                                          .Where(File.Exists)
                                          .OrderBy(p => p))
                yield return path;
        }

        private static string? ParseDrmArgValue(string? drmOrCardToken)
        {
            if (string.IsNullOrEmpty(drmOrCardToken)) return null;
            var idx = drmOrCardToken.IndexOf('=');
            if (idx < 0 || idx == drmOrCardToken.Length - 1) return null;
            var value = drmOrCardToken[(idx + 1)..].Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string NormalizeCardPath(string value)
        {
            if (value.StartsWith("/dev/dri/", StringComparison.OrdinalIgnoreCase))
                return value;
            if (value.StartsWith("card", StringComparison.OrdinalIgnoreCase))
                return $"/dev/dri/{value}";
            if (int.TryParse(value, out var idx))
                return $"/dev/dri/card{idx}";
            return value;
        }
    }
}
