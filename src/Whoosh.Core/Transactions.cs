using System.Text.Json;

namespace Whoosh.Core;

public sealed class Journal
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Status { get; set; } = "prepared";
    public string Family { get; set; } = "";
    public string SavedRoot { get; set; } = "";
    public List<JournalEntry> Entries { get; set; } = [];
}
public sealed class JournalEntry
{
    public string Relative { get; set; } = "";
    public string? BeforeHash { get; set; }
    public string AfterHash { get; set; } = "";
    public string? Backup { get; set; }
    public List<TextChange> Texts { get; set; } = [];
    public bool Restored { get; set; }
}
public sealed record OperationResult(string Status,int Changed,int Skipped,string Message);

public sealed class Transactions(string savedRoot,string stateRoot,string family)
{
    private string Active => Path.Combine(stateRoot,"active.json");
    private void Save(Journal j) => SafeFiles.AtomicWrite(Active,JsonSerializer.SerializeToUtf8Bytes(j,AppInfo.JsonOptions));
    private IDisposable Lock()
    {
        SafeFiles.NoLinks(stateRoot);Directory.CreateDirectory(stateRoot);
        try{return new FileStream(Path.Combine(stateRoot,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){throw new IOException("另一个操作正在执行，请等待完成。");}
    }
    private Journal? Load()
    {
        if(!File.Exists(Active)) return null;
        var j=JsonSerializer.Deserialize<Journal>(SafeFiles.ReadBounded(Active),AppInfo.JsonOptions)??throw new InvalidDataException("恢复账本为空。");
        if(j.Schema!=1 || j.Family!=family || !Path.GetFullPath(j.SavedRoot).Equals(Path.GetFullPath(savedRoot),StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(j.Id,"N",out _)) throw new InvalidDataException("恢复账本归属或版本不匹配。");
        if(j.Entries.Count>20000)throw new InvalidDataException("恢复账本条目异常。");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in j.Entries)
        {
            if(!SafeFiles.AllowedTarget(e.Relative)||!seen.Add(e.Relative))throw new InvalidDataException("恢复目标不合法或重复。");
            _=SafeFiles.Within(savedRoot,e.Relative);
            if(e.Backup!=null)
            {
                if(!e.Backup.StartsWith(j.Id+"/",StringComparison.Ordinal))throw new InvalidDataException("备份路径不属于当前事务。");
                var path=SafeFiles.Within(stateRoot,e.Backup);
                if(SafeFiles.HashFile(path)!=e.BeforeHash)throw new InvalidDataException("备份损坏，停止恢复。");
            }
            else if(e.BeforeHash!=null)throw new InvalidDataException("恢复账本缺少备份。");
            if(e.Relative=="Paks/MyWhooshChinese_P.pak")
            {if(e.AfterHash!=AppInfo.PakHash || e.Texts.Count!=0)throw new InvalidDataException("非法补丁账本。");}
            else if(!e.Relative.EndsWith("/WorkoutCategory.json",StringComparison.Ordinal))
            {if(e.Texts.Any(t=>t.Pointer!="/Name"&&!t.Pointer.Equals("/Description",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("拒绝非课程文字恢复。");}
            if(e.Texts.Count>0)
            {
                if(e.Backup==null)throw new InvalidDataException("文字恢复缺少原始备份。");
                var original=Translation.Parse(SafeFiles.ReadBounded(SafeFiles.Within(stateRoot,e.Backup)));
                foreach(var text in e.Texts)
                    if(Translation.Get(original,text.Pointer)?.GetValue<string>()!=text.Original)throw new InvalidDataException("账本原文与备份不一致。");
            }
        }
        return j;
    }
    public OperationResult Apply(Func<PatchPlan> buildPlan,Action assertClosed,Action<int>? fault=null)
    {
        using var guard=Lock();assertClosed();var existing=Load();
        if(existing is {Status:"prepared"}){Recover(existing);throw new IOException("检测到上次中断，已尝试回滚；请重新检查后再安装。");}
        if(existing is {Status:"conflict"})throw new IOException("上次事务有冲突，需先恢复或保留冲突文件后处理。");
        var plan=buildPlan();
        if(plan.Changes.Count==0)return new("unchanged",0,plan.Unmatched,"已匹配的内容无需重复安装；非本工具管理的旧补丁请使用原恢复程序。");
        if(existing is {Status:"installed"} && existing.Entries.Any(e=>!e.Restored))throw new IOException("已有本工具安装记录，请先恢复后更新，避免丢失首次备份。");
        if(existing!=null)SafeFiles.AtomicWrite(SafeFiles.Within(stateRoot,existing.Id+"/completed-journal.json"),JsonSerializer.SerializeToUtf8Bytes(existing,AppInfo.JsonOptions));
        var j=new Journal{Id=Guid.NewGuid().ToString("N"),Family=family,SavedRoot=Path.GetFullPath(savedRoot)};
        int i=0;
        foreach(var c in plan.Changes)
        {
            if(!SafeFiles.AllowedTarget(c.Relative))throw new InvalidDataException("写入目标不在允许范围。");
            var path=SafeFiles.Within(savedRoot,c.Relative);
            string? before=c.Before==null?null:SafeFiles.Hash(c.Before);
            if((File.Exists(path)?SafeFiles.HashFile(path):null)!=before)throw new IOException("文件已变化，请重新检查。");
            string? backup=null;
            if(c.Before!=null){backup=j.Id+"/"+(i++)+".bin";SafeFiles.AtomicWrite(SafeFiles.Within(stateRoot,backup),c.Before);}
            j.Entries.Add(new(){Relative=c.Relative,BeforeHash=before,AfterHash=SafeFiles.Hash(c.After),Backup=backup,Texts=c.Texts});
        }
        Save(j);int written=0;
        try
        {
            foreach(var c in plan.Changes)
            {
                assertClosed();var path=SafeFiles.Within(savedRoot,c.Relative);
                if((File.Exists(path)?SafeFiles.HashFile(path):null)!=(c.Before==null?null:SafeFiles.Hash(c.Before)))throw new IOException("并发改动，停止写入。");
                SafeFiles.AtomicWrite(path,c.After);
                if(SafeFiles.HashFile(path)!=SafeFiles.Hash(c.After))throw new IOException("安装后哈希失败。");
                written++;
                fault?.Invoke(written);
            }
            j.Status="installed";Save(j);return new("installed",written,plan.Unmatched,"文件安装与写回校验通过；游戏画面需启动后确认。");
        }
        catch{Recover(j);throw;}
    }
    private void Recover(Journal j)
    {
        int conflicts=0;
        foreach(var e in j.Entries.AsEnumerable().Reverse())
        {
            var p=SafeFiles.Within(savedRoot,e.Relative);string? h=File.Exists(p)?SafeFiles.HashFile(p):null;
            if(h==e.BeforeHash)continue;
            if(h!=e.AfterHash){conflicts++;continue;}
            if(e.Backup==null)File.Delete(p);else SafeFiles.AtomicWrite(p,SafeFiles.ReadBounded(SafeFiles.Within(stateRoot,e.Backup),32*1024*1024));
        }
        j.Status=conflicts==0?"rolled-back":"conflict";Save(j);
    }
    public OperationResult Restore(Action assertClosed)
    {
        using var guard=Lock();assertClosed();var j=Load();
        if(j==null)return new("unmanaged",0,0,"没有本工具备份账本。不会认领或删除旧安装器生成的补丁。");
        if(j.Status=="prepared"){Recover(j);return new(j.Status,0,0,"已处理中断事务。");}
        int changed=0,skipped=0;
        foreach(var e in j.Entries)
        {
            if(e.Restored)continue;assertClosed();var p=SafeFiles.Within(savedRoot,e.Relative);
            if(!File.Exists(p)){if(e.BeforeHash==null)e.Restored=true;else skipped++;Save(j);continue;}
            byte[] raw=SafeFiles.ReadBounded(p,32*1024*1024);var hash=SafeFiles.Hash(raw);
            if(hash==e.BeforeHash){e.Restored=true;Save(j);continue;}
            if(e.Relative=="Paks/MyWhooshChinese_P.pak")
            {
                if(hash!=e.AfterHash){skipped++;continue;}
                if(SafeFiles.HashFile(p)!=hash)throw new IOException("文件在恢复期间变化。");
                if(e.Backup==null)File.Delete(p);else SafeFiles.AtomicWrite(p,SafeFiles.ReadBounded(SafeFiles.Within(stateRoot,e.Backup),32*1024*1024));
                e.Restored=true;changed++;Save(j);continue;
            }
            if(hash==e.AfterHash)
            {
                SafeFiles.AtomicWrite(p,SafeFiles.ReadBounded(SafeFiles.Within(stateRoot,e.Backup!)));e.Restored=true;changed++;Save(j);continue;
            }
            // Restore only unchanged translated fields; retain later favorite/state edits.
            var node=Translation.Parse(raw);int fields=0;bool conflict=false;
            foreach(var t in e.Texts)
            {
                string? current;
                try{current=Translation.Get(node,t.Pointer)?.GetValue<string>();}catch{conflict=true;continue;}
                if(current==t.Patched){Translation.Set(node,t.Pointer,t.Original);fields++;}
                else if(current!=t.Original)conflict=true;
            }
            if(fields>0)
            {
                if(SafeFiles.HashFile(p)!=hash)throw new IOException("文件在恢复期间变化。");
                SafeFiles.AtomicWrite(p,Translation.Serialize(node,raw));changed++;
            }
            if(conflict)skipped++;else e.Restored=true;Save(j);
        }
        j.Status=j.Entries.All(e=>e.Restored)?"restored":"conflict";Save(j);
        return new(j.Status,changed,skipped,skipped>0?"已恢复可安全恢复的内容；未知修改被保留。":"已恢复本工具管理的内容，保留后续非文字状态。");
    }
    public string Status()
    {
        using var guard=Lock();var j=Load();return j?.Status??"unmanaged";
    }
}

public static class Operations
{
    public static object Check(bool diagnoseOnly=false)
    {
        var t=WindowsTarget.Detect(diagnoseOnly);
        if(diagnoseOnly)return new {AppInfo.Version,Package=t.Name,GameVersion=t.Version,Family=t.Family,KnownIdentity=t.Family==AppInfo.Family,Message="身份诊断不代表资源兼容性；安装前仍做完整哈希检查。"};
        WindowsTarget.AssertSupported(t);PayloadStore.Validate(AppInfo.Payload);var p=Translation.Plan(t.SavedRoot,AppInfo.Payload);
        var tx=new Transactions(t.SavedRoot,AppInfo.State,t.Family);
        return new {Supported=true,p.Scanned,p.Matched,p.AlreadyChinese,p.Unmatched,PendingFiles=p.Changes.Count,ManagedState=tx.Status(),Message="仅检查文件；不能替代游戏画面验证。"};
    }
    public static OperationResult Install()
    {
        WindowsTarget.AssertClosed();var t=WindowsTarget.Detect();WindowsTarget.AssertSupported(t);PayloadStore.Validate(AppInfo.Payload);
        return new Transactions(t.SavedRoot,AppInfo.State,t.Family).Apply(()=>Translation.Plan(t.SavedRoot,AppInfo.Payload),WindowsTarget.AssertClosed);
    }
    public static OperationResult Restore()
    {
        WindowsTarget.AssertClosed();var t=WindowsTarget.Detect();
        if(t.Family!=AppInfo.Family)throw new InvalidDataException("包族不匹配。");
        return new Transactions(t.SavedRoot,AppInfo.State,t.Family).Restore(WindowsTarget.AssertClosed);
    }
    public static string Display(object value)=>JsonSerializer.Serialize(value,AppInfo.JsonOptions);
    public static string Log(string command,object value)
    {
        string relative="logs/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff")+"-"+command+".json";
        var p=SafeFiles.Within(AppInfo.Home,relative);SafeFiles.AtomicWrite(p,JsonSerializer.SerializeToUtf8Bytes(value,AppInfo.JsonOptions));return p;
    }
}
