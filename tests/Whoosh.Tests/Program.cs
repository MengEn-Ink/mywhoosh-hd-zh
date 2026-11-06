using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Whoosh.Core;

if(args.Length>0 && args[0]=="--crash-child")return CrashHarness.Child(args);
int passed=0;var failed=new List<string>();
void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL "+name+": "+e);}}
void Assert(bool ok,string message="assertion failed"){if(!ok)throw new Exception(message);}
void Reject(Action action){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,"Expected rejection");}
string work=Path.Combine(Path.GetTempPath(),"whoosh-zh-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
string Case(){var p=Path.Combine(work,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
const string rel="PersistentDownloadDir/DefaultCache/Cycling/Trainingplans/Workouts/test.json";
byte[] Before()=>Encoding.UTF8.GetBytes("{\"Name\":\"Tempo\",\"description\":\"ride\",\"Favorite\":false,\"Power\":210}");
byte[] After()=>Encoding.UTF8.GetBytes("{\"Name\":\"节奏\",\"description\":\"骑行\",\"Favorite\":false,\"Power\":210}");
List<TextChange> Texts()=>[new("/Name","Tempo","节奏"),new("/description","ride","骑行")];
(string root,string state,string file) Fixture(){string root=Case(),state=Case(),file=SafeFiles.Within(root,rel);SafeFiles.AtomicWrite(file,Before());return(root,state,file);}
PatchPlan Plan()=>new([new(rel,Before(),After(),Texts())],1,1,0,0);
Test("unsafe path rejected",()=>{foreach(var p in new[]{"../escape","C:/escape","/escape","folder/../escape","file:stream","folder./x"})Reject(()=>SafeFiles.Within(work,p));});
Test("known target allowlist",()=>{Assert(SafeFiles.AllowedTarget(rel));Assert(!SafeFiles.AllowedTarget("SaveGames/account.sav"));});
Test("atomic exact bytes",()=>{string p=Path.Combine(Case(),"x.bin");SafeFiles.AtomicWrite(p,[1,2,3]);Assert(File.ReadAllBytes(p).SequenceEqual(new byte[]{1,2,3}));});
Test("UTF8 strict decode",()=>Reject(()=>Translation.Decode([0xff,0x00,0xfe])));
Test("UTF16LE BOM preserved",()=>{var raw=Translation.Encode("{\"Name\":\"节奏\"}",new UnicodeEncoding(false,false,true),true);var n=Translation.Parse(raw);Assert(n["Name"]!.GetValue<string>()=="节奏");Assert(Translation.Serialize(n,raw).AsSpan().StartsWith(new byte[]{0xff,0xfe}));});
Test("UTF16BE BOM preserved",()=>{var raw=Translation.Encode("{\"Name\":\"节奏\"}",new UnicodeEncoding(true,false,true),true);Assert(Translation.Decode(raw).Text.Contains("节奏"));});
Test("UTF8 BOM preserved",()=>{var raw=Translation.Encode("{}",new UTF8Encoding(false,true),true);Assert(Translation.Serialize(Translation.Parse(raw),raw).AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf}));});
Test("JSON pointers escaped",()=>{var n=JsonNode.Parse("{\"a/b\":{\"~x\":\"old\"}}")!;Translation.Set(n,"/a~1b/~0x","new");Assert(Translation.Get(n,"/a~1b/~0x")!.GetValue<string>()=="new");});
Test("category translation exact",()=>{var n=JsonNode.Parse("{\"x\":[\"Beginner\",\"other\"],\"count\":3}")!;var edits=Translation.ReplaceCategories(n,new(){{"Beginner","初级"}});Assert(edits.Count==1&&edits[0].Pointer=="/x/0");Assert(n["count"]!.GetValue<int>()==3);});
Test("install and exact restore",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");Assert(tx.Apply(Plan,()=>{}).Changed==1,"changed count");Assert(File.ReadAllBytes(f.file).SequenceEqual(After()),"installed bytes");var restored=tx.Restore(()=>{});Assert(restored.Status=="restored","restore status: "+restored.Status);Assert(File.ReadAllBytes(f.file).SequenceEqual(Before()),"restored bytes: "+Encoding.UTF8.GetString(File.ReadAllBytes(f.file)));});
Test("restore preserves Favorite and power",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});var n=Translation.Parse(File.ReadAllBytes(f.file));n["Favorite"]=true;SafeFiles.AtomicWrite(f.file,Encoding.UTF8.GetBytes(n.ToJsonString()));tx.Restore(()=>{});n=Translation.Parse(File.ReadAllBytes(f.file));Assert(n["Favorite"]!.GetValue<bool>()&&n["Power"]!.GetValue<int>()==210&&n["Name"]!.GetValue<string>()=="Tempo");});
Test("unknown text edit preserved",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});var n=Translation.Parse(File.ReadAllBytes(f.file));n["Name"]="my own";SafeFiles.AtomicWrite(f.file,Encoding.UTF8.GetBytes(n.ToJsonString()));Assert(tx.Restore(()=>{}).Skipped==1);Assert(Translation.Parse(File.ReadAllBytes(f.file))["Name"]!.GetValue<string>()=="my own");});
Test("failure after write rolls back",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");Reject(()=>tx.Apply(Plan,()=>{},_=>throw new IOException("injected")));Assert(File.ReadAllBytes(f.file).SequenceEqual(Before()));Assert(tx.Status()=="unmanaged");Assert(!File.Exists(Path.Combine(f.state,"pending-install.json")));});
Test("external change prevents install",()=>{var f=Fixture();SafeFiles.AtomicWrite(f.file,[1,2]);Reject(()=>new Transactions(f.root,f.state,"test").Apply(Plan,()=>{}));Assert(File.ReadAllBytes(f.file).SequenceEqual(new byte[]{1,2}));});
Test("closed guard prevents writes",()=>{var f=Fixture();Reject(()=>new Transactions(f.root,f.state,"test").Apply(Plan,()=>throw new IOException("running")));Assert(File.ReadAllBytes(f.file).SequenceEqual(Before()));});
Test("repeat install is no-op",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});Assert(tx.Apply(()=>new([],1,1,1,0),()=>{}).Status=="unchanged");});
Test("repeat restore is no-op",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});tx.Restore(()=>{});Assert(tx.Restore(()=>{}).Changed==0);});
Test("foreign journal rejected",()=>{var f=Fixture();new Transactions(f.root,f.state,"test").Apply(Plan,()=>{});Reject(()=>new Transactions(f.root,f.state,"other").Restore(()=>{}));});
Test("tampered backup rejected",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});var backup=Directory.EnumerateFiles(f.state,"original-*.bin",SearchOption.AllDirectories).Single();File.WriteAllText(backup,"tampered");Reject(()=>tx.Restore(()=>{}));Assert(File.ReadAllBytes(f.file).SequenceEqual(After()));});
Test("tampered journal path rejected",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});string active=Path.Combine(f.state,"active.json");var n=JsonNode.Parse(File.ReadAllText(active))!;n["Entries"]![0]!["Relative"]="../outside";File.WriteAllText(active,n.ToJsonString());Reject(()=>tx.Restore(()=>{}));});
Test("unmanaged restore does not delete",()=>{var f=Fixture();Assert(new Transactions(f.root,f.state,"test").Restore(()=>{}).Status=="unmanaged");Assert(File.Exists(f.file));});
Test("operation file lock excludes concurrent",()=>{var f=Fixture();using var l=new FileStream(Path.Combine(f.state,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);Reject(()=>new Transactions(f.root,f.state,"test").Apply(Plan,()=>{}));});
Test("ZIP traversal rejected",()=>{string dir=Case(),zip=Path.Combine(dir,"bad.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){using var s=new StreamWriter(z.CreateEntry("../escape").Open());s.Write("x");}Reject(()=>PayloadStore.ImportZip(zip,Path.Combine(dir,"out")));Assert(!File.Exists(Path.Combine(dir,"escape")));});
Test("ZIP missing payload rejected",()=>{string dir=Case(),zip=Path.Combine(dir,"bad.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){using var s=new StreamWriter(z.CreateEntry("readme.txt").Open());s.Write("x");}Reject(()=>PayloadStore.ImportZip(zip,Path.Combine(dir,"out")));});
Test("missing payload fails closed",()=>Reject(()=>PayloadStore.Validate(Case())));
Test("embedded payload extracts and validates",()=>{string folder=Case();Assert(PayloadStore.EnsurePayload(folder));PayloadStore.Validate(folder);Assert(Directory.GetFiles(folder).Length==4);});
Test("embedded payload valid cache is no-op",()=>{string folder=Case();PayloadStore.EnsurePayload(folder);Assert(!PayloadStore.EnsurePayload(folder));});
Test("embedded payload repairs corrupt cache",()=>{string folder=Case();PayloadStore.EnsurePayload(folder);File.WriteAllText(Path.Combine(folder,"courses.json"),"corrupt");Assert(PayloadStore.EnsurePayload(folder));PayloadStore.Validate(folder);});
Test("interrupted prepared transaction recovers",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});string active=Path.Combine(f.state,"active.json");var n=JsonNode.Parse(File.ReadAllText(active))!;n["Status"]="prepared";File.WriteAllText(active,n.ToJsonString());Reject(()=>tx.Apply(Plan,()=>{}));Assert(File.ReadAllBytes(f.file).SequenceEqual(Before()));Assert(tx.Status()=="rolled-back");});
Test("recovery preserves unknown external changes",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});string active=Path.Combine(f.state,"active.json");var n=JsonNode.Parse(File.ReadAllText(active))!;n["Status"]="prepared";File.WriteAllText(active,n.ToJsonString());File.WriteAllText(f.file,"external");tx.Restore(()=>{});Assert(tx.Status()=="conflict"&&File.ReadAllText(f.file)=="external");});
Test("tampered original text rejected",()=>{var f=Fixture();var tx=new Transactions(f.root,f.state,"test");tx.Apply(Plan,()=>{});string active=Path.Combine(f.state,"active.json");var n=JsonNode.Parse(File.ReadAllText(active))!;n["Entries"]![0]!["Texts"]![0]!["Original"]="fake";File.WriteAllText(active,n.ToJsonString());Reject(()=>tx.Restore(()=>{}));});
Test("ZIP duplicate path rejected",()=>{string dir=Case(),zip=Path.Combine(dir,"bad.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){z.CreateEntry("a.txt");z.CreateEntry("A.txt");}Reject(()=>PayloadStore.ImportZip(zip,Path.Combine(dir,"out")));});
Test("window mode apply and exact reset",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");var raw=Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\nResolutionSizeX=1920\r\nResolutionSizeY=1080\r\nFrameRateLimit=40\r\n");SafeFiles.AtomicWrite(ini,raw);var w=GameWindow.Apply(root,state,1280,720,()=>{});Assert(w.Windowed&&w.Width==1280&&w.BackupAvailable);GameWindow.Restore(root,state,()=>{});Assert(File.ReadAllBytes(ini).SequenceEqual(raw));Assert(!File.Exists(SafeFiles.Within(root,"Config/Windows/Game.ini")));});
Test("window mode preserves unrelated changes",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");SafeFiles.AtomicWrite(ini,Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\nFullscreenMode=0\nFrameRateLimit=40\n"));GameWindow.Apply(root,state,1280,720,()=>{});string current=File.ReadAllText(ini).Replace("FrameRateLimit=40","FrameRateLimit=50");File.WriteAllText(ini,current);GameWindow.Restore(root,state,()=>{});Assert(File.ReadAllText(ini).Contains("FrameRateLimit=50"));Assert(File.ReadAllText(ini).Contains("FullscreenMode=0"));});
Test("window repeated apply retains first baseline",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");var raw=Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\nFullscreenMode=0\n");SafeFiles.AtomicWrite(ini,raw);GameWindow.Apply(root,state,1280,720,()=>{});GameWindow.Apply(root,state,1600,900,()=>{});GameWindow.Restore(root,state,()=>{});Assert(File.ReadAllBytes(ini).SequenceEqual(raw));});
Test("window invalid size fails before changes",()=>{string root=Case(),state=Case();Reject(()=>GameWindow.Apply(root,state,100,50,()=>{}));Assert(!File.Exists(Path.Combine(state,"window.json")));});
Test("window running guard prevents writes",()=>{string root=Case(),state=Case();Reject(()=>GameWindow.Apply(root,state,1280,720,()=>throw new IOException("game running")));Assert(!File.Exists(Path.Combine(state,"window.json")));});
Test("INI duplicate replacement keeps other section",()=>{string s="[A]\nx=1\nx=2\n[B]\nx=9\n";string changed=GameWindow.UpdateSection(s,"A",new Dictionary<string,string?>{{"x","3"}});Assert(GameWindow.ReadSection(changed,"A")["x"]=="3");Assert(GameWindow.ReadSection(changed,"B")["x"]=="9");});
Test("window topmost preference stored and read",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");SafeFiles.AtomicWrite(ini,Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\n"));var w=GameWindow.Apply(root,state,1280,720,()=>{},true);Assert(w.Topmost,"topmost should be true after apply");var again=GameWindow.Inspect(root,state);Assert(again.Topmost,"topmost should persist across inspect");});
Test("window topmost defaults to false",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");SafeFiles.AtomicWrite(ini,Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\n"));var w=GameWindow.Apply(root,state,1280,720,()=>{});Assert(!w.Topmost,"topmost should default false");});
Test("window topmost preference toggle without size change",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");SafeFiles.AtomicWrite(ini,Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=2\r\nResolutionSizeX=1280\r\nResolutionSizeY=720\r\n"));GameWindow.Apply(root,state,1280,720,()=>{},false);var w=GameWindow.SetTopmostPreference(root,state,true,()=>{});Assert(w.Topmost,"topmost should be true");Assert(w.Width==1280&&w.Height==720,"size unchanged");var w2=GameWindow.SetTopmostPreference(root,state,false,()=>{});Assert(!w2.Topmost,"topmost toggled off");});
Test("window topmost cleared after restore",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");var raw=Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\nResolutionSizeX=1920\r\nResolutionSizeY=1080\r\n");SafeFiles.AtomicWrite(ini,raw);GameWindow.Apply(root,state,1280,720,()=>{},true);var restored=GameWindow.Restore(root,state,()=>{});Assert(!restored.Topmost,"topmost reset after restore");Assert(!restored.BackupAvailable,"backup removed after restore");});
Test("native window api safe on non-windows or no game",()=>{Assert(NativeWindow.FindGameWindow("MyWhooshNonexistent_xyz")==IntPtr.Zero);Assert(!NativeWindow.SetTopmost(IntPtr.Zero,true));Assert(NativeWindow.GetTitle(IntPtr.Zero)=="");Assert(!NativeWindow.IsTopmost(IntPtr.Zero));});
Test("window fullscreen mode parameter",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");SafeFiles.AtomicWrite(ini,Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\n"));GameWindow.Apply(root,state,1280,720,()=>{},false,0);var w0=GameWindow.Inspect(root,state);Assert(!w0.Windowed,"fullscreen mode 0 should not be windowed");GameWindow.Apply(root,state,1280,720,()=>{},false,1);Assert(!GameWindow.Inspect(root,state).Windowed,"borderless mode 1 not flagged as windowed");GameWindow.Apply(root,state,1280,720,()=>{},false,2);Assert(GameWindow.Inspect(root,state).Windowed,"windowed mode 2");});
Test("window invalid mode rejected",()=>{string root=Case(),state=Case();string ini=SafeFiles.Within(root,"Config/Windows/GameUserSettings.ini");SafeFiles.AtomicWrite(ini,Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\n"));Reject(()=>GameWindow.Apply(root,state,1280,720,()=>{},false,3));});
CrashHarness.Run(Test,Case,(ok,message)=>Assert(ok,message));
RegressionCases.Run(Test,Case,(ok,message)=>Assert(ok,message),Reject);
if(args.Length==3&&args[0]=="--integration")
{
    Test("full isolated cache install-check-restore",()=>
    {
        string root=Case(),state=Case();
        using(var zip=ZipFile.OpenRead(args[1]))foreach(var entry in zip.Entries)
        {
            if(!entry.FullName.StartsWith("PersistentDownloadDir/DefaultCache/Cycling/",StringComparison.Ordinal)||entry.FullName.EndsWith('/'))continue;
            var target=SafeFiles.Within(root,entry.FullName);using var input=entry.Open();using var memory=new MemoryStream();input.CopyTo(memory);SafeFiles.AtomicWrite(target,memory.ToArray());
        }
        PayloadStore.Validate(args[2]);
        // Convert a COPY of the snapshot to a controlled English baseline; never touch source cache.
        var reverse=Translation.CourseRows(args[2]).GroupBy(n=>(n["ChineseName"]!.GetValue<string>(),n["ChineseDescription"]!.GetValue<string>())).ToDictionary(g=>g.Key,g=>g.First());
        int resetToEnglish=0;
        foreach(var file in Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories))
        {
            byte[] raw=SafeFiles.ReadBounded(file);var n=Translation.Parse(raw);if(n is not JsonObject obj)continue;
            string? dk=obj.Select(k=>k.Key).FirstOrDefault(k=>k.Equals("Description",StringComparison.OrdinalIgnoreCase));
            if(dk==null||obj["Name"]==null)continue;
            if(reverse.TryGetValue((obj["Name"]!.GetValue<string>(),obj[dk]!.GetValue<string>()),out var row))
            {obj["Name"]=row["Name"]!.GetValue<string>();obj[dk]=row["Description"]!.GetValue<string>();SafeFiles.AtomicWrite(file,Translation.Serialize(obj,raw));resetToEnglish++;}
        }
        var original=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(root,p),SafeFiles.HashFile);
        var plan=Translation.Plan(root,args[2]);Assert(plan.Scanned==2063,"integration source size");Assert(plan.Matched==2063&&plan.Unmatched==0&&plan.AlreadyChinese==0,"complete English baseline coverage");
        var tx=new Transactions(root,state,"integration");var installed=tx.Apply(()=>plan,()=>{});
        Assert(installed.Changed==plan.Changes.Count,"real changed files");
        var check=Translation.Plan(root,args[2]);Assert(check.Changes.Count==0,"idempotent real cache");
        var restored=tx.Restore(()=>{});Assert(restored.Status=="restored","full restore status");
        foreach(var kv in original)Assert(SafeFiles.HashFile(Path.Combine(root,kv.Key))==kv.Value,"restore digest "+kv.Key);
        Assert(!File.Exists(Path.Combine(root,"Paks","MyWhooshChinese_P.pak")),"created pak removed");
        Console.WriteLine("INTEGRATION "+JsonSerializer.Serialize(new{plan.Scanned,plan.Matched,plan.Unmatched,ResetToEnglish=resetToEnglish,InstalledFiles=installed.Changed,RestoredFiles=restored.Changed,AllOriginalHashesRestored=true}));
    });
}
Console.WriteLine(JsonSerializer.Serialize(new{passed,failed=failed.Count,failures=failed},AppInfo.JsonOptions));
if(args.Length==2&&args[0]=="--result")File.WriteAllText(args[1],JsonSerializer.Serialize(new{passed,failed=failed.Count,failures=failed},AppInfo.JsonOptions));
// All files below work were created by this process. Keep failures for inspection.
if(failed.Count==0)Directory.Delete(work,true);
return failed.Count==0?0:1;
