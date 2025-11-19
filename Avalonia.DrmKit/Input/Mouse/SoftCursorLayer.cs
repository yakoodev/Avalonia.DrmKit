using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avalonia.DrmKit.Input
{
    /// <summary>
    /// Слой "мягкого" курсора, рисуемый поверх приложения.
    ///
    /// Возможности:
    /// - дефолтный кружок (как раньше);
    /// - полная кастомизация через Template/Content (текст, картинка, любая фигура);
    /// - биндинги к позиции курсора: CursorX / CursorY;
    /// - биндинг к состоянию кнопки: IsPressed;
    /// - смена цветов через стили (StrokeBrush / FillBrush / PressedFillBrush);
    /// - дефолтные цвета автоматически подстраиваются под светлую/тёмную тему;
    /// - авто-скрытие курсора по таймауту с плавным исчезновением (AutoHideDelay).
    /// </summary>
    public class SoftCursorLayer : ContentControl
    {
        private readonly double _size;
        private readonly double _stroke;

        private double _cursorX;
        private double _cursorY;
        private bool _isPressed;
        private bool _isCursorVisible;

        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _fadeTimer;
        private DateTimeOffset _fadeStart;
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

        // Позиция курсора — для биндингов в шаблоне
        public static readonly DirectProperty<SoftCursorLayer, double> CursorXProperty =
            AvaloniaProperty.RegisterDirect<SoftCursorLayer, double>(
                nameof(CursorX),
                o => o.CursorX);

        public static readonly DirectProperty<SoftCursorLayer, double> CursorYProperty =
            AvaloniaProperty.RegisterDirect<SoftCursorLayer, double>(
                nameof(CursorY),
                o => o.CursorY);

        public static readonly DirectProperty<SoftCursorLayer, bool> IsPressedProperty =
            AvaloniaProperty.RegisterDirect<SoftCursorLayer, bool>(
                nameof(IsPressed),
                o => o.IsPressed);

        /// <summary>
        /// Текущая логическая видимость курсора.
        /// true — курсор считается видимым (даже если он находится в фазе "затухания"),
        /// false — полностью скрыт.
        /// </summary>
        public static readonly DirectProperty<SoftCursorLayer, bool> IsCursorVisibleProperty =
            AvaloniaProperty.RegisterDirect<SoftCursorLayer, bool>(
                nameof(IsCursorVisible),
                o => o.IsCursorVisible);

        /// <summary>
        /// Таймаут простоя, после которого курсор будет автоматически скрыт.
        /// 0 или отрицательное значение — авто-скрытие отключено.
        /// Значение по умолчанию: 3 секунды.
        /// </summary>
        public static readonly StyledProperty<TimeSpan> AutoHideDelayProperty =
            AvaloniaProperty.Register<SoftCursorLayer, TimeSpan>(
                nameof(AutoHideDelay),
                defaultValue: TimeSpan.FromSeconds(3));

        /// <summary>
        /// Если true — дефолтный кружок не рисуется вообще.
        /// Нужен, когда вы хотите полностью заменить визуал через Template/Content.
        /// </summary>
        public static readonly StyledProperty<bool> DisableDefaultRenderingProperty =
            AvaloniaProperty.Register<SoftCursorLayer, bool>(nameof(DisableDefaultRendering));

        public bool DisableDefaultRendering
        {
            get => GetValue(DisableDefaultRenderingProperty);
            set => SetValue(DisableDefaultRenderingProperty, value);
        }

        public double CursorX
        {
            get => _cursorX;
            private set => SetAndRaise(CursorXProperty, ref _cursorX, value);
        }

        public double CursorY
        {
            get => _cursorY;
            private set => SetAndRaise(CursorYProperty, ref _cursorY, value);
        }

        public bool IsPressed
        {
            get => _isPressed;
            private set => SetAndRaise(IsPressedProperty, ref _isPressed, value);
        }

        public bool IsCursorVisible
        {
            get => _isCursorVisible;
            private set => SetAndRaise(IsCursorVisibleProperty, ref _isCursorVisible, value);
        }

        public TimeSpan AutoHideDelay
        {
            get => GetValue(AutoHideDelayProperty);
            set => SetValue(AutoHideDelayProperty, value);
        }

        // --- Стилизуемые кисти для дефолтного кружка ---

        /// <summary>
        /// Кисть контура курсора (если не задано — подбирается по теме).
        /// </summary>
        public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
            AvaloniaProperty.Register<SoftCursorLayer, IBrush?>(nameof(StrokeBrush));

        /// <summary>
        /// Кисть заливки курсора в обычном состоянии (если не задано — подбирается по теме).
        /// </summary>
        public static readonly StyledProperty<IBrush?> FillBrushProperty =
            AvaloniaProperty.Register<SoftCursorLayer, IBrush?>(nameof(FillBrush));

        /// <summary>
        /// Кисть заливки курсора при нажатой кнопке (если не задано — подбирается по теме).
        /// </summary>
        public static readonly StyledProperty<IBrush?> PressedFillBrushProperty =
            AvaloniaProperty.Register<SoftCursorLayer, IBrush?>(nameof(PressedFillBrush));

        public IBrush? StrokeBrush
        {
            get => GetValue(StrokeBrushProperty);
            set => SetValue(StrokeBrushProperty, value);
        }

        public IBrush? FillBrush
        {
            get => GetValue(FillBrushProperty);
            set => SetValue(FillBrushProperty, value);
        }

        public IBrush? PressedFillBrush
        {
            get => GetValue(PressedFillBrushProperty);
            set => SetValue(PressedFillBrushProperty, value);
        }

        // Кэш дефолтных кистей под конкретную тему
        private IBrush? _defaultStroke;
        private IBrush? _defaultFill;
        private IBrush? _defaultPressedFill;
        private ThemeVariant? _themeForDefaults;

        public SoftCursorLayer(double size, double stroke)
        {
            _size = size;
            _stroke = stroke;

            IsHitTestVisible = false;
            Focusable = false;

            // Изначально курсор скрыт, пока пользователь не "шевельнёт" мышь.
            IsCursorVisible = false;
            Opacity = 0.0;
        }

        /// <summary>
        /// Обновление позиции курсора (вызывается из EvdevMouseListener и PointerMoved TopLevel'а).
        /// </summary>
        public void UpdatePosition(Point p)
        {
            CursorX = p.X;
            CursorY = p.Y;
            NotifyActivity();
            InvalidateVisual();
        }

        /// <summary>
        /// Обновление состояния кнопки мыши.
        /// </summary>
        public void SetPressed(bool pressed)
        {
            IsPressed = pressed;
            NotifyActivity();
            InvalidateVisual();
        }

        /// <summary>
        /// Любая активность (движение/клик) — показать курсор и перезапустить таймер простоя.
        /// </summary>
        private void NotifyActivity()
        {
            // Показать курсор немедленно
            if (!IsCursorVisible || Opacity < 1.0)
            {
                IsCursorVisible = true;
                Opacity = 1.0;
            }

            // Остановить текущий fade, если он был
            _fadeTimer?.Stop();

            var delay = AutoHideDelay;
            if (delay <= TimeSpan.Zero)
            {
                _idleTimer?.Stop();
                return;
            }

            if (_idleTimer is null)
            {
                _idleTimer = new DispatcherTimer();
                _idleTimer.Tick += IdleTimer_Tick;
            }

            _idleTimer.Interval = delay;
            _idleTimer.Stop();
            _idleTimer.Start();
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            _idleTimer?.Stop();

            if (AutoHideDelay <= TimeSpan.Zero || !IsCursorVisible)
                return;

            StartFadeOut();
        }

        private void StartFadeOut()
        {
            _fadeStart = DateTimeOffset.Now;

            if (_fadeTimer is null)
            {
                _fadeTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
                };
                _fadeTimer.Tick += FadeTimer_Tick;
            }

            _fadeTimer.Stop();
            _fadeTimer.Start();
        }

        private void FadeTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = DateTimeOffset.Now - _fadeStart;
            var t = elapsed.TotalMilliseconds / FadeDuration.TotalMilliseconds;

            if (t >= 1.0)
            {
                _fadeTimer?.Stop();
                Opacity = 0.0;
                IsCursorVisible = false;
                return;
            }

            var newOpacity = 1.0 - t;
            if (newOpacity < 0.0)
                newOpacity = 0.0;

            Opacity = newOpacity;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            // При смене темы — пересоздаём дефолтные кисти
            if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
            {
                _defaultStroke = _defaultFill = _defaultPressedFill = null;
                _themeForDefaults = null;
                InvalidateVisual();
            }

            // При смене пользовательских кистей — перерисовываем
            if (change.Property == StrokeBrushProperty ||
                change.Property == FillBrushProperty ||
                change.Property == PressedFillBrushProperty)
            {
                InvalidateVisual();
            }

            if (change.Property == AutoHideDelayProperty)
            {
                // При выключении авто-скрытия просто останавливаем таймеры.
                if (AutoHideDelay <= TimeSpan.Zero)
                {
                    _idleTimer?.Stop();
                    _fadeTimer?.Stop();
                    // Курсор будет управляться только активностью.
                }
                else
                {
                    // Если курсор уже видим — перезапустим ожидание.
                    if (IsCursorVisible)
                        NotifyActivity();
                }
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            // Если пользователь полностью переопределяет визуал:
            // - он может задать Template/Content;
            // - и выставить DisableDefaultRendering = true, чтобы не рисовать кружок.
            if (DisableDefaultRendering)
                return;

            // Если пользователь задал Content/Template и не хочет дефолтный кружок —
            // он выставит DisableDefaultRendering=true в стиле.
            // По умолчанию кружок остаётся, чтобы не ломать старое поведение.
            if (Content is not null)
                return;

            EnsureDefaultBrushes();

            var radius = _size / 2;
            var rect = new Rect(CursorX - radius, CursorY - radius, _size, _size);
            var geometry = new EllipseGeometry(rect);

            var strokeBrush = StrokeBrush ?? _defaultStroke!;
            var fillBrush = IsPressed
                ? (PressedFillBrush ?? _defaultPressedFill!)
                : (FillBrush ?? _defaultFill!);

            var pen = new Pen(strokeBrush, _stroke);

            context.DrawGeometry(fillBrush, pen, geometry);
        }

        /// <summary>
        /// Подбирает дефолтные кисти под текущую тему (светлая/тёмная).
        /// </summary>
        private void EnsureDefaultBrushes()
        {
            var theme = ActualThemeVariant;
            if (_themeForDefaults == theme && _defaultStroke is not null)
                return;

            _themeForDefaults = theme;

            var isDark = theme == ThemeVariant.Dark;

            _defaultStroke = new SolidColorBrush(isDark ? Colors.White : Colors.Black);

            _defaultFill = new SolidColorBrush(
                isDark
                    ? Color.FromArgb(160, 0, 0, 0)      // тёмная полупрозрачная "пипка" на тёмной теме
                    : Color.FromArgb(230, 255, 255, 255) // почти белый круг на светлой теме
            );

            _defaultPressedFill = new SolidColorBrush(
                isDark
                    ? Color.FromArgb(192, 255, 255, 255) // при нажатии светлее на тёмной теме
                    : Color.FromArgb(200, 200, 200, 200) // при нажатии чуть темнее на светлой теме
            );
        }
    }
}
