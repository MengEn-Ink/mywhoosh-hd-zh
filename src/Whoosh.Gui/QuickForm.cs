using System.Text.Json;
using Whoosh.Core;

namespace Whoosh.Gui;

internal sealed class QuickPreferences
{
    public bool ShowFloating { get; set; } = true;
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    internal static QuickPreferences Load(string home)
    {
        try{return JsonSerializer.Deserialize<QuickPreferences>(SafeFiles.ReadBounded(Path.Combine(home,"quick-ui.json")),AppInfo.JsonOptions)??new();}
        catch{return new();}
    }
    internal void Save(string home)=>SafeFiles.AtomicWrite(SafeFiles.Within(home,"quick-ui.json"),JsonSerializer.SerializeToUtf8Bytes(this,AppInfo.JsonOptions));
    internal static Point Fit(Point wanted,Size size,IEnumerable<Rectangle> screens)
    {
        var all=screens.ToArray();if(all.Length==0)return Point.Empty;
        var r=all.FirstOrDefault(s=>s.Contains(wanted));if(r==Rectangle.Empty)r=all[0];
        return new Point(Math.Clamp(wanted.X,r.Left,Math.Max(r.Left,r.Right-size.Width)),Math.Clamp(wanted.Y,r.Top,Math.Max(r.Top,r.Bottom-size.Height)));
    }
}

internal sealed class QuickForm : Form
{
    private readonly MainForm _main;
    private readonly Label _status;
    private readonly Button _pin;
    private readonly ComboBox _size;
    private readonly Button _apply;
    internal Button PinButton=>_pin;
    internal Button ApplyButton=>_apply;
    internal ComboBox SizeChoice=>_size;
    internal Button ConsoleButton { get; }
    internal string StatusText=>_status.Text;
    internal event Action? HideRequested;
    internal event Action? ConsoleRequested;
    internal event Action? Moved;
    private Point? _drag;
    private bool _syncingSize;
    internal QuickForm(MainForm main)
    {
        _main=main;Text="MyWhoosh 快捷浮窗";FormBorderStyle=FormBorderStyle.FixedToolWindow;
        StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(340,244);BackColor=Palette.Bg;ForeColor=Palette.Ink;
        Font=new Font(Palette.Family,9f);Icon=AppIcon.Load();
        var header=new Label{Text="WHOOSH · 快捷设置   拖动此处移动",Dock=DockStyle.Top,Height=38,Padding=new Padding(12,10,0,0),ForeColor=Palette.Accent,BackColor=Palette.Rail};
        header.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left)_drag=new Point(e.X,e.Y);};
        header.MouseMove+=(_,e)=>{if(_drag is {} p && e.Button==MouseButtons.Left){Location=QuickPreferences.Fit(new Point(Left+e.X-p.X,Top+e.Y-p.Y),Size,Screen.AllScreens.Select(s=>s.WorkingArea));}};
        header.MouseUp+=(_,_)=>{_drag=null;Moved?.Invoke();};
        _status=new Label{Left=12,Top=48,Width=316,Height=42,AutoEllipsis=true};
        _pin=MakeButton("开启游戏置顶",12,94,316,32,()=>_main.DispatchQuick("toggle-topmost"));
        _size=new ComboBox{Left=12,Top=138,Width=160,DropDownStyle=ComboBoxStyle.DropDownList};
        _size.Items.AddRange(new object[]{"1280 × 720","1600 × 900","1920 × 1080","2560 × 1440"});_size.SelectedIndex=0;
        _size.SelectedIndexChanged+=(_,_)=>{if(_syncingSize||_size.SelectedIndex<0)return;var s=Sizes[_size.SelectedIndex];_main.DispatchQuick($"size:{s.W}:{s.H}");};
        _apply=MakeButton("退出游戏后应用",180,136,148,30,()=>_main.DispatchQuick(_main.View.WindowInterrupted?"recover-window":"apply-window"));
        ConsoleButton=MakeButton("打开主面板",12,180,150,30,()=>ConsoleRequested?.Invoke());
        var hide=MakeButton("收起到托盘",178,180,150,30,()=>HideRequested?.Invoke());
        var note=new Label{Text="浮窗需助手运行；尺寸需退出游戏后应用。",Left=12,Top=220,Width=316,Height=18,ForeColor=Palette.Muted};
        Controls.AddRange([header,_status,_pin,_size,_apply,ConsoleButton,hide,note]);
        ResizeEnd+=(_,_)=>Moved?.Invoke();
        FormClosing+=(_,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Moved?.Invoke();HideRequested?.Invoke();}};
        RefreshFromMain();
    }
    internal static readonly (int W,int H)[] Sizes=[(1280,720),(1600,900),(1920,1080),(2560,1440)];
    private static Button MakeButton(string text,int x,int y,int w,int h,Action action)
    {
        var b=new Button{Text=text,Left=x,Top=y,Width=w,Height=h,FlatStyle=FlatStyle.Flat,BackColor=Palette.Panel,ForeColor=Palette.Ink};
        b.FlatAppearance.BorderColor=Palette.Border;b.Click+=(_,_)=>action();return b;
    }
    internal void RefreshFromMain()
    {
        var v=_main.View;
        string status=_main.IsWorking?"处理中，窗口仍可拖动…":$"游戏：{(v.GameRunning?"运行中":"未启动")} · 实际置顶：{v.TopmostText}\n实际尺寸：{v.ActualSize}";
        if(_status.Text!=status)_status.Text=status;
        string pinText=v.Topmost?"关闭游戏置顶":"开启游戏置顶";if(_pin.Text!=pinText)_pin.Text=pinText;
        if(!_size.DroppedDown)
        {
            int selected=Array.FindIndex(Sizes,s=>s.W==v.WinW&&s.H==v.WinH);
            if(_size.SelectedIndex!=selected){_syncingSize=true;try{_size.SelectedIndex=selected;}finally{_syncingSize=false;}}
        }
        _pin.Enabled=!_main.IsWorking&&v.Supported&&!v.WindowInterrupted;
        _apply.Enabled=!_main.IsWorking&&v.Supported;
        _apply.Text=v.WindowInterrupted?"处理窗口中断":"退出游戏后应用";
        _size.Enabled=!_main.IsWorking;
    }
}

internal static class AppIcon
{
    internal static Icon Load()
    {
        using var stream=typeof(AppIcon).Assembly.GetManifestResourceStream("Whoosh.Gui.app.ico")??throw new InvalidDataException("应用图标缺失。");
        using var icon=new Icon(stream);return (Icon)icon.Clone();
    }
}
