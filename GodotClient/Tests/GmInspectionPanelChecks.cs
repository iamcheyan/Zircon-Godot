using System;
using System.Linq;
using Godot;
using ZirconClient.Controls;

public partial class GmInspectionPanelChecks : SceneTree
{
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        try
        {
            foreach (bool handheld in new[] { false, true })
            {
                using var viewport = new SubViewport { Size = handheld ? new Vector2I(640, 360) : new Vector2I(800, 600) };
                Root.AddChild(viewport);
                var layer = new CanvasLayer();
                viewport.AddChild(layer);
                var panel = new GmInspectionPanel(handheld);
                layer.AddChild(panel);
                int browsed = -1;
                panel.MapSelected += id => browsed = id;
                panel.CloseRequested += panel.Hide;
                GmInspectionRow[] maps = [new("比奇 [0]", "当前地图", 1), new("Lost Paradise [1]", "只查询", 5)];
                GmInspectionRow[] ports = [new("#4733 → d903", "目标落点：(196,161) 落点在障碍上\n未实测", 4733)];
                panel.Present("0 (440,398) · Lv.255 · 技能 61 · 已穿 10\n只读诊断", maps, ports, []);
                panel.Show();
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(this, SignalName.ProcessFrame);
                var box = panel.GetChildren().OfType<PanelContainer>().Single();
                Check(panel.Size == (Vector2)viewport.Size && new Rect2(Vector2.Zero, viewport.Size).Encloses(box.GetRect()),
                    $"panel fits the actual {viewport.Size} viewport");
                if (handheld)
                {
                    Press(Key.Down);
                    Check(browsed == -1, "direction key does not browse or travel");
                    Press(Key.Enter);
                    Check(browsed == 5, "A queries the selected map");
                    Press(Key.E);
                    var list = (ItemList)panel.FindChildren("*", "ItemList", true, false)[0];
                    Check(list.ItemCount == 1 && list.GetItemText(0).Contains("d903"), "R1 reaches portal results");
                    Press(Key.Right);
                    Press(Key.Down);
                    Check(browsed == 5, "detail navigation does not change the map query");
                }
                Press(Key.Escape);
                Check(!panel.Visible, "Escape/B closes the panel");
                Root.RemoveChild(viewport);
                void Press(Key code)
                {
                    using var key = new InputEventKey { Keycode = code, Pressed = true };
                    Check(panel.HandleKey(key), $"handled {code}");
                }
            }
            Quit(0);
        }
        catch (Exception ex) { GD.PrintErr(ex); Quit(1); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print($"PASS: {message}");
    }
}
