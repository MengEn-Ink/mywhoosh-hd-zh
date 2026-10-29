using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Whoosh.Core;

public static class PayloadStore
{
    public static void Validate(string folder)
    {
        foreach (var item in AppInfo.PayloadHashes)
        {
            var p = SafeFiles.Within(folder, item.Key);
            if (!File.Exists(p) || SafeFiles.HashFile(p) != item.Value) throw new InvalidDataException("资源未导入或校验失败：" + item.Key);
        }
    }
    public static void ImportZip(string zipPath, string destination)
    {
        SafeFiles.NoLinks(destination);
        using var input = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 128 * 1024 * 1024) throw new InvalidDataException("压缩包过大。");
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 256) throw new InvalidDataException("压缩包条目过多。");
        var picked = new Dictionary<string, byte[]>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            string rel = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(rel)) continue;
            _ = SafeFiles.Within(destination, rel);
            if (!names.Add(rel)) throw new InvalidDataException("压缩包存在重复路径。");
            string name = rel.Split('/').Last();
            if (!AppInfo.PayloadHashes.TryGetValue(name, out var expected)) continue;
            if (entry.Length > 32 * 1024 * 1024 || picked.ContainsKey(name)) throw new InvalidDataException("资源条目异常。");
            using var stream = entry.Open(); using var mem = new MemoryStream();
            byte[] buffer = new byte[8192]; int count;
            while ((count = stream.Read(buffer)) > 0) { if (mem.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("资源解压超限。"); mem.Write(buffer, 0, count); }
            byte[] bytes = mem.ToArray();
            if (SafeFiles.Hash(bytes) != expected) throw new InvalidDataException("资源 SHA-256 不匹配：" + name);
            picked.Add(name, bytes);
        }
        if (picked.Count != AppInfo.PayloadHashes.Count) throw new InvalidDataException("缺少完整的三项汉化资源。");
        foreach (var item in picked) SafeFiles.AtomicWrite(SafeFiles.Within(destination, item.Key), item.Value);
        Validate(destination);
    }
}

public sealed record TextChange(string Pointer, string Original, string Patched);
public sealed record PlannedChange(string Relative, byte[]? Before, byte[] After, List<TextChange> Texts);
public sealed record PatchPlan(List<PlannedChange> Changes, int Scanned, int Matched, int AlreadyChinese, int Unmatched);

public static class Translation
{
    public static (string Text, Encoding Encoding, bool Bom) Decode(byte[] raw)
    {
        if (raw.AsSpan().StartsWith(Encoding.UTF8.Preamble)) return (new UTF8Encoding(false, true).GetString(raw.AsSpan(3)), new UTF8Encoding(false, true), true);
        if (raw.AsSpan().StartsWith(Encoding.Unicode.Preamble)) return (new UnicodeEncoding(false, false, true).GetString(raw.AsSpan(2)), new UnicodeEncoding(false, false, true), true);
        if (raw.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble)) return (new UnicodeEncoding(true, false, true).GetString(raw.AsSpan(2)), new UnicodeEncoding(true, false, true), true);
        return (new UTF8Encoding(false, true).GetString(raw), new UTF8Encoding(false, true), false);
    }
    public static byte[] Encode(string text, Encoding encoding, bool bom)
    {
        byte[] bytes = encoding.GetBytes(text);
        if (!bom) return bytes;
        byte[] prefix = encoding.CodePage == 1200 ? [0xff,0xfe] : encoding.CodePage == 1201 ? [0xfe,0xff] : [0xef,0xbb,0xbf];
        return [..prefix, ..bytes];
    }
    public static JsonNode Parse(byte[] raw) => JsonNode.Parse(Decode(raw).Text) ?? throw new InvalidDataException("空 JSON。");
    public static byte[] Serialize(JsonNode node, byte[] original)
    {
        var format = Decode(original); return Encode(node.ToJsonString(AppInfo.JsonOptions), format.Encoding, format.Bom);
    }
    private static string Esc(string s) => s.Replace("~", "~0").Replace("/", "~1");
    public static JsonNode? Get(JsonNode node, string pointer)
    {
        foreach (var part in pointer.TrimStart('/').Split('/'))
        {
            string key = part.Replace("~1", "/").Replace("~0", "~");
            node = (node is JsonArray arr ? arr[int.Parse(key)] : node[key])!;
            if (node == null) return null;
        }
        return node;
    }
    public static void Set(JsonNode node, string pointer, string text)
    {
        int cut = pointer.LastIndexOf('/'); string key = pointer[(cut+1)..].Replace("~1", "/").Replace("~0", "~");
        var parent = cut == 0 ? node : Get(node, pointer[..cut])!;
        if (parent is JsonArray array) array[int.Parse(key)] = text; else parent[key] = text;
    }
    public static List<TextChange> ReplaceCategories(JsonNode node, Dictionary<string,string> dictionary, string pointer = "")
    {
        var changes = new List<TextChange>();
        IEnumerable<(string,JsonNode?)> children = node is JsonObject obj ? obj.Select(kv => (Esc(kv.Key),kv.Value)).ToArray() : node is JsonArray arr ? arr.Select((v,i)=>(i.ToString(),v)).ToArray() : [];
        foreach (var (key,value) in children)
        {
            if (value is JsonValue v && v.TryGetValue<string>(out var s) && dictionary.TryGetValue(s,out var replacement))
            { string path = pointer+"/"+key; Set(node,"/"+key,replacement); changes.Add(new(path,s,replacement)); }
            else if (value is JsonObject or JsonArray) changes.AddRange(ReplaceCategories(value,dictionary,pointer+"/"+key));
        }
        return changes;
    }
    public static PatchPlan Plan(string saved, string payload)
    {
        var dict = Parse(SafeFiles.ReadBounded(Path.Combine(payload,"courses.json"))).AsArray();
        var lookup = new Dictionary<(string,string),(string,string)>(); var translated = new HashSet<(string,string)>();
        foreach(var row in dict)
        {
            string name=row!["Name"]!.GetValue<string>(), desc=row["Description"]!.GetValue<string>();
            var val=(row["ChineseName"]!.GetValue<string>(),row["ChineseDescription"]!.GetValue<string>());
            if (lookup.TryGetValue((name,desc),out var old) && old!=val) throw new InvalidDataException("译文字典存在冲突。");
            lookup[(name,desc)]=val; translated.Add(val);
        }
        var changes = new List<PlannedChange>(); int scanned=0,matched=0,already=0,unmatched=0;
        var pak=SafeFiles.Within(saved,"Paks/MyWhooshChinese_P.pak"); var pakBytes=SafeFiles.ReadBounded(Path.Combine(payload,"MyWhooshChinese_P.pak"),32*1024*1024);
        if (File.Exists(pak)) { if(SafeFiles.HashFile(pak)!=SafeFiles.Hash(pakBytes)) throw new IOException("已有未知同名界面补丁，拒绝覆盖。"); }
        else changes.Add(new("Paks/MyWhooshChinese_P.pak",null,pakBytes,[]));
        foreach(string folder in new[]{"PersistentDownloadDir/DefaultCache/Cycling/Trainingplans/Workouts","PersistentDownloadDir/DefaultCache/Cycling/Workouts/Categories"})
        {
            string root=SafeFiles.Within(saved,folder); if(!Directory.Exists(root)) continue;
            var options=new EnumerationOptions { RecurseSubdirectories=true,IgnoreInaccessible=false,AttributesToSkip=FileAttributes.ReparsePoint };
            foreach(string file in Directory.EnumerateFiles(root,"*.json",options).OrderBy(p=>p,StringComparer.Ordinal))
            {
                scanned++; byte[] raw=SafeFiles.ReadBounded(file); var node=Parse(raw);
                if(node is not JsonObject obj) { unmatched++; continue; }
                string? dk=obj.Select(k=>k.Key).FirstOrDefault(k=>k.Equals("Description",StringComparison.OrdinalIgnoreCase));
                string? n=obj["Name"]?.GetValue<string>(),d=dk==null?null:obj[dk]?.GetValue<string>();
                if(n==null||d==null){unmatched++;continue;}
                if(translated.Contains((n,d))){already++;matched++;continue;}
                if(!lookup.TryGetValue((n,d),out var t)){unmatched++;continue;}
                var edits=new List<TextChange>();
                if(n!=t.Item1){obj["Name"]=t.Item1;edits.Add(new("/Name",n,t.Item1));}
                if(d!=t.Item2){obj[dk!]=t.Item2;edits.Add(new("/"+Esc(dk!),d,t.Item2));}
                matched++;
                if(edits.Count>0)changes.Add(new(Path.GetRelativePath(saved,file).Replace('\\','/'),raw,Serialize(node,raw),edits));
            }
        }
        if(scanned==0)throw new InvalidDataException("未发现课程缓存：先打开游戏训练列表，退出后重试。");
        string categoryRel="PersistentDownloadDir/DefaultCache/Cycling/Workouts/WorkoutCategory.json"; string category=SafeFiles.Within(saved,categoryRel);
        if(File.Exists(category))
        {
            var cd=Parse(SafeFiles.ReadBounded(Path.Combine(payload,"categories.json"))).AsObject().ToDictionary(k=>k.Key,k=>k.Value!.GetValue<string>());
            byte[] raw=SafeFiles.ReadBounded(category);var n=Parse(raw);var texts=ReplaceCategories(n,cd);
            if(texts.Count>0)changes.Add(new(categoryRel,raw,Serialize(n,raw),texts));
        }
        return new(changes,scanned,matched,already,unmatched);
    }
}
