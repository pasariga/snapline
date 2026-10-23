using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Snapline.UI;

/// <summary>Same Gaussian shadow profile, nine-sliced from tiny cached masks instead of
/// rerunning a blur shader over a changing multi-megapixel surface on every frame.</summary>
internal sealed class CachedFlightShadow : FrameworkElement
{
    internal const double Padding = 32;
    private const double Corner = Padding + Layout.FrameRadius;
    private readonly BitmapSource[][] _masks;
    private double _radius;
    private static readonly Dictionary<(uint dpi,byte alpha),BitmapSource[][]> Cache = new();

    public CachedFlightShadow(uint dpi)
    {
        IsHitTestVisible=false;
        var key=(dpi,Theme.GlassFill.A);
        if(!Cache.TryGetValue(key,out var masks)) Cache[key]=masks=Bake(dpi);
        _masks=masks;
    }
    internal static BitmapSource[][] GetMasks(uint dpi)
    {
        var key=(dpi,Theme.GlassFill.A);
        if(!Cache.TryGetValue(key,out var masks))Cache[key]=masks=Bake(dpi);
        return masks;
    }
    public double Radius { set { if(_radius==value)return; _radius=value; InvalidateVisual(); } }

    private static BitmapSource[][] Bake(uint dpi)
    {
        double scale=dpi/96.0;
        int pixels=(int)Math.Round((100+Padding*2)*scale);
        int corner=(int)Math.Round(Corner*scale);
        var result=new BitmapSource[(int)Layout.FrameRadius+1][];
        for(int radius=0;radius<result.Length;radius++)
        {
            var visual=new DrawingVisual { Opacity=.24*Theme.GlassFill.ScA,
                Effect=new BlurEffect { Radius=20,KernelType=KernelType.Gaussian } };
            using(var dc=visual.RenderOpen())
                dc.DrawRoundedRectangle(Brushes.Black,null,new Rect(Padding,Padding,100,100),radius,radius);
            var bitmap=new RenderTargetBitmap(pixels,pixels,dpi,dpi,PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            int[] cuts={0,corner,pixels-corner,pixels};
            result[radius]=new BitmapSource[9];
            for(int y=0;y<3;y++) for(int x=0;x<3;x++)
            {
                var piece=new CroppedBitmap(bitmap,new Int32Rect(cuts[x],cuts[y],cuts[x+1]-cuts[x],cuts[y+1]-cuts[y]));
                piece.Freeze(); result[radius][y*3+x]=piece;
            }
        }
        return result;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(ActualWidth<=0 || ActualHeight<=0)return;
        int low=Math.Clamp((int)Math.Floor(_radius),0,_masks.Length-1);
        int high=Math.Min(low+1,_masks.Length-1);
        double fraction=Math.Clamp(_radius-low,0,1);
        Draw(dc,_masks[low],high==low ? 1 : 1-fraction);
        if(high!=low && fraction>0) Draw(dc,_masks[high],fraction);
    }
    private void Draw(DrawingContext dc,BitmapSource[] pieces,double opacity)
    {
        if(opacity<=0)return;
        double cx=Math.Min(Corner,ActualWidth/2),cy=Math.Min(Corner,ActualHeight/2);
        double[] xs={0,cx,ActualWidth-cx,ActualWidth};
        double[] ys={0,cy,ActualHeight-cy,ActualHeight};
        dc.PushOpacity(opacity);
        for(int y=0;y<3;y++) for(int x=0;x<3;x++)
        {
            double w=xs[x+1]-xs[x],h=ys[y+1]-ys[y];
            if(w>0 && h>0) dc.DrawImage(pieces[y*3+x],new Rect(xs[x],ys[y],w,h));
        }
        dc.Pop();
    }
}
