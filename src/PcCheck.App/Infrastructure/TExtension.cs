using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using PcCheck.Core.Localization;

namespace PcCheck.App.Infrastructure;

/// <summary>
/// Chuỗi song ngữ trong XAML: <c>Text="{l:T nav.dashboard}"</c>.
/// Bind vào <see cref="Loc.Language"/> kèm khóa làm tham số, nên tự cập nhật khi đổi ngôn ngữ.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(Loc.Language))
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
            Converter = KeyConverter.Instance,
            ConverterParameter = Key,
        };
        return binding.ProvideValue(serviceProvider);
    }

    private sealed class KeyConverter : IValueConverter
    {
        public static readonly KeyConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Loc.T(parameter as string ?? "");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
