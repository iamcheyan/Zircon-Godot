using System;
using Godot;

namespace ZirconClient.Controls;

/// <summary>
/// 掌机/手柄全局焦点光标框 (Retro RPG Golden Focus Cursor)。
/// 跟随目标控件的全局边界，绘制高亮脉动金黄描边框与 4 个角标，
/// 明确当前获得方向键/摇杆/A键确认的 UI 焦点。
/// </summary>
public partial class FocusIndicator : Control
{
    private Control _target;
    private double _time;

    public FocusIndicator()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 120;
    }

    public Control Target => _target;

    public void SetTarget(Control target)
    {
        _target = target;
        Visible = target != null && target.IsVisibleInTree();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_target == null || !IsInstanceValid(_target) || !_target.IsVisibleInTree())
        {
            if (Visible) Visible = false;
            return;
        }
        if (!Visible) Visible = true;

        var targetGlobalRect = _target.GetGlobalRect();
        Vector2 pos = targetGlobalRect.Position - new Vector2(4, 4);
        Vector2 size = targetGlobalRect.Size + new Vector2(8, 8);

        if (GlobalPosition != pos || Size != size)
        {
            GlobalPosition = pos;
            Size = size;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_target == null || Size.X <= 0 || Size.Y <= 0) return;

        float pulse = Mathf.Sin((float)_time * 5.0f) * 0.18f + 0.82f;
        Color gold = new Color(1.0f, 0.85f, 0.25f, pulse);
        Color goldTrans = new Color(1.0f, 0.75f, 0.2f, pulse * 0.18f);
        Color cornerGlow = new Color(1.0f, 1.0f, 0.85f, pulse);

        Rect2 rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, goldTrans, true);
        DrawRect(rect, gold, false, 2.0f);

        float cornerLen = Mathf.Clamp(Mathf.Min(Size.X, Size.Y) / 3.5f, 6f, 14f);
        float w = Size.X;
        float h = Size.Y;

        // Top-Left
        DrawLine(new Vector2(0, 0), new Vector2(cornerLen, 0), cornerGlow, 3f);
        DrawLine(new Vector2(0, 0), new Vector2(0, cornerLen), cornerGlow, 3f);

        // Top-Right
        DrawLine(new Vector2(w, 0), new Vector2(w - cornerLen, 0), cornerGlow, 3f);
        DrawLine(new Vector2(w, 0), new Vector2(w, cornerLen), cornerGlow, 3f);

        // Bottom-Left
        DrawLine(new Vector2(0, h), new Vector2(cornerLen, h), cornerGlow, 3f);
        DrawLine(new Vector2(0, h), new Vector2(0, h - cornerLen), cornerGlow, 3f);

        // Bottom-Right
        DrawLine(new Vector2(w, h), new Vector2(w - cornerLen, h), cornerGlow, 3f);
        DrawLine(new Vector2(w, h), new Vector2(w, h - cornerLen), cornerGlow, 3f);
    }
}
