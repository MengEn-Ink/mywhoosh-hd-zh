using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Whoosh.Core;

namespace Whoosh.Gui;

internal enum CockpitPage { Overview, Chinese, Window, Restore }

/// <summary>
/// 03「竞速座舱」界面：整块自绘画布 + 命中区域分发，不使用 Region 裁切子控件，
/// 避免旧版圆角 Region 导致的 GDI 句柄泄漏与 TableLayoutPanel 递归布局。
/// 所有坐标按 DeviceDpi 缩放，面板本身可任意缩放窗口。
/// </summary>
internal sealed class CockpitView : Control
{
    internal CockpitPage Page { get; set; }
    internal bool Busy { get; set; }
    internal string Status { get; set; } = "准备就绪";
    internal string Notice { get; set; } = "";
    internal int Scanned { get; set; }
    internal int Matched { get; set; }
    internal int Already { get; set; }
    internal int Unmatched { get; set; }
    internal int Pending { get; set; }
    internal bool HasStats { get; set; }
    internal int WinW { get; set; } = 1280;
    internal int WinH { get; set; } = 720;
    internal bool Topmost { get; set; }
    internal bool Windowed { get; set; } = true;
    internal bool BackupAvailable { get; set; }
    internal string BackupWhen { get; set; } = "";
    internal string PayloadState { get; set; } = "未检查";
    internal string LastCheck { get; set; } = "—";
    internal bool GameRunning { get; set; }
    internal string GameVersion { get; set; } = "未检测";
    internal bool Supported { get; set; }
    internal bool? ActualTopmost { get; set; }
    internal string ActualSize { get; set; } = "未启动";
    internal bool WindowBackupAvailable { get; set; }
    internal bool InstallInterrupted { get; set; }
    internal bool WindowInterrupted { get; set; }
    internal int? CaptureDpi { get; set; }
    internal IReadOnlyList<string> RenderedText => _texts;
    internal IReadOnlyList<(RectangleF Rect,string Action)> Hits => _hits;
    private readonly List<string> _texts=[];
    internal (string Label,string Action) PrimaryChinese => InstallInterrupted ? ("处理中断记录","recover-install") : HasStats && Pending>0 && Supported ? ("安装 / 更新中文","install") : (HasStats?"重新检查中文":"检查中文状态","check");
    internal string TopmostText => ActualTopmost switch {true=>"已置顶",false=>Topmost?"守护待重试":"未置顶",_=>Topmost?"等待游戏启动":"未启动"};
    internal List<string> Log { get; } = [];
    internal List<string> Warnings { get; } = [];
    internal event Action<string>? Act;

    private readonly List<(RectangleF Rect, string Action)> _hits = [];
    private string? _hover;
    private readonly System.Windows.Forms.Timer _spin = new() { Interval = 160 };

    // Scale up with DPI only while the viewport can hold the minimum logical layout.
    private float S => Math.Max(0.75f,Math.Min((CaptureDpi??DeviceDpi)/96f,Math.Min(ClientSize.Width/960f,ClientSize.Height/720f)));
    private float P(double v) => (float)(v * S);
    private float RailW => P(104);

    public CockpitView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Palette.Bg;
        TabStop = false;
        _spin.Tick += (_, _) => { if (Busy) Invalidate(); };
        _spin.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _spin.Dispose();
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- 绘制入口

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Warnings.Clear();
        _texts.Clear();
        _hits.Clear();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float w = ClientSize.Width, h = ClientSize.Height;
        using (var back = new SolidBrush(Palette.Bg)) g.FillRectangle(back, 0, 0, w, h);

        DrawRail(g, w, h);
        float top = DrawHeader(g, w);
        float areaH = h - top - P(58) - P(6);
        float x = RailW + P(24);
        float availW = w - RailW - P(48);

        if (areaH < P(240) || availW < P(520))
        {
            Txt(g, "窗口太小，请拖动边缘放大。", x, top + P(10), P(14), Palette.Muted, false, availW);
        }
        else
        {
            var area = new RectangleF(x, top, availW, areaH);
            switch (Page)
            {
                case CockpitPage.Overview: DrawOverview(g, area); break;
                case CockpitPage.Chinese: DrawChinesePage(g, area); break;
                case CockpitPage.Window: DrawWindowPage(g, area); break;
                case CockpitPage.Restore: DrawRestorePage(g, area); break;
            }
        }

        DrawFooter(g, w, h);
        if (Busy) DrawBusyMark(g, w);
    }

    private void DrawRail(Graphics g, float w, float h)
    {
        using (var rail = new SolidBrush(Palette.Rail)) g.FillRectangle(rail, 0, 0, RailW, h);
        using (var sep = new Pen(Palette.Line)) g.DrawLine(sep, RailW - 0.5f, 0, RailW - 0.5f, h);

        float lx = P(14), ly = P(26);
        Slab(g, new RectangleF(lx, ly, P(36), P(36)), P(8), Palette.Accent);
        TxtCenter(g, "W", new RectangleF(lx, ly + P(6), P(36), P(28)), P(20), Palette.AccentInk, true);
        Txt(g, "中文助手", lx, ly + P(44), P(9.5), Palette.Dim, true, RailW - lx - P(8));

        string[] labels = ["总览", "汉化", "窗口", "恢复"];
        float ny = ly + P(98);
        float itemH = P(62), gap = P(8), iw = RailW - P(24);
        for (int i = 0; i < labels.Length; i++)
        {
            var r = new RectangleF(P(12), ny + i * (itemH + gap), iw, itemH);
            bool selected = (int)Page == i;
            if (selected) Slab(g, r, P(9), Palette.Accent);
            else
            {
                using var path = SlabPath(r, P(9));
                if (_hover == "nav:" + i) { using var fill = new SolidBrush(Palette.Panel); g.FillPath(fill, path); }
                using var edge = new Pen(Palette.Line);
                g.DrawPath(edge, path);
            }
            Color ink = selected ? Palette.AccentInk : Palette.Ink;
            Color num = selected ? Color.FromArgb(120, 100, 0) : Palette.Dim;
            Txt(g, labels[i], r.X + P(10), r.Y + P(10), P(13.5), ink, true, r.Width - P(16));
            Txt(g, "0" + (i + 1), r.X + P(10), r.Y + P(34), P(9.5), num, false, r.Width - P(16));
            Hit(r.X - P(4), r.Y - P(2), r.Width + P(8), r.Height + P(4), "nav:" + i);
        }

        float st = h - P(132);
        using (var dot = new SolidBrush(Palette.Accent)) g.FillEllipse(dot, lx, st + P(2), P(6), P(6));
        Txt(g, Busy ? "处理中" : "已就绪", lx + P(11), st - P(3), P(10), Palette.Ink, false, RailW - lx - P(14));
        Txt(g, "本地 · 离线", lx, st + P(18), P(9.5), Palette.Dim, false, RailW - lx - P(12));
        Txt(g, "v" + AppInfo.Version, lx, st + P(36), P(9), Palette.Dim, false, RailW - lx - P(12));
    }

    private float DrawHeader(Graphics g, float w)
    {
        float x = RailW + P(24), y = P(16);
        float bw = Math.Min(P(560), (w - RailW - P(48)) * 0.47f);
        float bh = P(80);
        Slab(g, new RectangleF(x, y, bw, bh), P(12), Palette.Accent);

        float px = FitSize(g, "WHOOSH · 中文控制台", P(21), true, bw - P(36), P(13));
        Txt(g, "WHOOSH · 中文控制台", x + P(18), y + P(13), px, Palette.AccentInk, true, bw - P(36));
        Txt(g, "语言 / 窗口 / 恢复，一个面板搞定", x + P(18), y + P(48), P(10.5),
            Color.FromArgb(46, 58, 0), false, bw - P(36));

        float tx = x + bw + P(32), right = w - P(24);
        float avail = right - tx;
        if (avail > P(330))
        {
            float badgeW = P(92);
            float cells = (avail - badgeW - P(18)) / 3f;
            Telemetry(g, tx, y + P(8), cells, "实际版本", GameVersion);
            Telemetry(g, tx + cells, y + P(8), cells, "已中文课程", HasStats ? Already.ToString("N0") + " 条" : "—");
            Telemetry(g, tx + cells * 2, y + P(8), cells, "备份状态", BackupAvailable ? "已保存" : "未创建");
            if (cells > P(90))
            {
                using var div = new Pen(Palette.Line);
                g.DrawLine(div, tx + cells - P(10), y + P(10), tx + cells - P(10), y + P(58));
                g.DrawLine(div, tx + cells * 2 - P(10), y + P(10), tx + cells * 2 - P(10), y + P(58));
            }
            Badge(g, new RectangleF(right - badgeW, y + P(26), badgeW, P(26)), "本地 · 离线", Palette.Muted);
        }

        float titleY = y + bh + P(26);
        float reserve = w - RailW > P(900) ? P(250) : 0;
        float maxTitleW = w - x - P(24) - reserve;
        (string title, string sub) = Headline();
        float tp = FitSize(g, title, P(34), true, maxTitleW, P(19));
        float titleH=tp*1.35f;
        Txt(g, title, x - P(2), titleY, tp, Palette.Ink, true, maxTitleW, titleH);
        float subY = titleY + titleH + P(6);
        float sp = P(13.5);
        Txt(g, sub, x - P(2), subY, sp, Palette.Muted, false, maxTitleW, sp * 1.35f);
        float bottom = subY + sp * 1.35f + P(26);

        if (w - x > P(760))
        {
            float rx = w - P(24), sy = titleY + P(6), ey = subY + P(18);
            using var thin = new Pen(Palette.Line, P(1.4f));
            using var glow = new Pen(Palette.Accent, P(2.2f));
            for (int i = 0; i < 5; i++)
            {
                float sx0 = rx - P(236) + i * P(40);
                var pen = i == 4 ? glow : thin;
                g.DrawLine(pen, sx0, ey, sx0 + P(26), sy);
            }
            float labelPx = P(10);
            Txt(g, "资源内置 · 不依赖外部下载",
                rx - TW(g, "资源内置 · 不依赖外部下载", labelPx, false), ey + P(8), labelPx, Palette.Dim, false);
        }
        return bottom;
    }

    private (string, string) Headline() => Page switch
    {
        CockpitPage.Chinese => ("把中文，装进每一节课。", "先检查、再写入。全程自动备份，出错自动回退。"),
        CockpitPage.Window => ("让游戏，停在想要的尺寸。", "窗口化、可拖动、置顶。设置一次，以后都记得。"),
        CockpitPage.Restore => ("不满意？一键回原版。", "只撤销本工具写入的内容，收藏和成绩都留着。"),
        _ => ("骑行前，先调好这三件事。", "语言、窗口、恢复。一次检查，之后打开游戏就能用。"),
    };

    private void Telemetry(Graphics g, float x, float y, float w, string label, string value)
    {
        Txt(g, label, x, y, P(10.5), Palette.Dim, false, w - P(12));
        float vp = w > P(130) ? P(19) : P(15);
        Txt(g, value, x, y + P(18), vp, Palette.Ink, true, w - P(12), vp * 1.3f);
    }

    private void DrawFooter(Graphics g, float w, float h)
    {
        float y = h - P(48);
        using (var accent = new Pen(Palette.Accent, P(2))) g.DrawLine(accent, RailW + P(24), y, w - P(24), y);
        Txt(g, "安全提示：所有改动都在本机完成，修改前自动备份。", RailW + P(24), y + P(10), P(11.5), Palette.Muted,
            false, w - RailW - P(310));
        float lx = w - P(24);
        float lw = TW(g, "查看操作记录 →", P(11.5), false);
        Txt(g, "查看操作记录 →", lx - lw, y + P(9), P(11.5), Palette.Accent, false);
        float helpW=TW(g,"上手帮助",P(11),false);
        Txt(g,"上手帮助",lx-lw-P(20)-helpW,y+P(9),P(11),Palette.Accent,false);
        Hit(lx-lw-P(26)-helpW,y+P(4),helpW+P(12),P(22),"help");
        Hit(lx - lw - P(6), y + P(4), lw + P(12), P(22), "open-log");
    }

    private void DrawBusyMark(Graphics g, float w)
    {
        string dots = new string('.', 1 + (Environment.TickCount / 400) % 3);
        float px = P(11.5), tw = TW(g, "处理中" + dots, px, true);
        var r = new RectangleF(w - P(24) - tw - P(28), P(10), tw + P(28), P(26));
        using var path = RoundedPath(r, P(13));
        using (var fill = new SolidBrush(Palette.Accent)) g.FillPath(fill, path);
        Txt(g, "处理中" + dots, r.X + P(14), r.Y + P(7), px, Palette.AccentInk, true);
    }

    // ---------------------------------------------------------------- 总览三列

    private void DrawOverview(Graphics g, RectangleF area)
    {
        float gap = P(18);
        float avail = area.Width - gap * 2;
        float c1 = MathF.Round(avail * 0.285f);
        float c2 = MathF.Round(avail * 0.295f);
        float c3 = avail - c1 - c2;
        ColumnChinese(g, new RectangleF(area.X, area.Y, c1, area.Height));
        ColumnWindow(g, new RectangleF(area.X + c1 + gap, area.Y, c2, area.Height));
        ColumnRestore(g, new RectangleF(area.X + c1 + c2 + gap * 2, area.Y, c3, area.Height));
    }

    private void ColumnChinese(Graphics g, RectangleF r)
    {
        var c = Card(g, r, "01", "中文界面");
        float btnH = P(44), btnY = r.Y + r.Height - P(18) - btnH;
        float regionH = btnY - P(12) - c.Y;
        float dia = Math.Clamp(Math.Min(c.Width * 0.66f, regionH * 0.56f), P(84), P(206));
        float blockH = dia + P(14) + P(20) + P(26) + P(42);
        float ringY = c.Y + Math.Max(P(2), (regionH - blockH) / 2);
        float progress = HasStats && Scanned > 0 ? Math.Clamp(Already / (float)Scanned, 0f, 1f) : 0f;
        Ring(g, c.X + c.Width / 2, ringY, dia, progress);

        float capY = ringY + dia + P(14);
        string caption = !HasStats ? "还没有检查过" : Pending == 0 ? "语言包已就绪" : "待写入 " + Pending + " 个文件";
        Txt(g, caption, c.X, capY, P(13), Pending > 0 && HasStats ? Palette.Warn : Palette.Accent, true, c.Width, P(20), StringAlignment.Center);

        float bodyY = capY + P(26);
        float bodyPx = c.Width < P(280) ? P(11) : P(12);
        if (bodyY + bodyPx * 3.2f < btnY - P(10))
            Txt(g, "课程名称与界面文字都变中文。", c.X, bodyY, bodyPx, Palette.Muted, false, c.Width, P(44));

        Button(g, new RectangleF(c.X, btnY, c.Width, btnH), PrimaryChinese.Label, true, PrimaryChinese.Action);
    }

    private void ColumnWindow(Graphics g, RectangleF r)
    {
        var c = Card(g, r, "02", "游戏窗口");
        float btnH = P(44), btnY = r.Y + r.Height - P(18) - btnH;
        float mockH = Math.Min(P(136), r.Height * 0.30f);
        float blockH = mockH + P(10) + P(18) + P(34) + P(38) + P(48);
        float top = c.Y + Math.Max(P(2), (btnY - P(12) - c.Y - blockH) / 2);

        MockWindow(g, new RectangleF(c.X, top, c.Width, mockH));
        float noteY = top + mockH + P(10);
        Txt(g, "缩放受游戏限制，可选预设。", c.X, noteY, P(11.5), Palette.Muted, false, c.Width, P(18));

        float toggleY = noteY + P(34);
        Txt(g, "始终置顶", c.X, toggleY, P(13.5), Palette.Ink, true, c.Width - P(64));
        Switch(g, c.X + c.Width, toggleY + P(1), Topmost, "toggle-topmost");
        Txt(g, "置顶期间，请保持助手运行。", c.X, toggleY + P(24), P(11), Palette.Muted, false, c.Width, P(16));

        float hintY = toggleY + P(50);
        Hint(g, c.X, hintY, c.Width, "Alt + Enter：游戏内切全屏");
        Hint(g, c.X, hintY + P(22), c.Width, "置顶守护：每 2.5 秒确认");

        Button(g, new RectangleF(c.X, btnY, c.Width, btnH), WindowInterrupted?"处理窗口中断":"应用窗口设置", true, WindowInterrupted?"recover-window":"apply-window");
    }

    private void Hint(Graphics g, float x, float y, float w, string text)
    {
        using (var dot = new SolidBrush(Palette.Accent)) g.FillEllipse(dot, x, y + P(4), P(4), P(4));
        Txt(g, text, x + P(12), y, P(10.5), Palette.Muted, false, w - P(12), P(16));
    }

    private void ColumnRestore(Graphics g, RectangleF r)
    {
        var c = Card(g, r, "03", "恢复原版");
        float btnH = P(42), btnY = r.Y + r.Height - P(18) - btnH;
        string[] steps = ["自动保存修改前的备份", "保留英文原文与原有窗口设置", "随时一键切回英文界面"];
        float snapH = P(78);
        float blockH = P(92) + P(12) + snapH + P(10) + P(16);
        float stepY = c.Y + Math.Max(P(2), (btnY - P(12) - c.Y - blockH) / 2);
        for (int i = 0; i < steps.Length; i++)
        {
            float y = stepY + i * P(30);
            using (var box = new Pen(Palette.Accent))
                g.DrawRectangle(box, c.X, y + P(4), P(15), P(15));
            Txt(g, (i + 1).ToString(), c.X + P(4), y + P(3), P(9.5), Palette.Accent, true);
            Txt(g, steps[i], c.X + P(24), y + P(3), P(12), Palette.Muted, false, c.Width - P(24), P(18));
        }

        float snapY = stepY + P(92);
        InfoBox(g, new RectangleF(c.X, snapY, c.Width, snapH));
        Txt(g, "最近的备份", c.X + P(12), snapY + P(9), P(10), Palette.Dim, false, c.Width - P(24));
        Txt(g, string.IsNullOrEmpty(BackupWhen) ? "还没有备份" : BackupWhen, c.X + P(12), snapY + P(24), P(12.5),
            Palette.Ink, true, c.Width - P(24));
        Txt(g, BackupAvailable ? "可恢复：界面 / 课程" : "本工具没有汉化回退记录", c.X + P(12), snapY + P(44), P(10), Palette.Dim, false, c.Width - P(24));

        float noteY = snapY + snapH + P(10);
        Txt(g, "恢复后重新打开游戏，即回到英文界面。", c.X, noteY, P(11), Palette.Muted, false, c.Width, P(16));

        float split = MathF.Round(c.Width * 0.56f);
        Button(g, new RectangleF(c.X, btnY, split, btnH), "恢复原版英文", false, "restore");
        float lx = c.X + c.Width;
        float lw = TW(g, "打开备份目录 →", P(11.5), false);
        Txt(g, "打开备份目录 →", lx - lw, btnY + P(12), P(11.5), Palette.Accent, false);
        Hit(lx - lw - P(6), btnY + P(6), lw + P(12), P(24), "open-backup");
    }

    // ---------------------------------------------------------------- 详情页

    private void DrawChinesePage(Graphics g, RectangleF area)
    {
        float leftW = MathF.Round(area.Width * 0.6f);
        var left = new RectangleF(area.X, area.Y, leftW, area.Height);
        var right = new RectangleF(area.X + leftW + P(18), area.Y, area.Width - leftW - P(18), area.Height);

        var c = Card(g, left, "01", "中文界面");
        float btnH = P(44), btnY = left.Y + left.Height - P(18) - btnH;
        float y = c.Y + P(2);
        y = Row(g, c.X, y, c.Width, "扫描的课程文件", HasStats ? Scanned.ToString("N0") : "—");
        y = Row(g, c.X, y, c.Width, "精确匹配译文", HasStats ? Matched.ToString("N0") : "—");
        y = Row(g, c.X, y, c.Width, "已经是中文", HasStats ? Already.ToString("N0") : "—");
        y = Row(g, c.X, y, c.Width, "未匹配（保持原文）", HasStats ? Unmatched.ToString("N0") : "—");
        y = Row(g, c.X, y, c.Width, "本次待写入文件", HasStats ? Pending.ToString("N0") : "—");
        y += P(4);
        y = Row(g, c.X, y, c.Width, "内置语言包", PayloadState);
        y = Row(g, c.X, y, c.Width, "上次检查", LastCheck);

        y += P(16);
        Txt(g, "安装会做什么", c.X, y, P(11), Palette.Accent, true, c.Width);
        y += P(24);
        string[] steps =
        [
            "备份原始文件，随时可以回退",
            "只替换界面文字和课程译文",
            "重新打开游戏，中文立即生效",
        ];
        float stepGap=ClientSize.Height<P(780)?P(18):P(22);
        for (int i = 0; i < steps.Length; i++)
        {
            using (var dot = new SolidBrush(Palette.PanelAlt)) g.FillEllipse(dot, c.X, y + i * stepGap + P(3), P(14), P(14));
            Txt(g, (i + 1).ToString(), c.X + P(4), y + i * stepGap + P(4), P(9), Palette.Accent, true);
            Txt(g, steps[i], c.X + P(22), y + i * stepGap, P(11.5), Palette.Muted, false, c.Width - P(22), P(18));
        }
        y += stepGap*3+P(4);

        float noteY = y + P(6);
        if (noteY + P(30) < btnY - P(34))
            Txt(g, "安装前请完全退出游戏。所有改动先写入备份，出错会自动回滚。", c.X, noteY, P(11.5), Palette.Muted, false, c.Width, P(32));

        float split = MathF.Round(c.Width * 0.4f);
        Button(g, new RectangleF(c.X, btnY, split, btnH), "重新检查", false, "check");
        Button(g, new RectangleF(c.X + split + P(10), btnY, c.Width - split - P(10), btnH), InstallInterrupted?"处理中断记录":Supported?"安装 / 更新中文":"先检查游戏构建", true, InstallInterrupted?"recover-install":Supported?"install":"check");

        float lw = TW(g, "导入语言资源包 →", P(11), false);
        Txt(g, "导入语言资源包 →", c.X, btnY - P(24), P(11), Palette.Accent, false);
        Hit(c.X - P(4), btnY - P(30), lw + P(8), P(24), "import");

        RightStatusCard(g, right);
    }

    private void DrawWindowPage(Graphics g, RectangleF area)
    {
        float leftW = MathF.Round(area.Width * 0.6f);
        var left = new RectangleF(area.X, area.Y, leftW, area.Height);
        var right = new RectangleF(area.X + leftW + P(18), area.Y, area.Width - leftW - P(18), area.Height);

        var c = Card(g, left, "02", "游戏窗口");
        float btnH = P(44), btnY = left.Y + left.Height - P(18) - btnH;

        (int W, int H)[] presets = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1440)];
        float chipGap = P(8);
        float chipW = (c.Width - chipGap * (presets.Length - 1)) / presets.Length;
        for (int i = 0; i < presets.Length; i++)
        {
            var pr = new RectangleF(c.X + i * (chipW + chipGap), c.Y + P(2), chipW, P(34));
            bool on = WinW == presets[i].W && WinH == presets[i].H;
            Chip(g, pr, presets[i].W + "×" + presets[i].H, on, "size:" + presets[i].W + ":" + presets[i].H);
        }

        float y = c.Y + P(48);
        y = Row(g, c.X, y, c.Width, "预设尺寸（待应用）", WinW + " × " + WinH);
        y = Row(g, c.X, y, c.Width, "实际窗口尺寸", ActualSize);
        y = Row(g, c.X, y, c.Width, "拖动边缘缩放", "已请求（游戏决定）");
        y = Row(g, c.X, y, c.Width, "最大化 / 最小化", "由游戏支持");

        float toggleY = y + P(10);
        Txt(g, "始终置顶", c.X, toggleY, P(13.5), Palette.Ink, true, c.Width - P(64));
        Switch(g, c.X + c.Width, toggleY + P(1), Topmost, "toggle-topmost");
        Txt(g, "固定在普通窗口前，助手需保持运行。", c.X, toggleY + P(24), P(11), Palette.Muted, false, c.Width, P(16));

        float rowY = toggleY + P(50);
        rowY = Row(g, c.X, rowY, c.Width, "全屏切换（游戏内）", "Alt + Enter");
        rowY = Row(g, c.X, rowY, c.Width, "实际置顶", TopmostText);
        rowY = Row(g, c.X, rowY, c.Width, "置顶守护", Topmost ? "每 2.5 秒确认" : "未开启");

        float noteY = btnY - P(24);
        if (noteY > rowY + P(6))
            Txt(g, "退出游戏后应用。拖边无效时，请选预设尺寸。", c.X, noteY, P(11), Palette.Muted, false, c.Width, P(18));

        float split = MathF.Round(c.Width * 0.5f);
        Button(g, new RectangleF(c.X, btnY, split, btnH), WindowInterrupted?"处理窗口中断":"应用窗口设置", true, WindowInterrupted?"recover-window":"apply-window");
        Button(g, new RectangleF(c.X + split + P(10), btnY, c.Width - split - P(10), btnH), "恢复窗口设置", false, "reset-window");

        RightStatusCard(g, right);
    }

    private void DrawRestorePage(Graphics g, RectangleF area)
    {
        float leftW = MathF.Round(area.Width * 0.6f);
        var left = new RectangleF(area.X, area.Y, leftW, area.Height);
        var right = new RectangleF(area.X + leftW + P(18), area.Y, area.Width - leftW - P(18), area.Height);

        var c = Card(g, left, "03", "恢复原版");
        float btnH = P(44), btnY = left.Y + left.Height - P(18) - btnH;

        string[] steps = ["安装前自动保存原始文件", "只回滚本工具写入的文字与补丁", "收藏、成绩、后续训练记录全部保留"];
        float blockH = P(96) + P(78) + P(18) + P(87);
        float y = c.Y + Math.Min(P(26), Math.Max(P(2), (btnY - P(12) - c.Y - blockH) / 2));
        for (int i = 0; i < steps.Length; i++)
        {
            float sy = y + i * P(30);
            using (var box = new Pen(Palette.Accent)) g.DrawRectangle(box, c.X, sy + P(4), P(15), P(15));
            Txt(g, (i + 1).ToString(), c.X + P(4), sy + P(3), P(9.5), Palette.Accent, true);
            Txt(g, steps[i], c.X + P(24), sy + P(3), P(12), Palette.Muted, false, c.Width - P(24), P(18));
        }
        y += P(96);

        float snapH = P(78);
        InfoBox(g, new RectangleF(c.X, y, c.Width, snapH));
        Txt(g, "最近的备份", c.X + P(12), y + P(9), P(10), Palette.Dim, false, c.Width - P(24));
        Txt(g, string.IsNullOrEmpty(BackupWhen) ? "还没有备份" : BackupWhen, c.X + P(12), y + P(24), P(13),
            Palette.Ink, true, c.Width - P(24));
        Txt(g, BackupAvailable ? "可恢复：界面 / 课程" : "本工具没有汉化回退记录", c.X + P(12), y + P(44), P(10.5), Palette.Dim, false, c.Width - P(24));
        Txt(g, BackupAvailable ? "恢复前仍会检查当前文件。" : "没有可恢复的备份记录。", c.X + P(12), y + P(58), P(10.5),
            BackupAvailable ? Palette.Ok : Palette.Warn, false, c.Width - P(24));

        float rowY = y + snapH + P(18);
        rowY = Row(g, c.X, rowY, c.Width, "备份覆盖", BackupAvailable ? "已记录的汉化文件" : "无汉化备份");
        rowY = Row(g, c.X, rowY, c.Width, "恢复方式", "按记录逐文件还原");
        rowY = Row(g, c.X, rowY, c.Width, "收藏与成绩", "始终保留");

        float lw = TW(g, "导入旧版回退记录 →", P(11), false);
        Txt(g, "导入旧版回退记录 →", c.X, btnY - P(24), P(11), Palette.Accent, false);
        Hit(c.X - P(4), btnY - P(30), lw + P(8), P(24), "migrate");

        float split = MathF.Round(c.Width * 0.52f);
        Button(g, new RectangleF(c.X, btnY, split, btnH), InstallInterrupted?"处理中断记录":"恢复原版英文", true, InstallInterrupted?"recover-install":"restore");
        Button(g, new RectangleF(c.X + split + P(10), btnY, c.Width - split - P(10), btnH), "打开备份目录", false, "open-backup");

        RightStatusCard(g, right);
    }

    private void RightStatusCard(Graphics g, RectangleF r)
    {
        var c = Card(g, r, "", "状态");
        float y = c.Y + P(2);
        y = Row(g, c.X, y, c.Width, "管理器版本", "v" + AppInfo.Version);
        y = Row(g, c.X, y, c.Width, "游戏进程", GameRunning ? "运行中" : "未运行");
        y = Row(g, c.X, y, c.Width, "恢复记录", Status);
        y = Row(g, c.X, y, c.Width, "实际置顶", TopmostText);
        y = Row(g, c.X, y, c.Width, "内置资源包", PayloadState);
        y = Row(g, c.X, y, c.Width, "数据目录", "本机 AppData");
        if (!string.IsNullOrEmpty(Notice))
        {
            TxtEllipsis(g, "提示：" + Notice, new RectangleF(c.X,y+P(2),c.Width,P(36)), P(10.5), Palette.Warn);
            y += P(40);
        }
        y += P(8);
        Txt(g, "最近操作", c.X, y, P(11), Palette.Accent, true, c.Width);
        y += P(22);
        int shown = 0;
        for (int i = Log.Count - 1; i >= 0 && shown < 5; i--, shown++)
        {
            float maxH = P(30);
            if (y + maxH > r.Bottom - P(52)) break;
            TxtEllipsis(g, Log[i], new RectangleF(c.X,y,c.Width,maxH), P(10.5), Palette.Muted);
            y += maxH + P(4);
        }
        if (shown == 0) Txt(g, "还没有操作记录。", c.X, y, P(10.5), Palette.Dim, false, c.Width);

        float lw = TW(g, "查看完整记录 →", P(11), false);
        float ly = r.Bottom - P(30);
        Txt(g, "查看完整记录 →", c.X + c.Width - lw, ly, P(11), Palette.Accent, false);
        Hit(c.X + c.Width - lw - P(6), ly - P(6), lw + P(12), P(24), "show-details");
    }

    // ---------------------------------------------------------------- 组件

    private void Card(Graphics g, RectangleF r, string index, string title, out RectangleF content)
    {
        using var path = SlabPath(r, P(14));
        using (var fill = new SolidBrush(Palette.Panel)) g.FillPath(fill, path);
        using (var edge = new Pen(Palette.Border, P(1))) g.DrawPath(edge, path);

        float pad = P(18), headY = r.Y;
        if (index.Length > 0)
        {
            Txt(g, index, r.X + pad, headY + P(26), P(11), Palette.Accent, true, P(30));
            float px = FitSize(g, title, P(19), true, r.Width - pad * 2 - P(34), P(13));
            Txt(g, title, r.X + pad + P(30), headY + P(22), px, Palette.Ink, true, r.Width - pad * 2 - P(34), P(26));
        }
        else
        {
            float px = FitSize(g, title, P(19), true, r.Width - pad * 2, P(13));
            Txt(g, title, r.X + pad, headY + P(22), px, Palette.Ink, true, r.Width - pad * 2, P(26));
        }
        float lineY = r.Y + P(64);
        using (var sep = new Pen(Palette.Line)) g.DrawLine(sep, r.X + pad, lineY, r.Right - pad, lineY);
        content = new RectangleF(r.X + pad, lineY + P(12), r.Width - pad * 2, r.Bottom - lineY - P(12) - pad);
    }

    private RectangleF Card(Graphics g, RectangleF r, string index, string title)
    {
        Card(g, r, index, title, out var content);
        return content;
    }

    private void Ring(Graphics g, float cx, float top, float d, float progress)
    {
        var rect = new RectangleF(cx - d / 2, top, d, d);
        using (var outer = new Pen(Palette.Border, P(9))) g.DrawEllipse(outer, rect);
        if (progress > 0.01f)
        {
            using var accent = new Pen(Palette.Accent, P(9)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(accent, rect, -90, Math.Clamp(progress, 0.02f, 1f) * 360f);
        }
        using (var inner = new Pen(Palette.Line, P(1))) g.DrawEllipse(inner, RectangleF.Inflate(rect, -P(10), -P(10)));
        TxtCenter(g, "中文", new RectangleF(rect.X, rect.Y + d * 0.34f, d, d * 0.28f), P(19), Palette.Ink, true);
        if (d > P(112))
            TxtCenter(g, "课程译文", new RectangleF(rect.X, rect.Y + d * 0.58f, d, d * 0.2f), P(9), Palette.Dim, false);
    }

    private void MockWindow(Graphics g, RectangleF r)
    {
        using var path = RoundedPath(r, P(8));
        using (var fill = new SolidBrush(Palette.PanelAlt)) g.FillPath(fill, path);
        using (var edge = new Pen(Palette.Line, P(1))) g.DrawPath(edge, path);
        float bar = P(24);
        using (var line = new Pen(Palette.Line)) g.DrawLine(line, r.X, r.Y + bar, r.Right, r.Y + bar);
        for (int i = 0; i < 3; i++)
        {
            using var dot = new SolidBrush(Palette.Border);
            g.FillEllipse(dot, r.X + P(11) + i * P(11), r.Y + P(9), P(5.5f), P(5.5f));
        }
        Txt(g, "MyWhoosh.exe", r.X + P(52), r.Y + P(6), P(9.5), Palette.Dim, false, r.Width - P(60));

        float pw = TW(g, WinW.ToString(), P(23), true);
        float mw = TW(g, "×", P(13), true);
        float pw2 = TW(g, WinH.ToString(), P(23), true);
        float total = pw + mw + pw2 + P(18);
        float sx = r.X + (r.Width - total) / 2;
        float ty = r.Y + bar + P(16);
        Txt(g, WinW.ToString(), sx, ty, P(23), Palette.Ink, true);
        Txt(g, "×", sx + pw + P(9), ty + P(5), P(13), Palette.Muted, true);
        Txt(g, WinH.ToString(), sx + pw + mw + P(18), ty, P(23), Palette.Ink, true);
        TxtCenter(g, "普通小窗 · 可拖动标题栏", new RectangleF(r.X, r.Y + bar + P(48), r.Width, P(18)), P(10), Palette.Muted, false);

        using (var grip = new Pen(Palette.Dim, P(1)))
            for (int i = 1; i <= 3; i++)
                g.DrawLine(grip, r.Right - P(10) - i * P(5), r.Bottom - P(8), r.Right - P(10), r.Bottom - P(8) - i * P(5));
    }

    private void InfoBox(Graphics g, RectangleF r)
    {
        using var path = RoundedPath(r, P(8));
        using (var fill = new SolidBrush(Palette.PanelAlt)) g.FillPath(fill, path);
        using (var edge = new Pen(Palette.Line)) g.DrawPath(edge, path);
        using (var strip = new SolidBrush(Palette.Accent)) g.FillRectangle(strip, r.X, r.Y, P(3), r.Height);
    }

    private void Switch(Graphics g, float right, float y, bool on, string action)
    {
        float w = P(48), h = P(26);
        var r = new RectangleF(right - w, y, w, h);
        using var path = RoundedPath(r, h / 2);
        using (var fill = new SolidBrush(on ? Palette.Accent : Palette.PanelAlt)) g.FillPath(fill, path);
        using (var edge = new Pen(on ? Palette.Accent : Palette.Border)) g.DrawPath(edge, path);
        float kd = h - P(6);
        var knob = new RectangleF(on ? r.Right - kd - P(3) : r.X + P(3), y + P(3), kd, kd);
        using (var fill = new SolidBrush(on ? Palette.AccentInk : Palette.Muted)) g.FillEllipse(fill, knob);
        Hit(r.X - P(10), r.Y - P(10), w + P(20), h + P(24), action);
    }

    private void Button(Graphics g, RectangleF r, string label, bool primary, string action)
    {
        bool hover = _hover == action;
        if (primary)
        {
            using var path = SlabPath(r, P(7));
            Color fillColor = Busy ? Palette.Blend(Palette.Accent, Palette.PanelAlt, 0.45f)
                : hover ? Palette.Blend(Palette.Accent, Color.White, 0.18f) : Palette.Accent;
            using (var fill = new SolidBrush(fillColor)) g.FillPath(fill, path);
            float px = FitSize(g, label, P(13.5), true, r.Width - P(24), P(10));
            TxtCenter(g, label, new RectangleF(r.X, r.Y + (r.Height - px * 1.25f) / 2, r.Width, px * 1.25f), px, Palette.AccentInk, true);
            if (Busy) return;
        }
        else
        {
            using var path = SlabPath(r, P(7));
            using (var fill = new SolidBrush(hover ? Palette.Panel : Palette.PanelAlt)) g.FillPath(fill, path);
            using (var edge = new Pen(hover ? Palette.Accent : Palette.Border)) g.DrawPath(edge, path);
            float px = FitSize(g, label, P(12.5), true, r.Width - P(20), P(9.5));
            TxtCenter(g, label, new RectangleF(r.X, r.Y + (r.Height - px * 1.25f) / 2, r.Width, px * 1.25f), px, Palette.Ink, true);
            if (Busy) return;
        }
        Hit(r.X, r.Y, r.Width, r.Height, action);
    }

    private void Chip(Graphics g, RectangleF r, string label, bool selected, string action)
    {
        bool hover = _hover == action;
        using var path = SlabPath(r, P(6));
        using (var fill = new SolidBrush(selected ? Palette.Accent : hover ? Palette.Panel : Palette.PanelAlt))
            g.FillPath(fill, path);
        using (var edge = new Pen(selected ? Palette.Accent : Palette.Border)) g.DrawPath(edge, path);
        float px = FitSize(g, label, P(11), true, r.Width - P(10), P(8));
        TxtCenter(g, label, new RectangleF(r.X, r.Y + (r.Height - px * 1.25f) / 2, r.Width, px * 1.25f), px,
            selected ? Palette.AccentInk : Palette.Muted, true);
        Hit(r.X, r.Y, r.Width, r.Height, action);
    }

    private void Badge(Graphics g, RectangleF r, string text, Color color)
    {
        using var path = RoundedPath(r, r.Height / 2);
        using (var edge = new Pen(Palette.Border)) g.DrawPath(edge, path);
        float px = P(9.5);
        TxtCenter(g, text, r, px, color, false);
    }

    private float Row(Graphics g, float x, float y, float w, string label, string value)
    {
        float lp = P(12), vp = P(13);
        float labelW=w*0.48f,valueW=w-labelW-P(10);
        float labelSize=FitSize(g,label,lp,false,labelW,P(9));
        float valueSize=FitSize(g,value,vp,true,valueW,P(9));
        TxtEllipsis(g,label,new RectangleF(x,y+P(3),labelW,P(19)),labelSize,Palette.Muted);
        TxtEllipsis(g,value,new RectangleF(x+labelW+P(10),y,valueW,P(21)),valueSize,value.Length>14?Palette.Muted:Palette.Ink,true,StringAlignment.Far);
        float rowH=ClientSize.Height<P(780)?P(25):P(29);
        using (var sep = new Pen(Palette.Line)) g.DrawLine(sep, x, y + rowH-P(5), x + w, y + rowH-P(5));
        return y + rowH;
    }

    // ---------------------------------------------------------------- 文本与几何

    private void Txt(Graphics g, string text, float x, float y, float px, Color color, bool bold = false,
        float maxW = 0, float maxH = 0, StringAlignment align = StringAlignment.Near)
    {
        if (string.IsNullOrEmpty(text)) return;
        _texts.Add(text);
        using var font = Palette.F(px, bold);
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = align,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoClip,
        };
        float width = maxW > 0 ? maxW : 4096;
        var size = g.MeasureString(text, font, new SizeF(width, 4096), sf);
        if (maxW > 0)
        {
            if (size.Width > maxW + 2f) Warnings.Add($"超宽 {maxW:0}px：{Short(text)} 需要 {size.Width:0}px");
            if (maxH > 0 && size.Height > maxH + 3f) Warnings.Add($"超高 {maxH:0}px：{Short(text)} 需要 {size.Height:0}px");
            g.DrawString(text, font, brush, new RectangleF(x, y, maxW, maxH > 0 ? maxH : 4096), sf);
        }
        else g.DrawString(text, font, brush, x, y, sf);
    }

    private void TxtEllipsis(Graphics g,string text,RectangleF box,float px,Color color,bool bold=false,StringAlignment align=StringAlignment.Near)
    {
        _texts.Add(text);
        using var font=Palette.F(px,bold);using var brush=new SolidBrush(color);
        using var sf=new StringFormat(StringFormat.GenericTypographic){Alignment=align,Trimming=StringTrimming.EllipsisCharacter};
        g.DrawString(text,font,brush,box,sf);
    }
    private void TxtCenter(Graphics g, string text, RectangleF box, float px, Color color, bool bold)
    {
        _texts.Add(text);
        using var font = Palette.F(px, bold);
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoClip,
        };
        g.DrawString(text, font, brush, box, sf);
    }

    private float TW(Graphics g, string text, float px, bool bold)
    {
        _texts.Add(text);
        using var font = Palette.F(px, bold);
        return g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
    }

    private float FitSize(Graphics g, string text, float px, bool bold, float maxW, float floor)
    {
        float p = px;
        while (p > floor && TW(g, text, p, bold) > maxW) p -= 0.5f;
        return p;
    }

    private static string Short(string text) => text.Length <= 20 ? text : text[..20] + "…";

    private void Hit(float x, float y, float w, float h, string action) =>
        _hits.Add((new RectangleF(x, y, w, h), action));

    private void Slab(Graphics g, RectangleF r, float cut, Color fill)
    {
        using var path = SlabPath(r, cut);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    private static GraphicsPath SlabPath(RectangleF r, float cut)
    {
        var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new PointF(r.X + cut, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Bottom - cut),
            new PointF(r.Right - cut, r.Bottom), new PointF(r.X, r.Bottom), new PointF(r.X, r.Y + cut),
        });
        return path;
    }

    private static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---------------------------------------------------------------- 交互

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? hit = null;
        foreach (var h in _hits)
            if (h.Rect.Contains(e.Location)) { hit = h.Action; break; }
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = hit == null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null) { _hover = null; Cursor = Cursors.Default; Invalidate(); }
    }

    internal bool ClickActionForTest(string action)
    {
        var hits=_hits.Where(h=>h.Action==action).ToArray();
        if(hits.Length==0)return false;
        var r=hits[0].Rect;
        OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,(int)(r.X+r.Width/2),(int)(r.Y+r.Height/2),0));
        return true;
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || Busy) return;
        foreach (var h in _hits)
            if (h.Rect.Contains(e.Location)) { Act?.Invoke(h.Action); return; }
    }
}
