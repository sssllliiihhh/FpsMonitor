using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FpsMonitor.Models;
using FpsMonitor.Services;
using FpsMonitor.Views;
using Microsoft.Win32;
using ScottPlot;

namespace FpsMonitor;

public partial class MainWindow : Window
{
    private const int HistoryLength = 120;

    private readonly AppSettings _settings;
    private readonly PresentMonService _presentMon = new();
    private readonly HardwareMonitorService _hardware = new();
    private readonly DispatcherTimer _sampleTimer;

    private readonly double[] _xAxis = Enumerable.Range(0, HistoryLength).Select(i => (double)i).ToArray();
    private readonly double[] _fpsHistory = new double[HistoryLength];
    private readonly double[] _frametimeHistory = new double[HistoryLength];
    private readonly double[] _cpuHistory = new double[HistoryLength];
    private readonly double[] _gpuHistory = new double[HistoryLength];

    private GameProcessInfo? _selectedGame;
    private OverlayWindow? _overlay;
    private bool _isMonitoring;
    private bool _isInitializingUi = true;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        PresentMonPathBox.Text = _settings.PresentMonPath;

        LoadOverlaySettingsToUi();
        ConfigurePlot();

        _presentMon.UnexpectedExit += OnPresentMonUnexpectedExit;

        _sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sampleTimer.Tick += SampleTimer_Tick;

        Closing += MainWindow_Closing;
        _isInitializingUi = false;
    }

    private void LoadOverlaySettingsToUi()
    {
        CornerCombo.SelectedIndex = (int)_settings.OverlayCorner;
        OpacitySlider.Value = _settings.OverlayOpacity;
        CompactCheckBox.IsChecked = _settings.OverlayCompact;
        ShowOnePercentCheckBox.IsChecked = _settings.OverlayShowOnePercentLow;
        ShowVramCheckBox.IsChecked = _settings.OverlayShowVram;
    }

    private void ConfigurePlot()
    {
        var plt = TelemetryPlot.Plot;
        plt.FigureBackground.Color = ScottPlot.Color.FromHex("#161B22");
        plt.DataBackground.Color = ScottPlot.Color.FromHex("#0D1117");
        plt.Grid.MajorLineColor = ScottPlot.Color.FromHex("#242B38");
        plt.Axes.Color(ScottPlot.Color.FromHex("#8B949E"));
        plt.Axes.Left.Label.Text = "FPS";
        plt.Axes.Bottom.Label.Text = "секунды";

        // Отключаем лишнюю реакцию на колесо мыши, чтобы график не сбивался случайно.
        TelemetryPlot.UserInputProcessor.IsEnabled = false;
        RefreshPlot();
    }

    private void RefreshPlot()
    {
        var plt = TelemetryPlot.Plot;
        plt.Clear();

        if (ChartFpsRadio.IsChecked == true)
        {
            var sig = plt.Add.Scatter(_xAxis, _fpsHistory);
            sig.Color = ScottPlot.Color.FromHex("#22C55E");
            sig.LineWidth = 2.2f;
            sig.MarkerSize = 0;
            sig.FillY = true;
            sig.FillYValue = 0;
            sig.FillYColor = ScottPlot.Color.FromHex("#22C55E").WithAlpha(45);

            var max = Math.Max(60, _fpsHistory.Max() * 1.15);
            plt.Axes.SetLimits(0, HistoryLength - 1, 0, max);
            plt.Axes.Left.Label.Text = "FPS";
        }
        else if (ChartFrametimeRadio.IsChecked == true)
        {
            var sig = plt.Add.Scatter(_xAxis, _frametimeHistory);
            sig.Color = ScottPlot.Color.FromHex("#3B82F6");
            sig.LineWidth = 2.2f;
            sig.MarkerSize = 0;
            sig.FillY = true;
            sig.FillYValue = 0;
            sig.FillYColor = ScottPlot.Color.FromHex("#3B82F6").WithAlpha(45);

            var max = Math.Max(20, _frametimeHistory.Max() * 1.2);
            plt.Axes.SetLimits(0, HistoryLength - 1, 0, max);
            plt.Axes.Left.Label.Text = "Frametime (мс)";
        }
        else
        {
            var cpuSig = plt.Add.Scatter(_xAxis, _cpuHistory);
            cpuSig.Color = ScottPlot.Color.FromHex("#3B82F6");
            cpuSig.LineWidth = 2f;
            cpuSig.MarkerSize = 0;
            cpuSig.LegendText = "CPU %";

            var gpuSig = plt.Add.Scatter(_xAxis, _gpuHistory);
            gpuSig.Color = ScottPlot.Color.FromHex("#A855F7");
            gpuSig.LineWidth = 2f;
            gpuSig.MarkerSize = 0;
            gpuSig.LegendText = "GPU %";

            plt.ShowLegend(Alignment.UpperLeft);
            plt.Legend.BackgroundColor = ScottPlot.Color.FromHex("#1C212B");
            plt.Legend.FontColor = ScottPlot.Color.FromHex("#F0F6FC");
            plt.Legend.OutlineColor = ScottPlot.Color.FromHex("#2D3544");

            plt.Axes.SetLimits(0, HistoryLength - 1, 0, 105);
            plt.Axes.Left.Label.Text = "Нагрузка (%)";
        }

        TelemetryPlot.Refresh();
    }

    private void BrowsePresentMon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите PresentMon.exe",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*"
        };

        var current = PresentMonPathBox.Text.Trim();
        if (!string.IsNullOrEmpty(current) && Directory.Exists(Path.GetDirectoryName(current)))
            dialog.InitialDirectory = Path.GetDirectoryName(current);

        if (dialog.ShowDialog(this) == true)
        {
            PresentMonPathBox.Text = dialog.FileName;
            _settings.PresentMonPath = dialog.FileName;
            _settings.Save();
        }
    }

    private void SelectGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GameSelectionWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedGame is not null)
        {
            _selectedGame = dialog.SelectedGame;
            SelectedGameText.Text = _selectedGame.Label;
            SelectedGameText.Foreground = (Brush)FindResource("TextPrimary");

            if (_isMonitoring)
            {
                StopMonitoring();
                StartMonitoring();
            }
        }
    }

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_isMonitoring)
            StopMonitoring();
        else
            StartMonitoring();
    }

    private void StartMonitoring()
    {
        var presentMonPath = PresentMonPathBox.Text.Trim();
        if (!File.Exists(presentMonPath))
        {
            MessageBox.Show(this,
                $"Файл не найден:\n{presentMonPath}\n\nУкажите путь к PresentMon.exe через «Обзор…».",
                "PresentMon не найден", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selectedGame is null)
        {
            MessageBox.Show(this, "Сначала выберите игру.", "Игра не выбрана",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _settings.PresentMonPath = presentMonPath;
        _settings.Save();

        try
        {
            _presentMon.Start(presentMonPath, _selectedGame.ExeName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось запустить PresentMon:\n{ex.Message}",
                "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ClearHistoryArrays();
        RefreshPlot();

        _isMonitoring = true;
        _sampleTimer.Start();
        StartStopButton.Content = "Стоп";
        StatusText.Text = $"Мониторинг: {_selectedGame.DisplayName} ({_selectedGame.ExeName})";
    }

    private void StopMonitoring()
    {
        _sampleTimer.Stop();
        _isMonitoring = false;
        _presentMon.Stop(PresentMonPathBox.Text.Trim());

        StartStopButton.Content = "Старт";
        ResetCardsText();
        StatusText.Text = "Остановлено.";
    }

    private void ResetStats_Click(object sender, RoutedEventArgs e)
    {
        _presentMon.ResetSessionStats();
        ClearHistoryArrays();
        RefreshPlot();
        StatusText.Text = _isMonitoring ? "Статистика сессии сброшена." : "График очищен.";
    }

    private async void SampleTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isMonitoring)
            return;

        var hw = await Task.Run(() => _hardware.Collect());
        var fps = _presentMon.GetSnapshot();

        var stats = new SystemStats
        {
            Fps = fps.CurrentFps,
            AvgFps = fps.AvgFps,
            MinFps = fps.MinFps,
            MaxFps = fps.MaxFps,
            OnePercentLowFps = fps.OnePercentLowFps,
            FrametimeMs = fps.FrametimeMs,
            CpuUsage = hw.CpuUsage,
            CpuTemp = hw.CpuTemp,
            RamUsage = hw.RamUsage,
            RamUsedGb = hw.RamUsedGb,
            RamTotalGb = hw.RamTotalGb,
            GpuUsage = hw.GpuUsage,
            GpuTemp = hw.GpuTemp,
            VramUsedMb = hw.VramUsedMb,
            VramTotalMb = hw.VramTotalMb
        };

        UpdateUi(stats);
    }

    private void UpdateUi(SystemStats stats)
    {
        FpsValueText.Text = stats.Fps.ToString();
        FpsValueText.Foreground = stats.Fps switch
        {
            >= 60 => (Brush)FindResource("AccentGreen"),
            >= 30 => (Brush)FindResource("AccentAmber"),
            > 0 => (Brush)FindResource("AccentRed"),
            _ => (Brush)FindResource("TextSecondary")
        };

        FrametimeSubText.Text = stats.FrametimeMs > 0
            ? $"Frametime: {stats.FrametimeMs:0.0} мс"
            : "Frametime: —";

        AvgOnePercentText.Text = stats.AvgFps > 0
            ? $"{stats.AvgFps} / {stats.OnePercentLowFps}"
            : "— / —";
        MinMaxText.Text = stats.MaxFps > 0
            ? $"Min: {stats.MinFps}  |  Max: {stats.MaxFps}"
            : "Min: —  |  Max: —";

        CpuValueText.Text = $"{stats.CpuUsage:0}%";
        CpuTempSubText.Text = stats.CpuTemp.HasValue
            ? $"Температура: {stats.CpuTemp:0} °C"
            : "Температура: —";

        GpuValueText.Text = $"{stats.GpuUsage:0}%";
        GpuTempSubText.Text = stats.GpuTemp.HasValue
            ? $"Температура: {stats.GpuTemp:0} °C"
            : "Температура: —";

        VramValueText.Text = stats.VramTotalMb > 0
            ? $"{stats.VramUsedMb:0} / {stats.VramTotalMb:0} МБ"
            : "—";

        RamValueText.Text = $"{stats.RamUsage:0}%";
        RamSubText.Text = stats.RamTotalGb > 0
            ? $"Объём: {stats.RamUsedGb:0.0} / {stats.RamTotalGb:0.0} ГБ"
            : "Объём: —";

        PushHistory(_fpsHistory, stats.Fps);
        PushHistory(_frametimeHistory, stats.FrametimeMs);
        PushHistory(_cpuHistory, stats.CpuUsage);
        PushHistory(_gpuHistory, stats.GpuUsage);
        RefreshPlot();

        if (_overlay is not null && OverlayToggle.IsChecked == true)
            _overlay.UpdateStats(stats);
    }

    private void ResetCardsText()
    {
        FpsValueText.Text = "—";
        FpsValueText.Foreground = (Brush)FindResource("AccentGreen");
        FrametimeSubText.Text = "Frametime: —";
        AvgOnePercentText.Text = "— / —";
        MinMaxText.Text = "Min: —  |  Max: —";
        CpuValueText.Text = "—";
        CpuTempSubText.Text = "Температура: —";
        GpuValueText.Text = "—";
        GpuTempSubText.Text = "Температура: —";
        VramValueText.Text = "—";
        RamValueText.Text = "—";
        RamSubText.Text = "Объём: —";
    }

    private void ClearHistoryArrays()
    {
        Array.Clear(_fpsHistory);
        Array.Clear(_frametimeHistory);
        Array.Clear(_cpuHistory);
        Array.Clear(_gpuHistory);
    }

    private static void PushHistory(double[] buffer, double value)
    {
        Array.Copy(buffer, 1, buffer, 0, buffer.Length - 1);
        buffer[^1] = value;
    }

    private void ChartMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitializingUi)
            RefreshPlot();
    }

    private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (OverlayToggle.IsChecked == true)
        {
            _overlay ??= new OverlayWindow();
            _overlay.ApplySettings(_settings);
            _overlay.Show();
        }
        else
        {
            _overlay?.Hide();
        }
    }

    private void OverlaySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingUi)
            return;

        if (CornerCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<OverlayCorner>(item.Tag?.ToString(), out var corner))
        {
            _settings.OverlayCorner = corner;
        }

        _settings.OverlayOpacity = Math.Round(OpacitySlider.Value, 2);
        _settings.OverlayCompact = CompactCheckBox.IsChecked == true;
        _settings.OverlayShowOnePercentLow = ShowOnePercentCheckBox.IsChecked == true;
        _settings.OverlayShowVram = ShowVramCheckBox.IsChecked == true;
        _settings.Save();

        _overlay?.ApplySettings(_settings);
    }

    private void OnPresentMonUnexpectedExit(int exitCode)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isMonitoring)
                return;

            StopMonitoring();
            MessageBox.Show(this,
                $"PresentMon неожиданно завершился (код {exitCode}).\n\n" +
                "Возможные причины: неверный путь к PresentMon.exe, несовместимая версия или занятая ETW-сессия.",
                "PresentMon завершился", MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopMonitoring();
        _overlay?.Close();
        _presentMon.Dispose();
        _hardware.Dispose();
    }
}
