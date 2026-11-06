using System.Text.Json;

namespace Whoosh.Core;

/// <summary>Persistent multi-file batch. Commit the metadata last; recover only the current batch.</summary>
public static class DurableBatch
{
    public sealed class Record
    {
        public string Id { get; set; } = "";
        public string Root { get; set; } = "";
        public string Metadata { get; set; } = "";
        public string? BeforeMetadata { get; set; }
        public string? BeforeMetadataHash { get; set; }
        public string AfterMetadata { get; set; } = "";
        public string AfterMetadataHash { get; set; } = "";
        public List<Item> Items { get; set; } = [];
    }
    public sealed class Item
    {
        public string Relative { get; set; } = "";
        public string? BeforeHash { get; set; }
        public string? Backup { get; set; }
        public string? AfterHash { get; set; }
    }
    public sealed record Change(string Relative, byte[]? Before, byte[]? After);
    private static string? HashAt(string p) => File.Exists(p) ? SafeFiles.HashFile(p) : null;
    private static void Put(string p, byte[]? bytes) { if (bytes == null) { SafeFiles.NoLinks(p); if (File.Exists(p)) File.Delete(p); } else SafeFiles.AtomicWrite(p, bytes); }

    public static bool Recover(string root, string state, string pendingName, string metadataName,
        Func<string, bool> allowed, Action<byte[]> validateMetadata)
    {
        string pending = SafeFiles.Within(state, pendingName);
        if (!File.Exists(pending)) return false;
        var r = JsonSerializer.Deserialize<Record>(SafeFiles.ReadBounded(pending), AppInfo.JsonOptions)
            ?? throw new InvalidDataException("批次记录为空。");
        if (!Guid.TryParseExact(r.Id, "N", out _) || r.Root != Path.GetFullPath(root) || r.Metadata != metadataName || r.Items.Count > 20000)
            throw new InvalidDataException("批次记录归属不匹配。");
        byte[] LoadBlob(string rel, string hash)
        {
            if (!rel.StartsWith("batch-" + r.Id + "/", StringComparison.Ordinal)) throw new InvalidDataException("批次备份路径不匹配。");
            byte[] bytes = SafeFiles.ReadBounded(SafeFiles.Within(state, rel), 32 * 1024 * 1024);
            if (SafeFiles.Hash(bytes) != hash) throw new InvalidDataException("批次备份已损坏。");
            return bytes;
        }
        byte[] after = LoadBlob(r.AfterMetadata, r.AfterMetadataHash);
        validateMetadata(after);
        byte[]? before = r.BeforeMetadata == null ? null : LoadBlob(r.BeforeMetadata, r.BeforeMetadataHash ?? "");
        if (before != null) validateMetadata(before);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rollback = new List<(string Path, byte[]? Bytes, Item Entry)>();
        foreach (var e in r.Items)
        {
            if (!allowed(e.Relative) || !seen.Add(e.Relative)) throw new InvalidDataException("批次目标非法或重复。");
            var path = SafeFiles.Within(root, e.Relative);
            byte[]? bytes = e.Backup == null ? null : LoadBlob(e.Backup, e.BeforeHash ?? "");
            if (e.Backup == null && e.BeforeHash != null) throw new InvalidDataException("批次缺少备份。");
            rollback.Add((path, bytes, e));
        }
        string meta = SafeFiles.Within(state, metadataName);
        string? current = HashAt(meta);
        // Metadata is written only after every file was verified. A crash after that is committed.
        if (current == r.AfterMetadataHash) { File.Delete(pending); return true; }
        if (current != r.BeforeMetadataHash) throw new IOException("批次账本被外部修改，保留现场。");
        bool conflict = false;
        foreach (var e in rollback.AsEnumerable().Reverse())
        {
            string? hash = HashAt(e.Path);
            if (hash == e.Entry.BeforeHash) continue;
            if (hash != e.Entry.AfterHash) { conflict = true; continue; }
            Put(e.Path, e.Bytes);
        }
        if (conflict) throw new IOException("批次回滚有冲突，未知修改已保留。请勿删除批次备份。");
        Put(meta, before);
        File.Delete(pending);
        return true;
    }

    public static void Apply(string root, string state, string pendingName, string metadataName,
        IReadOnlyList<Change> changes, byte[]? beforeMetadata, byte[] afterMetadata,
        Func<string, bool> allowed, Action<byte[]> validateMetadata, Action closed, Action<int>? fault = null)
    {
        SafeFiles.NoLinks(state);
        if (File.Exists(SafeFiles.Within(state, pendingName))) throw new IOException("存在待恢复批次。");
        if(beforeMetadata!=null && SafeFiles.Hash(beforeMetadata)==SafeFiles.Hash(afterMetadata) && changes.Count>0)throw new InvalidDataException("批次提交标记必须唯一。");
        validateMetadata(afterMetadata);
        if (beforeMetadata != null) validateMetadata(beforeMetadata);
        if (HashAt(SafeFiles.Within(state, metadataName)) != (beforeMetadata == null ? null : SafeFiles.Hash(beforeMetadata)))
            throw new IOException("账本在规划期间变化。");
        var r = new Record { Id = Guid.NewGuid().ToString("N"), Root = Path.GetFullPath(root), Metadata = metadataName };
        string Blob(string name, byte[] data) { string rel = "batch-" + r.Id + "/" + name; SafeFiles.AtomicWrite(SafeFiles.Within(state, rel), data); return rel; }
        if (beforeMetadata != null) { r.BeforeMetadata = Blob("before-meta.bin", beforeMetadata); r.BeforeMetadataHash = SafeFiles.Hash(beforeMetadata); }
        r.AfterMetadata = Blob("after-meta.bin", afterMetadata); r.AfterMetadataHash = SafeFiles.Hash(afterMetadata);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var c in changes)
        {
            if (!allowed(c.Relative) || !seen.Add(c.Relative)) throw new InvalidDataException("批次目标非法或重复。");
            string? beforeHash = c.Before == null ? null : SafeFiles.Hash(c.Before);
            if (HashAt(SafeFiles.Within(root, c.Relative)) != beforeHash) throw new IOException("文件在规划期间变化。");
            r.Items.Add(new Item { Relative = c.Relative, BeforeHash = beforeHash,
                Backup = c.Before == null ? null : Blob("current-" + i + ".bin", c.Before),
                AfterHash = c.After == null ? null : SafeFiles.Hash(c.After) });
            i++;
        }
        string pending = SafeFiles.Within(state, pendingName);
        SafeFiles.AtomicWrite(pending, JsonSerializer.SerializeToUtf8Bytes(r, AppInfo.JsonOptions));
        try
        {
            i = 0;
            foreach (var c in changes)
            {
                closed(); string path = SafeFiles.Within(root, c.Relative);
                if (HashAt(path) != r.Items[i].BeforeHash) throw new IOException("批次写入前发现并发修改。");
                Put(path, c.After);
                if (HashAt(path) != r.Items[i].AfterHash) throw new IOException("批次写入校验失败。");
                i++;
                fault?.Invoke(i);
            }
            closed();
            if(HashAt(SafeFiles.Within(state,metadataName))!=r.BeforeMetadataHash)throw new IOException("批次提交前账本发生变化。");
            Put(SafeFiles.Within(state, metadataName), afterMetadata);
            fault?.Invoke(-1); // test boundary: metadata committed but pending marker not removed
            // Keep immutable batches for inspection; never delete user or game backups here.
            File.Delete(pending);
        }
        catch { Recover(root, state, pendingName, metadataName, allowed, validateMetadata); throw; }
    }
}
