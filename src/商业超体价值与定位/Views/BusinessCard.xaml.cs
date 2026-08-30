using System.Windows.Controls;

namespace 商业超体价值与定位.Views;

public partial class BusinessCard : UserControl
{
    public static readonly System.Windows.DependencyProperty TitleProperty =
        System.Windows.DependencyProperty.Register(nameof(Title), typeof(string), typeof(BusinessCard));

    public static readonly System.Windows.DependencyProperty DescriptionProperty =
        System.Windows.DependencyProperty.Register(nameof(Description), typeof(string), typeof(BusinessCard));

    public static readonly System.Windows.DependencyProperty ColorProperty =
        System.Windows.DependencyProperty.Register(nameof(Color), typeof(string), typeof(BusinessCard));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public BusinessCard()
    {
        InitializeComponent();
    }
}
