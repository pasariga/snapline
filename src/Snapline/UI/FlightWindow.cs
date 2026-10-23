using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Snapline.Interop;
using static Snapline.Interop.Native;

namespace Snapline.UI;

/// <summary>
/// The capture lifting off the screen and flying up to the line. It turns
/// into the hanging card on the way: it shrinks, tilts into place and grows
/// its glass frame and clip, so there is nothing left to change on landing.
/// Also the discarded card falling off the line, drawn over the whole screen
/// so it is never cut by the strip.
/// </summary>
public sealed class FlightWindow : Window
{
    // Only the transient flight image changes its scaling algorithm. Saved images,
    // the stationary card, copying, opening and editing retain the upstream path.
    internal static bool FastResampling { get; set; } = false;
    internal static bool FixedPhotoLayer { get; set; } = false;
    internal static bool CachedShadow { get; set; } = false;
    internal static bool UseCompositor { get; set; } = true;
    internal static Action<FlightStatistics>? StatisticsSink { get; set; }
    public const double Duration = 0.44;
    internal const double FadeDuration = 0.075;
    internal static double FlightEase(double raw) => 1 - Math.Pow(1 - Math.Clamp(raw, 0, 1), 3);
    private const double Arc = 30;

    private readonly Canvas _canvas = new();
    private readonly Grid _container = new();
    private readonly Border _glass;
    private readonly Image _photo;
    private readonly Canvas? _photoLayer;
    private readonly ScaleTransform _photoScale = new();
    private readonly TranslateTransform _photoOffset = new();
    private readonly RectangleGeometry _photoClip = new();
    private readonly CachedFlightShadow? _cachedShadow;
    private readonly Border _edge;
    private readonly Grid _clip;
    private readonly RotateTransform _rotate = new();
    private readonly DropShadowEffect _shadow;

    private readonly Rect _from, _to;   // in window DIPs
    private readonly double _tilt;
    private readonly bool _falling;
    private readonly double _duration;
    private readonly Stopwatch _clock = new();
    private readonly Action? _completion;
    private readonly double _scale;
    private FlightProbe? _probe;
    private static readonly List<FlightWindow> Current = new();

    /// <summary>A capture flying from where it was taken to its place on the line. Rects in physical pixels.</summary>
    public static void Fly(BitmapSource image, RECT from, RECT to, double tilt, Display monitor, int seed, Action completion)
    {
        from=PreviewRect(from,monitor,Core.Settings.Current.CompactFlightPreview);
        if (UseCompositor)
        {
            try
            {
                CompositorFlight.Start(image,from,to,tilt,monitor,seed,completion);
                return;
            }
            catch (Exception ex) { Core.Log.Error($"GPU flight setup fallback: 0x{ex.HResult:X8}"); }
        }
        FlyWpf(image,from,to,tilt,monitor,seed,completion);
    }
    internal static RECT PreviewRect(RECT from,Display monitor,bool compact)
    {
        if(!compact || from.Width<monitor.Bounds.Width*.65) return from;
        double ratio=Math.Min(1,monitor.Bounds.Width*.45/from.Width);
        int w=(int)Math.Round(from.Width*ratio),h=(int)Math.Round(from.Height*ratio);
        int x=from.Left+(from.Width-w)/2,y=from.Top+(from.Height-h)/2;
        return new RECT(x,y,x+w,y+h);
    }
    internal static void FlyWpf(BitmapSource image, RECT from, RECT to, double tilt, Display monitor, int seed, Action completion)
    {
        var w = new FlightWindow(image, from, to, tilt, monitor, falling: false, Duration, completion, seed);
        Current.Add(w);
        w.Run();
    }

    /// <summary>A discarded card falling off the line: 520 points down, tilting further, fading, 0.55 s ease in.</summary>
    public static void Fall(BitmapSource image, RECT card, double tilt, Display monitor, int seed)
    {
        var w = new FlightWindow(image, card, card, tilt, monitor, falling: true, 0.55, null, seed);
        Current.Add(w);
        w.Run();
    }

    internal static void SaveVerificationFrame(BitmapSource image, RECT from, RECT to,
        Display monitor, double progress, string path)
    {
        var window = new FlightWindow(image, from, to, -2, monitor, false, Duration, null, 17);
        window.Update(progress);
        var canvas = window._canvas;
        canvas.Width = window.Width; canvas.Height = window.Height;
        canvas.Measure(new Size(window.Width, window.Height));
        canvas.Arrange(new Rect(0, 0, window.Width, window.Height));
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(window._windowRect.Width, window._windowRect.Height,
            monitor.Dpi, monitor.Dpi, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        Core.Thumbnails.SavePng(bitmap, path);
        window.Close();
    }

    private FlightWindow(BitmapSource image, RECT from, RECT to, double tilt, Display monitor, bool falling, double duration, Action? completion, int seed)
    {
        _tilt = tilt;
        _falling = falling;
        _duration = duration;
        _completion = completion;
        _scale = monitor.Scale;

        // The window covers just what the flight needs, with room for the arc and the shadow.
        int pad = (int)(80 * _scale);
        int left = Math.Min(from.Left, to.Left) - pad, top = Math.Min(from.Top, to.Top) - pad;
        int right = Math.Max(from.Right, to.Right) + pad;
        int bottom = Math.Max(from.Bottom, to.Bottom) + pad + (falling ? (int)(560 * _scale) : 0);
        left = Math.Max(left, monitor.Bounds.Left); top = Math.Max(top, monitor.Bounds.Top);
        right = Math.Min(right, monitor.Bounds.Right); bottom = Math.Min(bottom, monitor.Bounds.Bottom);
        _windowRect = new RECT(left, top, right, bottom);

        _from = new Rect((from.Left - left) / _scale, (from.Top - top) / _scale, from.Width / _scale, from.Height / _scale);
        _to = new Rect((to.Left - left) / _scale, (to.Top - top) / _scale, to.Width / _scale, to.Height / _scale);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left / _scale; Top = top / _scale; Width = (right - left) / _scale; Height = (bottom - top) / _scale;
        Content = _canvas;

        _shadow = new DropShadowEffect { Color = Colors.Black, Opacity = 0.24, BlurRadius = 20, ShadowDepth = 5, Direction = 270 };
        _glass = new Border { Background = Theme.Freeze(new SolidColorBrush(Theme.GlassFill)), Effect = _shadow };
        if (!falling && CachedShadow)
        {
            _cachedShadow = new CachedFlightShadow(monitor.Dpi)
            {
                Margin = new Thickness(-CachedFlightShadow.Padding,5-CachedFlightShadow.Padding,
                    -CachedFlightShadow.Padding,-5-CachedFlightShadow.Padding),
            };
            _glass.Effect = null;
        }
        _photo = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_photo, !falling && FastResampling
            ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality);
        if (!falling && FixedPhotoLayer)
        {
            _photo.Width = _from.Width; _photo.Height = _from.Height;
            _photo.CacheMode = new BitmapCache();
            _photoLayer = new Canvas { Width = _from.Width, Height = _from.Height,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Clip = _photoClip };
            _photoLayer.Children.Add(_photo);
            var transform = new TransformGroup();
            transform.Children.Add(_photoScale); transform.Children.Add(_photoOffset);
            _photoLayer.RenderTransform = transform;
            _photoClip.Rect = new Rect(0, 0, _from.Width, _from.Height);
            RenderOptions.SetBitmapScalingMode(_photoLayer, BitmapScalingMode.LowQuality);
        }
        _edge = new Border
        {
            BorderThickness = new Thickness(0.75),
            BorderBrush = Theme.Freeze(new LinearGradientBrush(Theme.GlassEdgeTop, Theme.GlassEdgeBottom, 90)),
        };
        _clip = Pegs.Create(seed: seed, pinned: false);
        _clip.HorizontalAlignment = HorizontalAlignment.Left;
        _clip.VerticalAlignment = VerticalAlignment.Top;
        if (_cachedShadow is not null) _container.Children.Add(_cachedShadow);
        _container.Children.Add(_glass);
        _container.Children.Add((UIElement?)_photoLayer ?? _photo);
        _container.Children.Add(_edge);
        _container.Children.Add(_clip);
        _container.RenderTransform = _rotate;
        _canvas.Children.Add(_container);
    }

    private readonly RECT _windowRect;
    private IntPtr _ownHandle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        _ownHandle=source.Handle;
        FullScreen.Own.Add(source.Handle);
        AddExStyle(source.Handle, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
        SetWindowPos(source.Handle, HWND_TOPMOST, _windowRect.Left, _windowRect.Top, _windowRect.Width, _windowRect.Height, SWP_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering-=OnRendering;
        Current.Remove(this);
        if(_ownHandle!=IntPtr.Zero) FullScreen.Own.Remove(_ownHandle);
        base.OnClosed(e);
    }

    private void Run()
    {
        if (_falling) { Update(1); UpdateFall(0); } else Update(0);
        Show();
        _clock.Start();
        if (StatisticsSink is not null) _probe = new FlightProbe(_duration);
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        _probe?.BeginFrame(e);
        double k = Math.Min(1, _clock.Elapsed.TotalSeconds / _duration);
        if (_falling) UpdateFall(k); else Update(k);
        _probe?.RecordSurface(_windowRect.Width, _windowRect.Height);
        _probe?.EndFrame();
        if (k < 1) return;
        CompositionTarget.Rendering -= OnRendering;
        if (_probe is not null) StatisticsSink?.Invoke(_probe.Finish(_clock.Elapsed.TotalMilliseconds));
        _completion?.Invoke();
        Current.Remove(this);
        if (_falling)
        {
            Close();
            return;
        }
        // The real card fades in underneath; this one fades out over it.
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromSeconds(FadeDuration));
        fade.Completed += (_, _) => { Close(); AnimationBenchmark.FlightClosed(); };
        BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateFall(double raw)
    {
        double ease = raw * raw * raw;
        double angle = _tilt + (_tilt * 7 + 20) * ease;
        double w = _to.Width, h = _to.Height;
        Place(_to.Left + w / 2, _to.Top + 520 * ease, w, h, angle, Layout.FrameRadius, Layout.FrameInset, 1);
        _container.Opacity = 1 - ease;
    }

    private void Update(double raw)
    {
        double k = FlightEase(raw);
        double chrome = Ease.Smooth(k, 0.35, 1);
        double Lerp(double a, double b) => a + (b - a) * k;

        double w = Lerp(_from.Width, _to.Width), h = Lerp(_from.Height, _to.Height);
        double topX = Lerp(_from.Left + _from.Width / 2, _to.Left + _to.Width / 2);
        double topY = Lerp(_from.Top, _to.Top) - Math.Sin(Math.PI * k) * Arc;
        double inset = Layout.FrameInset * k;
        double radius = Lerp(0, Layout.FrameRadius);
        Place(topX, topY, w, h, _tilt * k, radius, inset, chrome);
    }

    private void Place(double topX, double topY, double w, double h, double angle, double radius, double inset, double chrome)
    {
        _container.Width = w; _container.Height = h;
        if (!_falling && FastResampling)
            RenderOptions.SetBitmapScalingMode(_photo, chrome >= 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);
        Canvas.SetLeft(_container, topX - w / 2);
        Canvas.SetTop(_container, topY);
        _rotate.CenterX = w / 2; _rotate.CenterY = 0;
        _rotate.Angle = angle;

        _glass.CornerRadius = new CornerRadius(radius);
        _glass.Opacity = chrome;
        _edge.CornerRadius = new CornerRadius(radius);
        _edge.Opacity = chrome;
        _shadow.Opacity = 0.24 * Math.Max(chrome, 0.35);
        if (_cachedShadow is not null)
        {
            bool fits = w>=32 && h>=32;
            _glass.Effect = fits ? null : _shadow;
            _cachedShadow.Opacity = fits ? chrome*Math.Max(chrome,.35) : 0;
            _cachedShadow.Radius = radius;
        }

        double pr = Math.Max(0, radius - inset);
        if (_photoLayer is not null)
        {
            // The image itself keeps a fixed layout and cached full-resolution pixels.
            // Its parent's render transform and mask reproduce upstream's inset/rounding.
            double sx = Math.Max(.00001, (w-2*inset)/_from.Width);
            double sy = Math.Max(.00001, (h-2*inset)/_from.Height);
            _photoScale.ScaleX = sx; _photoScale.ScaleY = sy;
            _photoOffset.X = inset; _photoOffset.Y = inset;
            _photoClip.RadiusX = pr/sx; _photoClip.RadiusY = pr/sy;
            RenderOptions.SetBitmapScalingMode(_photoLayer,
                chrome >= 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);
        }
        else
        {
            _photo.Margin = new Thickness(inset);
            _photo.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, w - 2 * inset), Math.Max(0, h - 2 * inset)), pr, pr);
        }

        // The clip grips the top edge: 26 points tall, 12 of them over the card.
        _clip.Margin = new Thickness(w / 2 - _clip.Width / 2, -(Layout.PinHeight - 12), 0, 0);
        _clip.Opacity = chrome;
    }

}
