using System.Windows;
using System.Windows.Media;

namespace PcCheck.App.Controls;

/// <summary>Biểu đồ đường mini cho lịch sử tải CPU/RAM (vẽ trực tiếp, không dùng thư viện biểu đồ).</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        var w = ActualWidth;
        var h = ActualHeight;
        if (values is null || values.Count < 2 || w <= 0 || h <= 0) return;

        var capacity = Math.Max(Capacity, values.Count);
        var step = w / (capacity - 1);
        var offset = (capacity - values.Count) * step;
        var max = Maximum <= 0 ? 1 : Maximum;

        Point P(int i) => new(offset + i * step, h - 1 - Math.Clamp(values[i] / max, 0, 1) * (h - 2));

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(P(0), false, false);
            for (var i = 1; i < values.Count; i++) ctx.LineTo(P(i), true, true);
        }

        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(offset, h), true, true);
            for (var i = 0; i < values.Count; i++) ctx.LineTo(P(i), true, false);
            ctx.LineTo(new Point(offset + (values.Count - 1) * step, h), true, false);
        }

        area.Freeze();

        var fill = Stroke.Clone();
        fill.Opacity = 0.14;
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}
