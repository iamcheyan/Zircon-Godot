using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;
using ZirconClient.HandheldActions;
using Time = Godot.Time;

namespace ZirconClient.Controls;

/// <summary>F2 four-corner handheld presentation. No gameplay input or packet ownership.</summary>
public partial class HandheldHud : Control
{
    private readonly GameScene _game;
    private readonly List<int> _buffIcons = new();
    private readonly HandheldBindingChoice[] _directActions = new HandheldBindingChoice[6];
    private readonly HandheldBindingChoice[] _fixedActions = new HandheldBindingChoice[6];
    private readonly HandheldMiniMap _map = new();
    private HandheldActionSnapshot _actionState;
    private Rect2[] _layout, _actionSlots;
    private int _shownPage = -1;
    private Font _font;
    private double _nextRefresh, _toastUntil;
    private string _toast = "";
    private static readonly Color Ink = new(.045f,.075f,.05f,.72f), Edge = new(.74f,.76f,.63f,.68f), Text = new(.94f,.93f,.85f);

    public static float ResourceFraction(int current, int maximum) => maximum <= 0 ? 0 : Math.Clamp(current/(float)maximum,0,1);
    /// <summary>Experience bar sits directly under the mana bar, in the classic thin style.</summary>
    internal static Rect2 ExperienceBounds(Vector2 viewport)
    {
        var mp = LayoutForViewport(viewport)[1];
        return new Rect2(mp.Position + new Vector2(15, 10), new Vector2(125, 5));
    }

    internal static float ExperienceFraction(decimal experience, decimal maximum) =>
        maximum <= 0 ? 1f : Math.Clamp((float)(experience / maximum), 0, 1);
    internal static Rect2[] ActionSlotBounds() => ActionSlotBounds(new Vector2(640, 360));
    internal static Rect2[] ActionSlotBounds(Vector2 viewport)
    {
        var area = DirectActionBounds(viewport);
        return new[] {
            new Rect2(area.Position + new Vector2(108, 20), new Vector2(26, 28)),
            new Rect2(area.Position + new Vector2(108, 52), new Vector2(26, 28)),
            new Rect2(area.Position + new Vector2(42, 20), new Vector2(26, 28)),
            new Rect2(area.Position + new Vector2(42, 52), new Vector2(26, 28)),
            new Rect2(area.Position + new Vector2(10, 52), new Vector2(26, 28)),
            new Rect2(area.Position + new Vector2(74, 52), new Vector2(26, 28))
        };
    }
    private static Rect2 DirectActionBounds(Vector2 viewport) => new(182, viewport.Y - 110, 148, 94);

    internal static Rect2 TargetCardBounds(Vector2 viewport)
    {
        const int width = 232, height = 78, margin = 16;
        float y = viewport.Y - margin - height;
        // Narrow displays put the target above the actions instead of covering them.
        if (viewport.X - margin - width < DirectActionBounds(viewport).End.X + 8)
            y = DirectActionBounds(viewport).Position.Y - height - 8;
        return new Rect2(viewport.X - margin - width, y, width, height);
    }

    // Coordinates are internal viewport pixels (640x360 on TG5040), not desktop HUD units.
    public static Rect2[] LayoutForViewport(Vector2 v)
    {
        float m = 16;
        var actions = DirectActionBounds(v);
        return new[] {
            new Rect2(m,18,125,9), new Rect2(m,35,99,7),
            new Rect2(v.X-m-82,15,82,82),
            new Rect2(m,v.Y-m-78,158,78),
            actions,
            new Rect2(actions.Position.X, v.Y - m - 170, 158, 126)
        };
    }

    public HandheldHud(GameScene game)
    {
        _game=game; MouseFilter=MouseFilterEnum.Ignore; ZIndex=80;
        _font=MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        AddChild(_map);
        SetProcess(true);
    }

    public void ShowToast(string text)
    {
        _toast=text ?? ""; _toastUntil=Time.GetTicksMsec()+1300;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_game==null || !Visible) return;
        Scale=Vector2.One/Math.Max(.01f,GameScene.UiScale);
        var v=GetViewport().GetVisibleRect().Size;
        if (v.X<=0 || v.Y<=0) return;
        if (_layout == null || Size != v)
        {
            Size = v;
            _layout = LayoutForViewport(v);
            _actionSlots = ActionSlotBounds(v);
            _map.Position = _layout[2].Position;
            _map.Size = _layout[2].Size;
        }
        _map.Source=_game.HandheldMiniMapSource;
        _map.PlayerDirection=_game.HandheldPlayerDirection;
        double now=Time.GetTicksMsec();
        _actionState = _game.HandheldActionState;
        if (now >= _nextRefresh || _shownPage != _actionState.CurrentPage)
        {
            _shownPage = _actionState.CurrentPage;
            _nextRefresh = now + 200;
            for (int i = 0; i < _directActions.Length; i++) _directActions[i] = _game.HandheldDirectAction(i);
            for (int i = 0; i < _fixedActions.Length; i++) _fixedActions[i] = _game.HandheldFixedAction(FixedKeys[i]);
            _game.FillHandheldHudBuffs(_buffIcons);
        }
        QueueRedraw();
    }

    private void Label(Vector2 at,string text,int size=9,Color? colour=null,float width=-1)
    {
        if(_font==null || string.IsNullOrEmpty(text))return;
        size=MirSkin.ScaledSize(size);
        DrawString(_font,at+new Vector2(0,1),text,HorizontalAlignment.Left,width,size,new Color(0,0,0,.9f));
        DrawString(_font,at,text,HorizontalAlignment.Left,width,size,colour ?? Text);
    }
    private void Meter(Rect2 r,float fraction,Color colour)
    {
        DrawRect(r,Ink); DrawRect(r,Edge,false,1);
        var inner=new Rect2(r.Position+Vector2.One,r.Size-Vector2.One*2);
        inner.Size=new Vector2(Math.Max(0,inner.Size.X*fraction),inner.Size.Y);
        DrawRect(inner,colour);
    }
    private void Icon(Rect2 rect,Texture2D texture,float alpha=1)
    {
        if(texture==null)return;
        var size=texture.GetSize();
        if(size.X<=0 || size.Y<=0)return;
        size*=Math.Min(rect.Size.X/size.X,rect.Size.Y/size.Y);
        DrawTextureRect(texture,new Rect2(rect.Position+(rect.Size-size)/2,size),false,new Color(1,1,1,alpha));
    }
    private void Heart(Vector2 p)
    {
        var c=new Color(.95f,.85f,.75f);
        DrawCircle(p+new Vector2(2,2),2.6f,c); DrawCircle(p+new Vector2(6,2),2.6f,c);
        DrawColoredPolygon(new[]{p+new Vector2(-.5f,3),p+new Vector2(8.5f,3),p+new Vector2(4,9)},c);
    }
    public override void _Draw()
    {
        if(_layout==null || _game==null)return;
        Heart(_layout[0].Position+new Vector2(0,-1));
        var hp=_layout[0]; hp.Position+=new Vector2(15,0); hp.Size-=new Vector2(15,0);
        var mp=_layout[1]; mp.Position+=new Vector2(15,0); mp.Size-=new Vector2(15,0);
        float health=ResourceFraction(_game.CurrentHealth,_game.PlayerStats[Stat.Health]);
        Meter(hp,health,new Color(.77f,.39f,.31f));
        Meter(mp,ResourceFraction(_game.CurrentMana,_game.PlayerStats[Stat.Mana]),new Color(.43f,.65f,.73f));
        DrawExperienceBar();
        var drop=_layout[1].Position;
        DrawColoredPolygon(new[]{drop+new Vector2(4,-1),drop+new Vector2(0,5),drop+new Vector2(2,8),drop+new Vector2(6,8),drop+new Vector2(8,5)},new Color(.67f,.81f,.85f));
        float buffY=53;
        if(health<=.25f && _game.PlayerStats[Stat.Health]>0)
        {
            Label(new Vector2(31,55),$"{_game.CurrentHealth:N0} / {_game.PlayerStats[Stat.Health]:N0}",9,new Color(1,.79f,.65f));
            buffY=62;
        }
        for(int i=0;i<Math.Min(8,_buffIcons.Count);i++)
            Icon(new Rect2(31+i*15,buffY,12,12),MirSkin.GetTexture(LibraryFile.CBIcon,_buffIcons[i]));
        if(_buffIcons.Count>8)Label(new Vector2(153,buffY+10),$"+{_buffIcons.Count-8}",8);
        DrawTargetCard(_game.HandheldHudTarget());
        DrawFixedActions();
        DrawDirectActions();
        DrawShoulderHint();
        if(Time.GetTicksMsec()<_toastUntil)
        {
            float w=_font.GetStringSize(_toast,fontSize:10).X;
            Label(new Vector2((Size.X-w)/2,Size.Y-126),_toast,10);
        }
    }
    private static readonly string[] DirectLabels = { "X", "Y", "↑", "↓", "←", "→" };
    private static readonly string[] FixedKeys = { "L2tap", "L2hold", "R2tap", "R2hold", "L1+R1", "L2+R2" };
    private static readonly string[] FixedLabels = { "L2", "L2·", "R2", "R2·", "L1+R1", "L2+R2" };

    private void DrawTargetCard(HandheldTargetInfo target)
    {
        if (string.IsNullOrWhiteSpace(target.Name)) return;
        var card = TargetCardBounds(Size);
        DrawRect(card, new Color(.06f,.08f,.09f,.88f));
        DrawRect(card, Edge, false, 1);
        var portrait = new Rect2(card.Position + new Vector2(7,7), new Vector2(54,64));
        DrawRect(portrait, new Color(.025f,.035f,.04f,.9f));
        DrawRect(portrait, Edge, false, 1);
        if (target.Icon != null) Icon(new Rect2(portrait.Position + Vector2.One*3, portrait.Size-Vector2.One*6), target.Icon);
        else Label(portrait.Position + new Vector2(8,36), "无图", 10, new Color("9ca3af"), 40);
        float x = card.Position.X + 69, width = card.Size.X - 77;
        Label(new Vector2(x,card.Position.Y+18), FitTargetText((target.Locked ? "锁定 · " : "")+target.Name,12,width),12,new Color("e2bf68"),width);
        Label(new Vector2(x,card.Position.Y+35), FitTargetText(target.Detail,10,width),10,Text,width);
        if (target.Health.HasValue)
            Meter(new Rect2(x,card.Position.Y+42,width,5),target.Health.Value,new Color(.77f,.24f,.19f));
        Label(new Vector2(x,card.Position.Y+64), FitTargetText(target.Stats,10,width),10,new Color("b6c1ad"),width);
    }

    private string FitTargetText(string text,int size,float width)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int fontSize=MirSkin.ScaledSize(size);
        if (_font.GetStringSize(text,fontSize:fontSize).X<=width) return text;
        while (text.Length>0 && _font.GetStringSize(text+"…",fontSize:fontSize).X>width)
        {
            int take=text.Length-1;
            if (take>0 && char.IsLowSurrogate(text[take]) && char.IsHighSurrogate(text[take-1])) take--;
            text=text[..take];
        }
        return text+"…";
    }

    private void DrawExperienceBar()
    {
        decimal max = _game.PlayerMaxExperience;
        var bar = ExperienceBounds(Size);
        Meter(bar, ExperienceFraction(_game.PlayerExperience, max), new Color(.86f, .73f, .34f));
        string text = max <= 0 ? "MAX" : $"{_game.PlayerExperience / max * 100m:0.#}%  {_game.PlayerExperience:N0}/{max:N0}";
        Label(new Vector2(bar.End.X + 5, bar.End.Y), text, 8, new Color(.92f, .88f, .68f), 240);
    }

    private void DrawFixedActions()
    {
        var p = _layout[3].Position;
        DrawRect(new Rect2(p, _layout[3].Size), Ink);
        DrawRect(new Rect2(p, _layout[3].Size), Edge, false, 1);
        for (int i = 0; i < 6; i++)
        {
            var choice = _fixedActions[i];
            Rect2 r = i switch
            {
                0 => new Rect2(p + new Vector2(5, 39), new Vector2(25, 25)),
                1 => new Rect2(p + new Vector2(25, 24), new Vector2(19, 19)),
                2 => new Rect2(p + new Vector2(43, 39), new Vector2(25, 25)),
                3 => new Rect2(p + new Vector2(63, 24), new Vector2(19, 19)),
                4 => new Rect2(p + new Vector2(91, 29), new Vector2(28, 28)),
                _ => new Rect2(p + new Vector2(124, 29), new Vector2(28, 28))
            };
            bool active = i < 2 ? _actionState?.Pressed.Contains(HandheldButton.L2) == true
                : i < 4 ? _actionState?.Pressed.Contains(HandheldButton.R2) == true
                : i == 4 ? _actionState?.Pressed.Contains(HandheldButton.L1) == true && _actionState.Pressed.Contains(HandheldButton.R1)
                : _actionState?.Pressed.Contains(HandheldButton.L2) == true && _actionState.Pressed.Contains(HandheldButton.R2);
            DrawRect(r, active ? new Color("34482e") : new Color("203527"));
            DrawRect(r, Edge, false, active ? 2 : 1);
            if (choice?.Icon != null) Icon(new Rect2(r.Position + Vector2.One * 2, r.Size - Vector2.One * 4), choice.Icon);
            else if (choice?.Binding.Kind == ActionKind.Menu) Label(r.Position + new Vector2(5, r.Size.Y * .65f), "⌖", 12, new Color("d9bc73"));
            else Label(r.Position + new Vector2(4, r.Size.Y * .65f), "·", 11, new Color("77836f"));
            Label(r.Position + new Vector2(1, -2), FixedLabels[i], i < 4 ? 8 : 7, Text);
            if (choice?.Binding.Kind == ActionKind.Item)
            {
                string count = choice.Detail.StartsWith("持有 ", StringComparison.Ordinal) ? choice.Detail[3..] : "";
                if (count.Length != 0) Label(r.Position + new Vector2(r.Size.X - 10, r.Size.Y - 1), count, 8, new Color("f1eddb"));
            }
        }
        if (_actionState?.Pressed.Contains(HandheldButton.L1) == true && _actionState.Pressed.Contains(HandheldButton.R1))
            Label(p + new Vector2(88, 68), _fixedActions[4]?.Name ?? "空", 8, new Color("d9bc73"), 68);
        if (_actionState?.Pressed.Contains(HandheldButton.L2) == true && _actionState.Pressed.Contains(HandheldButton.R2))
            Label(p + new Vector2(88, 68), _fixedActions[5]?.Name ?? "空", 8, new Color("d9bc73"), 68);
    }

    private void DrawDirectActions()
    {
        var area = _layout[4];
        DrawRect(area, Ink); DrawRect(area, Edge, false, 1);
        int page = _actionState?.CurrentPage ?? 0;
        Label(area.Position + new Vector2(8, 11), "L1", 8);
        for (int i = 0; i < 2; i++)
            DrawRect(new Rect2(area.Position + new Vector2(31 + i * 8, 6), new Vector2(5, 3)), page == i ? new Color("d9bc73") : new Color("53604d"));
        Label(area.Position + new Vector2(55, 11), "R1", 8);
        var slots = _actionSlots;
        for (int i = 0; i < slots.Length; i++)
        {
            var r = slots[i]; var choice = _directActions[i];
            bool pressed = _actionState?.Pressed.Contains(HandheldActionPage.DirectButtons()[i]) == true;
            DrawRect(r, pressed ? new Color("34482e") : new Color("203527"));
            DrawRect(r, Edge, false, pressed ? 2 : 1);
            if (choice?.Icon != null) Icon(new Rect2(r.Position + new Vector2(2, 2), new Vector2(r.Size.X - 4, 18)), choice.Icon);
            else if (choice?.Binding.Kind == ActionKind.Menu) Label(r.Position + new Vector2(8, 19), "⌖", 12, new Color("d9bc73"));
            else Label(r.Position + new Vector2(10, 19), "·", 11, new Color("77836f"));
            Label(r.Position + new Vector2(2, r.Size.Y + 1), DirectLabels[i], 8, new Color("f1eddb"));
            if (choice?.Binding.Kind == ActionKind.Item && choice.Detail.StartsWith("持有 ", StringComparison.Ordinal))
                Label(r.Position + new Vector2(16, r.Size.Y + 1), choice.Detail[3..], 7, new Color("d9bc73"));
            if (choice?.Binding.Kind == ActionKind.Skill && TryCooldown(choice.Binding.StableId, out double seconds) && seconds > 0)
            {
                DrawRect(new Rect2(r.Position + Vector2.One, r.Size - Vector2.One * 2), new Color(0.03f, 0.06f, 0.05f, .72f));
                Label(r.Position + new Vector2(9, 18), Math.Ceiling(seconds).ToString(), 10);
            }
        }
    }

    private bool TryCooldown(string stableId, out double seconds)
    {
        seconds = 0;
        if (!int.TryParse(stableId, out int id)) return false;
        var magic = _game.FindHandheldSkillForHud(id);
        if (magic == null) return false;
        var next = magic.NextCast;
        bool toggle = magic.Info.School == MagicSchool.Toggle || magic.Info.Property == MagicProperty.Toggle
            || magic.Info.Magic is MagicType.Thrusting or MagicType.HalfMoon or MagicType.DestructiveSurge or MagicType.FlameSplash;
        if (toggle && _game.ToggleTime > next) next = _game.ToggleTime;
        seconds = Math.Max(0, (next - Library.Time.Now).TotalSeconds);
        return true;
    }

    private void DrawShoulderHint()
    {
        if (_actionState == null) return;
        bool left = _actionState.Pressed.Contains(HandheldButton.L2);
        bool right = _actionState.Pressed.Contains(HandheldButton.R2);
        bool shoulders = _actionState.Pressed.Contains(HandheldButton.L1) && _actionState.Pressed.Contains(HandheldButton.R1);
        if (!left && !right && !shoulders) return;
        var r = new Rect2(10, Math.Max(90, Size.Y - 214), 194, 106);
        DrawRect(r, new Color("14221b", .94f)); DrawRect(r, new Color("476044"), false, 1);
        string key = left ? "L2" : right ? "R2" : "L1 + R1";
        Label(r.Position + new Vector2(9, 18), key + " · 松开 / 按住 / 组合", 11, new Color("f1eddb"));
        int tapIndex = left ? 0 : right ? 2 : 4;
        int holdIndex = left ? 1 : right ? 3 : 5;
        Label(r.Position + new Vector2(9, 39), "松开  " + (_fixedActions[tapIndex]?.Name ?? "空"), 10);
        Label(r.Position + new Vector2(9, 58), "长按  " + (_fixedActions[holdIndex]?.Name ?? "空"), 10);
        Label(r.Position + new Vector2(9, 77), "组合  " + (_fixedActions[shoulders ? 4 : 5]?.Name ?? "空"), 10);
        var progress = _actionState.LongPressProgress;
        DrawRect(new Rect2(r.Position + new Vector2(9, 88), new Vector2(176, 5)), new Color("203527"));
        DrawRect(new Rect2(r.Position + new Vector2(9, 88), new Vector2(176 * (float)progress, 5)), new Color("d9bc73"));
    }
}
