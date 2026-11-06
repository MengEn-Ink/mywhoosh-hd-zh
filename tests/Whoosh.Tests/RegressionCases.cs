using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Whoosh.Core;

internal static class RegressionCases
{
    public static void Run(Action<string,Action> test,Func<string> dir,Action<bool,string> assert,Action<Action> reject)
    {
        const string rel="PersistentDownloadDir/DefaultCache/Cycling/Trainingplans/Workouts/test.json";
        byte[] Raw(string name="Tempo",string desc="ride",bool favorite=false)=>Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {Name=name,description=desc,Favorite=favorite,Power=210}));
        PlannedChange Change(string path,byte[] before,string name="节奏",string desc="骑行")
        {
            var n=Translation.Parse(before);string dk=n.AsObject().Select(k=>k.Key).First(k=>k.Equals("Description",StringComparison.OrdinalIgnoreCase));
            var edits=new List<TextChange>{new("/Name",n["Name"]!.GetValue<string>(),name),new("/"+dk,n[dk]!.GetValue<string>(),desc)};
            n["Name"]=name;n[dk]=desc;return new(path,before,Translation.Serialize(n,before),edits);
        }
        PatchPlan Plan(params PlannedChange[] changes)=>new(changes.ToList(),changes.Length,changes.Length,0,0);
        (string Root,string State,Transactions Tx) Fixture()
        {
            string root=dir(),state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,rel),Raw());return(root,state,new(root,state,"test"));
        }
        byte[] FileBytes(string root,string path)=>SafeFiles.ReadBounded(SafeFiles.Within(root,path));
        Journal Ledger(string state)=>JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(Path.Combine(state,"active.json")),AppInfo.JsonOptions)!;
        test("CLI window topmost flag parsed without game writes",()=>{
            var plain=CliArguments.Window(["window","--width","1280","--height","720","--yes"]);
            var pinned=CliArguments.Window(["window","--width","1280","--height","720","--yes","--topmost"]);
            assert(!plain.Topmost&&pinned.Topmost&&pinned.Width==1280&&pinned.Height==720,"topmost flag lost");
        });
        test("CLI window rejects trailing and malformed arguments",()=>{
            foreach(var args in new string[][]{
                ["window","--width","1280","--height","720","--yes","--bad"],
                ["window","--width","1280","--height","720","--yes","--topmost","true"],
                ["window","--width","-1","--height","720","--yes"],
                ["window","--width","1,280","--height","720","--yes"]})reject(()=>CliArguments.Window(args));
        });
        test("published-resource selftest uses isolated new directory",()=>{
            string parent=dir(),target=Path.Combine(parent,"new-payload");
            PayloadStore.SelfTest(target);PayloadStore.Validate(target);
            assert(Directory.GetFiles(target).Length==4,"four resources absent");
            var hashes=Directory.GetFiles(target).ToDictionary(p=>Path.GetFileName(p)!,SafeFiles.HashFile);
            reject(()=>PayloadStore.SelfTest(target));
            assert(Directory.GetFiles(target).All(p=>hashes[Path.GetFileName(p)]==SafeFiles.HashFile(p)),"existing selftest directory overwritten");
        });
        test("explicit recovery rejects running guard without file writes",()=>{
            var f=Fixture();var old=FileBytes(f.Root,rel);
            reject(()=>f.Tx.RecoverPending(()=>throw new IOException("game running")));
            reject(()=>GameWindow.RecoverPending(f.Root,f.State,()=>throw new IOException("game running")));
            assert(old.SequenceEqual(FileBytes(f.Root,rel)),"recovery ignored closed guard");
        });
        test("explicit recovery no-op retains installed first baseline",()=>{
            var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});
            var ledger=File.ReadAllBytes(Path.Combine(f.State,"active.json"));
            assert(f.Tx.RecoverPending(()=>{}).Status=="unchanged"&&ledger.SequenceEqual(File.ReadAllBytes(Path.Combine(f.State,"active.json"))),"no-op recovery touched baseline");
        });
        const string second="PersistentDownloadDir/DefaultCache/Cycling/Trainingplans/Workouts/second.json";
        test("incremental new file retains first backup and restores all",()=>{
            var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});var first=Ledger(f.State);string backup=first.Entries[0].Backup!;DateTime? created=first.CreatedUtc;
            SafeFiles.AtomicWrite(SafeFiles.Within(f.Root,second),Raw());f.Tx.Apply(()=>Plan(Change(second,Raw())),()=>{});
            var j=Ledger(f.State);assert(j.Id==first.Id&&j.Entries.Count==2&&j.Entries[0].Backup==backup&&j.CreatedUtc==created,"baseline changed");
            assert(f.Tx.Restore(()=>{}).Changed==2,"restore both batches");assert(FileBytes(f.Root,rel).SequenceEqual(Raw())&&FileBytes(f.Root,second).SequenceEqual(Raw()),"all original bytes");
        });
        test("incremental failure rolls back new batch only",()=>{
            var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});var old=File.ReadAllBytes(Path.Combine(f.State,"active.json"));SafeFiles.AtomicWrite(SafeFiles.Within(f.Root,second),Raw());
            reject(()=>f.Tx.Apply(()=>Plan(Change(second,Raw())),()=>{},_=>throw new IOException("injected")));
            assert(FileBytes(f.Root,rel).SequenceEqual(Change(rel,Raw()).After),"first installation lost");assert(FileBytes(f.Root,second).SequenceEqual(Raw()),"batch not rolled back");assert(File.ReadAllBytes(Path.Combine(f.State,"active.json")).SequenceEqual(old),"old journal lost");
        });
        test("same course re-download preserves favorite and original backup",()=>{
            var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});string backup=Ledger(f.State).Entries[0].Backup!;
            var downloaded=Raw(favorite:true);SafeFiles.AtomicWrite(SafeFiles.Within(f.Root,rel),downloaded);f.Tx.Apply(()=>Plan(Change(rel,downloaded)),()=>{});
            assert(Ledger(f.State).Entries[0].Backup==backup,"first backup replaced");f.Tx.Restore(()=>{});var n=Translation.Parse(FileBytes(f.Root,rel));assert(n["Name"]!.GetValue<string>()=="Tempo"&&n["Favorite"]!.GetValue<bool>(),"favorite lost");
        });
        test("same path new semantic course rejected without writes",()=>{
            var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});var changed=Raw("New server course","new instructions");SafeFiles.AtomicWrite(SafeFiles.Within(f.Root,rel),changed);
            reject(()=>f.Tx.Apply(()=>Plan(Change(rel,changed)),()=>{}));assert(FileBytes(f.Root,rel).SequenceEqual(changed),"unknown new course overwritten");
        });
        test("incremental rollback preserves external text conflict",()=>{
            var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});SafeFiles.AtomicWrite(SafeFiles.Within(f.Root,second),Raw());
            reject(()=>f.Tx.Apply(()=>Plan(Change(second,Raw())),()=>{},_=>{SafeFiles.AtomicWrite(SafeFiles.Within(f.Root,second),Raw("external","keep"));throw new IOException();}));
            assert(Translation.Parse(FileBytes(f.Root,second))["Name"]!.GetValue<string>()=="external","external overwritten");assert(f.Tx.Status()=="prepared","pending conflict hidden");reject(()=>f.Tx.Restore(()=>{}));
        });
        test("incremental no-op does not replace journal",()=>{var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});var old=File.ReadAllBytes(Path.Combine(f.State,"active.json"));f.Tx.Apply(()=>Plan(),()=>{});assert(old.SequenceEqual(File.ReadAllBytes(Path.Combine(f.State,"active.json"))),"noop rewrote metadata");});
        test("old schema journal remains readable without creation date",()=>{var f=Fixture();f.Tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});var n=JsonNode.Parse(File.ReadAllBytes(Path.Combine(f.State,"active.json")))!;n.AsObject().Remove("CreatedUtc");n.AsObject().Remove("Revision");File.WriteAllText(Path.Combine(f.State,"active.json"),n.ToJsonString());assert(f.Tx.Summary().CreatedUtc==null&&f.Tx.Restore(()=>{}).Status=="restored","old journal incompatible");});
        test("game version update can restore known managed files",()=>{var root=dir();var state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,rel),Raw());var tx=new Transactions(root,state,AppInfo.Family);tx.Apply(()=>Plan(Change(rel,Raw())),()=>{});var target=new Target(AppInfo.PackageName,AppInfo.Family,"99.0.0.0",dir(),root);assert(Operations.RestoreTarget(target,state,()=>{}).Status=="restored","updated version cannot restore");});
        test("restore other family rejected",()=>{reject(()=>Operations.RestoreTarget(new("x","other","99",dir(),dir()),dir(),()=>{}));});
        test("duplicate batch target rejected before writes",()=>{var f=Fixture();reject(()=>f.Tx.Apply(()=>Plan(Change(rel,Raw()),Change(rel,Raw())),()=>{}));assert(FileBytes(f.Root,rel).SequenceEqual(Raw()),"duplicate wrote files");});
        foreach(string key in new[]{"Description","description","dEsCrIpTiOn"})test("course mixed key install-check-restore "+key,()=>{
            var payload=dir();PayloadStore.EnsurePayload(payload);var row=Translation.CourseRows(payload).First();string root=dir(),state=dir();
            var n=new JsonObject{["Name"]=row["Name"]!.GetValue<string>(),[key]=row["Description"]!.GetValue<string>(),["Favorite"]=false,["Power"]=210};byte[] raw=Encoding.UTF8.GetBytes(n.ToJsonString());SafeFiles.AtomicWrite(SafeFiles.Within(root,rel),raw);
            var tx=new Transactions(root,state,"test");tx.Apply(()=>Translation.Plan(root,payload),()=>{});assert(Translation.Plan(root,payload).AlreadyChinese==1,"not detected Chinese");assert(Translation.Parse(FileBytes(root,rel)).AsObject().ContainsKey(key),"key changed");tx.Restore(()=>{});assert(FileBytes(root,rel).SequenceEqual(raw),"original lost");
        });
        test("three supplement originals exact translated and fully restored",()=>{
            var payload=dir();PayloadStore.EnsurePayload(payload);var rows=Translation.CourseRows(payload).Where(n=>n["Name"]!.GetValue<string>()=="15min Varied Tempo #1").ToArray();assert(rows.Length==3,"missing supplement variants");string root=dir(),state=dir();var originals=new Dictionary<string,byte[]>();
            for(int i=0;i<rows.Length;i++){string p=rel.Replace("test.json",i+".json");var n=new JsonObject{["Name"]=rows[i]["Name"]!.GetValue<string>(),["description"]=rows[i]["Description"]!.GetValue<string>(),["Power"]=210,["Duration"]=900,["Favorite"]=true};var raw=Encoding.UTF8.GetBytes(n.ToJsonString());originals[p]=raw;SafeFiles.AtomicWrite(SafeFiles.Within(root,p),raw);}
            var tx=new Transactions(root,state,"test");var plan=Translation.Plan(root,payload);assert(plan.Matched==3&&plan.Unmatched==0,"variant not matched");tx.Apply(()=>plan,()=>{});
            foreach(var p in originals.Keys){var n=Translation.Parse(FileBytes(root,p));string d=n["description"]!.GetValue<string>();assert(d.Contains("第3区")&&d.Contains("15分钟")&&d.Contains("英文原文")&&n["Power"]!.GetValue<int>()==210&&n["Duration"]!.GetValue<int>()==900&&n["Favorite"]!.GetValue<bool>(),"semantic/parameter mismatch");}
            assert(Translation.Plan(root,payload).Changes.Count==0,"supplement not idempotent");tx.Restore(()=>{});foreach(var p in originals)assert(FileBytes(root,p.Key).SequenceEqual(p.Value),"variant restore mismatch");
        });
        test("unknown course not guessed by same prefix",()=>{var payload=dir();PayloadStore.EnsurePayload(payload);var root=dir();var raw=Raw("15min Varied Tempo #1","new server description");SafeFiles.AtomicWrite(SafeFiles.Within(root,rel),raw);var plan=Translation.Plan(root,payload);assert(plan.Unmatched==1&&plan.Changes.All(c=>c.Relative!=rel),"guessed translation");});
        test("course path over 260 characters install and restore",()=>{
            var payload=dir();PayloadStore.EnsurePayload(payload);var row=Translation.CourseRows(payload).First();var root=dir();var state=dir();string longRel=rel.Replace("test.json",string.Join('/',Enumerable.Repeat(new string('a',60),4))+"/test.json");byte[] raw=Raw(row["Name"]!.GetValue<string>(),row["Description"]!.GetValue<string>());SafeFiles.AtomicWrite(SafeFiles.Within(root,longRel),raw);var tx=new Transactions(root,state,"test");tx.Apply(()=>Translation.Plan(root,payload),()=>{});tx.Restore(()=>{});assert(FileBytes(root,longRel).SequenceEqual(raw),"long path restore mismatch");
        });
        string ini="Config/Windows/GameUserSettings.ini";
        byte[] Ini()=>Encoding.UTF8.GetBytes("[/Script/Engine.GameUserSettings]\r\nFullscreenMode=0\r\nResolutionSizeX=1920\r\nResolutionSizeY=1080\r\nFrameRateLimit=40\r\n");
        test("window apply second write failure rolls back first and metadata",()=>{string root=dir(),state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,ini),Ini());reject(()=>GameWindow.Apply(root,state,1280,720,()=>{},fault:n=>{if(n==1)throw new IOException();}));assert(FileBytes(root,ini).SequenceEqual(Ini())&&!File.Exists(SafeFiles.Within(root,"Config/Windows/Game.ini"))&&!File.Exists(Path.Combine(state,"window.json")),"window partial write remains");});
        test("window reapply failure restores previous applied settings",()=>{string root=dir(),state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,ini),Ini());GameWindow.Apply(root,state,1280,720,()=>{});byte[] old=FileBytes(root,ini),ledger=File.ReadAllBytes(Path.Combine(state,"window.json"));reject(()=>GameWindow.Apply(root,state,1600,900,()=>{},fault:_=>throw new IOException()));assert(old.SequenceEqual(FileBytes(root,ini))&&ledger.SequenceEqual(File.ReadAllBytes(Path.Combine(state,"window.json"))),"prior settings lost");});
        test("window restore failure rolls back restored files",()=>{string root=dir(),state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,ini),Ini());GameWindow.Apply(root,state,1280,720,()=>{});byte[] old=FileBytes(root,ini);reject(()=>GameWindow.Restore(root,state,()=>{},_=>throw new IOException()));assert(old.SequenceEqual(FileBytes(root,ini))&&GameWindow.Inspect(root,state).BackupAvailable,"partial restore");});
        test("window unknown user field preserved and own field conflict rejected",()=>{string root=dir(),state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,ini),Ini());GameWindow.Apply(root,state,1280,720,()=>{});var raw=FileBytes(root,ini);var text=Encoding.UTF8.GetString(raw).Replace("ResolutionSizeX=1280","ResolutionSizeX=1400");SafeFiles.AtomicWrite(SafeFiles.Within(root,ini),Encoding.UTF8.GetBytes(text));reject(()=>GameWindow.Restore(root,state,()=>{}));assert(Encoding.UTF8.GetString(FileBytes(root,ini)).Contains("ResolutionSizeX=1400"),"user size overwritten");});
        test("window topmost preference alone is not recoverable config backup",()=>{string root=dir(),state=dir();GameWindow.SetTopmostPreference(root,state,true,()=>{});assert(!GameWindow.Inspect(root,state).BackupAvailable,"fake backup shown");});
        test("window timestamp stable on size and topmost changes",()=>{string root=dir(),state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,ini),Ini());GameWindow.Apply(root,state,1280,720,()=>{});var date=GameWindow.BackupCreated(root,state);GameWindow.SetTopmostPreference(root,state,true,()=>{});GameWindow.Apply(root,state,1600,900,()=>{});assert(date==GameWindow.BackupCreated(root,state),"first timestamp changed");});
        test("tampered window key rejected without write",()=>{string root=dir(),state=dir();SafeFiles.AtomicWrite(SafeFiles.Within(root,ini),Ini());GameWindow.Apply(root,state,1280,720,()=>{});var path=Path.Combine(state,"window.json");var n=JsonNode.Parse(File.ReadAllBytes(path))!;n["Applied"]![ini]!["AccountSecret"]="x";File.WriteAllText(path,n.ToJsonString());reject(()=>GameWindow.Restore(root,state,()=>{}));});
        test("six game process variants recognized and helper excluded",()=>{foreach(var p in new[]{"MyWhoosh","MyWhooshHD","MyWhoosh-Win64-Shipping","MyWhoosh-WinGDK-Shipping","MyWhoosh-UWP64-Shipping","mywhooshhd-win64-shipping"})assert(WindowsTarget.IsGameProcess(p),"game not recognized "+p);foreach(var p in new[]{"MyWhooshZH","MyWhooshChinesePatch","MyWhoosh_Helper","MWCN-Setup-1.1.0"})assert(!WindowsTarget.IsGameProcess(p),"helper treated as game");assert(WindowsTarget.BlocksWrites("MWCN-Setup-1.1.0"),"legacy installer not blocked");});
        test("real native client size no window safe",()=>assert(NativeWindow.ClientSize(IntPtr.Zero)==null,"zero window size"));
        test("legacy migration valid backup idempotent and restore favorite",()=>LegacyCase(false,false));
        test("legacy migration tampered backup rejected or skipped",()=>LegacyCase(true,false));
        test("legacy migration traversal rejected",()=>LegacyCase(false,true));
        void LegacyCase(bool corrupt,bool traversal)
        {
            var payload=dir();PayloadStore.EnsurePayload(payload);var row=Translation.CourseRows(payload).First();string root=dir(),state=dir(),legacy=dir();
            var original=Raw(row["Name"]!.GetValue<string>(),row["Description"]!.GetValue<string>());var current=Raw(row["ChineseName"]!.GetValue<string>(),row["ChineseDescription"]!.GetValue<string>(),true);SafeFiles.AtomicWrite(SafeFiles.Within(root,rel),current);
            var h=SafeFiles.Hash(original);SafeFiles.AtomicWrite(SafeFiles.Within(legacy,"Courses/Backups/"+h+".json"),corrupt?Raw("corrupt","no"):original);
            var record=new {Relative=traversal?"../../escape.json":"Trainingplans/Workouts/test.json",OriginalName=row["Name"]!.GetValue<string>(),OriginalDescription=row["Description"]!.GetValue<string>(),NewName=row["ChineseName"]!.GetValue<string>(),NewDescription=row["ChineseDescription"]!.GetValue<string>()};SafeFiles.AtomicWrite(SafeFiles.Within(legacy,"Courses/ledger.json"),JsonSerializer.SerializeToUtf8Bytes(new[]{record}));
            var category=Encoding.UTF8.GetBytes("{\"name\":\"no mapping\"}");string ch=SafeFiles.Hash(category);SafeFiles.AtomicWrite(SafeFiles.Within(legacy,"Courses/Backups/"+ch+".json"),category);SafeFiles.AtomicWrite(SafeFiles.Within(legacy,"Courses/categories-ledger.json"),JsonSerializer.SerializeToUtf8Bytes(new{backup=ch+".json"}));SafeFiles.AtomicWrite(SafeFiles.Within(root,"PersistentDownloadDir/DefaultCache/Cycling/Workouts/WorkoutCategory.json"),category);
            if(traversal){reject(()=>LegacyMigration.Import(root,state,legacy,payload,()=>{}));assert(FileBytes(root,rel).SequenceEqual(current),"migration wrote game");return;}
            var result=LegacyMigration.Import(root,state,legacy,payload,()=>{});
            if(corrupt){assert(result.Skipped==1,"corrupt original accepted");return;}
            assert(result.Changed>=1,"migration absent");var tx=new Transactions(root,state,AppInfo.Family);assert(tx.Summary().CanRestore,"migration not recoverable");LegacyMigration.Import(root,state,legacy,payload,()=>{});tx.Restore(()=>{});var n=Translation.Parse(FileBytes(root,rel));assert(n["Name"]!.GetValue<string>()==row["Name"]!.GetValue<string>()&&n["Favorite"]!.GetValue<bool>(),"migration restore favorite mismatch");
        }
    }
}
