using System.Text;
using System.Text.Json;

namespace Whoosh.Core;

public sealed record WindowState(bool Windowed,int Width,int Height,bool BackupAvailable,bool Topmost,string Message);
public sealed class WindowBackup
{
    public string SavedRoot { get; set; }="";
    public Dictionary<string,byte[]?> Originals { get; set; }=[];
    public Dictionary<string,Dictionary<string,string>> Applied { get; set; }=[];
    public bool Topmost { get; set; }
}
public static class GameWindow
{
    private const string UserSection="/Script/Engine.GameUserSettings";
    private const string ProjectSection="/Script/EngineSettings.GeneralProjectSettings";
    private static readonly string[] Files=["Config/Windows/GameUserSettings.ini","Config/Windows/Game.ini"];
    public static Dictionary<string,string> ReadSection(string text,string section)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);bool inside=false;
        foreach(string line in text.Replace("\r\n","\n").Split('\n'))
        {
            string t=line.Trim();if(t.StartsWith('[')&&t.EndsWith(']')){inside=t[1..^1].Equals(section,StringComparison.OrdinalIgnoreCase);continue;}
            if(!inside||t.StartsWith(';')||t.StartsWith('#'))continue;int i=t.IndexOf('=');if(i>0)values[t[..i].Trim()]=t[(i+1)..].Trim();
        }
        return values;
    }
    public static string UpdateSection(string text,string section,IReadOnlyDictionary<string,string?> changes)
    {
        string nl=text.Contains("\r\n")?"\r\n":"\n";var lines=text.Replace("\r\n","\n").Split('\n').ToList();
        int start=lines.FindIndex(l=>l.Trim().Equals("["+section+"]",StringComparison.OrdinalIgnoreCase));
        if(start<0){if(lines.Count>0&&lines[^1]!="")lines.Add("");lines.Add("["+section+"]");start=lines.Count-1;}
        int end=start+1;while(end<lines.Count&&!lines[end].Trim().StartsWith('['))end++;
        foreach(var c in changes)
        {
            for(int i=end-1;i>start;i--){string t=lines[i].Trim();int eq=t.IndexOf('=');if(eq>0&&t[..eq].Trim().Equals(c.Key,StringComparison.OrdinalIgnoreCase)){lines.RemoveAt(i);end--;}}
            if(c.Value!=null){lines.Insert(end,c.Key+"="+c.Value);end++;}
        }
        return string.Join(nl,lines);
    }
    public static WindowState Inspect(string saved,string state)
    {
        string p=SafeFiles.Within(saved,Files[0]);var vals=File.Exists(p)?ReadSection(Translation.Decode(SafeFiles.ReadBounded(p)).Text,UserSection):[];
        int Read(string k)=>vals.TryGetValue(k,out var s)&&int.TryParse(s,out var n)?n:0;
        string journal=Path.Combine(state,"window.json");
        bool topmost=false;
        if(File.Exists(journal)){try{topmost=JsonSerializer.Deserialize<WindowBackup>(SafeFiles.ReadBounded(journal),AppInfo.JsonOptions)?.Topmost??false;}catch{}}
        return new(Read("FullscreenMode")==2,Read("ResolutionSizeX"),Read("ResolutionSizeY"),File.Exists(journal),topmost,"配置检查不替代游戏实际窗口验证。");
    }
    public static WindowState Apply(string saved,string state,int width,int height,Action closed,bool topmost=false,int fullscreenMode=2)
    {
        if(width<800||width>3840||height<450||height>2160)throw new ArgumentException("窗口范围：宽800–3840，高450–2160。");
        if(fullscreenMode<0||fullscreenMode>2)throw new ArgumentException("窗口模式：0全屏 1无边框 2窗口。");
        string fm=fullscreenMode.ToString();
        bool borderless=fullscreenMode==1;
        closed();SafeFiles.NoLinks(state);Directory.CreateDirectory(state);
        using var guard=new FileStream(Path.Combine(state,"window.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string journal=Path.Combine(state,"window.json");var b=File.Exists(journal)?Load(saved,journal):new WindowBackup{SavedRoot=Path.GetFullPath(saved)};
        b.Topmost=topmost;
        var settings=new Dictionary<string,Dictionary<string,string>>
        {
            [Files[0]]=new(){["FullscreenMode"]=fm,["LastConfirmedFullscreenMode"]=fm,["PreferredFullscreenMode"]=fm,["ResolutionSizeX"]=width.ToString(),["ResolutionSizeY"]=height.ToString(),["LastUserConfirmedResolutionSizeX"]=width.ToString(),["LastUserConfirmedResolutionSizeY"]=height.ToString(),["DesiredScreenWidth"]=width.ToString(),["DesiredScreenHeight"]=height.ToString(),["LastUserConfirmedDesiredScreenWidth"]=width.ToString(),["LastUserConfirmedDesiredScreenHeight"]=height.ToString()},
            [Files[1]]=new(){["bUseBorderlessWindow"]=borderless?"True":"False",["bAllowWindowResize"]="True",["bShouldWindowPreserveAspectRatio"]="False",["bAllowMaximize"]="True",["bAllowMinimize"]="True"}
        };
        var staged=new Dictionary<string,byte[]>();
        foreach(var item in settings)
        {
            string p=SafeFiles.Within(saved,item.Key);byte[]? raw=File.Exists(p)?SafeFiles.ReadBounded(p):null;
            if(!b.Originals.ContainsKey(item.Key))b.Originals[item.Key]=raw;
            var f=raw==null?(Text:"",Encoding:(Encoding)new UTF8Encoding(false,true),Bom:false):Translation.Decode(raw);
            staged[item.Key]=Translation.Encode(UpdateSection(f.Text,item.Key==Files[0]?UserSection:ProjectSection,item.Value.ToDictionary(k=>k.Key,k=>(string?)k.Value)),f.Encoding,f.Bom);
        }
        b.Applied=settings;SafeFiles.AtomicWrite(journal,JsonSerializer.SerializeToUtf8Bytes(b,AppInfo.JsonOptions));
        foreach(var item in staged){closed();SafeFiles.AtomicWrite(SafeFiles.Within(saved,item.Key),item.Value);}
        return Inspect(saved,state);
    }
    private static WindowBackup Load(string saved,string journal)
    {
        var b=JsonSerializer.Deserialize<WindowBackup>(SafeFiles.ReadBounded(journal),AppInfo.JsonOptions)??throw new InvalidDataException("窗口备份损坏。");
        if(!Path.GetFullPath(b.SavedRoot).Equals(Path.GetFullPath(saved),StringComparison.OrdinalIgnoreCase)||b.Originals.Keys.Any(k=>!Files.Contains(k))||b.Applied.Keys.Any(k=>!Files.Contains(k)))throw new InvalidDataException("窗口备份归属不匹配。");return b;
    }
    public static WindowState Restore(string saved,string state,Action closed)
    {
        closed();string journal=Path.Combine(state,"window.json");if(!File.Exists(journal))return Inspect(saved,state);
        using var guard=new FileStream(Path.Combine(state,"window.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var b=Load(saved,journal);
        foreach(var item in b.Originals)
        {
            string p=SafeFiles.Within(saved,item.Key);if(!File.Exists(p))throw new IOException("窗口配置已丢失，请先检查。");
            byte[] raw=SafeFiles.ReadBounded(p);var f=Translation.Decode(raw);string section=item.Key==Files[0]?UserSection:ProjectSection;
            var old=item.Value==null?new Dictionary<string,string>():ReadSection(Translation.Decode(item.Value).Text,section);
            var values=b.Applied[item.Key].Keys.ToDictionary(k=>k,k=>old.TryGetValue(k,out var v)?v:null);
            string reverted=UpdateSection(f.Text,section,values);
            // Preserve unrelated settings; when semantically equal to the baseline restore original bytes.
            if(item.Value!=null&&SameSettings(reverted,Translation.Decode(item.Value).Text))SafeFiles.AtomicWrite(p,item.Value);
            else if(item.Value==null&&string.IsNullOrWhiteSpace(reverted.Replace("["+section+"]","")))File.Delete(p);
            else SafeFiles.AtomicWrite(p,Translation.Encode(reverted,f.Encoding,f.Bom));
        }
        File.Move(journal,journal+".restored-"+Guid.NewGuid().ToString("N"));return Inspect(saved,state);
    }
    private static bool SameSettings(string a,string b)
    {
        static string Norm(string s)=>string.Join("\n",s.Split('\n').Select(x=>x.Trim()).Where(x=>x.Length>0).OrderBy(x=>x,StringComparer.Ordinal));
        return Norm(a)==Norm(b);
    }
    public static WindowState Current()=>Inspect(WindowsTarget.Detect().SavedRoot,AppInfo.State);
    public static WindowState Enable(int w,int h,bool topmost=false,int fullscreenMode=2){var t=WindowsTarget.Detect();WindowsTarget.AssertSupported(t);return Apply(t.SavedRoot,AppInfo.State,w,h,WindowsTarget.AssertClosed,topmost,fullscreenMode);}
    public static WindowState Reset(){var t=WindowsTarget.Detect();return Restore(t.SavedRoot,AppInfo.State,WindowsTarget.AssertClosed);}
    /// <summary>把已运行的游戏窗口置顶或取消置顶；游戏未运行时仅记录偏好。</summary>
    public static WindowState SetTopmostPreference(string saved,string state,bool topmost,Action closed)
    {
        closed();SafeFiles.NoLinks(state);Directory.CreateDirectory(state);
        using var guard=new FileStream(Path.Combine(state,"window.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string journal=Path.Combine(state,"window.json");
        var b=File.Exists(journal)?Load(saved,journal):new WindowBackup{SavedRoot=Path.GetFullPath(saved)};
        b.Topmost=topmost;SafeFiles.AtomicWrite(journal,JsonSerializer.SerializeToUtf8Bytes(b,AppInfo.JsonOptions));
        return Inspect(saved,state);
    }
    /// <summary>对正在运行的 MyWhoosh 窗口执行置顶/取消置顶，返回是否命中窗口。</summary>
    public static bool PinTopmost(bool topmost)
    {
        var hWnd=NativeWindow.FindGameWindow("MyWhoosh");
        if(hWnd==IntPtr.Zero)return false;
        return NativeWindow.SetTopmost(hWnd,topmost);
    }
    /// <summary>持续置顶守护：仅当用户在配置中选择置顶且游戏窗口存在时，重新压回最前。</summary>
    public static bool KeepPinned()
    {
        try
        {
            var t=WindowsTarget.Detect();var s=Inspect(t.SavedRoot,AppInfo.State);
            return s.Topmost&&PinTopmost(true);
        }
        catch{return false;}
    }
    public static WindowState SetTopmost(bool topmost){var t=WindowsTarget.Detect();WindowsTarget.AssertSupported(t);var s=SetTopmostPreference(t.SavedRoot,AppInfo.State,topmost,()=>{});PinTopmost(topmost);return s;}
}
