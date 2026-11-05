using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.IO;
using Whoosh.Core;
namespace Whoosh.Gui;

internal static class Program
{
    [STAThread]static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException+=(s,e)=>File.WriteAllText(Path.Combine(Path.GetTempPath(),"mywhooshzh-crash.log"),"THREAD: "+e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException+=(s,e)=>File.WriteAllText(Path.Combine(Path.GetTempPath(),"mywhooshzh-crash.log"),"DOMAIN: "+e.ExceptionObject);
        if(args.Length>=2&&args[0]=="--screenshot")
        {
            using var f=new MainForm();f.ClientSize=new(1180,780);f.Show();Application.DoEvents();
            using var bmp=new Bitmap(f.ClientSize.Width,f.ClientSize.Height);
            f.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);
            f.Close();return;
        }
        Application.Run(new MainForm());
    }
}

#region 颜色与基础控件
internal static class Theme
{
    public static readonly Color Bg=Color.FromArgb(17,18,20);
    public static readonly Color Sidebar=Color.FromArgb(27,28,31);
    public static readonly Color Card=Color.FromArgb(38,39,42);
    public static readonly Color CardHover=Color.FromArgb(48,49,52);
    public static readonly Color Text=Color.FromArgb(237,239,247);
    public static readonly Color Muted=Color.FromArgb(164,171,188);
    public static readonly Color Blue=Color.FromArgb(47,126,247);
    public static readonly Color Green=Color.FromArgb(42,184,93);
    public static readonly Color Red=Color.FromArgb(230,80,80);
    public static readonly Color Border=Color.FromArgb(55,57,62);
}

/// <summary>卡片面板：纯色填充，方形（禁用 Region 以避免 GDI 对象泄漏与布局递归）。</summary>
public sealed class Card:Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius{get;set;}
    public Card(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);BackColor=Theme.Card;}
}

/// <summary>自定义开关。</summary>
public sealed class Toggle:Control
{
    private bool on;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On{get=>on;set{on=value;Invalidate();}}
    public event EventHandler? Toggled;
    public Toggle(){DoubleBuffered=true;Size=new(44,24);Cursor=Cursors.Hand;}
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<4||Height<4)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        int h=Math.Min(Height,Width/2);
        using var path=new GraphicsPath();path.AddArc(0,0,h,h,90,180);
        path.AddArc(Width-h,0,h,h,270,180);path.CloseFigure();
        using var brush=new SolidBrush(on?Theme.Blue:Color.FromArgb(70,72,78));
        e.Graphics.FillPath(brush,path);
        int d=Math.Max(2,h-6);int x=on?Width-d-3:3;
        using var knob=new SolidBrush(Color.White);
        e.Graphics.FillEllipse(knob,x,3,d,d);
    }
    protected override void OnMouseDown(MouseEventArgs e){on=!on;Invalidate();Toggled?.Invoke(this,EventArgs.Empty);}
}

/// <summary>分段按钮组。</summary>
public sealed class Segmented:Panel
{
    private readonly List<Button> buttons=[];
    private int selected;
    public int Selected=>selected;
    public event EventHandler? SelectionChanged;
    public Segmented(string[] items)
    {
        BackColor=Color.FromArgb(30,31,34);
        for(int i=0;i<items.Length;i++)
        {
            int idx=i;
            var b=new Button{Text=items[i],FlatStyle=FlatStyle.Flat,BackColor=Color.Transparent,ForeColor=Theme.Muted,Height=30,Margin=new Padding(4,4,4,4),FlatAppearance={BorderSize=0},Cursor=Cursors.Hand,Font=new("Microsoft YaHei UI",9)};
            b.Click+=(_,_)=>Select(idx);
            b.FlatAppearance.MouseOverBackColor=Color.Transparent;
            b.FlatAppearance.MouseDownBackColor=Color.Transparent;
            Controls.Add(b);buttons.Add(b);
        }
        Height=38;
        Select(0);
    }
    public void Select(int i)
    {
        selected=i;
        for(int k=0;k<buttons.Count;k++)
        {
            buttons[k].BackColor=k==i?Theme.Blue:Color.Transparent;
            buttons[k].ForeColor=k==i?Color.White:Theme.Muted;
        }
        SelectionChanged?.Invoke(this,EventArgs.Empty);
    }
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if(buttons.Count==0)return;
        int w=Width/buttons.Count;
        for(int i=0;i<buttons.Count;i++)buttons[i].SetBounds(i*w,4,w-2,30);
    }
}
#endregion

public sealed class MainForm:Form
{
    private readonly Panel content=new(){Dock=DockStyle.Fill,BackColor=Theme.Bg};
    private readonly Label stateLabel=new(){AutoSize=true,ForeColor=Theme.Text,Font=new("Microsoft YaHei UI",10)};
    private readonly TextBox logBox=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Theme.Card,ForeColor=Theme.Muted,Font=new("Consolas",9)};
    private readonly List<Button> actions=[];
    private readonly Label matchedLbl=new(),missingLbl=new(),gameLbl=new(),alreadyLbl=new();
    private readonly Label windowStatusLbl=new();
    private bool busy;private string page="仪表盘";
    private readonly NumericUpDown width=new(){Minimum=800,Maximum=3840,Value=1280,Width=110,BackColor=Theme.Card,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle};
    private readonly NumericUpDown height=new(){Minimum=450,Maximum=2160,Value=720,Width=110,BackColor=Theme.Card,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle};
    private readonly Toggle topmostGame=new();
    private readonly Toggle topmostApp=new();
    private Segmented? modeSeg;
    private Control? _stack;

    public MainForm()
    {
        Text="MyWhoosh HD 中文管理器 "+AppInfo.Version;
        ClientSize=new(1180,780);MinimumSize=new(960,660);
        StartPosition=FormStartPosition.CenterScreen;
        BackColor=Theme.Bg;ForeColor=Theme.Text;Font=new("Microsoft YaHei UI",10);
        DoubleBuffered=true;AutoScaleMode=AutoScaleMode.Font;

        Controls.Add(content);Controls.Add(BuildSidebar());
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;MessageBox.Show(this,"操作进行中，请等待完成。","提示",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
        Render();
        Shown+=async(_,_)=>await Run("bootstrap",()=>{PayloadStore.EnsurePayload(AppInfo.Payload);return Operations.Check();},false);
        var pinTimer=new System.Windows.Forms.Timer{Interval=2500};pinTimer.Tick+=(_,_)=>GameWindow.KeepPinned();pinTimer.Start();
    }

    private Panel BuildSidebar()
    {
        var sidebar=new Panel{Dock=DockStyle.Left,Width=210,BackColor=Theme.Sidebar};
        var nav=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,20,16,16),BackColor=Theme.Sidebar};

        nav.Controls.Add(new Label{Text="WHOOSH",Font=new("Microsoft YaHei UI",18,FontStyle.Bold),ForeColor=Theme.Text,AutoSize=true,Height=32});
        nav.Controls.Add(new Label{Text="中文管理器",ForeColor=Theme.Muted,AutoSize=true,Height=22});
        nav.Controls.Add(new Label{Height=16});

        foreach(string item in new[]{"仪表盘","汉化管理","游戏窗口","日志与帮助"})
            nav.Controls.Add(NavButton(item,()=>{page=item;Render();}));

        nav.Controls.Add(new Label{Height=30});
        topmostApp.On=false;
        topmostApp.Toggled+=(_,_)=>{TopMost=topmostApp.On;};
        var pinRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Theme.Sidebar};
        pinRow.Controls.Add(new Label{Text="管理器窗口置顶",ForeColor=Theme.Muted,AutoSize=true,Height=24,TextAlign=ContentAlignment.MiddleLeft});
        pinRow.Controls.Add(topmostApp);
        nav.Controls.Add(pinRow);

        nav.Controls.Add(new Label{Height=24});
        nav.Controls.Add(new Label{Text="v"+AppInfo.Version,ForeColor=Theme.Muted,AutoSize=true,Height=20});
        nav.Controls.Add(new Label{Text="本地安全管理 · 非官方汉化工具",ForeColor=Theme.Muted,AutoSize=true,Height=20});
        sidebar.Controls.Add(nav);
        return sidebar;
    }

    private Button NavButton(string text,Action click)
    {
        var b=new Button{Text="  "+text,FlatStyle=FlatStyle.Flat,BackColor=Color.Transparent,ForeColor=Theme.Muted,Width=178,Height=42,Margin=new Padding(0,4,0,4),TextAlign=ContentAlignment.MiddleLeft,FlatAppearance={BorderSize=0},Cursor=Cursors.Hand,Font=new("Microsoft YaHei UI",10)};
        b.FlatAppearance.MouseOverBackColor=Color.FromArgb(45,46,50);
        b.FlatAppearance.MouseDownBackColor=Color.FromArgb(55,56,60);
        b.Click+=(_,_)=>{click();UpdateNavActive();};
        return b;
    }

    private void UpdateNavActive()
    {
        var sidebar=Controls.OfType<Panel>().FirstOrDefault(p=>p.Dock==DockStyle.Left);
        if(sidebar==null)return;
        var nav=sidebar.Controls.OfType<FlowLayoutPanel>().First();
        foreach(var c in nav.Controls.OfType<Button>())
        {
            bool active=c.Text.Trim()==page;
            c.BackColor=active?Color.FromArgb(45,46,50):Color.Transparent;
            c.ForeColor=active?Theme.Text:Theme.Muted;
        }
    }

    private Button Button(string text,Action click,bool primary=true)
    {
        var b=new Button{Text=text,FlatStyle=FlatStyle.Flat,BackColor=primary?Theme.Blue:Theme.Card,ForeColor=Color.White,Height=38,Padding=new Padding(16,0,16,0),Margin=new Padding(0,6,10,6),Cursor=Cursors.Hand,FlatAppearance={BorderSize=0},Font=new("Microsoft YaHei UI",9),AutoSize=true};
        b.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(70,145,250):Theme.CardHover;
        b.Click+=(_,_)=>click();return b;
    }

    private Button ActionButton(string label,string cmd,Func<object> action,bool confirm=false)
    {
        var b=Button(label,async()=>{if(confirm&&MessageBox.Show(this,"请先完全退出 MyWhoosh 游戏。\n操作会备份并修改相应文件；未知改动保留。\n继续？",label,MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;await Run(cmd,action);});
        actions.Add(b);return b;
    }

    private static Label Lbl(string s,int size=10,bool bold=false,Color? color=null)=>new(){Text=s,AutoSize=true,ForeColor=color??Theme.Text,Font=new("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,0,0,8)};

    /// <summary>创建填充单元格的指标卡片（方形，避免 Region 与 TableLayoutPanel 布局冲突）。</summary>
    private Card MetricCard(string title,Label value,string detail)
    {
        var c=new Card{Dock=DockStyle.Fill,Margin=new Padding(6),Radius=0};
        var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10),BackColor=Theme.Card};
        flow.Controls.Add(Lbl(title,9,false,Theme.Muted));
        value.AutoSize=true;value.ForeColor=Theme.Text;value.Font=new("Microsoft YaHei UI",22,FontStyle.Bold);value.Text="--";
        flow.Controls.Add(value);
        flow.Controls.Add(new Label{Text=detail,ForeColor=Theme.Muted,AutoSize=true});
        c.Controls.Add(flow);return c;
    }

    private Card InfoCard(string title,Control value)
    {
        var c=new Card{Dock=DockStyle.Fill,Margin=new Padding(6),Radius=0};
        var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10),BackColor=Theme.Card};
        flow.Controls.Add(Lbl(title,9,false,Theme.Muted));
        flow.Controls.Add(value);
        c.Controls.Add(flow);return c;
    }

    /// <summary>构建 N 列百分比表格并填充子控件（宽度由 Dock=Top 自动撑满）。</summary>
    private TableLayoutPanel MetricTable(int cols,int height,params Control[] children)
    {
        var t=new TableLayoutPanel{ColumnCount=cols,RowCount=1,Height=height,Dock=DockStyle.Top,Margin=new Padding(0,0,0,14)};
        for(int i=0;i<cols;i++)t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/cols));
        t.RowStyles.Add(new RowStyle(SizeType.Percent,100f));
        for(int i=0;i<children.Length;i++)t.Controls.Add(children[i],i,0);
        return t;
    }

    private void Render()
    {
        File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Render start page={page}\n");
        actions.Clear();
        // 先把可复用字段控件从旧树摘出，避免随旧树一起被 Dispose
        foreach(var c in new Control[]{stateLabel,logBox,matchedLbl,missingLbl,gameLbl,alreadyLbl,windowStatusLbl,width,height,topmostGame})
            c.Parent?.Controls.Remove(c);
        // 释放旧控件的 GDI/句柄资源（Controls.Clear 不会自动 Dispose）
        for(int i=content.Controls.Count-1;i>=0;i--)content.Controls[i].Dispose();
        content.Controls.Clear();

        var stack=new Panel{Dock=DockStyle.Top,AutoSize=true,BackColor=Theme.Bg};
        _stack=stack;content.Controls.Add(stack);

        // 页头
        var header=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=false,Padding=new Padding(0,0,0,12),BackColor=Theme.Bg};
        header.Controls.Add(Lbl(page,24,true));
        header.Controls.Add(new Label{Text="● "+(busy?"处理中":"就绪"),ForeColor=busy?Theme.Blue:Theme.Green,AutoSize=true,Margin=new Padding(14,12,0,0)});
        stack.Controls.Add(header);

        stateLabel.Text=busy?"正在处理…请勿启动游戏":"准备就绪 · 操作当前账户";
        stateLabel.Dock=DockStyle.Top;stateLabel.Height=24;
        stack.Controls.Add(stateLabel);

        // 指标行（Dock=Top 自动撑满，列百分比分配）
        stack.Controls.Add(MetricTable(3,140,
            MetricCard("游戏构建",gameLbl,"版本与主资源校验"),
            MetricCard("已匹配课程",matchedLbl,"缓存扫描结果"),
            MetricCard("未匹配课程",missingLbl,"保留原文不改参数")));

        stack.Controls.Add(MetricTable(2,120,
            MetricCard("已汉化课程",alreadyLbl,"无需重复安装"),
            InfoCard("窗口状态",windowStatusLbl)));
        windowStatusLbl.Text="待检查";windowStatusLbl.AutoSize=true;windowStatusLbl.ForeColor=Theme.Text;windowStatusLbl.Font=new("Microsoft YaHei UI",11);

        if(page is "仪表盘" or "汉化管理")
        {
            var card=new Card{Dock=DockStyle.Top,Height=250,Margin=new Padding(0,0,0,14)};
            var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12),BackColor=Theme.Card};
            flow.Controls.Add(Lbl("汉化管理",14,true));
            flow.Controls.Add(Lbl("汉化资源已内置，首次运行自动释放到本地。也可手动导入上游 ZIP 覆盖。",10,false,Theme.Muted));
            var row=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Theme.Card};
            var import=Button("高级：导入其他资源包",Import,false);actions.Add(import);row.Controls.Add(import);
            row.Controls.Add(ActionButton("检查状态","check",()=>Operations.Check()));
            row.Controls.Add(ActionButton("安装 / 更新","install",()=>Operations.Install(),true));
            flow.Controls.Add(row);
            var row2=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Theme.Card};
            row2.Controls.Add(ActionButton("导入旧版回退记录","migrate",()=>LegacyMigration.Current(),true));
            row2.Controls.Add(ActionButton("回退原版英文","restore",()=>Operations.Restore(),true));
            flow.Controls.Add(row2);
            flow.Controls.Add(Lbl("没有经过核验的备份，不认领未知文件；恢复会保留后续收藏和文字冲突。",9,false,Theme.Muted));
            card.Controls.Add(flow);stack.Controls.Add(card);

            if(page=="仪表盘")
            {
                var tips=new Card{Dock=DockStyle.Top,Height=110,Margin=new Padding(0,0,0,14)};
                var tf=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12),BackColor=Theme.Card};
                tf.Controls.Add(Lbl("小白两步上手",12,true));
                tf.Controls.Add(Lbl("1. 点击「检查状态」确认游戏版本匹配（汉化资源已内置）\n2. 退出游戏后点击「安装 / 更新」，再启动游戏看中文",10,false,Theme.Muted));
                tips.Controls.Add(tf);stack.Controls.Add(tips);
            }
        }
        else if(page=="游戏窗口")
        {
            var card=new Card{Dock=DockStyle.Top,Height=340,Margin=new Padding(0,0,0,14)};
            var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12),BackColor=Theme.Card};
            flow.Controls.Add(Lbl("MyWhoosh 游戏窗口",14,true));
            flow.Controls.Add(Lbl("关闭默认全屏，启用可拖动缩放的窗口模式，并可选将 MyWhoosh 游戏窗口钉死在最前。",10,false,Theme.Muted));

            var sizeRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0,8,0,8),BackColor=Theme.Card};
            sizeRow.Controls.Add(Lbl("窗口宽度：",10,false,Theme.Muted));sizeRow.Controls.Add(width);
            sizeRow.Controls.Add(Lbl("  高度：",10,false,Theme.Muted));sizeRow.Controls.Add(height);
            flow.Controls.Add(sizeRow);

            modeSeg=new Segmented(new[]{"窗口模式(可缩放)","全屏","无边框窗口"});
            modeSeg.Width=460;modeSeg.Margin=new Padding(0,4,0,12);
            flow.Controls.Add(modeSeg);

            var topRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0,4,0,12),BackColor=Theme.Card};
            topRow.Controls.Add(Lbl("MyWhoosh 游戏窗口置顶：",10,false,Theme.Muted));
            topmostGame.On=false;topRow.Controls.Add(topmostGame);
            topRow.Controls.Add(Lbl("  （管理器运行期间每 2.5 秒压回最前，其他置顶软件也无法遮挡；关闭即停止守护）",9,false,Theme.Muted));
            flow.Controls.Add(topRow);

            var btnRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Theme.Card};
            int[] modeMap={2,0,1};
            btnRow.Controls.Add(ActionButton("应用窗口设置","window",()=>GameWindow.Enable((int)width.Value,(int)height.Value,topmostGame.On,modeMap[modeSeg!.Selected]),true));
            btnRow.Controls.Add(ActionButton("恢复原窗口设置","window-restore",()=>GameWindow.Reset(),true));
            btnRow.Controls.Add(ActionButton("立即置顶 MyWhoosh","window-topmost",()=>GameWindow.SetTopmost(true)));
            flow.Controls.Add(btnRow);
            flow.Controls.Add(Lbl("仅修改 Saved 配置，保留首次备份。需退出游戏后应用，下次启动检查。\n拖动窗口边框的真实效果以游戏实际表现为准。",9,false,Theme.Muted));
            card.Controls.Add(flow);stack.Controls.Add(card);
        }
        else
        {
            var card=new Card{Dock=DockStyle.Top,Height=210,Margin=new Padding(0,0,0,14)};
            var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12),BackColor=Theme.Card};
            flow.Controls.Add(Lbl("使用与安全边界",14,true));
            flow.Controls.Add(Lbl("资源包：上游 v1.1.0 ZIP，自行导入，不执行其中脚本。\n原版回退：先核验迁移旧记录，再恢复，或恢复本工具安装。\n日志只在本地；不要上传存档、账号或整个备份目录。\n未知构建拒绝安装，不修改 WindowsApps 和训练功率。",10,false,Theme.Muted));
            card.Controls.Add(flow);stack.Controls.Add(card);
        }

        // 日志（标题 Dock=Top，文本框 Dock=Fill 撑满剩余高度）
        var logCard=new Card{Dock=DockStyle.Top,Height=220,Margin=new Padding(0,0,0,0)};
        var logTitle=Lbl("操作日志",11,true);logTitle.Dock=DockStyle.Top;logTitle.Height=24;
        logBox.Dock=DockStyle.Fill;logBox.Multiline=true;logBox.ReadOnly=true;
        logCard.Controls.Add(logBox);
        logCard.Controls.Add(logTitle);
        stack.Controls.Add(logCard);

        // Dock=Top 在同一 Panel 中后添加者会排在上方；统一反序，保证“标题→指标→操作→日志”的阅读顺序。
        var kids=stack.Controls.Cast<Control>().ToList();
        stack.Controls.Clear();
        for(int i=kids.Count-1;i>=0;i--)stack.Controls.Add(kids[i]);

        UpdateNavActive();
        File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Render end page={page}\n");
    }

    private void Import()
    {
        using var f=new OpenFileDialog{Filter="汉化 ZIP|*.zip",Title="选择上游 v1.1.0 资源包"};
        if(f.ShowDialog(this)!=DialogResult.OK)return;
        _=Run("import",()=>{PayloadStore.ImportZip(f.FileName,AppInfo.Payload);return new OperationResult("imported",3,0,"资源哈希通过，未执行任何第三方代码。");});
    }

    private async Task Run(string cmd,Func<object> action,bool record=true)
    {
        File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Run start cmd={cmd}\n");
        if(busy)return;busy=true;foreach(var b in actions)b.Enabled=false;
        stateLabel.Text="正在处理 · 请勿启动游戏";Render();
        File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Run after first render\n");
        try
        {
            var result=await Task.Run(action);
            File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Run action done\n");
            string json=Operations.Display(result);
            logBox.Text=json+(record?"\r\n\r\n日志："+Operations.Log(cmd,result):"");
            var n=System.Text.Json.JsonSerializer.SerializeToElement(result,AppInfo.JsonOptions);
            if(n.TryGetProperty("Scanned",out _))
            {
                matchedLbl.Text=n.GetProperty("Matched").ToString();
                missingLbl.Text=n.GetProperty("Unmatched").ToString();
                alreadyLbl.Text=n.GetProperty("AlreadyChinese").ToString();
                gameLbl.Text=AppInfo.GameVersion;
            }
            if(result is WindowState w)
            {
                windowStatusLbl.Text=$"{(w.Windowed?"窗口模式":"全屏/其他")} · {w.Width}×{w.Height} · {(w.BackupAvailable?"可恢复":"无备份")} · 置顶:{(w.Topmost?"开":"关")}";
                topmostGame.On=w.Topmost;
            }
            stateLabel.Text="操作完成 · 文件核验与游戏画面验证分别报告";
            File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Run result processed\n");
        }
        catch(Exception e){File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Run caught: {e.GetType().Name}: {e.Message}\n");stateLabel.Text="已安全停止";logBox.Text="操作未完成\r\n"+e.Message;}
        finally{File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Run finally start\n");busy=false;foreach(var b in actions)b.Enabled=true;Render();File.AppendAllText(Path.Combine(Path.GetTempPath(),"mwzh-trace.log"),$"[{DateTime.Now:HH:mm:ss.fff}] Run finally done\n");}
    }
}
