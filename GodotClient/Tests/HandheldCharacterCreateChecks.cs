using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using ZirconClient.Controls;
using ZirconClient.Scripts;

public partial class HandheldCharacterCreateChecks : SceneTree
{
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var scene = new SelectScene();
        var layer = new CanvasLayer();
        var box = new DXControl();
        var title = new DXLabel();
        try
        {
            const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
            void Set(string name, object value) => typeof(SelectScene).GetField(name, instance).SetValue(scene, value);
            scene.AddChild(layer);
            layer.AddChild(box);
            box.AddControl(title);
            Set("_uiLayer", layer);
            Set("_legacyCreateExplainBox", box);
            Set("_legacyCreateExplainTitle", title);
            var update = typeof(SelectScene).GetMethod("UpdateLegacyCreateExplain", instance);
            var labels = (List<DXLabel>)typeof(SelectScene).GetField("_legacyCreateExplainLines", instance).GetValue(scene);
            foreach (float scale in new[] { .75f, 1f, 1.25f })
            {
                layer.Transform = Transform2D.Identity.Scaled(Vector2.One * scale);
                MirSkin.SetUiScale(scale);
                for (int job = 0; job < 3; job++)
                {
                    Set("_legacyCreateClassIndex", job);
                    update.Invoke(scene, null);
                    var lines = labels.Where(label => label.Visible).Select(label => label.Text).ToArray();
                    string description = LegacyEiText.JobDescription(job);
                    if (string.Concat(lines) != description.Replace("\r", "").Replace("\n", ""))
                        throw new InvalidOperationException("Wrapping must preserve the full job description");
                    bool fits = AutoLoginArgs.Handheld
                        ? lines.All(line => MirSkin.MeasureTextPhysical(line, 11).X <= 430 * scale)
                        : lines.SequenceEqual(LegacyEiDialogText.Wrap(description, 430, 11));
                    if (!fits) throw new InvalidOperationException($"Job {job} description overflows or changes desktop wrapping at scale {scale}");
                }
            }
            GD.Print("PASS: three job descriptions fit the handheld panel or retain desktop wrapping at each scale");
            Quit(0);
        }
        catch (Exception error) { GD.PrintErr(error); Quit(1); }
        finally { scene.Free(); }
    }
}
