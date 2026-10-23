using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapline.Core;
using Snapline.Interop;
using static Snapline.Interop.Native;
using Line=Snapline.Core.Line;

namespace Snapline.UI;

/// <summary>Opt-in synthetic tests; no desktop capture, input injection or personal files.</summary>
internal static class BehaviorTests
{
    public static bool Active {get;private set;}
    private static string _mode="",_output="";
    private static readonly List<object> Checks=new();
    public static int Run(string mode,string output)
    {
        Active=true;_mode=mode;_output=Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        Settings.ReadOnly=true;Settings.Current.SoundOn=false;Settings.Current.Welcomed=true;
        Settings.Current.Pegged.Clear();Settings.Current.OwnedCaptures.Clear();
        Settings.Current.CaptureSessionKey=CaptureSession.CurrentKey;Settings.Current.CaptureSessionDay=CaptureSession.Today;
        var app=new App {ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();return app.Run();
    }
    public static async void Start(App app)
    {
        try
        {
            if(_mode=="selftest") await SelfTest();else if(_mode=="clear-dialog") await ClearDialogTest();else await PanelBench();
            File.WriteAllText(_output,JsonSerializer.Serialize(new {At=DateTimeOffset.Now,Mode=_mode,Passed=true,Checks},new JsonSerializerOptions {WriteIndented=true}));
            app.Shutdown(0);
        }
        catch(Exception ex)
        {
            File.WriteAllText(_output,JsonSerializer.Serialize(new {At=DateTimeOffset.Now,Mode=_mode,Passed=false,Error=ex.ToString(),Checks},new JsonSerializerOptions {WriteIndented=true}));
            app.Shutdown(1);
        }
    }
    private static void Check(string name,bool passed)
    {
        Checks.Add(new {Name=name,Passed=passed});
        if(!passed) throw new InvalidOperationException(name);
    }
    internal static string ImageFile(string name,int width=640,int height=400)
    {
        string path=Path.Combine(Path.GetDirectoryName(_output)!,name+".png");
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Colors.SteelBlue,Colors.LightSkyBlue,35),null,new Rect(0,0,width,height));
            for(int x=10;x<width;x+=45) dc.DrawLine(new Pen(Brushes.White,1),new Point(x,0),new Point(x,height));
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        Thumbnails.SavePng(bitmap,path);return path;
    }
    private static async Task SelfTest()
    {
        var gate=new AutoHideGate();
        Check("inside never hides",!gate.ShouldHide(0,true,false,2.5)&&!gate.ShouldHide(100,true,false,2.5));
        Check("leave starts deadline",!gate.ShouldHide(101,false,false,2.5));
        Check("2.4 seconds remains open",!gate.ShouldHide(103.4,false,false,2.5));
        Check("reentry cancels",!gate.ShouldHide(103.45,true,false,2.5));
        Check("second leave starts fresh",!gate.ShouldHide(104,false,false,2.5)&&!gate.ShouldHide(106.4,false,false,2.5));
        Check("hide after2.5s",gate.ShouldHide(106.6,false,false,2.5));
        Check("interaction cancels even far outside",!gate.ShouldHide(110,false,true,2.5)&&!gate.ShouldHide(200,false,true,2.5));
        Check("post interaction fresh delay",!gate.ShouldHide(201,false,false,2.5)&&gate.ShouldHide(204,false,false,2.5));

        string fixture=ImageFile("managed-capture");string originalHash=CaptureSession.Hash(fixture);
        var s=new Settings {CaptureSessionKey="boot-A",CaptureSessionDay="2026-10-16"};s.Pegged.Add(fixture);
        Check("same session same day restores",!CaptureSession.Refresh(s,"boot-A","2026-10-16",_=>throw new Exception("Must not touch files"))&&s.Pegged.Count==1);
        Check("new boot clears hanging state",CaptureSession.Refresh(s,"boot-B","2026-10-16",_=>false)&&s.Pegged.Count==0);
        Check("external original survives reboot",CaptureSession.Hash(fixture)==originalHash);
        s.Pegged.Add(fixture);
        Check("next day clears state",CaptureSession.Refresh(s,"boot-B","2026-10-17",_=>false)&&s.Pegged.Count==0);
        var owned=ImageFile("owned-expiring");
        s.OwnedCaptures[owned]=new OwnedCapture {Folder=Path.GetDirectoryName(owned)!,Sha256=CaptureSession.Hash(owned),SessionKey="boot-B",Day="2026-10-17"};
        int recycled=0;
        CaptureSession.Refresh(s,"boot-C","2026-10-17",p=>{File.Move(p,p+".recycled-test",true);recycled++;return true;});
        Check("owned verified capture alone recycled",recycled==1&&!File.Exists(owned)&&File.Exists(owned+".recycled-test")&&File.Exists(fixture));
        var changed=ImageFile("user-replaced");var stamp=new OwnedCapture {Folder=Path.GetDirectoryName(changed)!,Sha256=CaptureSession.Hash(changed)};
        File.AppendAllText(changed,"changed by user");
        Check("replaced content is not eligible",!CaptureSession.CanRecycle(changed,stamp));
        Check("wrong folder is not eligible",!CaptureSession.CanRecycle(fixture,new OwnedCapture {Folder=Path.GetTempPath(),Sha256=originalHash}));
        var fresh=ImageFile("new-day-arrival");
        s.OwnedCaptures[fresh]=new OwnedCapture {Folder=Path.GetDirectoryName(fresh)!,Sha256=CaptureSession.Hash(fresh),SessionKey="boot-C",Day="2026-10-18"};
        CaptureSession.Refresh(s,"boot-C","2026-10-18",p=>{File.Move(p,p+".recycled-test",true);return true;});
        Check("capture arriving at midnight is retained",File.Exists(fresh));

        var b=Displays.Primary().Bounds;
        Check("captioned work-area filling app not fullscreen",!FullScreen.Classify(new RECT(-12,-12,b.Right+12,b.Bottom+12),b,0x14C00000,false,QUERY_USER_NOTIFICATION_STATE.Busy));
        Check("normal maximized app not fullscreen",!FullScreen.Classify(b,b,0x14C00000,true,QUERY_USER_NOTIFICATION_STATE.Busy));
        Check("ordinary window ignores global shell hint",!FullScreen.Classify(new RECT(20,30,800,900),b,0x14C00000,false,QUERY_USER_NOTIFICATION_STATE.RunningD3dFullScreen));
        Check("borderless fullscreen stays suppressed",FullScreen.Classify(b,b,0x90000000,false,QUERY_USER_NOTIFICATION_STATE.RunningD3dFullScreen));

        using var controller=new AppController(testing:true);
        var line=controller.TestLine;
        for(int i=0;i<12;i++){line.MaxItems=32;line.Hang(ImageFile("delete-sequence-"+i),quietly:true);}
        controller.TestCanvas.UpdateLayout();
        await Task.Delay(850);
        var r=controller.TestFrame;var inside=new POINT(r.Left+24,r.Top+90);var outside=new POINT(r.Left+400,r.Bottom+200);
        Thumbnails.SavePng(controller.TestCanvas.SnapshotForSlide(Displays.Primary().Dpi,true),
            Path.Combine(Path.GetDirectoryName(_output)!,"panel-synthetic-check.png"));
        var now=DateTime.Now.AddSeconds(10);double tick=0;
        controller.TickAt(inside,false,now,tick);
        Check("line starts revealed",controller.TestRevealed);
        var ids=line.Items.Where(x=>!x.Falling).Take(10).Select(x=>x.Id).ToList();
        Check("ten delete fixtures exist",ids.Count==10);
        var originalFiles=line.Items.Select(x=>x.Path).ToList();
        foreach(var id in ids)
        {
            line.Discard(id);
            tick+=.1;controller.TickAt(inside,true,now.AddSeconds(tick),tick);
            Check("delete stays revealed "+id,controller.TestRevealed);
            await Task.Delay(90);
            controller.TickAt(inside,false,now.AddSeconds(tick),tick);
        }
        Check("ten removals leave two",line.LiveCount==2);
        Check("discard external screenshots never deletes files",originalFiles.All(File.Exists));
        controller.TickAt(outside,false,now.AddSeconds(2),2);
        controller.TickAt(outside,false,now.AddSeconds(4.4),4.4);
        Check("controller still open before deadline",controller.TestRevealed);
        controller.TickAt(inside,false,now.AddSeconds(4.45),4.45);
        controller.TickAt(outside,false,now.AddSeconds(5),5);
        line.MenuOpen=true;controller.TickAt(outside,false,now.AddSeconds(20),20);
        Check("context menu prevents hide",controller.TestRevealed);
        line.MenuOpen=false;
        var remaining=line.Items.First(x=>!x.Falling).Id;
        line.PressedId=remaining;
        controller.TickAt(outside,true,now.AddSeconds(20.1),20.1);
        Check("held screenshot prevents outside hide",controller.TestRevealed);
        line.PressedId=null;line.DraggingId=remaining;
        controller.TickAt(outside,false,now.AddSeconds(20.2),20.2);
        Check("dragged screenshot prevents outside hide",controller.TestRevealed);
        line.DraggingId=null;
        line.MenuOpen=false;controller.TickAt(outside,false,now.AddSeconds(21),21);
        controller.TickAt(outside,false,now.AddSeconds(23.6),23.6);
        Check("controller hides after fresh leave delay",!controller.TestRevealed);
        controller.Toggle();controller.TickAt(inside,false,now.AddSeconds(24),24);
        int confirmations=0;
        bool cancelled=controller.ConfirmClear(()=>{
            confirmations++;
            Check("nested clear is rejected",!controller.ConfirmClear(()=>{confirmations++;return true;}));
            return false;
        });
        Check("cancel clear preserves captures",!cancelled&&line.LiveCount==2&&confirmations==1);
        int notifications=0;line.ItemsChanged+=()=>notifications++;
        Check("confirmed clear removes line items",controller.ConfirmClear(()=>true));
        controller.TickAt(inside,true,now.AddSeconds(24.1),24.1);
        Check("clear keeps line open under pointer",controller.TestRevealed);
        Check("clear is one layout notification",notifications==1&&line.LiveCount==0);
        Check("clear preserves every external file",originalFiles.All(File.Exists));
        var panel=Window.GetWindow(controller.TestCanvas)!;
        for(int i=0;i<4;i++)
        {
            controller.TestCanvas.Revealed=false;await Task.Delay(85);
            controller.TestCanvas.Revealed=true;await Task.Delay(85);
        }
        await Task.Delay(850);
        Check("rapid reentry finishes visible",controller.TestCanvas.Revealed && panel.Opacity==1
            && Math.Abs(((TranslateTransform)controller.TestCanvas.RenderTransform).Y)<.01);
        controller.TestCanvas.Revealed=false;await Task.Delay(650);
        Check("native hide restores interactive window opacity",!controller.TestCanvas.Revealed && panel.Opacity==1
            && ((TranslateTransform)controller.TestCanvas.RenderTransform).Y< -100);
        controller.TestCanvas.Revealed=true;await Task.Delay(650);
        Check("native reveal returns to exact resting position",controller.TestCanvas.Revealed && panel.Opacity==1
            && Math.Abs(((TranslateTransform)controller.TestCanvas.RenderTransform).Y)<.01);
        var full=ImageFile("full-quality",2560,1600);var fullHash=CaptureSession.Hash(full);
        Check("full resolution retained",Thumbnails.Load(full,int.MaxValue,1) is {PixelWidth:2560,PixelHeight:1600});
        Check("copy bytes unchanged",Thumbnails.PngBytes(full)!.SequenceEqual(File.ReadAllBytes(full))&&CaptureSession.Hash(full)==fullHash);
    }
    private static async Task ClearDialogTest()
    {
        using var controller=new AppController(testing:true);
        var path=ImageFile("clear-dialog-fixture");
        var hash=CaptureSession.Hash(path);
        controller.TestLine.Hang(path,quietly:true);
        await Task.Delay(1000);
        controller.TestRequestClearAll();
        Check("owned dialog returned a valid clear or cancel result",controller.TestLine.LiveCount is 0 or 1);
        Checks.Add(new {DialogChoice=controller.TestLine.LiveCount==0 ? "confirmed" : "cancelled"});
        Check("dialog never deletes original",File.Exists(path)&&CaptureSession.Hash(path)==hash);
    }
    private static async Task PanelBench()
    {
        PeggedControl.OptimizedRendering=_mode!="panel-baseline";
        CompositorFlight.PanelStatisticsSink=s=>Checks.Add(new {NativePanel=s});
        using var controller=new AppController(testing:true);
        for(int i=0;i<12;i++){controller.TestLine.MaxItems=32;controller.TestLine.Hang(ImageFile("panel-"+i),quietly:true);}
        await Task.Delay(1600);
        var frame=controller.TestFrame;
        var inside=new POINT(frame.Left+20,frame.Top+80);var outside=new POINT(frame.Left+400,frame.Bottom+200);
        for(int round=0;round<8;round++)
        {
            controller.TickAt(inside,false,DateTime.Now.AddSeconds(10),round*10);
            controller.TestCanvas.Revealed=false;await Task.Delay(400);
            var intervals=new List<double>();TimeSpan? previous=null;
            void Render(object? _,EventArgs e){if(e is RenderingEventArgs a){if(previous is {} p&&a.RenderingTime!=p)intervals.Add((a.RenderingTime-p).TotalMilliseconds);previous=a.RenderingTime;}}
            CompositionTarget.Rendering+=Render;
            double start=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
            var timer=Stopwatch.StartNew();
            controller.TestCanvas.Revealed=true;
            if(_mode!="panel-transition") foreach(var card in controller.TestCanvas.Cards)card.Breeze(now:true);
            await Task.Delay(900);
            controller.TestCanvas.Revealed=false;await Task.Delay(350);
            CompositionTarget.Rendering-=Render;
            intervals.Sort();
            double Q(double q)=>intervals.Count==0?0:intervals[(int)Math.Ceiling((intervals.Count-1)*q)];
            Checks.Add(new {Round=round,Metric="WPF render opportunities, not displayed-frame FPS",Cards=12,
                Hz=intervals.Count>0?1000/intervals.Average():0,MedianGapMs=Q(.5),P95GapMs=Q(.95),MaxGapMs=Q(1),
                LongGapsOver20Ms=intervals.Count(g=>g>20),ProcessCpuMs=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds-start,
                WallMs=timer.Elapsed.TotalMilliseconds,WorkingSet=Process.GetCurrentProcess().WorkingSet64});
        }
        CompositorFlight.PanelStatisticsSink=null;
    }

    public static int SessionFixture(string mode,string outputFolder)
    {
        _output=Path.Combine(Path.GetFullPath(outputFolder),"fixture-"+mode+".json");
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        Settings.ReadOnly=false;
        bool passed;
        if(mode=="write")
        {
            Settings.Current.Pegged.Clear();Settings.Current.PinnedPaths.Clear();Settings.Current.Spots.Clear();
            Settings.Current.CaptureSessionKey=CaptureSession.CurrentKey;Settings.Current.CaptureSessionDay=CaptureSession.Today;
            Settings.Current.SoundOn=false;
            var line=new Line();line.MaxItems=8;
            var first=line.Hang(ImageFile("restart-first"),quietly:true)!.Value;
            line.TogglePin(first);line.SetSpot(first,.32,true);
            line.Hang(ImageFile("restart-second"),quietly:true);
            passed=line.LiveCount==2;
        }
        else
        {
            bool expired=CaptureSession.Refresh(Settings.Current,CaptureSession.CurrentKey,CaptureSession.Today,_=>throw new Exception("Same session must not recycle"));
            var line=new Line();
            passed=!expired && line.LiveCount==2 && line.Items.Count(x=>x.Pinned)==1
                && line.Items.Any(x=>x.Spot is {} spot&&Math.Abs(spot-.32)<.0001);
        }
        File.WriteAllText(_output,JsonSerializer.Serialize(new {At=DateTimeOffset.Now,Mode=mode,Passed=passed,
            Process=Environment.ProcessId,CaptureCount=Settings.Current.Pegged.Count,SameSessionKey=CaptureSession.CurrentKey==Settings.Current.CaptureSessionKey}));
        return passed?0:1;
    }
}
