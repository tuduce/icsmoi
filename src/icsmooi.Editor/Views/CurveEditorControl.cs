using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using icsmooi.Models;

namespace icsmooi.Views;

/// <summary>
/// Minimal drag-to-shape editor for a <see cref="CurveNodeViewModel"/>'s control
/// points — the visual counterpart to <c>GraphEvaluator.EvalCurve</c>'s
/// piecewise-linear interpolation.
///
/// <para>Fully custom-rendered (no XAML template): draws the curve and
/// draggable point handles, fit to the current point set's bounding box (with
/// margin) the first time points are attached or the collection's item count
/// changes — not on every render, so dragging a point doesn't make the view
/// rescale out from under the pointer.</para>
///
/// <para>Double-click empty space to add a point there; double-click an
/// existing point to remove it.</para>
/// </summary>
public sealed class CurveEditorControl : Control
{
    public static readonly StyledProperty<ObservableCollection<CurvePoint>?> PointsProperty =
        AvaloniaProperty.Register<CurveEditorControl, ObservableCollection<CurvePoint>?>(nameof(Points));

    public ObservableCollection<CurvePoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private const double HandleRadius = 5;
    private const double HitRadius = 8;
    private const double Padding = 10;

    private double _minX, _maxX, _minY, _maxY;
    private bool _boundsValid;
    private CurvePoint? _dragging;

    static CurveEditorControl()
    {
        AffectsRender<CurveEditorControl>(PointsProperty);
    }

    public CurveEditorControl()
    {
        Width = 200;
        Height = 110;
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != PointsProperty) return;

        if (change.OldValue is ObservableCollection<CurvePoint> oldPoints)
            oldPoints.CollectionChanged -= OnPointsCollectionChanged;
        if (change.NewValue is ObservableCollection<CurvePoint> newPoints)
            newPoints.CollectionChanged += OnPointsCollectionChanged;

        FitToPoints();
    }

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        FitToPoints();
        InvalidateVisual();
    }

    /// <summary>Recomputes the visible data-space bounding box from the current point set.</summary>
    public void FitToPoints()
    {
        var points = Points;
        if (points is null || points.Count == 0)
        {
            _minX = 0; _maxX = 1; _minY = 0; _maxY = 1;
        }
        else
        {
            _minX = points.Min(p => p.X);
            _maxX = points.Max(p => p.X);
            _minY = points.Min(p => p.Y);
            _maxY = points.Max(p => p.Y);

            if (Math.Abs(_maxX - _minX) < 1e-9) { _minX -= 0.5; _maxX += 0.5; }
            if (Math.Abs(_maxY - _minY) < 1e-9) { _minY -= 0.5; _maxY += 0.5; }

            var xPad = (_maxX - _minX) * 0.15;
            var yPad = (_maxY - _minY) * 0.15;
            _minX -= xPad; _maxX += xPad;
            _minY -= yPad; _maxY += yPad;
        }

        _boundsValid = true;
        InvalidateVisual();
    }

    private Point ToScreen(double x, double y)
    {
        var w = Math.Max(1, Bounds.Width - 2 * Padding);
        var h = Math.Max(1, Bounds.Height - 2 * Padding);
        var sx = Padding + (x - _minX) / (_maxX - _minX) * w;
        var sy = Padding + h - (y - _minY) / (_maxY - _minY) * h;
        return new Point(sx, sy);
    }

    private (double X, double Y) ToData(Point screen)
    {
        var w = Math.Max(1, Bounds.Width - 2 * Padding);
        var h = Math.Max(1, Bounds.Height - 2 * Padding);
        var x = _minX + (screen.X - Padding) / w * (_maxX - _minX);
        var y = _minY + (h - (screen.Y - Padding)) / h * (_maxY - _minY);
        return (x, y);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds, 4);
        context.DrawRectangle(new Pen(Brushes.Gray, 1), bounds, 4);

        var points = Points;
        if (points is null || !_boundsValid) return;

        if (points.Count > 1)
        {
            var sorted = points.OrderBy(p => p.X).ToList();
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(ToScreen(sorted[0].X, sorted[0].Y), false);
                for (var i = 1; i < sorted.Count; i++)
                    ctx.LineTo(ToScreen(sorted[i].X, sorted[i].Y));
            }
            context.DrawGeometry(null, new Pen(Brushes.DeepSkyBlue, 2), geometry);
        }

        foreach (var p in points)
        {
            var screen = ToScreen(p.X, p.Y);
            context.DrawEllipse(Brushes.White, new Pen(Brushes.DeepSkyBlue, 1.5), screen, HandleRadius, HandleRadius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var points = Points;
        if (points is null) return;

        var pos = e.GetPosition(this);
        var hit = points.FirstOrDefault(p => Distance(ToScreen(p.X, p.Y), pos) <= HitRadius);

        if (e.ClickCount % 2 == 0)
        {
            if (hit is not null)
                points.Remove(hit);
            else
            {
                var (x, y) = ToData(pos);
                points.Add(new CurvePoint { X = x, Y = y });
            }
            e.Handled = true;
            return;
        }

        if (hit is not null)
        {
            _dragging = hit;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragging is null) return;

        var (x, y) = ToData(e.GetPosition(this));
        _dragging.X = x;
        _dragging.Y = y;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragging is null) return;

        _dragging = null;
        e.Pointer.Capture(null);
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
