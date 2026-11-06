using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;

internal sealed class Board:IDisposable
{
    public readonly Bitmap Image=new(1600,1000);
    public readonly Graphics G;
    public string Bg,Ink,Muted,Accent,Surface,Border;
    public readonly List<string> Problems=[];
    public Board(string bg,string ink,string muted,string accent,string surface,string border)
    { Bg=bg;Ink=ink;Muted=muted;Accent=accent;Surface=surface;Border=border;G=Graphics.FromImage(Image);G.SmoothingMode=SmoothingMode.AntiAlias;G.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;G.Clear(C(bg)); }
    public static Color C(string h)=>ColorTranslator.FromHtml(h);
    public void Box(float x,float y,float w,float h,string color,int r=0,string? border=null)
    {
        using var brush=new SolidBrush(C(color));using var path=new GraphicsPath();
        if(r==0)path.AddRectangle(new RectangleF(x,y,w,h));else{float d=Math.Min(r*2,Math.Min(w,h));path.AddArc(x,y,d,d,180,90);path.AddArc(x+w-d,y,d,d,270,90);path.AddArc(x+w-d,y+h-d,d,d,0,90);path.AddArc(x,y+h-d,d,d,90,90);path.CloseFigure();}
        G.FillPath(brush,path);if(border!=null){using var pen=new Pen(C(border),1.4f);G.DrawPath(pen,path);}
    }
    public void Slab(float x,float y,float w,float h,float cut,string color,string? border=null)
    {
        using var path=new GraphicsPath();path.AddPolygon(new[]{new PointF(x+cut,y),new PointF(x+w,y),new PointF(x+w,y+h-cut),new PointF(x+w-cut,y+h),new PointF(x,y+h),new PointF(x,y+cut)});
        G.FillPath(new SolidBrush(C(color)),path);if(border!=null){using var pen=new Pen(C(border),1.4f);G.DrawPath(pen,path);}
    }
    public void Line(float x,float y,float x2,float y2,string? c=null,float thick=1)
    {using var p=new Pen(C(c??Border),thick);G.DrawLine(p,x,y,x2,y2);}
    public void Txt(string text,float x,float y,float size=22,string? color=null,bool bold=false,float width=1300,string family="Microsoft YaHei")
    {
        using var f=new Font(family,size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
        using var br=new SolidBrush(C(color??Ink));using var sf=new StringFormat(StringFormat.GenericTypographic){Trimming=StringTrimming.None};
        var m=G.MeasureString(text,f,new SizeF(width,600),sf);
        if(y+m.Height>(y>=966?1000:966)||x<0||x+Math.Min(m.Width,width)>1601)Problems.Add($"text bounds: {text}");
        G.DrawString(text,f,br,new RectangleF(x,y,width,600),sf);
    }
    public void Badge(string text,float x,float y,string? bg=null,string? fg=null,int w=180)
    {Box(x,y,w,36,bg??Surface,18);Txt(text,x+14,y+5,18,fg??Accent,false,w-20);}
    public void Button(string text,float x,float y,float w=210,float h=60,bool primary=true,string? color=null)
    {Box(x,y,w,h,primary?(color??Accent):Surface,12,primary?null:Border);Txt(text,x+22,y+(h-27)/2,24,primary?(color=="#F2D327"||Accent=="#DFFF00"||Accent=="#F2D327"?"#11151C":"#FFFFFF"):Ink,true,w-38);}
    public void Switch(float x,float y,bool on=true)
    {Box(x,y,60,32,on?Accent:Border,16);using var br=new SolidBrush(Color.White);G.FillEllipse(br,x+(on?31:4),y+4,24,24);}
    public void Ring(float x,float y,int diameter,string text,string? accent=null)
    {using var p=new Pen(C(Border),8);G.DrawEllipse(p,x,y,diameter,diameter);using var a=new Pen(C(accent??Accent),8);a.StartCap=a.EndCap=LineCap.Round;G.DrawArc(a,x,y,diameter,diameter,-90,324);Txt(text,x+diameter*.19f,y+diameter*.29f,diameter*.2f,Ink,true,diameter);}
    public void Title(string title,string sub,float x=60,float y=110,int size=42)
    {Txt(title,x,y,size,Ink,true);Txt(sub,x,y+size+23,21,Muted);}
    public void Chrome(string label,bool light=false,int y=0)
    {Box(0,y,1600,56,light?Surface:Bg);Txt("MyWhoosh HD / 中文助手",30,y+17,18,Muted);Txt(label,660,y+17,18,Muted);Line(1452,y+28,1467,y+28,Muted,2);Box(1500,y+21,14,13,Bg,0,Muted);Line(1550,y+22,1563,y+35,Muted,2);Line(1563,y+22,1550,y+35,Muted,2);}
    public void Footer(int n,string name)
    {Box(0,966,1600,34,Bg);Txt($"{n:00}  {name}  /  界面方案预览 · 示例状态 · 尚未接入实际操作",34,973,17,Muted);}
    public void Save(string path)=>Image.Save(path,ImageFormat.Png);
    public void Dispose(){G.Dispose();Image.Dispose();}
}

internal static class Program
{
    static void Nav(Board b,int x,int y,int w,string[] names,int selected=0,bool horizontal=false)
    {
        for(int i=0;i<names.Length;i++)
        {int xx=horizontal?x+i*w:x,yy=horizontal?y:y+i*65;if(i==selected)b.Box(xx,yy,w-12,48,b.Surface,10);b.Txt(names[i],xx+18,yy+12,22,i==selected?b.Ink:b.Muted,i==selected,w-30);}
    }
    static void Windows(Board b,int x,int y,int w=420)
    {b.Txt("游戏窗口",x,y,27,null,true);b.Txt("1280 × 720",x,y+54,36,null,true);b.Txt("普通小窗 · 尺寸可调整",x,y+105,20,b.Muted);b.Line(x,y+153,x+w,y+153);b.Txt("游戏始终置顶",x,y+180,23);b.Switch(x+w-63,y+180);b.Txt("仅作用于 MyWhoosh 游戏",x,y+230,19,b.Muted);b.Button("应用窗口设置",x,y+276,w,58);}
    static void Recovery(Board b,int x,int y,int w=420)
    {b.Txt("随时回到原版",x,y,27,null,true);b.Txt("首次修改前，保留恢复备份。",x,y+48,21,b.Muted);b.Badge("恢复备份可用",x,y+91,w:180);b.Button("恢复原版英文",x,y+148,w,58,false);}
    static Board Graphite()
    {
        var b=new Board("#111315","#F2F5F8","#9CA6B3","#2A82FF","#22272D","#37404A");b.Chrome("01 / 石墨工作台");
        b.Box(0,56,225,910,"#191C20");b.Txt("WHOOSH",30,95,30,null,true,185);b.Txt("本地中文助手",30,142,19,b.Muted);
        Nav(b,20,225,194,["总览","语言与资源","游戏窗口","恢复中心","帮助"]);b.Badge("本地运行",30,853,w:158);
        b.Title("让骑行更顺手。","语言、窗口和恢复，在一个工作台完成。",270,98);
        b.Badge("游戏版本 6.2.3.0",1260,122,w:260);
        b.Box(270,226,740,320,b.Surface,24);b.Badge("中文已就绪",298,253,bg:"#143A2A",fg:"#7CE1AD");b.Txt("你的训练，现在是中文。",298,313,36,null,true,675);b.Txt("界面与课程资源已在本地准备好。",298,376,21,b.Muted);b.Button("检查并更新汉化",298,436,284);b.Txt("查看资源详情",635,455,21,b.Muted);b.Ring(868,270,98,"中文");
        b.Box(1030,226,505,522,b.Surface,24);Windows(b,1060,258,440);
        b.Box(270,566,356,182,b.Surface,20);b.Txt("课程中文覆盖",298,596,22,b.Muted);b.Txt("2,060",298,635,48,null,true);b.Txt("条课程名称与说明",298,699,19,b.Muted);
        b.Box(644,566,366,182,b.Surface,20);b.Txt("安全第一",672,598,26,null,true);b.Txt("修改前先备份\n未知内容保持原样",672,648,23,b.Muted);
        b.Box(270,770,1265,152,b.Surface,22);b.Txt("不习惯中文？",300,805,29,null,true);b.Txt("恢复原版英文，不影响你的骑行档案。",300,856,22,b.Muted);b.Button("恢复原版英文",1240,818,265,62,false);
        b.Footer(1,"石墨工作台");return b;
    }
    static Board Cloud()
    {
        var b=new Board("#F5F7FB","#18232E","#62717E","#1674EA","#FFFFFF","#DEE5ED");b.Chrome("02 / 云白极简",true);
        b.Txt("WHOOSH",62,92,26,null,true);Nav(b,558,82,190,["我的中文","窗口设置","备份与恢复","帮助"],0,true);b.Line(60,149,1540,149);
        b.Txt("少一点操作，\n多一点专注。",80,204,57,null,true,680);b.Txt("中文资源内置。打开助手，检查一次就能开始。",80,368,21,b.Muted);
        b.Button("检查并安装中文",80,435,305,72);b.Txt("已有备份，可随时恢复英文",80,532,23,b.Muted);b.Badge("本地处理 · 不上传存档",80,587,w:300);
        b.Box(820,205,690,415,"#EAF1FC",20);b.Txt("本次准备",862,242,25,null,true);
        string[] labels=["游戏版本","汉化资源","课程中文","恢复备份"];string[] vals=["6.2.3.0","随程序内置","2,060 条","已保存"];
        for(int i=0;i<4;i++){b.Txt(labels[i],862,309+i*69,23,b.Muted);b.Txt(vals[i],1230,309+i*69,25,null,true,240);if(i<3)b.Line(862,356+i*69,1468,356+i*69);}
        b.Line(80,686,1510,686);b.Txt("顺手调整你的游戏窗口",80,726,31,null,true);b.Txt("普通小窗",80,790,23,b.Muted);b.Txt("1280 × 720",80,835,37,null,true);
        b.Txt("不被普通窗口遮挡",545,790,23);b.Switch(824,790);b.Txt("可随时取消游戏置顶",545,844,20,b.Muted);b.Button("打开窗口设置",1000,788,270,67);b.Txt("恢复英文",1330,810,23,b.Muted, false,180);
        b.Footer(2,"云白极简");return b;
    }
    static Board Racing()
    {
        var b=new Board("#07121C","#F3F8FB","#8DA5B9","#D8FF00","#102232","#26455E");b.Chrome("03 / 竞速座舱");
        b.Box(0,56,112,910,"#050E17");
        b.Box(18,86,40,40,b.Accent,4);b.Txt("W",27,92,28,b.Bg,true,26);b.Txt("中文助手",18,140,15,b.Muted,false,86);
        b.Txt("导航",18,268,14,"#5C7B92",false,86);
        string[] nav=["总览","汉化","窗口","恢复"];
        for(int i=0;i<4;i++)
        {
            float y=300+i*96;bool on=i==0;
            if(on){b.Slab(14,y,82,74,10,b.Accent);b.Txt(nav[i],30,y+16,20,"#08121C",true,60);b.Txt($"{i+1:00}",30,y+50,12,"#41520A",false,60);}
            else{b.Slab(14,y,82,74,10,b.Surface,b.Border);b.Txt(nav[i],30,y+16,19,b.Muted,false,60);b.Txt($"{i+1:00}",30,y+50,12,"#5C7B92",false,60);}
        }
        b.Line(18,700,94,700,"#1B3448",1);b.Txt("状态",18,716,14,"#5C7B92",false,86);b.Txt("已就绪",18,738,17,b.Accent,true,86);
        b.Badge("本地",16,884,bg:"#13293A",fg:"#8DA5B9",w:78);

        b.Slab(136,80,606,106,20,b.Accent);
        b.Txt("WHOOSH · 中文控制台",166,110,32,"#07121C",true,540);
        b.Txt("语言 / 窗口 / 恢复，一个面板搞定",166,152,15,"#4E5F12",false,540);

        b.Txt("游戏版本",790,96,15,b.Muted,false,190);b.Txt("6.2.3.0",790,120,28,null,true,190);
        b.Line(966,92,966,178,"#1E3A50",1);
        b.Txt("中文课程",994,96,15,b.Muted,false,190);b.Txt("2,060 条",994,120,28,null,true,190);
        b.Line(1170,92,1170,178,"#1E3A50",1);
        b.Txt("备份状态",1198,96,15,b.Muted,false,190);b.Txt("已保存",1198,120,28,b.Accent,true,190);
        b.Badge("本地 · 离线",1400,120,bg:"#13293A",fg:"#8DA5B9",w:150);

        b.Txt("骑行前，先调好这三件事。",136,212,46,null,true,900);
        b.Txt("语言、窗口、恢复。检查一次，之后打开游戏就能用。",136,282,21,b.Muted,false,940);
        for(int i=0;i<5;i++){float x=1120+i*72;b.Line(x,300,x+46,236,"#1B3A50",2);}
        b.Line(1480,300,1526,236,"#3C5A16",2);
        b.Txt("资源内置 · 不依赖外部下载",1120,312,15,"#5C7B92",false,440);
        

        b.Slab(136,336,400,530,18,b.Surface,b.Border);
        b.Txt("01",160,352,16,b.Accent,true,60);b.Txt("中文界面",160,376,28,null,true,340);b.Line(160,428,512,428,"#1E3A50",1);
        b.Ring(242,462,188,"中文");
        b.Txt("语言包已就绪",160,672,18,b.Accent,true,340);
        b.Txt("课程名称、简介与界面文字，\n统一显示为中文。",160,706,17,b.Muted,false,344);
        b.Button("检查并安装中文",160,782,344,62);

        b.Slab(568,336,400,530,18,b.Surface,b.Border);
        b.Txt("02",592,352,16,b.Accent,true,60);b.Txt("游戏窗口",592,376,28,null,true,340);b.Line(592,428,944,428,"#1E3A50",1);
        b.Box(592,458,344,150,"#091722",0,b.Border);b.Box(592,458,344,30,"#17324A");
        using(var d=new SolidBrush(Board.C("#5C7B92"))){b.G.FillEllipse(d,606,468,8,8);b.G.FillEllipse(d,620,468,8,8);b.G.FillEllipse(d,634,468,8,8);}
        b.Txt("MyWhoosh.exe",652,464,14,b.Muted,false,140);
        b.Txt("1280 × 720",614,506,26,null,true,300);
        b.Txt("普通小窗 · 可拖动边缘缩放",614,546,16,b.Muted,false,300);
        b.Line(884,596,916,564,"#31546F",2);b.Line(896,596,916,576,"#31546F",2);b.Line(908,596,916,588,"#31546F",2);
        b.Txt("拖动窗口边缘，就能改成任意大小。",592,640,16,b.Muted,false,344);
        b.Txt("始终置顶",592,678,19,null,false,340);b.Switch(870,676,true);
        b.Txt("排在其他窗口前面，不被遮挡。",592,718,16,b.Muted,false,344);
        b.Button("应用窗口设置",592,782,344,62);

        b.Slab(1000,336,568,530,18,b.Surface,b.Border);
        b.Txt("03",1024,352,16,b.Accent,true,60);b.Txt("恢复原版",1024,376,28,null,true,470);b.Line(1024,428,1544,428,"#1E3A50",1);
        string[] steps=["首次修改前，自动保存一份备份","保留英文原文与原有窗口设置","随时一键切回英文界面"];
        for(int i=0;i<3;i++)
        {
            float y=456+i*40;
            b.Box(1024,y+4,6,6,b.Accent,0);
            b.Txt($"{i+1}",1044,y,15,"#5C7B92",false,40);
            b.Txt(steps[i],1072,y-3,19,b.Ink,false,470);
        }
        b.Box(1024,592,520,100,"#0B1A27",0,b.Border);
        b.Txt("备份快照",1044,608,15,"#5C7B92",false,200);b.Txt("今天 14:20 · 修改前",1044,630,18,null,false,300);
        b.Txt("可恢复：界面 / 课程 / 窗口设置",1044,660,15,b.Muted,false,460);
        b.Button("恢复原版英文",1024,736,296,60,false);b.Txt("打开备份目录 →",1350,754,18,b.Accent,false,190);
        b.Txt("恢复后重新打开游戏，即回到英文界面。",1024,818,16,b.Muted,false,500);

        b.Box(136,890,1410,2,b.Accent);
        b.Txt("安全提示：所有改动都在本机完成，修改前自动备份。",136,912,18,b.Muted,false,900);
        b.Txt("查看操作记录 →",1380,912,18,"#5C7B92",false,190);
        b.Footer(3,"竞速座舱");return b;
    }
    static Board Wizard()
    {
        var b=new Board("#F6F7FA","#162333","#617187","#2867E8","#FFFFFF","#DBE3EF");b.Chrome("04 / 小白向导",true);
        b.Box(0,56,400,910,"#E9EEF8");b.Txt("WHOOSH",54,104,29,null,true);b.Txt("第一次用？\n跟着三步就好。",54,214,37,null,true,320);
        string[] steps=["找到你的游戏","安装中文","按你的习惯显示"];
        for(int i=0;i<3;i++){b.Badge((i+1).ToString("00"),54,418+i*101,i==1?b.Accent:"#DBE4F4",i==1?"#FFFFFF":b.Muted,64);b.Txt(steps[i],143,426+i*101,24,null,i==1,235);}
        b.Txt("想回到英文？\n恢复入口一直都在。",54,792,24,b.Muted, false,295);
        b.Badge("第 2 步 / 共 3 步",474,124,w:245);b.Txt("给游戏换上中文。",474,205,49,null,true);b.Txt("无需另找资源包，助手已为你准备好。",478,284,25,b.Muted);
        b.Box(474,356,1020,179,b.Surface,18,b.Border);b.Txt("简体中文",512,387,32,null,true);b.Txt("游戏界面 + 课程名称与说明",512,440,24,b.Muted);b.Badge("资源已内置",1250,406,"#EBF6EF","#22784B",194);
        b.Box(474,561,1020,78,"#EDF3FF",12);b.Txt("安装前请退出游戏；助手会先保存恢复备份。",504,585,24,b.Muted);
        b.Button("安装中文并继续",474,679,1020,80);b.Txt("上一步",475,797,23,b.Muted);b.Txt("恢复原版英文",1220,797,23,b.Muted);
        b.Line(474,862,1494,862);b.Txt("接下来可选：1280 × 720 小窗  /  游戏置顶",475,898,23,b.Muted);
        b.Footer(4,"小白向导");return b;
    }
    static Board Sage()
    {
        var b=new Board("#F0F1E9","#253E34","#728078","#2F7560","#FAFBF7","#D6DFD5");b.Chrome("05 / 鼠尾草桌面",true);
        b.Txt("whoosh",66,91,38,null,true);b.Txt("中文助手",235,105,22,b.Muted);Nav(b,742,83,198,["总览","窗口设置","恢复与帮助"],0,true);
        b.Box(60,180,1480,352,"#DFE7D9",32);b.Txt("把注意力，\n留给骑行。",104,225,49,null,true);b.Txt("中文资源在本地，操作只需两步。",104,369,24,b.Muted);b.Button("检查并更新中文",104,431,306,62);
        b.Box(850,217,638,276,b.Surface,24);b.Txt("你的中文准备情况",881,246,27,null,true);b.Line(881,294,1450,294);b.Txt("课程资源",883,325,22,b.Muted);b.Txt("2,060 条",1204,319,31,null,true,235);b.Txt("当前语言",883,389,22,b.Muted);b.Txt("简体中文",1204,384,29,null,true,235);b.Badge("恢复备份已保存",881,438,w:241);
        b.Box(60,558,842,362,b.Surface,25);b.Txt("一个刚刚好的小窗",93,590,31,null,true);b.Txt("1280 × 720",93,650,44,null,true);b.Txt("边工作，边保持骑行画面可见。",93,719,24,b.Muted);b.Txt("游戏窗口置顶",93,772,24);b.Switch(418,773);b.Button("调整窗口",627,786,231,62);
        b.Box(927,558,613,362,b.Surface,25);Recovery(b,964,598,533);
        b.Footer(5,"鼠尾草桌面");return b;
    }
    static Board Swiss()
    {
        var b=new Board("#F7F7F3","#121413","#737970","#20231F","#EEEEEA","#BBBDB6");b.Chrome("06 / 瑞士排版",true);
        b.Txt("WHOOSH",60,106,32,null,true);b.Txt("语言与画面工具",1125,113,27,b.Muted);b.Line(60,171,1540,171,"#121413",3);
        b.Txt("更简单的\n中文骑行。",56,215,81,null,true,680);b.Box(785,220,755,336,"#F2D327");b.Txt("01",821,249,66,"#111413",true);b.Txt("语言",1017,269,38,"#111413",true);b.Txt("内置资源。检查后直接安装。",1017,344,24,"#484A38",false,460);b.Button("检查并安装中文",1017,430,460,72,true,"#111413");
        b.Line(60,610,1540,610,"#121413",2);b.Txt("02",60,660,63,null,true);b.Txt("小窗与置顶",270,665,37,null,true);b.Txt("1280 × 720 / 普通窗口",270,726,25,b.Muted);b.Switch(667,725);b.Button("设置画面",1072,675,468,73,false);
        b.Line(60,805,1540,805,"#121413",2);b.Txt("03",60,844,63,null,true);b.Txt("恢复英文",270,852,37,null,true);b.Txt("保留备份，随时回到原版。",742,865,25,b.Muted);b.Txt("打开恢复中心  →",1260,865,25,null,true,300);
        b.Footer(6,"瑞士排版");return b;
    }
    static Board Engineer()
    {
        var b=new Board("#111A20","#DBE8EC","#7F9AA4","#247F70","#19272F","#35505B");b.Chrome("07 / 工程控制台");
        b.Box(0,57,1600,65,b.Surface);b.Txt("WHOOSH / 工作区",28,77,27,null,true);b.Txt("总览       资源       窗口       恢复",498,81,22,b.Muted);b.Badge("本地 · 示例状态",1320,71,"#183D36","#75D5BD",239);
        b.Box(24,145,278,672,b.Surface,4,b.Border);b.Txt("资源导航",45,169,24,null,true);b.Line(44,213,282,213);string[] tree=["游戏构建","  6.2.3.0","中文资源","  界面补丁","  课程字典","恢复备份","  已保存记录","帮助与说明"];for(int i=0;i<tree.Length;i++)b.Txt(tree[i],46,246+i*61,22,i is 1 or 3 or 4 or 6?b.Muted:b.Ink,i is 0 or 2 or 5,245);
        b.Box(324,145,781,419,b.Surface,4,b.Border);b.Txt("资源检查结果",350,168,27,null,true);b.Badge("中文已准备",880,165,"#183D36","#75D5BD",190);b.Line(349,221,1080,221);b.Txt("检查项",350,248,21,b.Muted);b.Txt("结果",790,248,21,b.Muted);
        string[] a=["主资源构建","界面汉化资源","课程中文覆盖","恢复记录"],v=["版本匹配","资源内置","2,060 条","可用"];for(int i=0;i<4;i++){b.Txt(a[i],350,302+i*56,23);b.Txt(v[i],790,302+i*56,23,"#7FDABF",false,270);}
        b.Box(1125,145,451,672,b.Surface,4,b.Border);Windows(b,1153,181,389);b.Line(1153,557,1542,557);b.Txt("恢复中心",1153,591,26,null,true);b.Txt("保留其他未知修改",1153,640,22,b.Muted);b.Button("恢复原版英文",1153,704,389,60,false);
        b.Box(324,585,781,232,b.Surface,4,b.Border);b.Txt("本地日志 / 示例",350,609,23,b.Muted);b.Txt("[CHECK]  构建识别完成\n[RESOURCE]  内置资源可用\n[BACKUP]  首次修改前保留恢复记录",350,665,23,"#91B7A7",false,730,"Consolas");
        b.Button("检查并安装汉化",24,848,500,72);b.Txt("所有操作仅在本机执行，不上传存档。",558,875,23,b.Muted);
        b.Footer(7,"工程控制台");return b;
    }
    static Board Compact()
    {
        var b=new Board("#E5EDF5","#1B3650","#6D859D","#287FD5","#F5F9FD","#B8CBDD");
        using(var gradient=new LinearGradientBrush(new Rectangle(0,0,1600,966),Board.C("#DFEAF5"),Board.C("#F2F4F8"),35f))b.G.FillRectangle(gradient,0,0,1600,966);
        b.Txt("08 / 轻量悬浮舱",63,56,27,b.Muted,true);b.Txt("一个更小的助手窗口，\n不抢走游戏的空间。",67,232,45,null,true,585);b.Txt("汉化、恢复、窗口控制保留在一屏。",67,381,24,b.Muted, false,550);
        b.Box(680,99,830,800,"#FFFFFF",30,b.Border);b.Txt("WHOOSH · 中文助手",722,130,27,null,true);b.Txt("—   □   ×",1340,135,23,b.Muted,false,148);b.Line(721,181,1469,181);
        b.Badge("中文资源已就绪",722,218,"#E4F3EE","#23765F",237);b.Txt("打开游戏，就是中文。",722,278,34,null,true,729);b.Txt("资源已内置 / 示例课程 2,060 条",722,336,22,b.Muted);
        b.Button("检查并更新",722,393,351,71);b.Button("恢复英文",1094,393,374,71,false);b.Line(721,506,1469,506);
        b.Txt("游戏画面",722,550,27,null,true);b.Badge("小窗",1348,550,w:119);b.Txt("1280 × 720",722,602,41,null,true);b.Txt("调整尺寸",1300,618,23,b.Accent,false,180);
        b.Box(722,685,746,92,"#EAF2FB",18);b.Txt("让游戏置顶",745,714,27,null,true);b.Switch(1378,715);b.Txt("可随时取消；只管理游戏窗口。",722,819,22,b.Muted);
        b.Txt("窄窗口优先\n主功能不藏在高级设置里",70,643,30,b.Muted,false,540);b.Footer(8,"轻量悬浮舱");return b;
    }
    static Board Editorial()
    {
        var b=new Board("#F3EEE6","#362F30","#847773","#8B344C","#FFFCF5","#D7C8BF");b.Chrome("09 / 编辑式修复中心",true);
        b.Box(0,56,315,910,"#743148");b.Txt("WHOOSH",41,110,31,"#FFF8EB",true);b.Txt("中文与恢复",41,169,24,"#E5C5C9");string[] names=["我的语言","恢复中心","窗口设置","使用说明"];for(int i=0;i<4;i++){if(i==1)b.Box(23,290+i*76,268,54,"#8E4359",6);b.Txt(names[i],42,305+i*76,25,"#F9EEE7",i==1,244);}b.Txt("选择中文，\n也保留回去的路。",41,779,26,"#E5C5C9",false,238);
        b.Txt("所有改变，\n都应有退路。",361,111,58,null,true,720,"SimSun");b.Txt("修改前先保存备份，恢复时保留未知变动。",364,265,24,b.Muted);
        b.Box(1145,108,378,182,b.Surface,8,b.Border);b.Txt("当前语言",1176,133,23,b.Muted);b.Txt("简体中文",1176,177,36,null,true,300);b.Badge("资源随程序内置",1176,235,w:281);
        b.Line(385,378,385,800,b.Accent,3);string[] labels=["识别你的游戏","保存修改前的恢复记录","安装中文界面与课程"];
        for(int i=0;i<3;i++){b.Box(374,398+i*130,22,22,b.Accent,11);b.Txt(labels[i],427,390+i*130,28,null,true,580);b.Txt(new[]{"6.2.3.0 / 示例版本","备份只留在你的电脑","需要时恢复原版英文"}[i],427,439+i*130,22,b.Muted, false,580);}
        b.Button("检查并安装中文",364,834,311,67);b.Button("恢复英文原版",696,834,311,67,false);
        b.Box(1085,341,438,560,b.Surface,16,b.Border);Windows(b,1113,377,382);b.Txt("恢复记录详情  →",1113,824,23,b.Muted);
        b.Footer(9,"编辑式修复中心");return b;
    }
    static Board Accessible()
    {
        var b=new Board("#FFFFFF","#15202B","#586B7D","#145CCF","#F1F5FA","#BCD0E4");b.Chrome("10 / 大字无障碍",true);
        b.Txt("MyWhoosh 中文助手",58,112,44,null,true);b.Badge("资源内置，无需另找下载包",1110,122,"#E8F3EE","#205E42",426);b.Txt("你想做什么？",58,213,54,null,true);b.Txt("先退出游戏，再操作下面的按钮。",61,303,29,b.Muted);
        b.Box(60,394,714,220,"#E9F1FF",18);b.Txt("把游戏改成中文",88,423,35,null,true);b.Txt("游戏界面和课程说明一起汉化。",88,476,27,b.Muted);b.Button("检查并安装中文",88,527,658,65);
        b.Box(799,394,740,220,b.Surface,18);b.Txt("恢复到原版英文",829,423,35,null,true);b.Txt("使用备份恢复，不删除骑行档案。",829,476,27,b.Muted);b.Button("恢复英文",829,527,680,65,false);
        b.Box(60,643,1479,265,b.Surface,18);b.Txt("让游戏开成小窗口",88,671,34,null,true);b.Txt("尺寸：1280 × 720",88,727,30,b.Muted);b.Button("调整游戏窗口",88,803,375,70);
        b.Line(771,679,771,871);b.Txt("让游戏保持在前面",825,671,34,null,true);b.Txt("不被普通应用窗口盖住",825,728,29,b.Muted);b.Switch(1410,729);b.Txt("仅作用于游戏，随时可以关闭。",825,818,24,b.Muted);
        b.Footer(10,"大字无障碍");return b;
    }
    [STAThread]static void Main(string[] args)
    {
        var folder=Path.GetFullPath(args.Length>0?args[0]:"screenshots");Directory.CreateDirectory(folder);
        string[] names=["石墨工作台","云白极简","竞速座舱","小白向导","鼠尾草桌面","瑞士排版","工程控制台","轻量悬浮舱","编辑式修复中心","大字无障碍"];
        Func<Board>[] build=[Graphite,Cloud,Racing,Wizard,Sage,Swiss,Engineer,Compact,Editorial,Accessible];var report=new List<object>();
        for(int i=0;i<build.Length;i++)
        {using var b=build[i]();string name=$"{i+1:00}-{names[i]}.png";b.Save(Path.Combine(folder,name));report.Add(new{file=name,width=1600,height=1000,kind="native-design-preview",gameOperations=false,textBoundsWarnings=b.Problems});Console.WriteLine(name+"  warnings="+b.Problems.Count);}
        using(var overview=new Bitmap(1700,2808))
        using(var g=Graphics.FromImage(overview))
        using(var f=new Font("Microsoft YaHei",24,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var note=new Font("Microsoft YaHei",18,FontStyle.Regular,GraphicsUnit.Pixel))
        {
            g.Clear(Board.C("#EFF2F6"));g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            g.DrawString("MyWhoosh 中文助手 · 10 套全新布局",f,Brushes.Black,38,22);
            g.DrawString("每套均为界面候选预览，示例状态，尚未接入真实游戏操作。",note,Brushes.DimGray,38,62);
            for(int i=0;i<10;i++)
            {
                int x=38+(i%2)*832,y=112+(i/2)*535;
                g.DrawString($"{i+1:00}  {names[i]}",f,Brushes.Black,x,y);
                using var src=Image.FromFile(Path.Combine(folder,$"{i+1:00}-{names[i]}.png"));
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(src,new Rectangle(x,y+37,794,496));
            }
            overview.Save(Path.Combine(folder,"00-十套布局总览.png"),ImageFormat.Png);
        }
        if(report.Count!=10)throw new InvalidOperationException("十套候选缺失");
        File.WriteAllText(Path.Combine(folder,"render-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
