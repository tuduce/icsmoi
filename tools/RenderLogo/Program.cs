// Renders the icsmoi wordmark to a PNG for use outside the app (e.g. the root README), using the
// exact path geometry from the Icsmoi.Wordmark ControlTheme in
// icsmoi.Shared.Ui/Styles/Logo.axaml, so the letterforms can never visually drift from what the
// app actually draws. Colors are the literal Icsmoi.Text/Icsmoi.Accent/Icsmoi.Background hex
// values from Tokens.axaml, hardcoded rather than routed through DynamicResource: this tool
// builds its own minimal Application (no real App.axaml/theme-variant host), and in that context
// DynamicResource lookups inside a ControlTheme's template did not resolve when tested — direct
// Application.Resources indexing worked fine, but the template-relative TemplateBinding-style
// lookup did not. Hardcoding these already-fixed design tokens (the app is always dark, they
// never change at runtime) sidesteps that without giving up the real geometry.
//
// If Tokens.axaml's Icsmoi.Text/Icsmoi.Accent/Icsmoi.Background values or Logo.axaml's Wordmark
// path data ever change, update the constants/paths below to match.
//
// Usage: dotnet run --project tools/RenderLogo -- <output.png> [heightPx]

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

var outPath = args.Length > 0 ? args[0] : "wordmark-logo.png";
var wordmarkHeight = args.Length > 1 ? double.Parse(args[1]) : 120.0;

AppBuilder.Configure<Application>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// Tokens.axaml
var textBrush = new SolidColorBrush(Color.Parse("#F4F5F7"));   // Icsmoi.Text
var accentBrush = new SolidColorBrush(Color.Parse("#FF5A1F")); // Icsmoi.Accent
var backgroundBrush = new SolidColorBrush(Color.Parse("#101216")); // Icsmoi.Background

// Logo.axaml's Icsmoi.Wordmark ControlTemplate, verbatim path data on its 91.86 x 29 canvas.
var canvas = new Canvas { Width = 91.86, Height = 29 };
void AddPath(string data, IBrush stroke, double thickness, PenLineCap cap = PenLineCap.Flat, PenLineJoin join = PenLineJoin.Miter)
{
    canvas.Children.Add(new Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeLineCap = cap,
        StrokeJoin = join,
    });
}

AddPath("M3.25 12 V25", textBrush, 4.5, PenLineCap.Round); // i
AddPath("M22.44 14.32 A6.5 6.5 0 1 0 22.44 22.68", accentBrush, 4.5, PenLineCap.Round); // c
AddPath("M34.73 13.625 A3.25 3.25 0 1 0 31.91 18.5 A3.25 3.25 0 1 1 29.1 23.375", textBrush, 4.5, PenLineCap.Round, PenLineJoin.Round); // s
AddPath("M42.75 25 V16.5 A4.5 4.5 0 0 1 51.75 16.5 V25 M51.75 16.5 A4.5 4.5 0 0 1 60.75 16.5 V25", textBrush, 4.5, PenLineCap.Round, PenLineJoin.Round); // m
AddPath("M67.98 18.5 A6.5 6.5 0 1 1 80.98 18.5 A6.5 6.5 0 1 1 67.98 18.5 Z", accentBrush, 4.5); // o
AddPath("M88.61 12 V25", textBrush, 4.5, PenLineCap.Round); // i
AddPath("M3.25 4.5 H3.26 M88.61 4.5 H88.62", textBrush, 5.5, PenLineCap.Round); // i dots

var viewbox = new Viewbox { Height = wordmarkHeight, Child = canvas };

var padding = new Thickness(wordmarkHeight * 0.45, wordmarkHeight * 0.32);
var border = new Border
{
    Background = backgroundBrush,
    CornerRadius = new CornerRadius(wordmarkHeight * 0.22),
    Padding = padding,
    Child = viewbox,
};

var contentWidth = wordmarkHeight * (91.86 / 29.0);
var totalWidth = contentWidth + padding.Left + padding.Right;
var totalHeight = wordmarkHeight + padding.Top + padding.Bottom;

var window = new Window
{
    Content = border,
    Width = totalWidth,
    Height = totalHeight,
    SizeToContent = SizeToContent.Manual,
};

window.Show();
for (var i = 0; i < 5; i++)
{
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
}

using var frame = window.CaptureRenderedFrame();
if (frame is null)
{
    Console.Error.WriteLine("CaptureRenderedFrame returned null.");
    Environment.Exit(1);
}
frame.Save(outPath);

Console.WriteLine($"Wrote {outPath} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
Environment.Exit(0);
