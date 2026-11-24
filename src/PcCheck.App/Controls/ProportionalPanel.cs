using System.Windows;
using System.Windows.Controls;

namespace PcCheck.App.Controls;

/// <summary>
/// Xếp các phần tử theo hàng ngang, chiều rộng tỉ lệ với <see cref="WeightProperty"/> (vd dung lượng phân vùng),
/// mỗi phần tử có chiều rộng tối thiểu để phân vùng nhỏ (EFI, MSR) vẫn bấm được.
/// </summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentArrange | FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static double GetWeight(DependencyObject element) => (double)element.GetValue(WeightProperty);

    public static void SetWeight(DependencyObject element, double value) => element.SetValue(WeightProperty, value);

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }

    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? InternalChildren.Count * (MinItemWidth + Spacing) : availableSize.Width;
        var widths = Widths(width);
        double height = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(widths[i], availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = Widths(finalSize.Width);
        double x = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i] + Spacing;
        }

        return finalSize;
    }

    /// <summary>Mỗi phần tử nhận chiều rộng tối thiểu, phần còn lại chia theo trọng số.</summary>
    private double[] Widths(double total)
    {
        var count = InternalChildren.Count;
        var widths = new double[count];
        if (count == 0) return widths;

        var usable = Math.Max(0, total - Spacing * (count - 1));
        var min = Math.Min(MinItemWidth, usable / count);
        var weights = new double[count];
        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            weights[i] = Math.Max(0, GetWeight(InternalChildren[i]));
            sum += weights[i];
        }

        var extra = usable - min * count;
        for (var i = 0; i < count; i++) widths[i] = min + (sum > 0 ? extra * weights[i] / sum : extra / count);
        return widths;
    }
}
