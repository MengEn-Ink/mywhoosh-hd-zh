using System.Drawing.Imaging;
using System.Text.Json;
using Whoosh.Core;

namespace Whoosh.Gui;

internal static class QuickTests
{
    internal static int Run(string dir)
    {
        Directory.CreateDirectory(dir);var cases=new List<string>();var failures=new List<string>();
        void Assert(bool b,string why){if(!b)throw new InvalidOperationException(why);}
        void Test(string name,Action action){try{action();cases.Add(name);}catch(Exception e){failures.Add(name+": "+e.Message);}}
        int pins=0,applies=0;bool pin=false;int w=1280,h=720;
        var backend=new GuiBackend{TestMode=true,
            Snapshot=()=>new(AppInfo.GameVersion,true,"unmanaged",false,null,new WindowState(true,w,h,applies>0,pin,"隔离测试"),"内置 · 校验通过"),
            SetTopmost=v=>{pins++;pin=v;return new WindowState(true,w,h,false,pin,"隔离置顶");},
            ApplyWindow=(width,height,v)=>{applies++;w=width;h=height;return new WindowState(true,w,h,true,pin,"隔离窗口");}};
        using var main=new MainForm(backend,capture:true);main.PrepareForCapture(1280,860);
        using var quick=new QuickForm(main);main.StateChanged+=quick.RefreshFromMain;quick.Show();Application.DoEvents();
        void Wait(){var end=DateTime.UtcNow.AddSeconds(8);while(main.IsWorking&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}Application.DoEvents();Assert(!main.IsWorking,"timeout");quick.RefreshFromMain();}
        Test("float shows actual status not pin preference",()=>{Assert(quick.StatusText.Contains("未启动"),"actual unavailable mislabeled");quick.PinButton.PerformClick();Wait();Assert(pins==1&&pin&&quick.StatusText.Contains("等待游戏启动"),"preference presented as actual");});
        Test("float preset draft and explicit apply share core dispatch",()=>{quick.SizeChoice.SelectedIndex=1;Assert(main.View.WinW==1600&&applies==0,"preset wrote immediately");quick.ApplyButton.PerformClick();Wait();Assert(applies==1&&w==1600&&h==900,"apply did not dispatch");Assert(!main.View.BackupAvailable&&main.View.WindowBackupAvailable,"window backup became Chinese backup");});
        Test("float opens console and closes to tray",()=>{bool console=false,hidden=false;quick.ConsoleRequested+=()=>console=true;quick.HideRequested+=()=>{hidden=true;quick.Hide();};quick.ConsoleButton.PerformClick();Assert(console,"console missing");quick.Close();Assert(hidden&&!quick.IsDisposed,"float close quits process");quick.Show();});
        Test("main close can keep tray lifecycle alive",()=>{main.HideToTray=()=>{main.Hide();return true;};main.Close();Assert(!main.IsDisposed&&!main.Visible,"main disposed on hide");main.Show();main.HideToTray=null;});
        Test("screen changes clamp floating position",()=>{var p=QuickPreferences.Fit(new Point(9999,-999),new Size(340,244),[new Rectangle(0,0,1280,720)]);Assert(p.X>=0&&p.X<=940&&p.Y>=0&&p.Y<=476,"float offscreen");var n=QuickPreferences.Fit(new Point(-1700,20),new Size(340,244),[new Rectangle(-1920,0,1920,1080),new Rectangle(0,0,1280,720)]);Assert(n.X==-1700,"secondary screen position lost");});
        Test("floating preferences persist only to selected temp folder",()=>{string home=Path.Combine(dir,"prefs");var pref=new QuickPreferences{X=20,Y=40,ShowFloating=false};pref.Save(home);var loaded=QuickPreferences.Load(home);Assert(loaded.X==20&&loaded.Y==40&&!loaded.ShowFloating,"prefs mismatch");});
        Test("tray menu and wake signals share one desktop lifecycle",()=>{
            using var context=new DesktopContext(true,backend,Path.Combine(dir,"desktop-context"));
            var loaded=DateTime.UtcNow.AddSeconds(5);while(context.ConsoleForm.IsWorking&&DateTime.UtcNow<loaded){Application.DoEvents();Thread.Sleep(5);}
            Assert(!context.ConsoleForm.IsWorking,"context snapshot timeout");
            Assert(context.FloatingForm.Visible&&!context.ConsoleForm.Visible,"floating startup wrong");
            using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,context.SignalPrefix+".OpenConsole"))signal.Set();
            var end=DateTime.UtcNow.AddSeconds(3);while(!context.ConsoleForm.Visible&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(20);}Assert(context.ConsoleForm.Visible,"existing session not awakened");
            context.ConsoleForm.Close();Assert(!context.ConsoleForm.IsDisposed&&!context.ConsoleForm.Visible,"tray lifetime lost");
            ((ToolStripMenuItem)context.Menu.Items[0]).PerformClick();Assert(context.ConsoleForm.Visible,"tray open missing");
            ((ToolStripMenuItem)context.Menu.Items[context.Menu.Items.Count-1]).PerformClick();Assert(context.Exiting&&context.ConsoleForm.IsDisposed,"tray exit missing");
        });
        Test("application icon valid Windows ICO",()=>{using var icon=AppIcon.Load();Assert(icon.Width>0&&icon.Height>0,"icon invalid");});
        using(var bmp=new Bitmap(quick.Width,quick.Height)){quick.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(dir,"快捷浮窗.png"),ImageFormat.Png);}
        var report=new{pass=failures.Count==0,passed=cases.Count,failed=failures.Count,cases,failures,mode="isolated backend; no real game/AppData/tray creation"};
        File.WriteAllText(Path.Combine(dir,"quick-selftest.json"),JsonSerializer.Serialize(report,AppInfo.JsonOptions));return failures.Count==0?0:1;
    }
}
