using System.Diagnostics;
using System.Text.Json;
using Whoosh.Core;

namespace Whoosh.Gui;
internal static class PerformanceTests
{
    internal static int ReadOnly(string dir)
    {
        if(Directory.Exists(dir)||File.Exists(dir))throw new IOException("只读响应性测试使用新输出目录。");
        Directory.CreateDirectory(dir);
        // TestMode blocks prefs, timers, log writes, game pinning and actual tray creation.
        int ticks=0;double maxGap=0;var watch=Stopwatch.StartNew();double previous=0;
        using var heartbeat=new System.Windows.Forms.Timer{Interval=25};
        heartbeat.Tick+=(_,_)=>{double now=watch.Elapsed.TotalMilliseconds;maxGap=Math.Max(maxGap,now-previous);previous=now;ticks++;};
        using var form=new MainForm(new GuiBackend{TestMode=true},capture:true);
        heartbeat.Start();form.Show();double showMs=watch.Elapsed.TotalMilliseconds;
        while(form.IsWorking&&watch.Elapsed.TotalSeconds<45){Application.DoEvents();Thread.Sleep(5);}
        bool pass=!form.IsWorking&&form.View.Supported&&ticks>=5&&showMs<1000;
        var report=new{pass,showMs,snapshotMs=watch.Elapsed.TotalMilliseconds,uiHeartbeatTicks=ticks,maxHeartbeatGapMs=maxGap,supported=form.View.Supported,scope="actual current-user package/payload/backup reads, TestMode disables prefs/log/game writes and tray; no game launch"};
        File.WriteAllText(Path.Combine(dir,"readonly-responsiveness.json"),JsonSerializer.Serialize(report,AppInfo.JsonOptions));return pass?0:1;
    }
    internal static int Run(string dir)
    {
        if(Directory.Exists(dir)||File.Exists(dir))throw new IOException("性能测试使用新隔离目录。");
        Directory.CreateDirectory(dir);
        var cases=new List<string>();var failures=new List<string>();var metrics=new Dictionary<string,double>();
        void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        void Test(string name,Action action){try{action();cases.Add(name);}catch(Exception ex){failures.Add(name+": "+ex.Message);}}
        void PumpUntil(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(12);while(!done()&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(5);}Assert(done(),"timeout");}
        UiSnapshot Snapshot()=>new(AppInfo.GameVersion,true,"unmanaged",false,null,new(true,1280,720,false,false,"隔离"),"内置 · 校验通过");
        Test("slow initial snapshot keeps UI heartbeat responsive",()=>
        {
            using var gate=new ManualResetEventSlim();
            using var form=new MainForm(new GuiBackend{TestMode=true,Snapshot=()=>{gate.Wait(TimeSpan.FromSeconds(5));return Snapshot();}},capture:true);
            int ticks=0;using var heartbeat=new System.Windows.Forms.Timer{Interval=20};heartbeat.Tick+=(_,_)=>ticks++;heartbeat.Start();
            var watch=Stopwatch.StartNew();form.Show();double showMs=watch.Elapsed.TotalMilliseconds;
            try{var end=DateTime.UtcNow.AddMilliseconds(250);while(DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(5);}Assert(ticks>=5,"slow backend blocked UI timer");Assert(form.IsWorking,"snapshot not tracked as working");metrics["initialShowMs"]=showMs;metrics["heartbeatTicksDuringBlockedSnapshot"]=ticks;}
            finally{gate.Set();PumpUntil(()=>!form.IsWorking);}
        });
        Test("pin completion never calls full snapshot and preserves size draft",()=>
        {
            int snapshots=0;bool pin=false;
            using var form=new MainForm(new GuiBackend{TestMode=true,Snapshot=()=>{snapshots++;return Snapshot();},SetTopmost=v=>{pin=v;return new WindowState(true,1280,720,false,pin,"隔离置顶");}},capture:true);
            form.PrepareForCapture(1280,860);using var quick=new QuickForm(form);form.StateChanged+=quick.RefreshFromMain;quick.Show();Application.DoEvents();
            form.DispatchQuick("size:1600:900");int before=snapshots;var watch=Stopwatch.StartNew();quick.PinButton.PerformClick();PumpUntil(()=>!form.IsWorking);
            Assert(pin&&snapshots==before,"pin triggered expensive full snapshot");Assert(form.View.WinW==1600&&quick.SizeChoice.SelectedIndex==1,"pin erased draft or quick choice");metrics["isolatedPinCompletionMs"]=watch.Elapsed.TotalMilliseconds;
        });
        Test("runtime reads single flight and shared state stays consistent",()=>
        {
            int reads=0;using var gate=new ManualResetEventSlim();
            using var form=new MainForm(new GuiBackend{TestMode=true,Snapshot=Snapshot,Runtime=_=>{Interlocked.Increment(ref reads);gate.Wait(TimeSpan.FromSeconds(5));return new(true,true,"1600 × 900");}},capture:true);
            form.PrepareForCapture(1280,860);using var quick=new QuickForm(form);form.StateChanged+=quick.RefreshFromMain;quick.Show();Application.DoEvents();
            var first=form.RefreshRuntimeAsync();var second=form.RefreshRuntimeAsync();
            try{PumpUntil(()=>Volatile.Read(ref reads)==1);Assert(!first.IsCompleted&&second.IsCompleted,"runtime queued duplicate work");}
            finally{gate.Set();PumpUntil(()=>first.IsCompleted);}
            Assert(reads==1&&form.View.ActualSize=="1600 × 900"&&quick.StatusText.Contains("1600 × 900")&&quick.StatusText.Contains("已置顶"),"shared runtime state mismatch");
        });
        Test("late runtime snapshot cannot overwrite explicit pin result",()=>
        {
            int reads=0;using var gate=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();
            using var form=new MainForm(new GuiBackend{TestMode=true,Snapshot=Snapshot,Runtime=_=>{if(Interlocked.Increment(ref reads)==1){entered.Set();gate.Wait(TimeSpan.FromSeconds(5));return new(true,false,"old");}return new(true,true,"1600 × 900");},SetTopmost=v=>new WindowState(true,1280,720,false,v,"隔离")},capture:true);
            form.PrepareForCapture(1280,860);var reading=form.RefreshRuntimeAsync();PumpUntil(()=>entered.IsSet);form.DispatchQuick("toggle-topmost");gate.Set();PumpUntil(()=>!form.IsWorking&&reading.IsCompleted);
            Assert(form.View.Topmost&&form.View.ActualSize!="old","stale runtime overwrote state");
        });
        Test("hidden main errors reach floating observer",()=>
        {
            using var form=new MainForm(new GuiBackend{TestMode=true,Snapshot=Snapshot,ApplyWindow=(_,_,_)=>throw new IOException("隔离失败")},capture:true);
            form.PrepareForCapture(1280,860);form.Hide();string? failure=null;form.Failure+=s=>failure=s;form.DispatchQuick("apply-window");PumpUntil(()=>!form.IsWorking);Assert(failure?.Contains("隔离失败")==true&&form.View.Notice.Contains("隔离失败"),"hidden error silent");
        });
        Test("cancelled floating confirmation never writes",()=>
        {
            int writes=0;using var form=new MainForm(new GuiBackend{TestMode=true,Snapshot=Snapshot,ApplyWindow=(_,_,_)=>{writes++;return new WindowState(true,1280,720,false,false,"隔离");}},capture:true);
            form.PrepareForCapture(1280,860);form.Confirmation=()=>false;form.DispatchQuick("apply-window");Assert(writes==0&&!form.IsWorking,"cancel performed write");
        });
        var report=new{pass=failures.Count==0,passed=cases.Count,failed=failures.Count,cases,failures,metrics,scope="isolated slow backends and WinForms message pump; no real game/tray/AppData writes"};
        File.WriteAllText(Path.Combine(dir,"performance-tests.json"),JsonSerializer.Serialize(report,AppInfo.JsonOptions));return failures.Count==0?0:1;
    }
}
