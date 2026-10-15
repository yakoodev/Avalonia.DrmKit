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
    /// ���� "�������" �������, �������� ������ ����������.
    ///
    /// �����������:
    /// - ��������� ������ (��� ������);
    /// - ������ ������������ ����� Template/Content (�����, ��������, ����� ������);
    /// - �������� � ������� �������: CursorX / CursorY;
    /// - ������� � ��������� ������: IsPressed;
    /// - ����� ������ ����� ����� (StrokeBrush / FillBrush / PressedFillBrush);
    /// - ��������� ����� ������������� �������������� ��� �������/����� ����;
    /// - ����-������� ������� �� �������� � ������� ������������� (AutoHideDelay).
    /// </summary>
    public class SoftCursorLayer : ContentControl
    {
        private readonly double _size;
        private readonly double _stroke;

        private double _cursorX;
        private double _cursorY;
        private bool _isPressed;
        private bool _isCursorVisible;
        private bool _touchSuppressed;
        private bool _disabled;

        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _fadeTimer;
        private DateTimeOffset _fadeStart;
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

        // ������� ������� � ��� ��������� � �������
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
        /// ������� ���������� ��������� �������.
        /// true � ������ ��������� ������� (���� ���� �� ��������� � ���� "���������"),
        /// false � ��������� �����.
        /// </summary>
        public static readonly DirectProperty<SoftCursorLayer, bool> IsCursorVisibleProperty =
            AvaloniaProperty.RegisterDirect<SoftCursorLayer, bool>(
                nameof(IsCursorVisible),
                o => o.IsCursorVisible);

        /// <summary>
        /// ������� �������, ����� �������� ������ ����� ������������� �����.
        /// 0 ��� ������������� �������� � ����-������� ���������.
        /// �������� �� ���������: 3 �������.
        /// </summary>
        public static readonly StyledProperty<TimeSpan> AutoHideDelayProperty =
            AvaloniaProperty.Register<SoftCursorLayer, TimeSpan>(
                nameof(AutoHideDelay),
                defaultValue: TimeSpan.FromSeconds(3));

        /// <summary>
        /// ���� true � ��������� ������ �� �������� ������.
        /// �����, ����� �� ������ ��������� �������� ������ ����� Template/Content.
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

        // --- ����������� ����� ��� ���������� ������ ---

        /// <summary>
        /// ����� ������� ������� (���� �� ������ � ����������� �� ����).
        /// </summary>
        public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
            AvaloniaProperty.Register<SoftCursorLayer, IBrush?>(nameof(StrokeBrush));

        /// <summary>
        /// ����� ������� ������� � ������� ��������� (���� �� ������ � ����������� �� ����).
        /// </summary>
        public static readonly StyledProperty<IBrush?> FillBrushProperty =
            AvaloniaProperty.Register<SoftCursorLayer, IBrush?>(nameof(FillBrush));

        /// <summary>
        /// ����� ������� ������� ��� ������� ������ (���� �� ������ � ����������� �� ����).
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

        // ��� ��������� ������ ��� ���������� ����
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

            // ���������� ������ �����, ���� ������������ �� "���������" ����.
            IsCursorVisible = false;
            Opacity = 0.0;
        }

        /// <summary>
        /// ���������� ������� ������� (���������� �� EvdevMouseListener � PointerMoved TopLevel'�).
        /// </summary>
        public void UpdatePosition(Point p)
        {
            CursorX = p.X;
            CursorY = p.Y;
            NotifyActivity();
            InvalidateVisual();
        }

        /// <summary>
        /// ���������� ��������� ������ ����.
        /// </summary>
        public void SetPressed(bool pressed)
        {
            IsPressed = pressed;
            NotifyActivity();
            InvalidateVisual();
        }

        /// <summary>
        /// ����� ���������� (��������/����) � �������� ������ � ������������� ������ �������.
        /// </summary>
        /// <summary>
        /// Suppresses the cursor while a touch contact is active
        /// (DrmTouchOptions.HideCursorWhileTouching). While suppressed the
        /// cursor is hidden immediately and activity does not re-show it; when
        /// suppression ends the cursor stays hidden until the next real
        /// mouse/relative input triggers activity again.
        /// </summary>
        /// <summary>
        /// Курсор выключен настройкой (<see cref="Avalonia.DrmKit.DrmSoftCursor.IsEnabled"/> = false):
        /// не показывается вовсе, движение мыши его не возвращает. Нужен для чисто сенсорных
        /// киосков, где курсор поверх интерфейса только мешает.
        /// </summary>
        public void SetDisabled(bool disabled)
        {
            if (_disabled == disabled)
                return;

            _disabled = disabled;
            if (disabled)
            {
                _idleTimer?.Stop();
                _fadeTimer?.Stop();
                Opacity = 0.0;
                IsCursorVisible = false;
            }
        }

        public void SetTouchSuppressed(bool suppressed)
        {
            if (_touchSuppressed == suppressed)
                return;

            _touchSuppressed = suppressed;

            if (suppressed)
            {
                _idleTimer?.Stop();
                _fadeTimer?.Stop();
                Opacity = 0.0;
                IsCursorVisible = false;
            }
        }

        private void NotifyActivity()
        {
            if (_touchSuppressed || _disabled)
                return;

            // �������� ������ ����������
            if (!IsCursorVisible || Opacity < 1.0)
            {
                IsCursorVisible = true;
                Opacity = 1.0;
            }

            // ���������� ������� fade, ���� �� ���
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

            // ��� ����� ���� � ���������� ��������� �����
            if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
            {
                _defaultStroke = _defaultFill = _defaultPressedFill = null;
                _themeForDefaults = null;
                InvalidateVisual();
            }

            // ��� ����� ���������������� ������ � ��������������
            if (change.Property == StrokeBrushProperty ||
                change.Property == FillBrushProperty ||
                change.Property == PressedFillBrushProperty)
            {
                InvalidateVisual();
            }

            if (change.Property == AutoHideDelayProperty)
            {
                // ��� ���������� ����-������� ������ ������������� �������.
                if (AutoHideDelay <= TimeSpan.Zero)
                {
                    _idleTimer?.Stop();
                    _fadeTimer?.Stop();
                    // ������ ����� ����������� ������ �����������.
                }
                else
                {
                    // ���� ������ ��� ����� � ������������ ��������.
                    if (IsCursorVisible)
                        NotifyActivity();
                }
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            // ���� ������������ ��������� �������������� ������:
            // - �� ����� ������ Template/Content;
            // - � ��������� DisableDefaultRendering = true, ����� �� �������� ������.
            if (DisableDefaultRendering)
                return;

            // ���� ������������ ����� Content/Template � �� ����� ��������� ������ �
            // �� �������� DisableDefaultRendering=true � �����.
            // �� ��������� ������ �������, ����� �� ������ ������ ���������.
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
        /// ��������� ��������� ����� ��� ������� ���� (�������/�����).
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
                    ? Color.FromArgb(160, 0, 0, 0)      // ����� �������������� "�����" �� ����� ����
                    : Color.FromArgb(230, 255, 255, 255) // ����� ����� ���� �� ������� ����
            );

            _defaultPressedFill = new SolidColorBrush(
                isDark
                    ? Color.FromArgb(192, 255, 255, 255) // ��� ������� ������� �� ����� ����
                    : Color.FromArgb(200, 200, 200, 200) // ��� ������� ���� ������ �� ������� ����
            );
        }
    }
}
