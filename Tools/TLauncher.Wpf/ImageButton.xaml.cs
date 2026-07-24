using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TLauncher.Wpf;

/// <summary>
/// A bitmap button with normal / hover / pressed / disabled skins — the WPF stand-in for the launcher's
/// MFC <c>CHoverButton</c>. Set the four <c>*Source</c> images; raises <see cref="Click"/> on release.
/// </summary>
public partial class ImageButton : UserControl
{
    private bool _pressed;

    public ImageButton()
    {
        InitializeComponent();
        IsEnabledChanged += (_, _) => UpdateVisual();
        Loaded += (_, _) => UpdateVisual();
    }

    public event RoutedEventHandler? Click;

    public static readonly DependencyProperty NormalSourceProperty =
        DependencyProperty.Register(nameof(NormalSource), typeof(ImageSource), typeof(ImageButton),
            new PropertyMetadata(null, (d, _) => ((ImageButton)d).UpdateVisual()));
    public static readonly DependencyProperty HoverSourceProperty =
        DependencyProperty.Register(nameof(HoverSource), typeof(ImageSource), typeof(ImageButton), new PropertyMetadata(null));
    public static readonly DependencyProperty PressedSourceProperty =
        DependencyProperty.Register(nameof(PressedSource), typeof(ImageSource), typeof(ImageButton), new PropertyMetadata(null));
    public static readonly DependencyProperty DisabledSourceProperty =
        DependencyProperty.Register(nameof(DisabledSource), typeof(ImageSource), typeof(ImageButton), new PropertyMetadata(null));

    public ImageSource? NormalSource { get => (ImageSource?)GetValue(NormalSourceProperty); set => SetValue(NormalSourceProperty, value); }
    public ImageSource? HoverSource { get => (ImageSource?)GetValue(HoverSourceProperty); set => SetValue(HoverSourceProperty, value); }
    public ImageSource? PressedSource { get => (ImageSource?)GetValue(PressedSourceProperty); set => SetValue(PressedSourceProperty, value); }
    public ImageSource? DisabledSource { get => (ImageSource?)GetValue(DisabledSourceProperty); set => SetValue(DisabledSourceProperty, value); }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); UpdateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); _pressed = false; UpdateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        _pressed = true;
        CaptureMouse();
        UpdateVisual();
        e.Handled = true; // don't let the window start a drag
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        bool wasPressed = _pressed;
        _pressed = false;
        ReleaseMouseCapture();
        UpdateVisual();
        if (wasPressed && IsEnabled && IsMouseOver)
        {
            Click?.Invoke(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void UpdateVisual()
    {
        ImageSource? src =
            !IsEnabled ? (DisabledSource ?? NormalSource) :
            _pressed ? (PressedSource ?? HoverSource ?? NormalSource) :
            IsMouseOver ? (HoverSource ?? NormalSource) :
            NormalSource;
        Img.Source = src;
    }
}
