using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using icsmoi.Services.DirectInput;

namespace icsmoi.Views;

/// <summary>
/// A round dial for choosing a direction with the mouse: drag the handle dot (or click anywhere on
/// the dial) to point the force. <see cref="Angle"/> is in degrees, 0° = up ("forward", away from the
/// pilot) increasing clockwise — the convention <see cref="EffectDirection"/> documents.
///
/// <para>Fully custom-rendered (like <see cref="CurveEditorControl"/>) but drawn from the app's
/// design tokens rather than fixed colors: graphite face, fine rim and ticks, and the accent
/// orange — the color of "force" — for the fan, spoke and handle. Whole degrees by default;
/// hold Shift to snap to 15°.</para>
/// </summary>
public sealed class DirectionDialControl : Control
{
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<DirectionDialControl, double>(
            nameof(Angle), 0, defaultBindingMode: BindingMode.TwoWay,
            coerce: static (_, value) => EffectDirection.Normalize(value));

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    private const double Diameter = 96;
    private const double SnapDegrees = 15;
    private const double FanHalfWidthDegrees = 16;
    private const double HandleRadius = 6.5;

    private bool _dragging;
    private bool _hover;

    static DirectionDialControl()
    {
        AffectsRender<DirectionDialControl>(AngleProperty);
    }

    public DirectionDialControl()
    {
        Width = Diameter;
        Height = Diameter;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = false;
    }

    // ── Geometry ─────────────────────────────────────────────────────────────

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);
    private double FaceRadius => Math.Min(Bounds.Width, Bounds.Height) / 2 - 1;
    private double TrackRadius => FaceRadius - 12;

    /// <summary>The point at <paramref name="degrees"/> (0° up, clockwise) and <paramref name="radius"/> from the centre.</summary>
    private Point PointAt(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180.0;
        var c = Center;
        return new Point(c.X + radius * Math.Sin(radians), c.Y - radius * Math.Cos(radians));
    }

    // ── Theme tokens ─────────────────────────────────────────────────────────

    private Color TokenColor(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is Color color ? color : fallback;

    private static ImmutableSolidColorBrush Solid(Color color, double opacity = 1) =>
        new(Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B));

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var background = TokenColor("Icsmoi.BackgroundColor", Color.Parse("#101216"));
        var surfaceAlt = TokenColor("Icsmoi.SurfaceAltColor", Color.Parse("#232831"));
        var border = TokenColor("Icsmoi.BorderColor", Color.Parse("#313845"));
        var muted = TokenColor("Icsmoi.TextMutedColor", Color.Parse("#9AA3B2"));
        var text = TokenColor("Icsmoi.TextColor", Color.Parse("#F4F5F7"));
        var accent = TokenColor("Icsmoi.AccentColor", Color.Parse("#FF5A1F"));

        var c = Center;
        var face = FaceRadius;
        var track = TrackRadius;
        var angle = Angle;

        // Face: a soft lift toward the middle, falling to the deep background at the rim.
        var faceBrush = new ImmutableRadialGradientBrush(
            [new ImmutableGradientStop(0, surfaceAlt), new ImmutableGradientStop(1, background)],
            center: new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            gradientOrigin: new RelativePoint(0.5, 0.4, RelativeUnit.Relative),
            radiusX: RelativeScalar.Parse("50%"), radiusY: RelativeScalar.Parse("50%"));
        context.DrawEllipse(faceBrush, new ImmutablePen(Solid(border), 1.5), c, face, face);

        // Ticks: fine every 15°, stronger on the diagonals, longest on the cardinals.
        for (var i = 0; i < 24; i++)
        {
            var degrees = i * 15.0;
            if (i == 0) continue; // the forward marker below takes the top spot
            var (length, thickness, color) = i % 6 == 0 ? (7.0, 1.5, Solid(muted))
                : i % 3 == 0 ? (5.0, 1.25, Solid(muted, 0.6))
                : (3.0, 1.0, Solid(border));
            context.DrawLine(new ImmutablePen(color, thickness), PointAt(degrees, face - 2), PointAt(degrees, face - 2 - length));
        }

        // Forward marker: a small triangle at the top, pointing away from the pilot.
        var apex = PointAt(0, face - 1.5);
        var marker = new StreamGeometry();
        using (var g = marker.Open())
        {
            g.BeginFigure(apex, true);
            g.LineTo(new Point(apex.X - 4, apex.Y + 7));
            g.LineTo(new Point(apex.X + 4, apex.Y + 7));
            g.EndFigure(true);
        }
        context.DrawGeometry(Solid(text), null, marker);

        // The track the handle rides on.
        context.DrawEllipse(null, new ImmutablePen(Solid(border, 0.7), 1), c, track, track);

        // Fan: a translucent wedge either side of the chosen direction.
        var fan = new StreamGeometry();
        using (var g = fan.Open())
        {
            g.BeginFigure(c, true);
            g.LineTo(PointAt(angle - FanHalfWidthDegrees, track));
            g.ArcTo(PointAt(angle + FanHalfWidthDegrees, track), new Size(track, track), 0, false, SweepDirection.Clockwise);
            g.EndFigure(true);
        }
        context.DrawGeometry(Solid(accent, 0.20), null, fan);

        // Spoke from the hub to the handle.
        var handle = PointAt(angle, track);
        context.DrawLine(new ImmutablePen(Solid(accent, 0.9), 2.5, lineCap: PenLineCap.Round), c, handle);

        // Hub.
        context.DrawEllipse(Solid(accent), null, c, 3.5, 3.5);

        // Handle: a glowing dot with a light rim (echoing the node selection ring).
        if (_hover || _dragging)
            context.DrawEllipse(Solid(accent, 0.30), null, handle, HandleRadius + 5, HandleRadius + 5);
        context.DrawEllipse(Solid(accent), new ImmutablePen(Solid(text), 2), handle, HandleRadius, HandleRadius);
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        e.Pointer.Capture(this);
        SetAngleFrom(e);
        e.Handled = true; // keep the node itself from being dragged
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        SetAngleFrom(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    private void SetAngleFrom(PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        var c = Center;
        double dx = position.X - c.X, dy = position.Y - c.Y;
        if (dx * dx + dy * dy < 16) return; // right on the hub, the direction is undefined

        var degrees = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        degrees = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            ? Math.Round(degrees / SnapDegrees) * SnapDegrees
            : Math.Round(degrees);
        Angle = degrees;
    }
}
