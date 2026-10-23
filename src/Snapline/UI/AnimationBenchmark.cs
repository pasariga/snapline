using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snapline.Core;
using Snapline.Interop;
using static Snapline.Interop.Native;

namespace Snapline.UI;

/// <summary>Real on-screen FlightWindow A/B testing, isolated from captures and production settings.</summary>
internal static class AnimationBenchmark
{
    public static bool Active { get; private set; }
    private static string _output = "";
    private static string _variant = "";
    private static App? _app;
    private static readonly Queue<(int width,int height,int round,bool warmup)> Cases = new();
    private static readonly List<object> Results = new();
    private static (int width,int height,int round,bool warmup) _case;
    private static string _hashBefore = "";
    private static string _imagePath = "";
    private static double _decodeMs;
    private static Display? _monitor;
    private static DispatcherTimer? _delay;
    private static int _closedCount;
    private static Stopwatch? _captureClock;
    private static readonly List<object> Closures=new();

    public static int Run(string variant, string output)
    {
        if (variant is not ("baseline" or "optimized" or "native" or "retained" or "retained-compact" or "software" or "verify" or "verify-native")) throw new ArgumentException("Unknown variant");
        Active = true;
        _variant = variant;
        _output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        Settings.ReadOnly = true;
        Settings.Current.SoundOn = false;
        FlightWindow.FastResampling = variant != "baseline";
        FlightWindow.FixedPhotoLayer = variant != "baseline";
        FlightWindow.CachedShadow = variant != "baseline";
        FlightWindow.UseCompositor = variant is "native" or "retained" or "retained-compact";
        CompositorFlight.RetainedTimeline = variant.StartsWith("retained");
        Settings.Current.CompactFlightPreview=variant=="retained-compact";
        // Diagnostic only: no registry, driver, or production process is changed.
        if (variant == "software")
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        return app.Run();
    }
    public static void Start(App app)
    {
        _app = app;
        _monitor = Displays.Primary();
        if (_variant is "verify" or "verify-native")
        {
            try { VerifyImages(); app.Shutdown(0); }
            catch(Exception ex) { File.WriteAllText(_output+".error.txt",ex.ToString()); app.Shutdown(1); }
            return;
        }
        foreach (var size in new[] { (640,400), (1280,800), (2560,1600) })
        {
            Cases.Enqueue((size.Item1,size.Item2,-1,true));
            int rounds=int.TryParse(Environment.GetEnvironmentVariable("SNAPLINE_BENCH_ROUNDS"),out int n) ? Math.Clamp(n,1,20) : 10;
            for (int round=0; round<rounds; round++) Cases.Enqueue((size.Item1,size.Item2,round,false));
        }
        FlightWindow.StatisticsSink = Record;
        ScheduleNext();
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Next()
    {
        if (Cases.Count==0) { Complete(); return; }
        _case=Cases.Dequeue();
        _captureClock=Stopwatch.StartNew();
        _imagePath=Path.Combine(Path.GetDirectoryName(_output)!, $"synthetic-{_case.width}x{_case.height}.png");
        if (!File.Exists(_imagePath)) Generate(_imagePath,_case.width,_case.height);
        _hashBefore=Hash(_imagePath);
        var clock=Stopwatch.StartNew();
        int limit=Settings.Current.CompactFlightPreview ? 1152 : 1600;
        var image=Thumbnails.Load(_imagePath,limit,2) ?? throw new InvalidOperationException("Synthetic image did not decode");
        _decodeMs=clock.Elapsed.TotalMilliseconds;
        var b=_monitor!.Bounds;
        int width=Math.Min(_case.width,b.Width), height=Math.Min(_case.height,b.Height);
        int left=b.Left+(b.Width-width)/2, top=b.Top+(b.Height-height)/2;
        var from=new RECT(left,top,left+width,top+height);
        var size=Layout.CardSize(_case.width,_case.height);
        int cardW=(int)Math.Round(size.Width*_monitor.Scale),cardH=(int)Math.Round(size.Height*_monitor.Scale);
        int cardLeft=b.Left+(b.Width-cardW)/2;
        int cardTop=b.Top+(int)Math.Round((Layout.RopeY(b.Width/_monitor.Scale/2,b.Width/_monitor.Scale)-Layout.PinAbove+Layout.CardOffsetBelowTop)*_monitor.Scale);
        FlightWindow.Fly(image.Image,from,new RECT(cardLeft,cardTop,cardLeft+cardW,cardTop+cardH),-2.0,_monitor,17,()=>{});
    }
    private static void Record(FlightStatistics stats)
    {
        Results.Add(new { Variant=_variant, Width=_case.width, Height=_case.height,
            Round=_case.round, Warmup=_case.warmup, DecodeMs=_decodeMs,
            OriginalUnchanged=_hashBefore==Hash(_imagePath), Metrics=stats });
    }
    public static void FlightClosed()
    {
        if (!Active) return;
        _closedCount++;
        Closures.Add(new {Width=_case.width,Height=_case.height,Round=_case.round,Warmup=_case.warmup,EndToEndMs=_captureClock?.Elapsed.TotalMilliseconds});
        ScheduleNext();
    }
    private static void ScheduleNext()
    {
        _delay=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(350) };
        _delay.Tick+=(_,_)=> { _delay.Stop(); try { Next(); } catch(Exception ex) {
            File.WriteAllText(_output+".error.txt",ex.ToString()); _app!.Shutdown(1); } };
        _delay.Start();
    }
    private static void Complete()
    {
        var options=new JsonSerializerOptions { WriteIndented=true };
        File.WriteAllText(_output,JsonSerializer.Serialize(new { At=DateTimeOffset.Now,
            Variant=_variant, MetricType=_variant.StartsWith("retained") ? "DWM independent timeline; no per-frame callbacks or claimed displayed FPS" : _variant=="native" ? "GPU swap-chain submit cadence; not scanout FPS" : "WPF rendering callback timing; not compositor displayed FPS",
            CompositorFallbacks=CompositorFlight.Failures,
            RenderCapabilityTier=RenderCapability.Tier>>16,
            ProcessRenderMode=RenderOptions.ProcessRenderMode.ToString(),
            MonitorWidth=_monitor!.Bounds.Width, MonitorHeight=_monitor.Bounds.Height,
            Dpi=_monitor.Dpi, FlightSeconds=FlightWindow.Duration, FadeSeconds=FlightWindow.FadeDuration,
            ClosedFlights=_closedCount, Closures, Results },options));
        _app!.Shutdown(0);
    }
    private static void VerifyImages()
    {
        var root=Path.GetDirectoryName(_output)!;
        var checks=new List<object>();
        foreach(var size in new[] { (640,400),(1280,800),(2560,1600) })
        {
            var path=Path.Combine(root,$"synthetic-{size.Item1}x{size.Item2}.png");
            if(!File.Exists(path)) Generate(path,size.Item1,size.Item2);
            var before=Hash(path);
            var image=Thumbnails.Load(path,int.MaxValue,2)!.Value;
            if(image.PixelWidth!=size.Item1 || image.PixelHeight!=size.Item2)
                throw new InvalidOperationException("Original resolution changed");
            bool bytesMatch=Thumbnails.PngBytes(path)!.SequenceEqual(File.ReadAllBytes(path));
            var b=_monitor!.Bounds;
            int width=Math.Min(size.Item1,b.Width),height=Math.Min(size.Item2,b.Height);
            int left=b.Left+(b.Width-width)/2,top=b.Top+(b.Height-height)/2;
            var from=new RECT(left,top,left+width,top+height);
            var card=Layout.CardSize(size.Item1,size.Item2);
            int tw=(int)(card.Width*_monitor.Scale),th=(int)(card.Height*_monitor.Scale);
            var to=new RECT(b.Left+(b.Width-tw)/2,b.Top+90,b.Left+(b.Width+tw)/2,b.Top+90+th);
            foreach(double progress in new[] { 0.0,.35,.65,1.0 })
                foreach(bool fast in new[] { false,true })
                {
                    FlightWindow.FastResampling=fast; FlightWindow.FixedPhotoLayer=fast; FlightWindow.CachedShadow=fast;
                    FlightWindow.SaveVerificationFrame(image.Image,from,to,_monitor,progress,
                        Path.Combine(root,$"quality-{size.Item1}-{(fast ? "optimized":"baseline")}-{progress:F2}.png"));
                    if(fast && _variant=="verify-native")
                        CompositorFlight.SaveVerificationFrame(image.Image,from,to,_monitor,progress,
                            Path.Combine(root,$"quality-{size.Item1}-native-{progress:F2}.png"));
                }
            // One stationary line, two toggles: the optimization must not alter its pixels.
            var line=new Line(persist:false); line.Hang(path,quietly:true);
            var canvas=new LineCanvas(line) { Width=1000,Height=280 };
            canvas.Measure(new Size(1000,280)); canvas.Arrange(new Rect(0,0,1000,280));
            canvas.Revealed=true; canvas.Advance(1.2); canvas.UpdateLayout();
            string StaticPixels(bool fast)
            {
                FlightWindow.FastResampling=fast; FlightWindow.FixedPhotoLayer=fast; FlightWindow.CachedShadow=fast;
                var rtb=new RenderTargetBitmap(1500,420,144,144,PixelFormats.Pbgra32);
                rtb.Render(canvas);
                var pixels=new byte[1500*420*4]; rtb.CopyPixels(pixels,1500*4,0);
                return Convert.ToHexString(SHA256.HashData(pixels));
            }
            var baseline=StaticPixels(false); var optimized=StaticPixels(true);
            checks.Add(new { Width=size.Item1,Height=size.Item2,
                OriginalHashUnchanged=before==Hash(path),OriginalCopyBytesMatch=bytesMatch,
                StationaryPixelsIdentical=baseline==optimized });
            if(before!=Hash(path) || !bytesMatch || baseline!=optimized)
                throw new InvalidOperationException("Image integrity verification failed");
        }
        File.WriteAllText(_output,JsonSerializer.Serialize(new { At=DateTimeOffset.Now,
            FlightDuration=FlightWindow.Duration,FadeDuration=.16,Checks=checks },new JsonSerializerOptions {WriteIndented=true}));
    }
    private static void Generate(string path,int width,int height)
    {
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(25,43,70),Color.FromRgb(170,206,234),35),null,new Rect(0,0,width,height));
            for(int y=40;y<height;y+=42) dc.DrawLine(new Pen(Brushes.White,1),new Point(20,y),new Point(width-20,y));
            for(int x=24;x<width;x+=64) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(100,(byte)(x%255),180,90)),null,new Rect(x,30,26,Math.Max(10,height-80)));
            var text=new FormattedText($"Snapline synthetic animation test {width} x {height}\nOriginal image is never rewritten.",CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,new Typeface("Segoe UI"),Math.Max(18,width/50.0),Brushes.White,1);
            dc.DrawText(text,new Point(40,height*.43));
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        Thumbnails.SavePng(bitmap,path);
    }
}
