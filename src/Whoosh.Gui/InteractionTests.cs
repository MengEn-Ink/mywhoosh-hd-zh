using System.Drawing;
using System.Text.Json;
using Whoosh.Core;

namespace Whoosh.Gui;

internal static class InteractionTests
{
    internal static object Run()
    {
        var cases=new List<string>();var errors=new List<string>();
        void Test(string name,Action action){try{action();cases.Add(name);}catch(Exception ex){errors.Add(name+": "+ex.Message);}}
        void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        int checks=0,installs=0,recoveries=0,windowRecoveries=0;string journal="unmanaged";
        bool windowInterrupted=false;
        bool compatible=true;string version="6.2.3.0";
        var backend=new GuiBackend{
            TestMode=true,
            Snapshot=()=>new UiSnapshot(version,compatible,journal,journal=="installed",null,new WindowState(true,1280,720,false,false,windowInterrupted?"窗口批次中断，需先恢复。":"配置"),"内置 · 校验通过"),
            RecoverInstall=()=>{recoveries++;journal="installed";return new OperationResult("recovered",0,0,"已提交汉化保留");},
            RecoverWindow=()=>{windowRecoveries++;windowInterrupted=false;return new WindowState(true,1280,720,true,false,"中断已处理");},
            Check=()=>{checks++;return new{Supported=true,GameVersion="6.2.3.0",Scanned=10,Matched=10,AlreadyChinese=7,Unmatched=0,PendingFiles=3};},
            Install=()=>{installs++;journal="installed";return new OperationResult("installed",3,0,"测试后端，不写游戏");}
        };
        using var form=new MainForm(backend,capture:true);form.PrepareForCapture(1280,860);
        void Draw(){using var b=new Bitmap(form.View.Width,form.View.Height);form.View.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));}
        void Wait(){var end=DateTime.UtcNow.AddSeconds(10);while(form.IsWorking&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}Application.DoEvents();Assert(!form.IsWorking,"action timed out");Draw();}
        Test("first button is check and never installs",()=>{Draw();Assert(form.View.PrimaryChinese==("检查中文状态","check"),"wrong initial label");Assert(form.View.ClickActionForTest("check"),"check hit missing");Wait();Assert(checks==1&&installs==0,"check caused writes");Assert(form.View.Already==7&&form.View.Matched==10,"stats merged");Assert(form.View.RenderedText.Contains("7 条")&&!form.View.RenderedText.Contains("10 条"),"matched presented as Chinese");});
        Test("checked pending files expose explicit install",()=>{Assert(form.View.PrimaryChinese==("安装 / 更新中文","install"),"install affordance missing");Assert(form.View.ClickActionForTest("install"),"install hit missing");Wait();Assert(installs==1&&!form.View.HasStats,"installation not separate or stale stats");});
        Test("unmanaged is not presented as definitely uninstalled",()=>{journal="unmanaged";form.RefreshForTest();Assert(form.View.Status=="无本工具记录"&&!form.View.BackupAvailable,"unmanaged mislabelled");});
        Test("game version and actual pin are independent of preference",()=>{form.View.Topmost=true;form.View.ActualTopmost=null;Assert(form.View.TopmostText=="等待游戏启动","request misreported actual");form.View.ActualTopmost=false;Assert(form.View.TopmostText=="守护待重试","false actual hidden");form.View.ActualTopmost=true;Assert(form.View.TopmostText=="已置顶","true actual wrong");form.View.GameVersion="99.0.0.0";Draw();Assert(form.View.RenderedText.Contains("99.0.0.0"),"game version hardcoded");});
        Test("size selection stays draft and is not a window write",()=>{form.View.Page=CockpitPage.Window;Draw();Assert(form.View.ClickActionForTest("size:1600:900"),"preset hit missing");Wait();Assert(form.View.WinW==1600&&form.View.WinH==900&&installs==1,"draft disappeared");});
        Test("busy state blocks mouse actions",()=>{int old=checks;form.View.Page=CockpitPage.Chinese;form.View.Busy=true;Draw();Assert(!form.View.Hits.Any(h=>h.Action=="install"||h.Action=="check"),"busy action enabled");form.View.Busy=false;Assert(old==checks,"busy click performed");});
        Test("all navigation buttons hit real dispatch",()=>{for(int i=0;i<4;i++){Draw();Assert(form.View.ClickActionForTest("nav:"+i),"navigation missing");Assert((int)form.View.Page==i,"wrong page");}});
        Test("unsupported version fixture has no false backup",()=>{compatible=false;version="99.0.0.0";journal="unknown";form.RefreshForTest();Draw();Assert(form.View.GameVersion==version&&!form.View.Supported&&!form.View.BackupAvailable,"unsupported state stale");form.View.HasStats=true;form.View.Pending=3;Assert(form.View.PrimaryChinese.Action=="check","unsupported write enabled");});
        Test("help entry does not invoke file mutation",()=>{int before=installs;Draw();Assert(form.View.ClickActionForTest("help"),"help link missing");Assert(installs==before,"help mutates");});
        Test("pending install offers recovery without reinstall or full restore",()=>{
            compatible=true;version="6.2.3.0";journal="prepared";form.RefreshForTest();form.View.Page=CockpitPage.Overview;Draw();
            Assert(form.View.PrimaryChinese.Action=="recover-install"&&form.View.Notice.Contains("中断"),"interruption guidance absent");
            int before=installs;Assert(form.View.ClickActionForTest("recover-install"),"recovery hit absent");Wait();
            Assert(recoveries==1&&installs==before&&!form.View.InstallInterrupted,"recovery was install or stale");
        });
        Test("pending window offers independent recovery",()=>{
            windowInterrupted=true;form.RefreshForTest();form.View.Page=CockpitPage.Window;Draw();
            Assert(form.View.ClickActionForTest("recover-window"),"window recovery hit absent");Wait();
            Assert(windowRecoveries==1&&!form.View.WindowInterrupted,"window recovery stale");
        });
        Test("full long message stays available without file writes",()=>{
            form.View.Notice=new string('长',600);form.View.Log.Add(new string('记',1000));Draw();
            using var details=form.DetailsText();
            Assert(details.ReadOnly&&details.Multiline&&details.ScrollBars==ScrollBars.Both&&details.Text.Contains(form.View.Notice)&&details.Text.Contains(form.View.Log.Last()),"full details are not scrollable/read-only");
            Assert(form.View.ClickActionForTest("show-details")&&form.View.Notice.Length==600&&form.View.Log.Last().Length==1000,"details truncated in model");
        });
        Test("closing during an active operation waits for completion",()=>{
            using var slow=new MainForm(new GuiBackend{TestMode=true,Snapshot=backend.Snapshot,Check=()=>{Thread.Sleep(250);return new{Supported=true,GameVersion="6.2.3.0",Scanned=0,Matched=0,AlreadyChinese=0,Unmatched=0,PendingFiles=0};}},capture:true);
            slow.PrepareForCapture(1280,860);using(var bmp=new Bitmap(slow.View.Width,slow.View.Height))slow.View.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));
            Assert(slow.View.ClickActionForTest("check")&&slow.IsWorking,"slow action absent");slow.Close();
            Assert(!slow.IsDisposed&&slow.Visible,"closed while task active");
            var end=DateTime.UtcNow.AddSeconds(5);while(slow.IsWorking&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}Assert(!slow.IsWorking,"slow test timed out");slow.Close();Assert(slow.IsDisposed,"close blocked after completion");
        });
        return new{pass=errors.Count==0,passed=cases.Count,failed=errors.Count,cases,errors,backend="isolated fake operations; actual OnMouseUp and OnAction dispatch"};
    }
}
