
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avalonia.DrmKit.Input
{
    internal class SoftCursorLayer : Control
    {
        private Point _pos;
        private bool _pressed;
        private readonly double _size;
        private readonly double _stroke;

        public SoftCursorLayer(double size, double stroke)
        {
            _size = size;
            _stroke = stroke;
            IsHitTestVisible = false;
        }

        public void UpdatePosition(Point p) { _pos = p; InvalidateVisual(); }
        public void SetPressed(bool pressed) { _pressed = pressed; InvalidateVisual(); }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var radius = _size / 2;
            var rect = new Rect(_pos.X - radius, _pos.Y - radius, _size, _size);
            var geometry = new EllipseGeometry(rect);

            var stroke = new Pen(Brushes.White, _stroke);
            var fill = _pressed ? new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)) : null;
            context.DrawGeometry(fill, stroke, geometry);
        }
    }
}
