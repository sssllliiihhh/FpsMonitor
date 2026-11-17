using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FpsMonitor.Services;

public sealed record FpsSnapshot(
    int CurrentFps,
    int AvgFps,
    int MinFps,
    int MaxFps,
    int OnePercentLowFps,
    double FrametimeMs);

public sealed class PresentMonService : IDisposable
{
    private const string SessionName = "FPSMonitor";
    private const int RecentWindowSize = 120;
    private const int SessionMaxSamples = 18000;

    private static readonly string[] CandidateColumns =
    [
        "msbetweenpresents",
        "msbetweendisplaychange",
        "msbetweenappends",
        "frametime",
        "frametime_ms"
    ];

    private readonly object _lock = new();
    private readonly Queue<double> _recentFrameTimes = new(RecentWindowSize);
    private readonly List<double> _sessionFrameTimes = new(4096);

    private Process? _process;
    private CancellationTokenSource? _cts;
    private int _frameColumnIndex = -1;
    private bool _headerParsed;
    private DateTime _lastFrameUtc = DateTime.MinValue;
    private int _lastFps;
    private double _lastFrametimeMs;

    public event Action<int>? UnexpectedExit;

    public bool IsRunning => _process is { HasExited: false };

    public void Start(string presentMonPath, string processExeName)
    {
        Stop();

        if (!File.Exists(presentMonPath))
            throw new FileNotFoundException("PresentMon.exe не найден.", presentMonPath);

        lock (_lock)
        {
            _recentFrameTimes.Clear();
            _sessionFrameTimes.Clear();
            _frameColumnIndex = -1;
            _headerParsed = false;
            _lastFrameUtc = DateTime.MinValue;
            _lastFps = 0;
            _lastFrametimeMs = 0;
        }

        var psi = new ProcessStartInfo
        {
            FileName = presentMonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("--process_name");
        psi.ArgumentList.Add(processExeName);
        psi.ArgumentList.Add("--output_stdout");
        psi.ArgumentList.Add("--v1_metrics");
        psi.ArgumentList.Add("--no_console_stats");
        psi.ArgumentList.Add("--session_name");
        psi.ArgumentList.Add(SessionName);
        psi.ArgumentList.Add("--stop_existing_session");

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;

        if (!_process.Start())
            throw new InvalidOperationException("Не удалось запустить процесс PresentMon.");

        _cts = new CancellationTokenSource();
        var reader = _process.StandardOutput;
        var token = _cts.Token;

        _ = Task.Run(() => ReadLoopAsync(reader, presentMonPath, token), token);
    }

    public void ResetSessionStats()
    {
        lock (_lock)
        {
            _sessionFrameTimes.Clear();
        }
    }

    public FpsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            if (_lastFrameUtc != DateTime.MinValue && (DateTime.UtcNow - _lastFrameUtc).TotalSeconds > 2.5)
            {
                _lastFps = 0;
                _lastFrametimeMs = 0;
                _recentFrameTimes.Clear();
            }

            if (_sessionFrameTimes.Count == 0)
            {
                return new FpsSnapshot(_lastFps, 0, 0, 0, 0, _lastFrametimeMs);
            }

            var avgFt = _sessionFrameTimes.Average();
            var avgFps = avgFt > 0 ? (int)Math.Round(1000.0 / avgFt) : 0;

            var minFt = _sessionFrameTimes.Min();
            var maxFt = _sessionFrameTimes.Max();
            var maxFps = minFt > 0 ? (int)Math.Round(1000.0 / minFt) : 0;
            var minFps = maxFt > 0 ? (int)Math.Round(1000.0 / maxFt) : 0;

            // 1% Low FPS: среднее по 1% самых медленных кадров.
            var sortedDesc = _sessionFrameTimes.OrderDescending().ToList();
            var onePercentCount = Math.Max(1, sortedDesc.Count / 100);
            var slowAvgFt = sortedDesc.Take(onePercentCount).Average();
            var onePercentLowFps = slowAvgFt > 0 ? (int)Math.Round(1000.0 / slowAvgFt) : 0;

            return new FpsSnapshot(_lastFps, avgFps, minFps, maxFps, onePercentLowFps, _lastFrametimeMs);
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, string presentMonPath, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ProcessCsvLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка.
        }
        catch
        {
            // Поток закрыт при завершении PresentMon.
        }
    }

    private void ProcessCsvLine(string line)
    {
        var parts = line.Split(',');

        lock (_lock)
        {
            if (!_headerParsed)
            {
                _headerParsed = true;
                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < parts.Length; i++)
                {
                    var normalized = parts[i].Trim().Trim('\uFEFF', '"').ToLowerInvariant();
                    if (!string.IsNullOrEmpty(normalized))
                        map[normalized] = i;
                }

                foreach (var candidate in CandidateColumns)
                {
                    if (map.TryGetValue(candidate, out var idx))
                    {
                        _frameColumnIndex = idx;
                        break;
                    }
                }
                return;
            }

            if (_frameColumnIndex < 0 || parts.Length <= _frameColumnIndex)
                return;

            var raw = parts[_frameColumnIndex].Trim().Trim('"').Replace(',', '.');
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var frameTime))
                return;

            if (frameTime < 0.1 || frameTime > 1000.0)
                return;

            if (_recentFrameTimes.Count >= RecentWindowSize)
                _recentFrameTimes.Dequeue();
            _recentFrameTimes.Enqueue(frameTime);

            if (_sessionFrameTimes.Count >= SessionMaxSamples)
                _sessionFrameTimes.RemoveAt(0);
            _sessionFrameTimes.Add(frameTime);

            _lastFrameUtc = DateTime.UtcNow;
            var avgRecent = _recentFrameTimes.Average();
            _lastFrametimeMs = Math.Round(avgRecent, 1);
            _lastFps = avgRecent > 0 ? (int)Math.Round(1000.0 / avgRecent) : 0;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_cts is { IsCancellationRequested: false } && sender is Process proc)
        {
            UnexpectedExit?.Invoke(proc.ExitCode);
        }
    }

    public void Stop(string? presentMonPath = null)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        var proc = _process;
        _process = null;

        if (proc is not null)
        {
            var path = presentMonPath ?? proc.StartInfo.FileName;
            proc.Exited -= OnProcessExited;

            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000);
                }
            }
            catch
            {
                // Игнорируем, если процесс уже завершился.
            }
            finally
            {
                proc.Dispose();
            }

            TerminateEtwSession(path);
        }
    }

    private static void TerminateEtwSession(string presentMonPath)
    {
        if (string.IsNullOrWhiteSpace(presentMonPath) || !File.Exists(presentMonPath))
            return;

        try
        {
            using var killer = Process.Start(new ProcessStartInfo
            {
                FileName = presentMonPath,
                Arguments = $"--session_name {SessionName} --terminate_existing_session",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            killer?.WaitForExit(2000);
        }
        catch
        {
            // Игнорируем ошибку принудительного закрытия ETW-сессии.
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
