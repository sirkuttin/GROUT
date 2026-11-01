using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReleaseDock;

public static class Ui
{
    public static ImageSource AppIcon { get; } = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/GROUT.ico"));
    public static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/GROUT.ico"))!.Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(164, 180, 201));
    public static Window Window(string title, double width = 700, double height = 720)
    {
        var window = new Window { Title = title, Width = width, Height = height, MinWidth = 550, MinHeight = 500,
            Background = new SolidColorBrush(Color.FromRgb(12, 20, 34)), Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 14, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = AppIcon };
        ConstrainToScreen(window); return window;
    }
    public static void ConstrainToScreen(Window window)
    {
        void Fit()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(window);
            window.MaxWidth = Math.Max(280, area.Width / dpi.DpiScaleX - 24); window.MaxHeight = Math.Max(280, area.Height / dpi.DpiScaleY - 24);
            window.MinWidth = Math.Min(window.MinWidth, window.MaxWidth); window.MinHeight = Math.Min(window.MinHeight, window.MaxHeight);
            if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, window.MaxWidth);
            if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height, window.MaxHeight);
        }
        window.SourceInitialized += (_, _) => Fit(); window.DpiChanged += (_, _) => Fit();
    }
    public static TextBlock Text(string value, double size = 14, bool muted = false) => new() { Text = value, FontSize = size, Foreground = muted ? Muted : Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    public static Button Button(string label, RoutedEventHandler clicked) { var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, IsCancel = label == "Cancel", IsDefault = label is "Next →" or "Save settings" or "Add App" or "Add to library" }; b.Click += clicked; return b; }
    public static TextBox Input(string value = "") => new() { Text = value, Margin = new Thickness(0, 0, 0, 12), MinHeight = 36 };
    public static ComboBox Choice(IEnumerable<string> options, int index = 0)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(8), Foreground = Brushes.Black };
        foreach (var label in options) combo.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = label, Foreground = Brushes.Black }, Foreground = Brushes.Black });
        combo.SelectedIndex = index;
        return combo;
    }
    public static CheckBox Check(string label, bool value = false) => new() { Content = label, IsChecked = value, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
    public static StackPanel Panel(Window window)
    {
        var panel = new StackPanel { Margin = new Thickness(28) };
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return panel;
    }
}
