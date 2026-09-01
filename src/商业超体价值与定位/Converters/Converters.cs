using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace 商业超体价值与定位.Converters;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value;
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
            return visibility != Visibility.Visible;
        return false;
    }
}

public class StringToColorBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> ColorCache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string colorString && !string.IsNullOrEmpty(colorString))
        {
            if (ColorCache.TryGetValue(colorString, out var cachedBrush))
                return cachedBrush;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorString);
                var brush = new SolidColorBrush(color);
                ColorCache[colorString] = brush;
                return brush;
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(233, 69, 96));
            }
        }
        return new SolidColorBrush(Color.FromRgb(233, 69, 96));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percentage && parameter is double maxWidth)
        {
            return percentage * maxWidth;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            var format = parameter as string ?? "yyyy-MM-dd HH:mm";
            return dateTime.ToString(format);
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LinesToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int lines)
        {
            return lines * 22.0;
        }
        return 66.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 集合数量 → Visibility：数量为 0 时折叠，否则可见。
/// 用于按集合是否为空显示/隐藏区块（如诊断清单尚未解析时）。
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 字符串 → Visibility：空字符串/空白时折叠，否则可见。
/// 用于根据说明文本是否存在显示/隐藏辅助行。
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// ContentFormat 枚举值 → 对应的徽章背景色（十六进制字符串）。
/// </summary>
public class ContentFormatToColorConverter : IValueConverter
{
    private static readonly Dictionary<商业超体价值与定位.Models.ContentFormat, string> FormatColors = new()
    {
        { 商业超体价值与定位.Models.ContentFormat.Article,       "#2196F3" },  // 蓝色
        { 商业超体价值与定位.Models.ContentFormat.ShortVideoScript, "#E91E63" }, // 粉色
        { 商业超体价值与定位.Models.ContentFormat.ImagePost,    "#FF9800" },  // 橙色
        { 商业超体价值与定位.Models.ContentFormat.PosterCopy,   "#9C27B0" },  // 紫色
        { 商业超体价值与定位.Models.ContentFormat.PrivateMessage, "#4CAF50" }, // 绿色
        { 商业超体价值与定位.Models.ContentFormat.Generic,      "#607D8B" },  // 灰蓝
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is 商业超体价值与定位.Models.ContentFormat format &&
            FormatColors.TryGetValue(format, out var hex))
        {
            return hex;
        }
        return "#3A3F5C"; // 默认色
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
