namespace Emberward.Desktop;

using System.Globalization;
using Emberward.Desktop.Rendering;

internal sealed class PerfCaptureSession
{
    private readonly PerfCaptureOptions? _options;
    private readonly List<double> _frameMs;
    private readonly List<double> _swapchainWaitMs;
    private readonly List<double> _submitMs;
    private readonly List<double> _gpuMs;
    private int _framesSeen;

    private PerfCaptureSession(PerfCaptureOptions? options)
    {
        _options = options;
        int capacity = options is PerfCaptureOptions o ? o.MeasureFrames : 0;
        _frameMs = new List<double>(capacity);
        _swapchainWaitMs = new List<double>(capacity);
        _submitMs = new List<double>(capacity);
        _gpuMs = new List<double>(capacity);
    }

    public static PerfCaptureSession Create(PerfCaptureOptions? options)
    {
        if (options is not PerfCaptureOptions perf)
            return new PerfCaptureSession(null);

        var normalized = new PerfCaptureOptions(
            WarmupFrames: Math.Max(0, perf.WarmupFrames),
            MeasureFrames: Math.Max(1, perf.MeasureFrames),
            CsvPath: string.IsNullOrWhiteSpace(perf.CsvPath) ? null : perf.CsvPath);
        Console.WriteLine($"[Perf] enabled warmup={normalized.WarmupFrames} measure={normalized.MeasureFrames}");
        return new PerfCaptureSession(normalized);
    }

    /// <param name="render">Where the frame's render waited (<see cref="IGameRenderBackend.LastFrameTiming"/>).</param>
    public bool Capture(TimeSpan totalFrame, RenderTiming render)
    {
        if (_options is not PerfCaptureOptions perf)
            return false;

        _framesSeen++;
        if (_framesSeen > perf.WarmupFrames)
        {
            _frameMs.Add(totalFrame.TotalMilliseconds);
            _swapchainWaitMs.Add(render.SwapchainWaitMs);
            _submitMs.Add(render.SubmitMs);
            _gpuMs.Add(render.GpuMs);
        }

        return _frameMs.Count >= perf.MeasureFrames;
    }

    /// <param name="windowHidden">The run never showed its window, so the OS may hold back its presents.</param>
    public void ReportIfEnabled(bool windowHidden = false)
    {
        if (_options is not PerfCaptureOptions perf || _frameMs.Count == 0)
            return;

        double sum = 0;
        for (int i = 0; i < _frameMs.Count; i++)
            sum += _frameMs[i];

        double[] sorted = _frameMs.ToArray();
        Array.Sort(sorted);
        double avg = sum / _frameMs.Count;
        double min = sorted[0];
        double max = sorted[^1];
        double p50 = Percentile(sorted, 0.50);
        double p95 = Percentile(sorted, 0.95);
        double p99 = Percentile(sorted, 0.99);

        Console.WriteLine(
            $"[Perf] frames={_frameMs.Count} warmup={perf.WarmupFrames} avg={avg:F3}ms " +
            $"p50={p50:F3}ms p95={p95:F3}ms p99={p99:F3}ms min={min:F3}ms max={max:F3}ms");
        // The frame time above includes waiting on the display. These say where: the GPU's own time is what the
        // shaders cost, and a long swapchain wait or submit with a short GPU time is the OS holding presents back.
        ReportPart("swapchainWait", _swapchainWaitMs);
        ReportPart("submit", _submitMs);
        ReportPart("gpu", _gpuMs);
        var cpuMs = new List<double>(_frameMs.Count);
        for (int i = 0; i < _frameMs.Count; i++)
            cpuMs.Add(Math.Max(0, _frameMs[i] - _swapchainWaitMs[i] - _submitMs[i] - _gpuMs[i]));
        ReportPart("cpu", cpuMs);
        if (windowHidden)
            Console.WriteLine("[Perf] The window was hidden: the OS may throttle its presents, so frame times can be far " +
                "above the work done. Read the gpu and cpu lines, or capture with the window shown and the display on.");

        if (perf.CsvPath is null)
            return;

        string fullPath = Path.GetFullPath(perf.CsvPath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(fullPath, false);
        writer.WriteLine("frame,ms,swapchainWaitMs,submitMs,gpuMs");
        for (int i = 0; i < _frameMs.Count; i++)
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{i},{_frameMs[i]:F6},{_swapchainWaitMs[i]:F6},{_submitMs[i]:F6},{_gpuMs[i]:F6}"));
        Console.WriteLine($"[Perf] wrote csv: {fullPath}");
    }

    private static void ReportPart(string name, List<double> samples)
    {
        double[] sorted = samples.ToArray();
        Array.Sort(sorted);
        double sum = 0;
        foreach (double ms in sorted)
            sum += ms;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[Perf]   {name}: avg={sum / Math.Max(1, sorted.Length):F3}ms p50={Percentile(sorted, 0.50):F3}ms " +
            $"p95={Percentile(sorted, 0.95):F3}ms max={(sorted.Length > 0 ? sorted[^1] : 0):F3}ms"));
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
            return 0;
        int idx = (int)Math.Ceiling(sorted.Length * p) - 1;
        idx = Math.Clamp(idx, 0, sorted.Length - 1);
        return sorted[idx];
    }
}

public readonly record struct PerfCaptureOptions(
    int WarmupFrames,
    int MeasureFrames,
    string? CsvPath);
