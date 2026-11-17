using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FpsMonitor.Models;

namespace FpsMonitor.Views;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly DispatcherTimer _hoverTimer;
    private AppSettings _settings = new();
    private bool _isHovered;

    public OverlayWindow()
    {
        InitializeComponent();

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        _hoverTimer.Tick += (_, _) => UpdateHoverOpacity();

        Loaded += (_, _) =>
        {
            ApplyPosition();
            _hoverTimer.Start();
        };
        Unloaded += (_, _) => _hoverTimer.Stop();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        MetricsPanel.Orientation = settings.OverlayCompact
            ? Orientation.Horizontal
            : Orientation.Vertical;

        var itemMargin = settings.OverlayCompact
            ? new Thickness(0, 0, 12, 0)
            : new Thickness(0, 1, 0, 1);

        CpuText.Margin = itemMargin;
        GpuText.Margin = itemMargin;
        VramText.Margin = itemMargin;
        RamText.Margin = settings.OverlayCompact ? new Thickness(0) : new Thickness(0, 1, 0, 1);

        OnePercentText.Visibility = settings.OverlayShowOnePercentLow ? Visibility.Visible : Visibility.Collapsed;
        VramText.Visibility = settings.OverlayShowVram ? Visibility.Visible : Visibility.Collapsed;

        Opacity = _isHovered ? Math.Min(0.28, settings.OverlayOpacity) : settings.OverlayOpacity;
        UpdateLayout();
        ApplyPosition();
    }

    public void UpdateStats(SystemStats stats)
    {
        FpsText.Text = $"FPS: {stats.Fps}";
        FpsText.Foreground = stats.Fps switch
        {
            >= 60 => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            >= 30 => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            > 0 => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            _ => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
        };

        OnePercentText.Text = stats.OnePercentLowFps > 0
            ? $" | 1%: {stats.OnePercentLowFps}"
            : " | 1%: —";

        CpuText.Text = stats.CpuTemp.HasValue
            ? $"CPU: {stats.CpuUsage:0}% ({stats.CpuTemp:0}°C)"
            : $"CPU: {stats.CpuUsage:0}%";

        GpuText.Text = _settings.OverlayShowGpuTemp && stats.GpuTemp.HasValue
            ? $"GPU: {stats.GpuUsage:0}% ({stats.GpuTemp:0}°C)"
            : $"GPU: {stats.GpuUsage:0}%";

        VramText.Text = stats.VramTotalMb > 0
            ? $"VRAM: {stats.VramUsedMb:0}/{stats.VramTotalMb:0} МБ"
            : "VRAM: —";

        RamText.Text = $"RAM: {stats.RamUsage:0}% ({stats.RamUsedGb:0.0} ГБ)";

        if (_settings.OverlayCorner is OverlayCorner.TopRight or OverlayCorner.BottomRight)
            ApplyPosition();
    }

    private void ApplyPosition()
    {
        var area = SystemParameters.WorkArea;
        const double margin = 20;

        var width = ActualWidth > 0 ? ActualWidth : 220;
        var height = ActualHeight > 0 ? ActualHeight : 130;

        switch (_settings.OverlayCorner)
        {
            case OverlayCorner.TopLeft:
                Left = area.Left + margin;
                Top = area.Top + margin;
                break;
            case OverlayCorner.TopRight:
                Left = area.Right - width - margin;
                Top = area.Top + margin;
                break;
            case OverlayCorner.BottomLeft:
                Left = area.Left + margin;
                Top = area.Bottom - height - margin;
                break;
            case OverlayCorner.BottomRight:
                Left = area.Right - width - margin;
                Top = area.Bottom - height - margin;
                break;
        }
    }

    private void UpdateHoverOpacity()
    {
        if (!GetCursorPos(out var pt))
            return;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return;

        var transform = source.CompositionTarget.TransformFromDevice;
        var dipPoint = transform.Transform(new Point(pt.X, pt.Y));

        var bounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        var hovered = bounds.Contains(dipPoint);

        if (hovered != _isHovered)
        {
            _isHovered = hovered;
            Opacity = hovered ? Math.Min(0.28, _settings.OverlayOpacity) : _settings.OverlayOpacity;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
