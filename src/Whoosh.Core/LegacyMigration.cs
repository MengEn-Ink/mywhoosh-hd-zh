using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Whoosh.Core;

public static class LegacyMigration
{
    public static OperationResult Import(string saved,string state,string legacy,string payload,Action closed)
    {
        closed();PayloadStore.EnsurePayload(payload);SafeFiles.NoLinks(legacy);Directory.CreateDirectory(state);
        using var guard=new FileStream(Path.Combine(state,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var courses=Translation.Parse(SafeFiles.ReadBounded(Path.Combine(payload,"courses.json"))).AsArray();
        var known=courses.Where(n=>n!=null).ToDictionary(n=>(n!["Name"]!.GetValue<string>(),n["Description"]!.GetValue<string>()),n=>(n!["ChineseName"]!.GetValue<string>(),n["ChineseDescription"]!.GetValue<string>()));
        string active=Path.Combine(state,"active.json");Journal j=File.Exists(active)?JsonSerializer.Deserialize<Journal>(SafeFiles.ReadBounded(active),AppInfo.JsonOptions)!:new(){Id=Guid.NewGuid().ToString("N"),Family=AppInfo.Family,SavedRoot=Path.GetFullPath(saved)};
        if(j.Family!=AppInfo.Family||!Path.GetFullPath(j.SavedRoot).Equals(Path.GetFullPath(saved),StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(j.Id,"N",out _)||j.Status is "prepared" or "conflict")throw new InvalidDataException("当前账本不适合迁移，先处理冲突。");
        var existing=j.Entries.Where(e=>!e.Restored).Select(e=>e.Relative).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacyRecords=Translation.Parse(SafeFiles.ReadBounded(Path.Combine(legacy,"Courses","ledger.json"))).AsArray();
        var backupIndex=new Dictionary<(string,string),byte[]>();
        string backupDir=SafeFiles.Within(legacy,"Courses/Backups");
        foreach(var file in Directory.EnumerateFiles(backupDir,"*.json"))
        {
            var raw=SafeFiles.ReadBounded(file);if(SafeFiles.Hash(raw)!=Path.GetFileNameWithoutExtension(file).ToUpperInvariant())continue;
            var n=Translation.Parse(raw);string? dk=n.AsObject().Select(x=>x.Key).FirstOrDefault(k=>k.Equals("Description",StringComparison.OrdinalIgnoreCase));
            if(n["Name"] is JsonValue nv&&dk!=null&&n[dk] is JsonValue dv)backupIndex.TryAdd((nv.GetValue<string>(),dv.GetValue<string>()),raw);
        }
        int imported=0,skipped=0;
        foreach(var rec in legacyRecords)
        {
            string relative=rec!["Relative"]!.GetValue<string>().Replace('\\','/');string rel="PersistentDownloadDir/DefaultCache/Cycling/"+relative;
            if(!SafeFiles.AllowedTarget(rel))throw new InvalidDataException("旧账本路径不在课程范围。");
            string currentPath=SafeFiles.Within(saved,rel);if(existing.Contains(rel))continue;
            string name=rec["OriginalName"]!.GetValue<string>(),desc=rec["OriginalDescription"]!.GetValue<string>(),newName=rec["NewName"]!.GetValue<string>(),newDesc=rec["NewDescription"]!.GetValue<string>();
            if(!known.TryGetValue((name,desc),out var expected)||expected!=(newName,newDesc))throw new InvalidDataException("旧恢复记录与校验过的字典不一致。");
            if(!File.Exists(currentPath)||!backupIndex.TryGetValue((name,desc),out var original)){skipped++;continue;}
            var current=SafeFiles.ReadBounded(currentPath);var node=Translation.Parse(current);var obj=node.AsObject();string? key=obj.Select(x=>x.Key).FirstOrDefault(k=>k.Equals("Description",StringComparison.OrdinalIgnoreCase));
            if(obj["Name"]?.GetValue<string>()==name&&key!=null&&obj[key]?.GetValue<string>()==desc)continue;
            if(obj["Name"]?.GetValue<string>()!=newName||key==null||obj[key]?.GetValue<string>()!=newDesc){skipped++;continue;}
            var texts=new List<TextChange>{new("/Name",name,newName),new("/"+key,desc,newDesc)};
            // Baseline is the current file with only translated fields reverted, preserving user's newer state.
            var baseline=node.DeepClone();Translation.Set(baseline,"/Name",name);Translation.Set(baseline,"/"+key,desc);
            var before=Translation.Serialize(baseline,current);string backup=j.Id+"/legacy-"+(imported++)+".bin";
            SafeFiles.AtomicWrite(SafeFiles.Within(state,backup),before);
            j.Entries.Add(new(){Relative=rel,BeforeHash=SafeFiles.Hash(before),AfterHash=SafeFiles.Hash(current),Backup=backup,Texts=texts});existing.Add(rel);
        }
        string catRel="PersistentDownloadDir/DefaultCache/Cycling/Workouts/WorkoutCategory.json";
        if(!existing.Contains(catRel))
        {
            var entry=Translation.Parse(SafeFiles.ReadBounded(SafeFiles.Within(legacy,"Courses/categories-ledger.json")));
            string filename=entry["backup"]!.GetValue<string>();byte[] original=SafeFiles.ReadBounded(SafeFiles.Within(legacy,"Courses/Backups/"+filename));
            if(SafeFiles.Hash(original)!=Path.GetFileNameWithoutExtension(filename))throw new InvalidDataException("旧分类备份校验失败。");
            string path=SafeFiles.Within(saved,catRel);var raw=SafeFiles.ReadBounded(path);
            var categories=Translation.Parse(SafeFiles.ReadBounded(Path.Combine(payload,"categories.json"))).AsObject().ToDictionary(k=>k.Key,k=>k.Value!.GetValue<string>());
            var originalNode=Translation.Parse(original);var patched=originalNode.DeepClone();var texts=Translation.ReplaceCategories(patched,categories);
            var currentNode=Translation.Parse(raw);
            if(JsonNode.DeepEquals(currentNode,patched))
            {
                string backup=j.Id+"/legacy-category.bin";SafeFiles.AtomicWrite(SafeFiles.Within(state,backup),original);
                j.Entries.Add(new(){Relative=catRel,BeforeHash=SafeFiles.Hash(original),AfterHash=SafeFiles.Hash(raw),Backup=backup,Texts=texts});imported++;
            }
            else if(!JsonNode.DeepEquals(currentNode,originalNode))skipped++;
        }
        string pakRel="Paks/MyWhooshChinese_P.pak";string pak=SafeFiles.Within(saved,pakRel);
        if(File.Exists(pak)&&!existing.Contains(pakRel))
        {
            if(SafeFiles.HashFile(pak)!=AppInfo.PakHash)throw new InvalidDataException("界面PAK未知，不纳入迁移。");
            j.Entries.Add(new(){Relative=pakRel,BeforeHash=null,AfterHash=AppInfo.PakHash,Backup=null,Texts=[]});imported++;
        }
        closed();if(File.Exists(active))SafeFiles.AtomicWrite(SafeFiles.Within(state,j.Id+"/before-legacy-import-"+Guid.NewGuid().ToString("N")+".json"),SafeFiles.ReadBounded(active));
        j.Status="installed";SafeFiles.AtomicWrite(active,JsonSerializer.SerializeToUtf8Bytes(j,AppInfo.JsonOptions));
        return new("migrated",imported,skipped,"旧恢复记录已核验并导入；只写本工具备份账本，不改游戏。未知修改保留，不宣称可全部恢复。");
    }
    public static OperationResult Current()
    {
        var t=WindowsTarget.Detect();WindowsTarget.AssertSupported(t);
        return Import(t.SavedRoot,AppInfo.State,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MyWhooshChinesePatch"),AppInfo.Payload,WindowsTarget.AssertClosed);
    }
}
