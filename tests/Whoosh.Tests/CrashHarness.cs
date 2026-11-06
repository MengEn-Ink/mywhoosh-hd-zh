using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Whoosh.Core;

internal static class CrashHarness
{
    private const string Rel="PersistentDownloadDir/DefaultCache/Cycling/Trainingplans/Workouts/crash.json";
    private const string First="PersistentDownloadDir/DefaultCache/Cycling/Trainingplans/Workouts/first.json";
    private static byte[] Before=>Encoding.UTF8.GetBytes("{\"Name\":\"A\",\"description\":\"a\",\"Favorite\":true}");
    private static byte[] After=>Encoding.UTF8.GetBytes("{\"Name\":\"甲\",\"description\":\"甲课\",\"Favorite\":true}");
    private static PatchPlan Plan(string rel)=>new([new(rel,Before,After,[new("/Name","A","甲"),new("/description","a","甲课")])],1,1,0,0);
    internal static int Child(string[] args)
    {
        if(args.Length!=4)throw new ArgumentException("crash harness arguments");
        string mode=args[1],root=args[2],state=args[3];
        // This branch is test-only. The parent chooses fresh directories beneath its own temp suite.
        if(!root.StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase)||!state.StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("test root must be temporary");
        Action<int> terminate=n=>{if(mode=="committed"?n==-1:n==1)Environment.Exit(86);};
        if(mode=="first"||mode=="incremental"||mode=="committed")
        {
            var tx=new Transactions(root,state,"crash");
            if(mode=="incremental"){SafeFiles.AtomicWrite(SafeFiles.Within(root,First),Before);tx.Apply(()=>Plan(First),()=>{});}
            SafeFiles.AtomicWrite(SafeFiles.Within(root,Rel),Before);tx.Apply(()=>Plan(Rel),()=>{},terminate);
        }
        else
        {
            SafeFiles.AtomicWrite(SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini"),Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\nFullscreenMode=0\nResolutionSizeX=1920\n"));
            if(mode=="window-restore"){GameWindow.Apply(root,state,1280,720,()=>{});GameWindow.Restore(root,state,()=>{},terminate);}
            else GameWindow.Apply(root,state,1280,720,()=>{},fault:terminate);
        }
        return 99;
    }
    internal static void Run(Action<string,Action> test,Func<string> dir,Action<bool,string> assert)
    {
        foreach(string mode in new[]{"first","incremental","committed","window-apply","window-restore"})test("actual process termination recovers "+mode,()=>{
            string root=dir(),state=dir();var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach(var arg in new[]{"--crash-child",mode,root,state})start.ArgumentList.Add(arg);
            using var p=Process.Start(start)??throw new IOException("cannot start crash child");
            var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(30000)){p.Kill(true);throw new TimeoutException("crash child timeout");}
            Task.WaitAll(output,error);assert(p.ExitCode==86,"child did not exit at injected boundary: "+error.Result);
            if(mode=="committed")
            {
                var tx=new Transactions(root,state,"crash");tx.RecoverPending(()=>{});
                assert(tx.Status()=="installed","commit mistaken for rollback");
                assert(File.ReadAllBytes(SafeFiles.Within(root,Rel)).SequenceEqual(After),"committed files reverted");
                assert(!File.Exists(Path.Combine(state,"pending-install.json")),"committed marker not cleared");
            }
            else if(mode=="first"||mode=="incremental")
            {
                assert(File.Exists(Path.Combine(state,"pending-install.json")),"pending journal absent");var tx=new Transactions(root,state,"crash");
                tx.RecoverPending(()=>{});
                if(mode=="incremental")assert(File.ReadAllBytes(SafeFiles.Within(root,First)).SequenceEqual(After),"previous batch rolled back");
                assert(File.ReadAllBytes(SafeFiles.Within(root,Rel)).SequenceEqual(Before),"interrupted write not restored");
                assert(!File.Exists(Path.Combine(state,"pending-install.json")),"pending journal remained");
            }
            else
            {
                assert(File.Exists(Path.Combine(state,"pending-window.json")),"window pending absent");
                GameWindow.RecoverPending(root,state,()=>{});
                if(mode=="window-apply")assert(GameWindow.Inspect(root,state).Width==1920,"first window file not restored");
                else assert(GameWindow.Inspect(root,state).Width==1280&&GameWindow.Inspect(root,state).BackupAvailable,"restore crash lost baseline");
                assert(!File.Exists(Path.Combine(state,"pending-window.json")),"pending window not cleared");
            }
        });
    }
}
