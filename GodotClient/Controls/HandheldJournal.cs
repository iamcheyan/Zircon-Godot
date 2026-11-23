using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>Handheld-only journal. Input ownership and gameplay transitions belong to GameScene.</summary>
public partial class HandheldJournal : Control
{
    private static readonly string[] Tabs = { "背包", "人物", "技能", "任务", "社交", "系统" };
    private static readonly string[] TabPaths = {
        "M10 9V6h12v3M6 10h20v18H6ZM10 10v18m12-18v18M11 16h10v6H11Z",
        "M20 8a4 4 0 1 1-8 0a4 4 0 1 1 8 0ZM7 29v-9q0-6 9-6t9 6v9M12 18v11m8-11v11",
        "m16 3 3 9 10 4-10 3-3 10-4-10-9-3 9-4Zm9 0 1 4 4 1-4 1-1 4-1-4-4-1 4-1Z",
        "M7 4h19v24H7ZM3 8h8M3 15h8M3 23h8m4-14h7m-7 6h7m-7 6h5",
        "M3 7h26v20H3Zm0 0 13 12L29 7",
        "m13 3 6 0 1 4 4 2 4-1 3 5-3 3v3l2 3-3 5-4-1-3 2-1 3h-6l-1-4-4-2-4 1-3-5 3-3v-4l-2-3 3-5 4 1 3-2ZM20 16a4 4 0 1 1-8 0a4 4 0 1 1 8 0Z"
    };
    private readonly Texture2D[] tabIcons = new Texture2D[TabPaths.Length];
    private static readonly string[][] Categories = {
        new[] { "全部", "装备", "消耗", "材料", "任务", "腰带" },
        new[] { "装备", "属性", "抗性", "称号", "坐骑" },
        new[] { "已学", "主动", "开关", "被动", "未学" },
        new[] { "全部", "进行中", "已完成" }, new[] { "好友", "组队", "行会", "邮件", "排行" },
        new[] { "显示", "音频", "操作", "游戏", "帮助", "离开" }
    };
    private static readonly Color Ink = new("17251f"), Deep = new("101c16"), Tile = new("21352a"),
        Selected = new("344335"), Paper = new("f3eedb"), Muted = new("a4b29e"), Gold = new("d9bc73"), Line = new("495342");
    private readonly GameScene game;
    private readonly List<JournalEntry> rows = new();
    private readonly List<Rect2I> cells = new(); // y includes page number; one rectangle per stable entry
    private readonly Dictionary<(string, int, int), Bookmark> memory = new();
    private sealed class Bookmark { public string Id; public int Index, Scroll; public bool Sorted; }
    private sealed class Menu { public string Title; public IReadOnlyList<JournalAction> Actions; public int Index; }
    private readonly Stack<Menu> menus = new();
    private readonly int[] categories = new int[6];
    private readonly Control sheet = new();
    private readonly ScrollContainer scroll = new();
    private readonly Label detail = new();
    private readonly PaperDoll doll = GameScene.CreateJournalPaperDoll();
    private Font font;
    private Bookmark mark;
    private int tab, index, pages, scrollCap;
    private string feedback = "";
    private double elapsed;
    private bool projectionDirty;
    private Texture2D questPreview;
    private string primaryHint, secondaryHint;
    public void InvalidateProjection() => projectionDirty = true;
    private int CardTop => Preview ? 208 : tab == 2 || questPreview != null ? 180 : 135;
    private ulong occupied;
    private int Cat => categories[tab];
    private bool LegacyLayout => tab == 0 && Cat == 0 && mark?.Sorted != true;
    private int Cols => LegacyLayout ? game.JournalInventoryColumns : tab switch { 0 => Cat == 5 ? 5 : 6, 1 when Cat == 0 => 2, 2 => 6, _ => 1 };
    private int Lines => tab == 0 && Cat == 5 ? 2 : Cols == 1 ? 6 : 4;
    private int Page => rows.Count == 0 ? 0 : cells[index].Position.Y / Lines;
    private bool Preview => tab == 0 || (tab == 1 && Cat == 0);
    public bool IsOpen { get; private set; }
    public bool HasActionOverlay => menus.Count != 0;

    public HandheldJournal(GameScene game)
    {
        this.game = game;
        for (int i = 0; i < TabPaths.Length; i++)
        {
            using var image = new Image();
            image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='{TabPaths[i]}' stroke='white' fill='none' stroke-width='1.6'/></svg>");
            tabIcons[i] = ImageTexture.CreateFromImage(image);
        }
        Name = "HandheldJournal";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(sheet);
        sheet.Size = new(640, 360);
        sheet.MouseFilter = MouseFilterEnum.Ignore;
        sheet.Draw += DrawSheet;
        sheet.AddChild(doll);
        doll.Scale = Vector2.One * .38f;
        doll.Position = new(556, 208);
        sheet.AddChild(scroll);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        scroll.AddChild(detail);
        detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detail.VerticalAlignment = VerticalAlignment.Top;
        font = MirSkin.GetFont() ?? (Font)ThemeDB.FallbackFont;
        detail.AddThemeFontOverride("font", font);
        detail.AddThemeFontSizeOverride("font_size", 12);
        detail.AddThemeColorOverride("font_color", Paper);
        Resized += Fit;
        Visible = false;
        SetProcess(false);
    }
    private void Fit()
    {
        float scale = Math.Min(Size.X / 640, Size.Y / 360);
        sheet.Scale = Vector2.One * scale;
        sheet.Position = (Size - sheet.Size * scale) / 2;
        QueueRedraw();
    }
    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), Deep);
    public void Open()
    {
        IsOpen = Visible = true;
        Fit(); Load(); SetProcess(true);
    }
    public void Close()
    {
        if (IsOpen) Save();
        menus.Clear(); feedback = ""; doll.Visible = false; doll.SetProcess(false);
        Visible = IsOpen = false; SetProcess(false);
    }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        if (projectionDirty) { Save(); Reload(); }
        elapsed += delta;
        if (elapsed < 1) return;
        elapsed = 0;
        sheet.QueueRedraw(); // Status is live. Requery the projection on page entry and before actions.
    }
    private void Save()
    {
        if (mark == null) return;
        mark.Id = rows.Count > 0 ? rows[index].Id : null;
        mark.Index = index; mark.Scroll = scroll.ScrollVertical;
    }
    private void Load()
    {
        var key = (game.StartInfo?.Name ?? "", tab, Cat);
        if (!memory.TryGetValue(key, out mark)) memory[key] = mark = new Bookmark();
        feedback = ""; Reload();
    }
    private void Reload()
    {
        projectionDirty = false;
        rows.Clear(); game.JournalEntries(tab, Cat, rows);
        if (mark.Sorted) rows.Sort(static (a,b) => {
            int order = StringComparer.CurrentCulture.Compare(a.Name, b.Name);
            return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id);
        });
        index = -1;
        for (int i = 0; i < rows.Count; i++) if (rows[i].Id == mark.Id) { index = i; break; }
        if (index < 0)
        {
            if (menus.Count > 0) { menus.Clear(); feedback = "项目已变更，操作已取消"; }
            index = Math.Clamp(mark.Index, 0, Math.Max(0, rows.Count - 1));
        }
        Pack(); Present();
    }
    private void Pack()
    {
        cells.Clear(); occupied = 0;
        int page = 0, cols = Cols, lines = Lines;
        foreach (var row in rows)
        {
            int w = tab == 0 && Cat != 5 ? Math.Clamp(row.Width, 1, cols) : 1;
            int h = tab == 0 && Cat != 5 ? Math.Clamp(row.Height, 1, lines) : 1;
            int x, y;
            if (LegacyLayout)
            {
                int slot = game.JournalInventoryDisplayCell(row.Id);
                if (slot < 0) slot = cells.Count;
                cells.Add(new(slot % cols, slot / cols, w, h));
                page = Math.Max(page, slot / cols / lines);
                continue;
            }
            while (true)
            {
                for (y = 0; y <= lines-h; y++) for (x = 0; x <= cols-w; x++)
                {
                    ulong mask = 0;
                    for (int k = 0; k < h; k++) mask |= ((1UL << w)-1) << ((y+k)*cols+x);
                    if ((occupied & mask) != 0) continue;
                    occupied |= mask; cells.Add(new(x,y+page*lines,w,h)); goto placed;
                }
                page++; occupied = 0;
            }
            placed: ;
        }
        pages = page+1;
    }
    private JournalEntry Presentation(int i, bool detailRequested) => tab < 2
        ? game.JournalGearPresentation(tab, Cat, rows[i], detailRequested) : rows[i];
    private void Present()
    {
        bool modal = menus.Count > 0;
        doll.Visible = Preview && !modal; doll.SetProcess(doll.Visible);
        scroll.Visible = !modal;
        if (modal) { sheet.QueueRedraw(); return; }
        questPreview = tab == 3 && rows.Count != 0 ? game.JournalQuestPreview(rows[index].Id) : null;
        primaryHint = secondaryHint = null;
        if (rows.Count > 0)
        {
            var id = rows[index].Id;
            primaryHint = ActionHint(game.JournalActions(tab, Cat, id, false), "操作");
            secondaryHint = ActionHint(game.JournalActions(tab, Cat, id, true), "更多操作");
        }
        int y = CardTop + 40;
        scroll.Position = new(337,y); scroll.Size = new(280,312-y);
        doll.Scale = Vector2.One * (tab == 1 ? .48f : .43f);
        string text = rows.Count == 0 ? "此分类暂无记录。" : Presentation(index, true).Detail ?? "";
        if (detail.Text != text) detail.Text = text;
        scrollCap = mark.Scroll;
        Callable.From(RestoreScroll).CallDeferred();
        sheet.QueueRedraw();
    }
    private void RestoreScroll() { if (IsOpen) scroll.ScrollVertical = scrollCap; }
    public void ScrollDetail(int direction)
    {
        if (!IsOpen || menus.Count != 0) return;
        scroll.ScrollVertical += Math.Sign(direction)*30;
        scrollCap = mark.Scroll = scroll.ScrollVertical;
    }
    public bool Back()
    {
        if (menus.Count == 0) return false;
        menus.Pop(); Present(); return true;
    }
    public void Command(Key key)
    {
        if (!IsOpen) return;
        if (menus.TryPeek(out var menu))
        {
            if (key == Key.Up || key == Key.Down) menu.Index = Math.Clamp(menu.Index + (key == Key.Up ? -1 : 1), 0, menu.Actions.Count-1);
            if (key == Key.Enter)
            {
                Save(); Reload(); if (menus.Count == 0) return;
                var action = menu.Actions[menu.Index];
                if (action.Children != null) Push(action.Label, action.Children());
                else if (action.Execute != null) { menus.Clear(); feedback = action.Execute() ?? ""; if (IsOpen) Reload(); }
            }
            sheet.QueueRedraw(); return;
        }
        if (key is Key.Q or Key.E or Key.Key1 or Key.Key2)
        {
            Save();
            if (key is Key.Q or Key.E) tab = (tab + (key == Key.Q ? 5 : 1)) % 6;
            else categories[tab] = (Cat + (key == Key.Key1 ? Categories[tab].Length-1 : 1)) % Categories[tab].Length;
            Load(); return;
        }
        if (key == Key.F16) { Save(); mark.Sorted = !mark.Sorted; feedback = ""; Reload(); return; }
        if (rows.Count == 0) return;
        if (key is Key.Enter or Key.Space)
        {
            Save(); string id = mark.Id; Reload();
            if (rows.Count != 0 && rows[index].Id == id) Push(rows[index].Name, game.JournalActions(tab,Cat,id,key == Key.Space));
            return;
        }
        Move(key);
    }
    private void Push(string title, IReadOnlyList<JournalAction> actions)
    {
        if (actions == null || actions.Count == 0) feedback = "当前没有可用操作";
        else menus.Push(new Menu { Title = title, Actions = actions });
        Present();
    }
    private void Move(Key key)
    {
        bool horizontal = key is Key.Left or Key.Right;
        if (!horizontal && key is not (Key.Up or Key.Down)) return;
        int sign = key is Key.Left or Key.Up ? -1 : 1;
        Rect2I from = cells[index]; int best = index; float score = float.MaxValue;
        for (int i = 0; i < cells.Count; i++)
        {
            Rect2I to = cells[i];
            if (horizontal && to.Position.Y / Lines != Page) continue;
            var delta = (Vector2)(to.Position-from.Position) + (Vector2)(to.Size-from.Size)*.5f;
            float along = (horizontal ? delta.X : delta.Y)*sign;
            if (along <= 0) continue;
            int crossStart = horizontal ? to.Position.Y : to.Position.X, crossEnd = horizontal ? to.End.Y : to.End.X;
            int start = horizontal ? from.Position.Y : from.Position.X, end = horizontal ? from.End.Y : from.End.X;
            if (horizontal && (crossStart >= end || crossEnd <= start)) continue;
            float cost = along + Math.Abs(horizontal ? delta.Y : delta.X)*2;
            if (cost < score) { score = cost; best = i; }
        }
        if (best == index) return;
        index = best; mark.Scroll = 0; feedback = ""; Present();
    }

    private void Box(float x,float y,float w,float h,Color c) => sheet.DrawRect(new Rect2(x,y,w,h),c);
    private void Edge(float x,float y,float w,float h,Color c) => sheet.DrawRect(new Rect2(x,y,w,h),c,false,1);
    private void Text(float x,float y,string s,int size,Color c,float width) => sheet.DrawString(font,new(x,y),s ?? "",HorizontalAlignment.Left,width,size,c);
    private void Badge(int x,int y,string s,int w) { Box(x,y,w,14,Paper); Text(x+3,y+11,s,10,Deep,w-4); }
    private void DrawSheet()
    {
        Box(0,0,640,360,Ink); Box(0,0,640,70,Deep); Box(0,33,640,1,Line);
        Text(16,22,$"{game.StartInfo?.Name}   Lv.{game.PlayerLevel}",14,Paper,138);
        int hp = game.CurrentHealth, max = game.PlayerStats?[Stat.Health] ?? 0;
        Text(161,20,$"生命  {hp}/{max}",11,Paper,122); Box(161,24,83,2,Line);
        Box(161,24,83*Math.Clamp((float)hp/Math.Max(1,max),0,1),2,new Color("bb7361"));
        long gold = 0;
        foreach (var coin in game.Currencies) if (coin?.Info?.Type == CurrencyType.Gold) { gold = coin.Amount; break; }
        Text(299,21,"● 世界继续运行",11,Muted,98); Text(405,21,$"负重 {game.BagWeight}/{game.PlayerStats?[Stat.BagWeight]}",11,Muted,105);
        Text(519,21,$"金币 {gold:N0}",11,Gold,106);
        Badge(13,45,"L1",22); Badge(604,45,"R1",22);
        for (int i = 0; i < Tabs.Length; i++)
        {
            int x = 43+i*93; if (i == tab) { Box(x,35,88,33,Tile); Box(x,67,88,2,Gold); }
            sheet.DrawTextureRect(tabIcons[i],new Rect2(x+8,43,17,17),false,i == tab ? Gold : Muted);
            Text(x+32,57,Tabs[i],14,i == tab ? Paper : Muted,48);
        }
        Badge(16,78,"L2",22); int pos = 46;
        foreach (string category in Categories[tab])
        {
            int w = category.Length*12+15; bool active = category == Categories[tab][Cat];
            if (active) Box(pos,75,w,20,Selected);
            Text(pos+6,90,category,12,active ? Gold : Muted,w-8); pos += w+4;
        }
        Badge(pos+2,78,"R2",22); Text(515,90,mark?.Sorted == true ? "浏览顺序 · 名称" : "浏览顺序 · 默认",11,Muted,110);
        if (mark == null) return;
        Text(16,118,Categories[tab][Cat],15,Paper,195); Text(239,117,$"{Page+1} / {pages}",11,Gold,70);
        DrawRows(); DrawDetail();
        Box(0,324,640,36,Deep); Box(0,324,640,1,Line);
        bool modal = menus.Count != 0;
        if (modal || primaryHint != null) Hint(17,"A",modal ? "确认" : primaryHint);
        Hint(119,"B",modal ? "返回" : "关闭");
        if (!modal)
        {
            if (secondaryHint != null) Hint(179,"X",secondaryHint);
            if (rows.Count > 1) Hint(282,"Y","浏览排序");
            if (scroll.GetVScrollBar().Visible) Text(390,346,"右摇杆 · 滚动详情",11,Muted,129);
        }
        Badge(537,335,"Menu",32); Text(577,347,"关闭",12,Paper,45);
        if (menus.TryPeek(out var m)) DrawModal(m);
    }
    private static string ActionHint(IReadOnlyList<JournalAction> actions, string multiple) =>
        actions == null || actions.Count == 0 ? null : actions.Count == 1 ? actions[0].Label : multiple;
    private void Hint(int x,string key,string label)
    {
        sheet.DrawCircle(new(x+7,342),7,Paper); Text(x+3,346,key,10,Deep,10); Text(x+19,347,label,12,Paper,80);
    }
    private void DrawRows()
    {
        int cols = Cols, lines = Lines, page = Page;
        float cw = 298f/cols, ch = 172f/lines;
        if (cols > 1) for (int j=0;j<lines;j++) for(int i=0;i<cols;i++) Edge(16+i*cw,125+j*ch,cw-4,ch-4,Line);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = cells[i]; if (r.Position.Y/lines != page) continue;
            float x = 16+r.Position.X*cw,y=125+(r.Position.Y%lines)*ch,w=r.Size.X*cw-4,h=Math.Min(r.Size.Y,lines-r.Position.Y%lines)*ch-4;
            var row=Presentation(i,false); bool focus=i==index;
            Box(x,y,w,h,focus ? Selected : Tile); Edge(x,y,w,h,focus ? Gold : Line);
            bool list = cols <= 2;
            if (row.Icon != null)
            {
                Vector2 size=row.Icon.GetSize(); float side=list ? h-6 : Math.Min(w-6,h-5);
                float scale=Math.Min(side/Math.Max(1,size.X),side/Math.Max(1,size.Y)); size*=scale;
                sheet.DrawTextureRect(row.Icon,new Rect2(new Vector2(list ? x+4 : x+(w-size.X)/2,y+(h-size.Y)/2),size),false);
            }
            bool hasBadge = !string.IsNullOrEmpty(row.Badge);
            float badgeW = list && hasBadge ? Math.Min(w*.42f, font.GetStringSize(row.Badge,fontSize:10).X+6) : 0;
            if (list || row.Icon == null)
            {
                float inset=list && row.Icon != null ? h+1 : 5;
                Text(x+inset,y+h*.5f+4,row.Name,cols==1 ? 13 : 11,Paper,Math.Max(1,w-inset-badgeW-3));
            }
            if (hasBadge) Text(list ? x+w-badgeW : x+4,list ? y+h*.5f+4 : y+h-2,row.Badge,10,Gold,list ? badgeW-2 : w-7);
            if (focus) Box(x,y,3,8,Gold);
        }
        if (rows.Count == 0) Text(24,171,"此分类暂无记录",13,Muted,255);
        Text(16,313,string.IsNullOrEmpty(feedback) ? $"{rows.Count} 项　·　{Categories[tab][Cat]}" : feedback,11,string.IsNullOrEmpty(feedback) ? Muted : Gold,295);
    }
    private void DrawDetail()
    {
        int top=CardTop;
        if (Preview)
        {
            Text(333,128,"角色预览",12,Gold,120); Text(333,149,game.StartInfo?.Name,15,Paper,155);
            Text(333,169,$"等级 {game.PlayerLevel}　·　{game.StartInfo?.Class}",11,Muted,170);
            Text(333,191,"当前穿戴 · 角色纸娃娃",11,Muted,160);
        }
        else if (rows.Count > 0)
        {
            var row = rows[index];
            Text(333,117,$"{Tabs[tab]}  /  {Categories[tab][Cat]}",12,Gold,215);
            if (tab == 2 || questPreview != null)
            {
                var art = questPreview ?? row.Icon;
                if (art != null)
                {
                    Vector2 size = art.GetSize(); float scale = Math.Min(115f/Math.Max(1,size.X),69f/Math.Max(1,size.Y));
                    sheet.DrawTextureRect(art,new Rect2(new Vector2(554,141)-size*scale/2,size*scale),false);
                }
                Text(333,151,row.Badge,13,Paper,156);
            }
            else Text(496,117,row.Badge,11,Muted,120);
        }
        Box(326,top,298,316-top,Deep); Box(326,top,298,1,Line);
        Text(337,top+26,rows.Count == 0 ? Categories[tab][Cat] : rows[index].Name,16,Paper,275);
    }
    private void DrawModal(Menu m)
    {
        Box(0,0,640,324,new Color(0,0,0,.70f));
        const int visible = 4, step = 56;
        int count=Math.Min(visible,m.Actions.Count),h=50+count*step,y=(324-h)/2,start=m.Index/visible*visible;
        Box(146,y,348,h,Ink); Edge(146,y,348,h,Gold);
        Text(159,y+23,m.Title,15,Paper,285); Text(455,y+22,$"{menus.Count}",11,Gold,29);
        for(int k=0;k<count && start+k<m.Actions.Count;k++)
        {
            int n=start+k,ay=y+34+k*step; Box(157,ay,326,step-4,n==m.Index ? Selected : Deep);
            if(n==m.Index) Edge(157,ay,326,step-4,Gold);
            sheet.DrawMultilineString(font,new(165,ay+16),m.Actions[n].Label,HorizontalAlignment.Left,308,12,3,n==m.Index ? Gold : Paper);
        }
    }
}
