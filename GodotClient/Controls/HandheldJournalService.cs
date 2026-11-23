using System;
using System.Collections.Generic;
using Godot;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

// A fixed-resolution account book: two facing columns, with a separate review seal
// before an action can run. The book never owns the item, quantity or operation.
public partial class HandheldJournalService : Control
{
    internal sealed record Line(string Key, string Name, string Detail, Texture2D Icon = null, Func<string> Act = null);
    public const int PageSize = 5;
    private static readonly Color Ink = new("15251e"), Leaf = new("23382c"), Rule = new("50604b"),
        Seal = new("dfc381"), Paper = new("eee8d6"), Quiet = new("a6b6a3");
    private Font font;
    internal readonly List<Line>[] Columns = { new(), new() };
    private readonly int[] cursor = new int[2];
    private int side;
    private string title, leftTitle, rightTitle, message;
    private Line review;
    internal Line Selected => Columns[side].Count == 0 ? null : Columns[side][cursor[side]];

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    internal void OpenBook(string heading, string left, string right)
    {
        title = heading; leftTitle = left; rightTitle = right; side = 0;
        cursor[0] = cursor[1] = 0; review = null; message = "";
        Show(); QueueRedraw();
    }

    internal void RestoreSelection(int column, string key)
    {
        var list = Columns[column];
        int index = key == null ? -1 : list.FindIndex(x => x.Key == key);
        cursor[column] = index >= 0 ? index : Math.Clamp(cursor[column], 0, Math.Max(0, list.Count - 1));
    }
    internal string CursorKey(int column) => Columns[column].Count > cursor[column] ? Columns[column][cursor[column]].Key : null;
    internal void Move(int column, int delta)
    {
        if (review != null) return;
        if (column >= 0) side = column;
        cursor[side] = Math.Clamp(cursor[side] + delta, 0, Math.Max(0, Columns[side].Count - 1));
        message = ""; QueueRedraw();
    }
    internal bool Back()
    {
        if (review == null) return false;
        review = null; QueueRedraw(); return true;
    }
    internal void Confirm()
    {
        if (review == null)
        {
            message = "";
            review = Selected?.Act == null ? null : Selected;
        }
        else
        {
            var old = review;
            review = null;
            // Execute the reviewed closure, not a replacement action at the same list index.
            message = Selected?.Key == old.Key && Selected?.Detail == old.Detail && Selected?.Act != null
                ? old.Act() : "内容已变更，请重新核对。";
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Ink);
        float scale = Math.Min(Size.X / 640f, Size.Y / 360f);
        DrawSetTransform((Size - new Vector2(640,360) * scale) / 2, 0, Vector2.One * scale);
        Text("情 境 手 札", 18, 26, 12, Seal, 110);
        Text(title, 135, 30, 22, Paper, 484);
        for (int c = 0; c < 2; c++)
        {
            int x = 14 + c * 312;
            var list = Columns[c];
            DrawRect(new Rect2(x,44,302,210), Leaf);
            DrawRect(new Rect2(x,44,302,210), c == side ? Seal : Rule, false);
            Text(c == 0 ? leftTitle : rightTitle,x+10,66,15, c == side ? Seal : Paper,205);
            Text($"{(list.Count == 0 ? 0 : cursor[c] / PageSize + 1)} / {(list.Count + PageSize - 1) / PageSize}", x+244,66,12,Quiet,54);
            int first = cursor[c] / PageSize * PageSize;
            if (list.Count == 0) Text("暂无条目",x+15,108,14,Quiet,230);
            for (int i = first; i < Math.Min(first+PageSize,list.Count); i++)
            {
                int y = 76 + (i-first)*34;
                var row = list[i]; bool selected = c == side && cursor[c] == i;
                if (selected) DrawRect(new Rect2(x+3,y,296,31),new Color("41523c"));
                if (row.Icon != null) DrawTextureRect(row.Icon,new Rect2(x+8,y+2,28,28),false);
                Text(row.Name,x+40,y+21,14,selected ? Paper : Quiet,250);
            }
        }
        var focus = review ?? Selected;
        Text(review == null ? focus?.Name : "核对后确认 · " + review.Name,20,277,15,Seal,598);
        DrawMultilineString(font,new Vector2(20,300),string.IsNullOrEmpty(message) ? focus?.Detail ?? "当前没有可操作条目" : message,
            HorizontalAlignment.Left,601,14,2,Paper);
        DrawLine(new Vector2(14,330),new Vector2(626,330),Rule);
        Text(review == null ? "方向 浏览    肩键 翻页    A 核对    B 返回    Y 原窗口" : "A 执行已核对的操作      B 取消",20,351,13,Quiet,607);
    }
    private void Text(string value,int x,int y,int size,Color color,int width) => DrawString(font,new Vector2(x,y),value ?? "",HorizontalAlignment.Left,width,size,color);
}
