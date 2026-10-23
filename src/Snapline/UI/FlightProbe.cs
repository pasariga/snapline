using System.Diagnostics;
using System.Windows.Media;

namespace Snapline.UI;

internal sealed record FlightStatistics(int Frames, double RenderingCallbackHz,
    double MedianGapMs, double P95GapMs, double MaxGapMs, int GapsOver20Ms,
    double UpdateMeanMs, double UpdateMaxMs, double FlightWallMs,
    double ProcessCpuMs, long AllocatedBytes, long WorkingSetBytes, double ConfiguredDurationMs,
    double AverageSurfacePixels, long MaxSurfacePixels, double[] FrameGapsMs,
    double GcPauseMs, double MaxDrawMs, double MaxPresentMs,
    double SetupWallMs=0,double TotalProcessCpuMs=0);

/// <summary>Opt-in benchmark telemetry. Rendering callbacks are NOT displayed-frame FPS.</summary>
internal sealed class FlightProbe
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _duration;
    private readonly double _cpuStart = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
    private readonly long _allocStart = GC.GetTotalAllocatedBytes();
    private readonly double _gcPauseStart = GC.GetTotalPauseDuration().TotalMilliseconds;
    private double _drawMax,_presentMax;
    private readonly List<double> _frames = new();
    private readonly List<double> _updates = new();
    private readonly List<long> _surface = new();
    private long _updateStart;
    private TimeSpan? _lastRenderingTime;

    public FlightProbe(double duration) => _duration = duration;
    public void BeginFrame(EventArgs e)
    {
        var time = (e as RenderingEventArgs)?.RenderingTime;
        if (time is null || time != _lastRenderingTime)
            _frames.Add(_clock.Elapsed.TotalMilliseconds);
        _lastRenderingTime = time;
        _updateStart = Stopwatch.GetTimestamp();
    }
    public void EndFrame() => _updates.Add(Stopwatch.GetElapsedTime(_updateStart).TotalMilliseconds);
    public void SubmittedFrame() => _frames.Add(_clock.Elapsed.TotalMilliseconds);
    public void RecordCalls(double draw,double present){_drawMax=Math.Max(_drawMax,draw);_presentMax=Math.Max(_presentMax,present);}
    public void RecordSurface(int width,int height) => _surface.Add((long)width*height);
    public FlightStatistics Finish(double elapsed)
    {
        var gaps = _frames.Zip(_frames.Skip(1), (a, b) => b - a).Order().ToArray();
        double Quantile(double p) => gaps.Length == 0 ? 0 : gaps[(int)Math.Ceiling(p * (gaps.Length-1))];
        using var process = Process.GetCurrentProcess();
        double span = _frames.Count > 1 ? _frames[^1] - _frames[0] : 0;
        return new(_frames.Count, span > 0 ? 1000 * (_frames.Count-1) / span : 0,
            Quantile(.5), Quantile(.95), Quantile(1), gaps.Count(g=>g>20),
            _updates.Count>0 ? _updates.Average() : 0, _updates.Count>0 ? _updates.Max() : 0,
            elapsed, process.TotalProcessorTime.TotalMilliseconds - _cpuStart,
            GC.GetTotalAllocatedBytes() - _allocStart, process.WorkingSet64, _duration*1000,
            _surface.Count>0 ? _surface.Average() : 0, _surface.Count>0 ? _surface.Max() : 0,gaps,
            GC.GetTotalPauseDuration().TotalMilliseconds-_gcPauseStart,_drawMax,_presentMax);
    }
}
