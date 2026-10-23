using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using Snapline.Interop;
using static Snapline.Interop.Native;
using RF = System.Drawing.RectangleF;
using DAlpha = Vortice.DCommon.AlphaMode;
using DPixel = Vortice.DCommon.PixelFormat;
using Shadow = Vortice.Direct2D1.Effects.Shadow;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using InterpolationMode = Vortice.Direct2D1.InterpolationMode;
using GradientStop = Vortice.Direct2D1.GradientStop;

namespace Snapline.UI;

/// <summary>Transient GPU-composited flight; no layered-window readback, file writes,
/// global GPU preference or system configuration. The stationary line remains WPF.</summary>
internal static class CompositorFlight
{
    internal sealed record PanelStatistics(double CaptureMs,double SetupMs,double WallMs,double CpuMs,bool Success);
    internal static Action<PanelStatistics>? PanelStatisticsSink;
    internal static Action StartPanel(BitmapSource snapshot,RECT rect,double fromY,double toY,double seconds,
        Action ready,Action<bool> completed,double captureMs)
    {
        var dispatcher=Application.Current.Dispatcher;
        var start=Stopwatch.GetTimestamp();double cpu=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
        var photo=Read(snapshot);var empty=new Pixels(new byte[4],1,1);
        var host=new HwndSource(new HwndSourceParameters("Snapline panel transition") {
            PositionX=rect.Left,PositionY=rect.Top,Width=rect.Width,Height=rect.Height,
            WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=(int)(0x00200000|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT)});
        FullScreen.Own.Add(host.Handle);
        var cancel=new CancellationTokenSource();
        Task.Run(()=> {
            bool success=false;double setup=0;
            Renderer? renderer=null;
            try
            {
                cancel.Token.ThrowIfCancellationRequested();
                renderer=new Renderer(host.Handle,rect,96,photo,empty);
                cancel.Token.ThrowIfCancellationRequested();
                renderer.BeginPanelSlide(fromY,toY,seconds);
                setup=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                dispatcher.Invoke(()=>{SetWindowPos(host.Handle,HWND_TOPMOST,rect.Left,rect.Top,rect.Width,rect.Height,SWP_NOACTIVATE|SWP_SHOWWINDOW);ready();});
                renderer.PlayPanelSlide(fromY,toY,seconds);
                success=!cancel.Token.WaitHandle.WaitOne((int)Math.Ceiling(seconds*1000));
            }
            catch(OperationCanceledException){}
            catch(Exception ex){Core.Log.Error($"Panel compositor fallback: 0x{ex.HResult:X8}");}
            finally
            {
                if(!dispatcher.HasShutdownStarted) dispatcher.Invoke(()=> {
                    completed(success);
                    // Keep the final native frame until WPF has had a render
                    // turn. Disposing first caused a blank handoff frame.
                    var retire=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(55)};
                    retire.Tick+=(_,_)=>{
                        retire.Stop();FullScreen.Own.Remove(host.Handle);host.Dispose();
                        // The composition tree must survive until the WPF
                        // handoff, not just until the timeline has finished.
                        Task.Run(()=>renderer?.Dispose());
                    };retire.Start();
                    PanelStatisticsSink?.Invoke(new(captureMs,setup,Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                        Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds-cpu,success));
                });
                else renderer?.Dispose();
                cancel.Dispose();
            }
        });
        return ()=>{try{cancel.Cancel();}catch(ObjectDisposedException){}};
    }
    internal static int Failures { get; private set; }
    internal static string InitializationStage { get; private set; } = "not started";
    internal static bool UseShadowAtlas { get; set; } = true;
    internal static bool RetainedTimeline { get; set; } = true;
    private sealed record Pixels(byte[] Data,int Width,int Height);
    private static readonly Dictionary<(uint dpi,byte alpha,bool retained),Pixels[][]> MaskPixels=new();
    private sealed record PegPixels(Pixels Pixels,double Width,double Height);
    private static readonly Dictionary<(uint dpi,string style,int color,bool dark),PegPixels> PegCache=new();
    private static PegPixels GetPeg(uint dpi,double scale,int seed)
    {
        string style=Core.Settings.Current.PegStyle;
        var key=(dpi,style,style=="mixed"?Math.Abs(seed%6):0,Theme.AppsDark);
        if(PegCache.TryGetValue(key,out var cached)) return cached;
        var peg=Pegs.Create(seed:seed,pinned:false);
        const double pad=16;
        var canvas=new System.Windows.Controls.Canvas {Width=peg.Width+pad*2,Height=peg.Height+pad*2};
        canvas.Children.Add(peg);System.Windows.Controls.Canvas.SetLeft(peg,pad);System.Windows.Controls.Canvas.SetTop(peg,pad);
        canvas.Measure(new Size(canvas.Width,canvas.Height));canvas.Arrange(new Rect(0,0,canvas.Width,canvas.Height));canvas.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(canvas.Width*scale),(int)Math.Ceiling(canvas.Height*scale),dpi,dpi,PixelFormats.Pbgra32);
        bitmap.Render(canvas);bitmap.Freeze();
        cached=new(Read(bitmap),peg.Width,peg.Height);PegCache[key]=cached;return cached;
    }
    private static Pixels[][] ShadowPixels(uint dpi)
    {
        var key=(dpi,Theme.GlassFill.A,RetainedTimeline);
        if(MaskPixels.TryGetValue(key,out var cached)) return cached;
        var masks=CachedFlightShadow.GetMasks(dpi);
        // A retained flight only draws endpoints: only the final nine-slice
        // shadow is used. Reuse immutable CPU pixels between flights, never
        // recreate/upload all153 textures for a two-frame composition.
        if(RetainedTimeline)
        {
            var final=masks[^1].Select(Read).ToArray();
            cached=Enumerable.Repeat(final,masks.Length).ToArray();
        }
        else cached=masks.Select(row=>row.Select(Read).ToArray()).ToArray();
        MaskPixels[key]=cached;return cached;
    }
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("dcomp.dll",ExactSpelling=true)] private static extern int DCompositionCreateDevice3(IntPtr device,ref Guid iid,out IntPtr result);

    private static Pixels Read(BitmapSource source)
    {
        var converted=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
        converted.Freeze();
        var bytes=new byte[converted.PixelWidth*converted.PixelHeight*4];
        converted.CopyPixels(bytes,converted.PixelWidth*4,0);
        return new(bytes,converted.PixelWidth,converted.PixelHeight);
    }
    public static void Start(BitmapSource image,RECT from,RECT to,double tilt,Display monitor,int seed,Action completion)
    {
        var allStart=Stopwatch.GetTimestamp();
        double cpuStart=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
        var dispatcher=Application.Current.Dispatcher;
        var photo=Read(image);
        var peg=GetPeg(monitor.Dpi,monitor.Scale,seed);
        double pegWidth=peg.Width,pegHeight=peg.Height;
        const double pegPad=16;
        var pegPixels=peg.Pixels;
        var shadowMasks=UseShadowAtlas ? ShadowPixels(monitor.Dpi) : null;
        int pad=(int)(80*monitor.Scale);
        var bounds=monitor.Bounds;
        var rect=new RECT(Math.Max(bounds.Left,Math.Min(from.Left,to.Left)-pad),Math.Max(bounds.Top,Math.Min(from.Top,to.Top)-pad),
            Math.Min(bounds.Right,Math.Max(from.Right,to.Right)+pad),Math.Min(bounds.Bottom,Math.Max(from.Bottom,to.Bottom)+pad));
        var parameters=new HwndSourceParameters("Snapline Animation Test · GPU flight") {
            PositionX=rect.Left,PositionY=rect.Top,Width=rect.Width,Height=rect.Height,
            WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=(int)(0x00200000|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT),
            UsesPerPixelOpacity=false };
        var host=new HwndSource(parameters);
        var hwnd=host.Handle;
        host.AddHook((IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled)=> {
            if(msg==WM_NCHITTEST){handled=true;return new IntPtr(HTTRANSPARENT);}
            if(msg==WM_MOUSEACTIVATE){handled=true;return new IntPtr(MA_NOACTIVATE);}return IntPtr.Zero; });
        FullScreen.Own.Add(hwnd);
        var glass=Theme.GlassFill;var edgeTop=Theme.GlassEdgeTop;var edgeBottom=Theme.GlassEdgeBottom;
        Task.Run(()=> {
            bool landed=false,success=false;
            try
            {
                using var renderer=new Renderer(hwnd,rect,monitor.Dpi,photo,pegPixels,shadowMasks);
                if (RetainedTimeline)
                {
                    var setup = Stopwatch.StartNew();
                    renderer.BeginRetainedFlight(from,to,rect,monitor.Scale,tilt,pegWidth,pegHeight,pegPad,glass,edgeTop,edgeBottom);
                    dispatcher.Invoke(()=>SetWindowPos(hwnd,HWND_TOPMOST,rect.Left,rect.Top,rect.Width,rect.Height,SWP_NOACTIVATE|SWP_SHOWWINDOW));
                    renderer.CommitPreparedFlight();
                    double setupMs=Stopwatch.GetElapsedTime(allStart).TotalMilliseconds;
                    var timelineClock = Stopwatch.StartNew();
                    var timelineProbe = FlightWindow.StatisticsSink is null ? null : new FlightProbe(FlightWindow.Duration);
                    timelineProbe?.RecordSurface(rect.Width,rect.Height);
                    // Only lifetime bookkeeping. DWM owns all animation samples;
                    // no polling, redraw loop or per-frame swap-chain presents.
                    Thread.Sleep((int)Math.Ceiling(FlightWindow.Duration * 1000));
                    var timelineStats=timelineProbe?.Finish(timelineClock.Elapsed.TotalMilliseconds);
                    if(timelineStats is not null) timelineStats=timelineStats with {SetupWallMs=setupMs,TotalProcessCpuMs=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds-cpuStart};
                    landed=true;
                    dispatcher.Invoke(()=> { if(timelineStats is not null)FlightWindow.StatisticsSink?.Invoke(timelineStats);completion(); });
                    renderer.BeginFade(FlightWindow.FadeDuration);
                    Thread.Sleep((int)Math.Ceiling(FlightWindow.FadeDuration * 1000));
                    success=true;
                    return;
                }
                renderer.Draw(0,from,to,rect,monitor.Scale,tilt,pegWidth,pegHeight,pegPad,glass,edgeTop,edgeBottom);
                dispatcher.Invoke(()=>SetWindowPos(hwnd,HWND_TOPMOST,rect.Left,rect.Top,rect.Width,rect.Height,SWP_NOACTIVATE|SWP_SHOWWINDOW));
                renderer.Present();
                var clock=Stopwatch.StartNew();
                var probe=FlightWindow.StatisticsSink is null ? null : new FlightProbe(FlightWindow.Duration);
                double nextFrame=0;
                while(!landed)
                {
                    if(clock.Elapsed.TotalSeconds<nextFrame){Thread.Sleep(1);continue;}
                    double elapsed=clock.Elapsed.TotalSeconds;
                    nextFrame=elapsed+1.0/165;
                    long drawStart=Stopwatch.GetTimestamp();
                    renderer.Draw(Math.Min(1,elapsed/FlightWindow.Duration),from,to,rect,monitor.Scale,tilt,pegWidth,pegHeight,pegPad,glass,edgeTop,edgeBottom);
                    double drawMs=Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
                    long presentStart=Stopwatch.GetTimestamp();
                    bool submitted=renderer.Present();
                    probe?.RecordCalls(drawMs,Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds);
                    if(!landed)
                    {
                        if(submitted){probe?.SubmittedFrame();probe?.RecordSurface(rect.Width,rect.Height);}
                        if(submitted && elapsed>=FlightWindow.Duration)
                        {
                            var stats=probe?.Finish(clock.Elapsed.TotalMilliseconds);
                            landed=true;
                            dispatcher.Invoke(()=> { if(stats is not null)FlightWindow.StatisticsSink?.Invoke(stats);completion(); });
                        }
                        else if(clock.Elapsed.TotalSeconds>1.0)
                            throw new TimeoutException("Flight submission deadline exceeded");
                    }
                }
                // The last image is static during fade. Animate its opacity in the
                // compositor; waiting for another present on a nearly invisible window
                // can legitimately stop signaling the frame-latency event.
                renderer.BeginFade(FlightWindow.FadeDuration);
                Thread.Sleep((int)Math.Ceiling(FlightWindow.FadeDuration * 1000));
                success=true;
            }
            catch(Exception ex)
            {
                dispatcher.Invoke(()=> { Failures++;Core.Log.Error($"GPU flight fallback at {InitializationStage}, landed={landed}: 0x{ex.HResult:X8} {ex.StackTrace}"); });
            }
            finally
            {
                if(!dispatcher.HasShutdownStarted)
                    dispatcher.Invoke(()=> {
                        FullScreen.Own.Remove(hwnd);host.Dispose();
                        if(success)AnimationBenchmark.FlightClosed();
                        else if(!landed)FlightWindow.FlyWpf(image,from,to,tilt,monitor,seed,completion);
                        else AnimationBenchmark.FlightClosed();
                    });
            }
        });
    }

    internal static void SaveVerificationFrame(BitmapSource image,RECT from,RECT to,Display monitor,double progress,string path)
    {
        const double pegPad=16;
        var peg=Pegs.Create(seed:17,pinned:false);
        var canvas=new System.Windows.Controls.Canvas {Width=peg.Width+32,Height=peg.Height+32};
        canvas.Children.Add(peg);
        System.Windows.Controls.Canvas.SetLeft(peg,16);System.Windows.Controls.Canvas.SetTop(peg,16);
        canvas.Measure(new Size(canvas.Width,canvas.Height));canvas.Arrange(new Rect(0,0,canvas.Width,canvas.Height));canvas.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(canvas.Width*monitor.Scale),(int)Math.Ceiling(canvas.Height*monitor.Scale),monitor.Dpi,monitor.Dpi,PixelFormats.Pbgra32);
        bitmap.Render(canvas);bitmap.Freeze();
        int pad=(int)(80*monitor.Scale);var bounds=monitor.Bounds;
        var rect=new RECT(Math.Max(bounds.Left,Math.Min(from.Left,to.Left)-pad),Math.Max(bounds.Top,Math.Min(from.Top,to.Top)-pad),
            Math.Min(bounds.Right,Math.Max(from.Right,to.Right)+pad),Math.Min(bounds.Bottom,Math.Max(from.Bottom,to.Bottom)+pad));
        using var host=new HwndSource(new HwndSourceParameters("Snapline GPU image verification") {
            PositionX=rect.Left,PositionY=rect.Top,Width=rect.Width,Height=rect.Height,
            WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=(int)(0x00200000|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT) });
        var masks=UseShadowAtlas ? CachedFlightShadow.GetMasks(monitor.Dpi).Select(row=>row.Select(Read).ToArray()).ToArray() : null;
        using var renderer=new Renderer(host.Handle,rect,monitor.Dpi,Read(image),Read(bitmap),masks);
        renderer.Draw(progress,from,to,rect,monitor.Scale,-2,peg.Width,peg.Height,pegPad,Theme.GlassFill,Theme.GlassEdgeTop,Theme.GlassEdgeBottom);
        renderer.SavePixels(path,monitor.Dpi);
    }

    private sealed class Renderer : IDisposable
    {
        private readonly ID3D11Device _d3d;
        private readonly ID3D11DeviceContext _d3dContext;
        private readonly IDXGIDevice _dxgi;
        private readonly IDXGIFactory2 _dxgiFactory;
        private readonly IDXGISwapChain1 _swap;
        private readonly IDXGISwapChain2 _swap2;
        private readonly ID2D1Factory1 _factory;
        private readonly ID2D1Device _device;
        private readonly ID2D1DeviceContext _dc;
        private readonly ID2D1Bitmap1 _target,_photo,_peg;
        private readonly IDCompositionDesktopDevice _composition;
        private readonly IDCompositionTarget _compositionTarget;
        private readonly IDCompositionVisual2 _visual;
        private readonly IDCompositionVisual3 _visual3;
        private readonly Shadow _shadow;
        private readonly ID2D1SolidColorBrush _glass;
        private readonly ID2D1Bitmap1[][]? _shadowMasks;
        private IntPtr _ready;
        private readonly List<IDisposable> _owned=new();
        private ID2D1Bitmap1? _activeTarget;
        private IDCompositionVisual3? _timelineRoot;
        private T Own<T>(T resource) where T:IDisposable { _owned.Add(resource);return resource; }
        public IntPtr Ready=>_ready;

        public Renderer(IntPtr hwnd,RECT rect,uint dpi,Pixels photo,Pixels peg,Pixels[][]? shadowMasks=null)
        {
            try
            {
            InitializationStage="D3D11 device";
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null,DriverType.Hardware,DeviceCreationFlags.BgraSupport,
                new[] {FeatureLevel.Level_11_0,FeatureLevel.Level_10_1},out _d3d,out _d3dContext).CheckError();
            Own(_d3d);Own(_d3dContext);
            InitializationStage="DXGI device";
            _dxgi=Own(_d3d.QueryInterface<IDXGIDevice>());
            _dxgiFactory=Own(DXGI.CreateDXGIFactory2<IDXGIFactory2>(false));
            var description=new SwapChainDescription1 {Width=(uint)rect.Width,Height=(uint)rect.Height,
                Format=Format.B8G8R8A8_UNorm,BufferCount=2,SampleDescription=new SampleDescription(1,0),
                BufferUsage=Usage.RenderTargetOutput,SwapEffect=SwapEffect.FlipSequential,
                Scaling=Scaling.Stretch,AlphaMode=Vortice.DXGI.AlphaMode.Premultiplied,
                Flags=SwapChainFlags.FrameLatencyWaitableObject};
            InitializationStage="composition swap chain";
            _swap=Own(_dxgiFactory.CreateSwapChainForComposition(_d3d,description));
            _swap2=Own(_swap.QueryInterface<IDXGISwapChain2>());_swap2.MaximumFrameLatency=1;
            _ready=_swap2.FrameLatencyWaitableObject;
            InitializationStage="Direct2D device";
            _factory=Own(D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded));
            _device=Own(_factory.CreateDevice(_dxgi));_dc=Own(_device.CreateDeviceContext(DeviceContextOptions.None));
            using var surface=_swap.GetBuffer<IDXGISurface>(0);
            _target=Own(_dc.CreateBitmapFromDxgiSurface(surface,new BitmapProperties1(new DPixel(Format.B8G8R8A8_UNorm,DAlpha.Premultiplied),dpi,dpi,BitmapOptions.Target|BitmapOptions.CannotDraw)));
            _dc.Target=_target;_dc.Dpi=new Vortice.Mathematics.Size(dpi,dpi);
            _photo=Own(Upload(photo));_peg=Own(Upload(peg));
            if(shadowMasks is not null)
            {
                var uploaded=new Dictionary<Pixels,ID2D1Bitmap1>();
                _shadowMasks=shadowMasks.Select(row=>row.Select(p=>{
                    if(!uploaded.TryGetValue(p,out var texture))uploaded[p]=texture=Own(Upload(p));return texture;
                }).ToArray()).ToArray();
            }
            InitializationStage="DirectComposition desktop device";
            var compositionIid=typeof(IDCompositionDesktopDevice).GUID;
            Marshal.ThrowExceptionForHR(DCompositionCreateDevice3(_device.NativePointer,ref compositionIid,out IntPtr compositionPointer));
            _composition=Own(new IDCompositionDesktopDevice(compositionPointer));
            _composition.CreateTargetForHwnd(hwnd,true,out _compositionTarget).CheckError();
            Own(_compositionTarget);
            _composition.CreateVisual(out _visual).CheckError();
            Own(_visual);
            InitializationStage="DirectComposition visual3";
            _visual3=Own(_visual.QueryInterface<IDCompositionVisual3>());
            _visual.SetContent(_swap).CheckError();_compositionTarget.SetRoot(_visual).CheckError();_composition.Commit().CheckError();
            _shadow=Own(new Shadow(_dc) {BlurStandardDeviation=20f/3,Optimization=ShadowOptimization.Balanced});
            _glass=Own(_dc.CreateSolidColorBrush(new Color4(0,0,0,0)));
            InitializationStage="draw/present";
            }
            catch { Dispose();throw; }
        }
        private unsafe ID2D1Bitmap1 Upload(Pixels pixels)
        {
            fixed(byte* p=pixels.Data)
                return _dc.CreateBitmap(new SizeI(pixels.Width,pixels.Height),(IntPtr)p,(uint)(pixels.Width*4),
                    new BitmapProperties1(new DPixel(Format.B8G8R8A8_UNorm,DAlpha.Premultiplied),96,96));
        }
        private static Color4 Color(System.Windows.Media.Color c,float opacity=1)=>new(c.R/255f,c.G/255f,c.B/255f,c.A/255f*opacity);
        public void Draw(double raw,RECT from,RECT to,RECT rect,double scale,double tilt,
            double pegWidth,double pegHeight,double pegPad,System.Windows.Media.Color fill,System.Windows.Media.Color edgeTop,System.Windows.Media.Color edgeBottom)
        {
            double k=FlightWindow.FlightEase(raw),chrome=Ease.Smooth(k,.35,1);
            double Lerp(double a,double b)=>a+(b-a)*k;
            float w=(float)(Lerp(from.Width,to.Width)/scale),h=(float)(Lerp(from.Height,to.Height)/scale);
            float cx=(float)((Lerp(from.Left+from.Width/2.0,to.Left+to.Width/2.0)-rect.Left)/scale);
            float top=(float)((Lerp(from.Top,to.Top)-rect.Top)/scale-Math.Sin(Math.PI*k)*30);
            float inset=(float)(Layout.FrameInset*k),radius=(float)(Layout.FrameRadius*k);
            var rounded=new RoundedRectangle(new RF(0,0,w,h),radius,radius);
            // Record the shadow input in local coordinates; Direct2D evaluates its blur on GPU.
            ID2D1CommandList? commands=null;
            if(_shadowMasks is null || w<32 || h<32)
            {
                commands=_dc.CreateCommandList();
                _dc.Target=commands;_dc.Transform=Matrix3x2.Identity;
                _dc.BeginDraw();_dc.Clear(new Color4(0,0,0,0));
                _glass.Color=Color(fill);_dc.FillRoundedRectangle(rounded,_glass);_dc.EndDraw().CheckError();commands.Close().CheckError();
            }
            _dc.Target=_activeTarget ?? _target;
            _dc.Transform=Matrix3x2.Identity;_dc.BeginDraw();_dc.Clear(new Color4(0,0,0,0));
            _dc.Transform=Matrix3x2.CreateRotation((float)(tilt*k*Math.PI/180),new Vector2(w/2,0))*Matrix3x2.CreateTranslation(cx-w/2,top);
            if(chrome>0)
            {
                if(_shadowMasks is not null && w>=32 && h>=32)
                    DrawShadowAtlas(w,h,radius,(float)(chrome*Math.Max(chrome,.35)));
                else if(commands is not null)
                {
                    _shadow.SetInput(0,commands,true);
                    _shadow.Color=new Vector4(0,0,0,(float)(.24*Math.Max(chrome,.35)*chrome));
                    _dc.DrawImage(_shadow,new Vector2(0,5));
                }
                _glass.Color=Color(fill,(float)chrome);_dc.FillRoundedRectangle(rounded,_glass);
            }
            float pr=Math.Max(0,radius-inset);
            using var clip=_factory.CreateRoundedRectangleGeometry(new RoundedRectangle(new RF(inset,inset,w-2*inset,h-2*inset),pr,pr));
            var layer=new LayerParameters1 {ContentBounds=new Vortice.RawRectF(-10000,-10000,10000,10000),
                GeometricMask=clip,MaskAntialiasMode=AntialiasMode.PerPrimitive,MaskTransform=Matrix3x2.Identity,Opacity=1};
            _dc.PushLayer(layer,null!);
            _dc.DrawBitmap(_photo,new Vortice.RawRectF(inset,inset,w-inset,h-inset),1,InterpolationMode.HighQualityCubic,null,null);
            _dc.PopLayer();
            if(chrome>0)
            {
                using var stops=_dc.CreateGradientStopCollection(new[] {new GradientStop(0,Color(edgeTop)),new GradientStop(1,Color(edgeBottom))});
                using var edge=_dc.CreateLinearGradientBrush(new LinearGradientBrushProperties(Vector2.Zero,new Vector2(0,h)),stops);
                edge.Opacity=(float)chrome;_dc.DrawRoundedRectangle(rounded,edge,.75f);
                float px=(float)(w/2-pegWidth/2-pegPad),py=(float)(-(Layout.PinHeight-12)-pegPad);
                _dc.DrawBitmap(_peg,new Vortice.RawRectF(px,py,px+(float)(pegWidth+pegPad*2),py+(float)(pegHeight+pegPad*2)),(float)chrome,InterpolationMode.HighQualityCubic,null,null);
            }
            _dc.EndDraw().CheckError();
            commands?.Dispose();
        }
        private void DrawShadowAtlas(float w,float h,float radius,float opacity)
        {
            const float p=32,c=48;
            float[] xs={-p,-p+c,w+p-c,w+p};
            float[] ys={5-p,5-p+c,5+h+p-c,5+h+p};
            int low=Math.Clamp((int)Math.Floor(radius),0,16),high=Math.Min(low+1,16);
            float fraction=Math.Clamp(radius-low,0,1);
            void Draw(int level,float weight)
            {
                if(weight<=0)return;
                for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                    _dc.DrawBitmap(_shadowMasks![level][y*3+x],new Vortice.RawRectF(xs[x],ys[y],xs[x+1],ys[y+1]),
                        opacity*weight,InterpolationMode.Linear,null,null);
            }
            Draw(low,high==low ? 1 : 1-fraction);if(high!=low)Draw(high,fraction);
        }
        public bool Present()
        {
            var result=_swap.Present(0,PresentFlags.DoNotWait);
            if(result.Code==unchecked((int)0x887A000A))return false; // queue busy, not device loss
            result.CheckError();
            if(result.Code==0x087A0001){Thread.Sleep(8);return false;}
            return true;
        }

        public void BeginRetainedFlight(RECT from,RECT to,RECT rect,double scale,double tilt,
            double pegWidth,double pegHeight,double pegPad,System.Windows.Media.Color fill,
            System.Windows.Media.Color edgeTop,System.Windows.Media.Color edgeBottom)
        {
            // Keep the existing native GPU path, photo upload and cached shadow.
            // Rasterize exactly two immutable layers, then animate their visual
            // transforms on the compositor instead of redrawing a 4MP surface.
            Draw(0,from,to,rect,scale,tilt,pegWidth,pegHeight,pegPad,fill,edgeTop,edgeBottom);
            InitializationStage="retained photo present";
            _swap.Present(0,PresentFlags.None).CheckError();
            var desc=_swap.Description1;
            InitializationStage="retained chrome swapchain";
            var chromeSwap=Own(_dxgiFactory.CreateSwapChainForComposition(_d3d,desc));
            using(var surface=chromeSwap.GetBuffer<IDXGISurface>(0))
            {
                _activeTarget=Own(_dc.CreateBitmapFromDxgiSurface(surface,new BitmapProperties1(
                    new DPixel(Format.B8G8R8A8_UNorm,DAlpha.Premultiplied),_dc.Dpi.Width,_dc.Dpi.Height,
                    BitmapOptions.Target|BitmapOptions.CannotDraw)));
            }
            Draw(1,from,to,rect,scale,tilt,pegWidth,pegHeight,pegPad,fill,edgeTop,edgeBottom);
            _activeTarget=null;
            chromeSwap.Present(0,PresentFlags.None).CheckError();
            InitializationStage="retained chrome visual";
            _composition.CreateVisual(out var chrome).CheckError();Own(chrome);
            chrome.SetContent(chromeSwap).CheckError();
            var chrome3=Own(chrome.QueryInterface<IDCompositionVisual3>());
            _composition.CreateVisual(out var root).CheckError();Own(root);
            _timelineRoot=Own(root.QueryInterface<IDCompositionVisual3>());
            InitializationStage="retained tree";
            _compositionTarget.SetRoot(root).CheckError();
            root.AddVisual(_visual,false,null).CheckError();
            root.AddVisual(chrome,true,_visual).CheckError();
            _compositionTarget.SetRoot(root).CheckError();

            Matrix3x2 Matrix(double raw,bool frame)
            {
                double k=FlightWindow.FlightEase(raw);
                float w=(float)(from.Width+(to.Width-from.Width)*k);
                float h=(float)(from.Height+(to.Height-from.Height)*k);
                float x=(float)(from.Left+from.Width/2.0+(to.Left+to.Width/2.0-from.Left-from.Width/2.0)*k-rect.Left);
                float y=(float)(from.Top+(to.Top-from.Top)*k-rect.Top-Math.Sin(Math.PI*k)*30*scale);
                float inset=(float)(Layout.FrameInset*k*scale);
                float angle=(float)(tilt*k*Math.PI/180);
                if(frame)
                    return Matrix3x2.CreateTranslation(-(to.Left+to.Width/2f-rect.Left),-(to.Top-rect.Top))
                        *Matrix3x2.CreateRotation((float)(-tilt*Math.PI/180))
                        *Matrix3x2.CreateScale(w/to.Width,h/to.Height)
                        *Matrix3x2.CreateRotation(angle)*Matrix3x2.CreateTranslation(x,y);
                return Matrix3x2.CreateTranslation(-(from.Left-rect.Left),-(from.Top-rect.Top))
                    *Matrix3x2.CreateScale((w-2*inset)/from.Width,(h-2*inset)/from.Height)
                    *Matrix3x2.CreateTranslation(-w/2+inset,inset)
                    *Matrix3x2.CreateRotation(angle)*Matrix3x2.CreateTranslation(x,y);
            }
            void AnimateMatrix(IDCompositionVisual visual,bool frame)
            {
                InitializationStage="retained transform";
                _composition.CreateMatrixTransform(out var matrix).CheckError();Own(matrix);
                for(int row=0;row<3;row++)for(int col=0;col<2;col++)
                {
                    var r=row;var c=col;
                    float Element(double t)
                    {
                        var m=Matrix(t,frame);
                        return (r,c) switch {(0,0)=>m.M11,(0,1)=>m.M12,(1,0)=>m.M21,(1,1)=>m.M22,(2,0)=>m.M31,_=>m.M32};
                    }
                    matrix.SetMatrixElement(row,col,Curve(Element)).CheckError();
                }
                visual.SetTransform(matrix).CheckError();
            }
            AnimateMatrix(_visual,false);AnimateMatrix(chrome,true);
            InitializationStage="retained opacity";
            _visual3.SetOpacity(Curve(t=>(float)(1-Ease.Smooth(t,.55,.95)))).CheckError();
            chrome3.SetOpacity(Curve(t=>(float)Ease.Smooth(t,.38,.88))).CheckError();
            InitializationStage="retained commit";
            // Commit only after ShowWindow has completed. Otherwise the
            // independent timeline advances while its host is still hidden.
        }
        public void CommitPreparedFlight()=>_composition.Commit().CheckError();

        public void BeginPanelSlide(double from,double to,double seconds)
        {
            var pixels=_target.PixelSize;
            _dc.Target=_target;_dc.Transform=Matrix3x2.Identity;
            _dc.BeginDraw();_dc.Clear(new Color4(0,0,0,0));
            _dc.DrawBitmap(_photo,new Vortice.RawRectF(0,0,pixels.Width,pixels.Height),1,InterpolationMode.Linear,null,null);
            _dc.EndDraw().CheckError();_swap.Present(0,PresentFlags.None).CheckError();
            _visual.SetOffsetY((float)from).CheckError();
            _composition.Commit().CheckError();
        }
        public void PlayPanelSlide(double from,double to,double seconds)
        {
            _visual.SetOffsetY(Curve(t=>(float)(from+(to-from)*(1-Math.Pow(1-t,3))),seconds)).CheckError();
            _composition.Commit().CheckError();
        }
        private IDCompositionAnimation Curve(Func<double,float> sample,double seconds=FlightWindow.Duration)
        {
            _composition.CreateAnimation(out var animation).CheckError();Own(animation);
            const int segments=64;
            double step=seconds/segments;
            for(int i=0;i<segments;i++)
            {
                float a=sample(i/(double)segments),b=sample((i+1)/(double)segments);
                animation.AddCubic(i*step,a,(float)((b-a)/step),0,0).CheckError();
            }
            animation.End(seconds,sample(1)).CheckError();
            return animation;
        }
        public void SavePixels(string path,uint dpi)
        {
            var size=_target.PixelSize;
            using var cpu=_dc.CreateBitmap(size,new BitmapProperties1(new DPixel(Format.B8G8R8A8_UNorm,DAlpha.Premultiplied),dpi,dpi,BitmapOptions.CpuRead|BitmapOptions.CannotDraw));
            cpu.CopyFromBitmap(_target).CheckError();
            var mapped=cpu.Map(MapOptions.Read);
            try
            {
                int stride=size.Width*4;var bytes=new byte[stride*size.Height];
                for(int y=0;y<size.Height;y++) Marshal.Copy(IntPtr.Add(mapped.Bits,(int)mapped.Pitch*y),bytes,y*stride,stride);
                var image=BitmapSource.Create(size.Width,size.Height,dpi,dpi,PixelFormats.Pbgra32,null,bytes,stride);
                image.Freeze();Core.Thumbnails.SavePng(image,path);
            }
            finally {cpu.Unmap();}
        }
        public void BeginFade(double seconds)
        {
            _composition.CreateAnimation(out var animation).CheckError();
            using(animation)
            {
                animation.AddCubic(0,1,(float)(-1/seconds),0,0).CheckError();
                animation.End(seconds,0).CheckError();
                (_timelineRoot ?? _visual3).SetOpacity(animation).CheckError();_composition.Commit().CheckError();
            }
        }
        public void Dispose()
        {
            if(_dc is not null) _dc.Target=null;
            if(_ready!=IntPtr.Zero){CloseHandle(_ready);_ready=IntPtr.Zero;}
            for(int i=_owned.Count-1;i>=0;i--) { try { _owned[i].Dispose(); } catch { } }
            _owned.Clear();
        }
    }
}
