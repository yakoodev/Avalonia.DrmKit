using Avalonia.Input;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.DrmKit.Input
{
    public record KeyboardLayout(

        string Name,
        Dictionary<Key, (string normal, string shifted)> Item2, // для букв normal = lower, shifted можно оставить таким же; регистр применим позже
        string[][] Rows // строки для экранной клавиатуры (лейблы кнопок)
    );

    public static class KeyboardLayoutManager
    {
        public static event EventHandler<string>? CurrentChanged;
        public static event EventHandler<bool>? CapsChanged;     // НОВОЕ

        private static readonly Dictionary<string, KeyboardLayout> _layouts = new(StringComparer.OrdinalIgnoreCase);
        private static string _current = "en";
        private static bool _caps;                               // НОВОЕ

        public static string Current => _current;
        public static bool Caps => _caps;                     // НОВОЕ

        public static void RegisterLayout(KeyboardLayout layout)
            => _layouts[layout.Name] = layout;

        public static void SetCurrent(string name)
        {
            if (!_layouts.ContainsKey(name) || _current.Equals(name, StringComparison.OrdinalIgnoreCase)) return;
            _current = name;
            if (Dispatcher.UIThread.CheckAccess()) CurrentChanged?.Invoke(null, _current);
            else Dispatcher.UIThread.Post(() => CurrentChanged?.Invoke(null, _current));
        }
        public static void SetCaps(bool on)
        {
            if (_caps == on) return;
            _caps = on;
            if (Dispatcher.UIThread.CheckAccess()) CapsChanged?.Invoke(null, _caps);
            else Dispatcher.UIThread.Post(() => CapsChanged?.Invoke(null, _caps));
        }

        public static KeyboardLayout GetCurrent()
            => _layouts[_current];

        public static bool TryMap(Key key, bool shift, bool caps, out string? text)
        {
            var layout = GetCurrent();
            if (!layout.Item2.TryGetValue(key, out var pair))
            {
                text = null;
                return false;
            }

            // Выбор базового варианта: для пунктуации/цифр — по Shift, для букв — всегда normal (нижний регистр),
            // а регистр потом применим через caps^shift.
            var normal = pair.normal;
            var shifted = pair.shifted;

            string candidate;
            if (IsLetter(normal))
                candidate = normal;
            else
                candidate = shift ? shifted : normal;

            // Применяем регистр для букв (Caps XOR Shift)
            if (IsLetter(candidate))
            {
                var upper = shift ^ caps;
                candidate = upper ? candidate.ToUpperInvariant() : candidate.ToLowerInvariant();
            }

            text = candidate;
            return !string.IsNullOrEmpty(text);

            static bool IsLetter(string s) => s.Length == 1 && char.IsLetter(s[0]);
        }

        // Регистрируем две дефолтные раскладки
        static KeyboardLayoutManager()
        {
            RegisterDefaultLayouts();
        }

        public static void RegisterDefaultLayouts()
        {
            if (_layouts.Count > 0) return;

            // --- EN ---
            var enMap = new Dictionary<Key, (string, string)>
            {
                // буквы: normal=lower, shifted игнорируем для букв
                [Key.A] = ("a", "a"),
                [Key.B] = ("b", "b"),
                [Key.C] = ("c", "c"),
                [Key.D] = ("d", "d"),
                [Key.E] = ("e", "e"),
                [Key.F] = ("f", "f"),
                [Key.G] = ("g", "g"),
                [Key.H] = ("h", "h"),
                [Key.I] = ("i", "i"),
                [Key.J] = ("j", "j"),
                [Key.K] = ("k", "k"),
                [Key.L] = ("l", "l"),
                [Key.M] = ("m", "m"),
                [Key.N] = ("n", "n"),
                [Key.O] = ("o", "o"),
                [Key.P] = ("p", "p"),
                [Key.Q] = ("q", "q"),
                [Key.R] = ("r", "r"),
                [Key.S] = ("s", "s"),
                [Key.T] = ("t", "t"),
                [Key.U] = ("u", "u"),
                [Key.V] = ("v", "v"),
                [Key.W] = ("w", "w"),
                [Key.X] = ("x", "x"),
                [Key.Y] = ("y", "y"),
                [Key.Z] = ("z", "z"),
                // цифры/знаки
                [Key.D0] = ("0", ")"),
                [Key.D1] = ("1", "!"),
                [Key.D2] = ("2", "@"),
                [Key.D3] = ("3", "#"),
                [Key.D4] = ("4", "$"),
                [Key.D5] = ("5", "%"),
                [Key.D6] = ("6", "^"),
                [Key.D7] = ("7", "&"),
                [Key.D8] = ("8", "*"),
                [Key.D9] = ("9", "("),
                [Key.Space] = (" ", " "),
                [Key.OemMinus] = ("-", "_"),
                [Key.OemPlus] = ("=", "+"),
                [Key.OemOpenBrackets] = ("[", "{"),
                [Key.OemCloseBrackets] = ("]", "}"),
                [Key.OemSemicolon] = ("; ", ":"),
                [Key.OemQuotes] = ("'", "\""),
                [Key.OemComma] = (",", "<"),
                [Key.OemPeriod] = (".", ">"),
                [Key.OemQuestion] = ("/", "?"),
                [Key.OemBackslash] = ("\\", "|"),
                [Key.OemTilde] = ("`", "~"),
            };

            var enRows = new[]
            {
                new [] { "@","/","*","+","-","_",".","<-","Del" },
                new [] { "1","2","3","4","5","6","7","8","9","0" },
                new [] { "q","w","e","r","t","y","u","i","o","p" },
                new [] { "a","s","d","f","g","h","j","k","l" },
                new [] { "z","x","c","v","b","n","m",".","," },
                new [] { "Rus", "CAPS", "SPACE", "Enter","Cancel" },
            };

            RegisterLayout(new KeyboardLayout("en", enMap, enRows));

            // --- RU (йцукен) ---
            var ruMap = new Dictionary<Key, (string, string)>
            {
                // буквы (нижний регистр; регистр применим позже)
                [Key.Q] = ("й", "й"),
                [Key.W] = ("ц", "ц"),
                [Key.E] = ("у", "у"),
                [Key.R] = ("к", "к"),
                [Key.T] = ("е", "е"),
                [Key.Y] = ("н", "н"),
                [Key.U] = ("г", "г"),
                [Key.I] = ("ш", "ш"),
                [Key.O] = ("щ", "щ"),
                [Key.P] = ("з", "з"),
                [Key.OemOpenBrackets] = ("х", "х"),
                [Key.OemCloseBrackets] = ("ъ", "ъ"),

                [Key.A] = ("ф", "ф"),
                [Key.S] = ("ы", "ы"),
                [Key.D] = ("в", "в"),
                [Key.F] = ("а", "а"),
                [Key.G] = ("п", "п"),
                [Key.H] = ("р", "р"),
                [Key.J] = ("о", "о"),
                [Key.K] = ("л", "л"),
                [Key.L] = ("д", "д"),
                [Key.OemSemicolon] = ("ж", "ж"),
                [Key.OemQuotes] = ("э", "э"),

                [Key.Z] = ("я", "я"),
                [Key.X] = ("ч", "ч"),
                [Key.C] = ("с", "с"),
                [Key.V] = ("м", "м"),
                [Key.B] = ("и", "и"),
                [Key.N] = ("т", "т"),
                [Key.M] = ("ь", "ь"),
                [Key.OemComma] = ("б", "б"),
                [Key.OemPeriod] = ("ю", "ю"),
                [Key.OemQuestion] = (".", "."),

                // цифры (оставим EN-поведение для простоты; при желании заменить на «RU цифры»)
                [Key.D0] = ("0", ")"),
                [Key.D1] = ("1", "!"),
                [Key.D2] = ("2", "\""),
                [Key.D3] = ("3", "№"),
                [Key.D4] = ("4", ";"),
                [Key.D5] = ("5", "%"),
                [Key.D6] = ("6", ":"),
                [Key.D7] = ("7", "?"),
                [Key.D8] = ("8", "*"),
                [Key.D9] = ("9", "("),

                [Key.Space] = (" ", " "),
                [Key.OemMinus] = ("-", "_"),
                [Key.OemPlus] = ("=", "+"),
                [Key.OemBackslash] = ("\\", "/"),
                [Key.OemTilde] = ("ё", "ё"),
            };

            var ruRows = new[]
            {
                new [] { "@","/","*","+","-","_",".","<-","Del" },
                new [] { "1","2","3","4","5","6","7","8","9","0" },
                new [] { "й","ц","у","к","е","н","г","ш","щ","з" },
                new [] { "ф","ы","в","а","п","р","о","л","д" },
                new [] { "я","ч","с","м","и","т","ь","б","ю" },
                new [] { "Eng", "CAPS", "SPACE", "Enter","Cancel" },
            };

            RegisterLayout(new KeyboardLayout("ru", ruMap, ruRows));

            _current = "en"; // текущая по умолчанию
        }
    }
}
