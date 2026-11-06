using System.Text.Json;
using System.Text.Json.Nodes;

namespace Whoosh.Core;

public sealed class Journal
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Status { get; set; } = "prepared";
    public string Family { get; set; } = "";
    public string SavedRoot { get; set; } = "";
    public string Revision { get; set; } = "";
    public DateTime? CreatedUtc { get; set; }
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
    private Journal? Load() => File.Exists(Active) ? ValidateBytes(SafeFiles.ReadBounded(Active)) : null;
    internal Journal ValidateBytes(byte[] bytes)
    {
        var j=JsonSerializer.Deserialize<Journal>(bytes,AppInfo.JsonOptions)??throw new InvalidDataException("恢复账本为空。");
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
        using var guard=Lock();assertClosed();
        if(DurableBatch.Recover(savedRoot,stateRoot,"pending-install.json","active.json",SafeFiles.AllowedTarget,b=>ValidateBytes(b)))
            throw new IOException("上次增量批次已恢复，请重新检查再安装。");
        var existing=Load();
        if(existing is {Status:"prepared"}){Recover(existing);throw new IOException("检测到上次中断，已尝试回滚；请重新检查后再安装。");}
        if(existing is {Status:"conflict"})throw new IOException("上次事务有冲突，需先恢复或保留冲突文件后处理。");
        var plan=buildPlan();
        if(plan.Changes.Count==0)return new("unchanged",0,plan.Unmatched,"已匹配的内容无需重复安装；非本工具管理的旧补丁请使用原恢复程序。");
        byte[]? beforeMetadata=File.Exists(Active)?SafeFiles.ReadBounded(Active):null;
        bool incremental=existing is {Status:"installed"} && existing.Entries.Any(e=>!e.Restored);
        if(!incremental && existing!=null)SafeFiles.AtomicWrite(SafeFiles.Within(stateRoot,existing.Id+"/completed-journal-"+Guid.NewGuid().ToString("N")+".json"),JsonSerializer.SerializeToUtf8Bytes(existing,AppInfo.JsonOptions));
        var j=incremental?existing!:new Journal{Id=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow,Family=family,SavedRoot=Path.GetFullPath(savedRoot)};
        foreach(var c in plan.Changes)
        {
            if(!SafeFiles.AllowedTarget(c.Relative))throw new InvalidDataException("写入目标不在允许范围。");
            var path=SafeFiles.Within(savedRoot,c.Relative);
            string? before=c.Before==null?null:SafeFiles.Hash(c.Before);
            if((File.Exists(path)?SafeFiles.HashFile(path):null)!=before)throw new IOException("文件已变化，请重新检查。");
            var prior=j.Entries.SingleOrDefault(e=>e.Relative.Equals(c.Relative,StringComparison.OrdinalIgnoreCase));
            if(prior is {Restored:false})
            {
                if(c.Relative=="Paks/MyWhooshChinese_P.pak" && before!=null && before!=prior.AfterHash)
                    throw new IOException("已有界面补丁变化，拒绝更新。");
                // Re-download of the same known course is safe; a new semantic course at the same path is not.
                foreach(var t in c.Texts)
                {
                    var first=prior.Texts.SingleOrDefault(x=>x.Pointer==t.Pointer);
                    if(first==null || first.Original!=t.Original)throw new IOException("同一路径课程原文变化，请先恢复该记录后重新检查。");
                }
                prior.AfterHash=SafeFiles.Hash(c.After);
                foreach(var t in c.Texts)
                {
                    int ix=prior.Texts.FindIndex(x=>x.Pointer==t.Pointer);
                    prior.Texts[ix]=new(t.Pointer,prior.Texts[ix].Original,t.Patched);
                }
            }
            else
            {
                if(prior!=null)j.Entries.Remove(prior);
                string? backup=null;
                if(c.Before!=null){backup=j.Id+"/original-"+Guid.NewGuid().ToString("N")+".bin";SafeFiles.AtomicWrite(SafeFiles.Within(stateRoot,backup),c.Before);}
                j.Entries.Add(new(){Relative=c.Relative,BeforeHash=before,AfterHash=SafeFiles.Hash(c.After),Backup=backup,Texts=c.Texts});
            }
        }
        j.Status="installed";j.Revision=Guid.NewGuid().ToString("N");
        DurableBatch.Apply(savedRoot,stateRoot,"pending-install.json","active.json",
            plan.Changes.Select(c=>new DurableBatch.Change(c.Relative,c.Before,c.After)).ToArray(),
            beforeMetadata,JsonSerializer.SerializeToUtf8Bytes(j,AppInfo.JsonOptions),SafeFiles.AllowedTarget,b=>ValidateBytes(b),assertClosed,fault);
        return new("installed",plan.Changes.Count,plan.Unmatched,incremental?"增量更新完成，首次备份仍保留。":"文件安装与写回校验通过；游戏画面需启动后确认。");    }
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
    private static JsonNode NonText(JsonNode node,List<TextChange> texts)
    {
        var copy=node.DeepClone();foreach(var t in texts)Translation.Set(copy,t.Pointer,"");return copy;
    }
    public OperationResult RecoverPending(Action assertClosed)
    {
        using var guard=Lock();assertClosed();
        bool recovered=DurableBatch.Recover(savedRoot,stateRoot,"pending-install.json","active.json",SafeFiles.AllowedTarget,b=>ValidateBytes(b));
        var j=Load();
        if(j is {Status:"prepared"}){Recover(j);return new(j.Status,0,0,"旧中断记录已处理；请重新检查。冲突现场请保留。");}
        return new(recovered?"recovered":"unchanged",0,0,recovered?"仅处理上次未完成批次，已提交的汉化保留。请重新检查。":"没有待处理中断批次。");
    }
    public OperationResult Restore(Action assertClosed)
    {
        using var guard=Lock();assertClosed();
        DurableBatch.Recover(savedRoot,stateRoot,"pending-install.json","active.json",SafeFiles.AllowedTarget,b=>ValidateBytes(b));
        var j=Load();
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
            if(hash==e.AfterHash && e.Backup!=null &&
                JsonNode.DeepEquals(NonText(Translation.Parse(raw),e.Texts),NonText(Translation.Parse(SafeFiles.ReadBounded(SafeFiles.Within(stateRoot,e.Backup))),e.Texts)))
            {
                if(SafeFiles.HashFile(p)!=hash)throw new IOException("文件在恢复期间变化。");
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
    public (string Status,bool CanRestore,DateTime? CreatedUtc,int Entries) Summary()
    {
        using var guard=Lock();var j=Load();
        bool pending=File.Exists(SafeFiles.Within(stateRoot,"pending-install.json"));
        return (pending?"prepared":j?.Status??"unmanaged",!pending && j is {Status:"installed" or "conflict"} && j.Entries.Any(e=>!e.Restored),j?.CreatedUtc,j?.Entries.Count??0);
    }
    public string Status() => Summary().Status;
}

public static class Operations
{
    public static object Check(bool diagnoseOnly=false)
    {
        var t=WindowsTarget.Detect(diagnoseOnly);
        if(diagnoseOnly)return new {AppInfo.Version,Package=t.Name,GameVersion=t.Version,Family=t.Family,KnownIdentity=t.Family==AppInfo.Family,Message="身份诊断不代表资源兼容性；安装前仍做完整哈希检查。"};
        WindowsTarget.AssertSupported(t);PayloadStore.EnsurePayload(AppInfo.Payload);var p=Translation.Plan(t.SavedRoot,AppInfo.Payload);
        var tx=new Transactions(t.SavedRoot,AppInfo.State,t.Family);
        return new {Supported=true,GameVersion=t.Version,p.Scanned,p.Matched,p.AlreadyChinese,p.Unmatched,PendingFiles=p.Changes.Count,ManagedState=tx.Status(),Message="仅检查文件；不能替代游戏画面验证。"};
    }
    public static OperationResult Install()
    {
        WindowsTarget.AssertClosed();var t=WindowsTarget.Detect();WindowsTarget.AssertSupported(t);PayloadStore.EnsurePayload(AppInfo.Payload);
        return new Transactions(t.SavedRoot,AppInfo.State,t.Family).Apply(()=>Translation.Plan(t.SavedRoot,AppInfo.Payload),WindowsTarget.AssertClosed);
    }
    public static OperationResult Restore()
    {
        WindowsTarget.AssertClosed();var t=WindowsTarget.Detect();
        return RestoreTarget(t,AppInfo.State,WindowsTarget.AssertClosed);
    }
    public static OperationResult RecoverPending()
    {
        WindowsTarget.AssertClosed();var t=WindowsTarget.Detect();
        if(t.Family!=AppInfo.Family)throw new InvalidDataException("包族不匹配。");
        return new Transactions(t.SavedRoot,AppInfo.State,t.Family).RecoverPending(WindowsTarget.AssertClosed);
    }
    public static OperationResult RestoreTarget(Target t,string state,Action closed)
    {
        closed();if(t.Family!=AppInfo.Family)throw new InvalidDataException("包族不匹配。");
        return new Transactions(t.SavedRoot,state,t.Family).Restore(closed);
    }
    public static string Display(object value)=>JsonSerializer.Serialize(value,AppInfo.JsonOptions);
    public static string Log(string command,object value)
    {
        string relative="logs/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff")+"-"+command+".json";
        var p=SafeFiles.Within(AppInfo.Home,relative);SafeFiles.AtomicWrite(p,JsonSerializer.SerializeToUtf8Bytes(value,AppInfo.JsonOptions));return p;
    }
}
