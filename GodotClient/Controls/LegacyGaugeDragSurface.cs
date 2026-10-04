using System;
using Godot;

namespace ZirconClient.Controls;

/// <summary>
/// 旧版 EI 滚动条/锁链命中面。
/// EI 的 gauge 命中（0x42FFD0 → F707 0x417D00）把 pointer Y 的比例写回滚动值；
/// 按下/拖动/滚轮都作用在指定的 DXVScrollBar 上，拖动方向 = 轨道方向（向下拖 → 行窗下移）。
/// </summary>
public partial class LegacyGaugeDragSurface : DXControl
{
    public DXVScrollBar Target;
    public float Pad = 10f;
    private bool _gaugeDragging;

    public void SetTarget(DXVScrollBar target) => Target = target;

    public LegacyGaugeDragSurface()
    {
        MouseWheel += (s, e) => Target?.DoMouseWheel(s, e);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_gaugeDragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _gaugeDragging = false;
            }
            else
            {
                Vector2 localMouse = GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
                ApplyGaugeY(localMouse.Y);
            }
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        base._GuiInput(e);
        if (Target == null) return;

        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _gaugeDragging = mb.Pressed;
            if (mb.Pressed)
            {
                ApplyGaugeY((float)mb.Position.Y);
                AcceptEvent();
            }
        }
    }

    private void ApplyGaugeY(float y)
    {
        if (Target == null) return;
        int range = Target.MaxValue - Target.MinValue - Target.VisibleSize;
        if (range <= 0) return;
        float travel = Mathf.Max(1f, Size.Y - Pad * 2f);
        float t = Mathf.Clamp((y - Pad) / travel, 0f, 1f);
        Target.Value = Target.MinValue + (int)Math.Round(t * range);
    }
}
