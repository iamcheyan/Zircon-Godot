using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using ZirconClient.Controls;

// godot-mono --headless --path GodotClient --script res://Tests/HandheldNpcDialogueChecks.cs
public partial class HandheldNpcDialogueChecks : SceneTree
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }

    public override async void _Initialize()
    {
        HandheldNpcDialogue view = null;
        SubViewport viewport = null;
        try
        {
            await ToSignal(this, SignalName.ProcessFrame);
            viewport = new SubViewport { Size = new Vector2I(640, 360), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            Root.AddChild(viewport);
            view = new HandheldNpcDialogue();
            viewport.AddChild(view);
            var options = new List<(int id, string label)>();
            for (int i = 0; i < 13; i++) options.Add((100 + i, $"请问第 {i + 1} 件事？"));
            view.SetPage("驿站老板娘", "[color=#e2bf68]长途跋涉的客人，先歇歇脚。[/color]\n路上风沙很大，听说北边的旧道最近不太平。", null, options);
            view.Open();

            Check(view.OptionCount == 13 && view.SelectedIndex == 0 && view.OptionPage == 0,
                "new page loads title, prose and selectable options with initial focus");
            Check(view.HandleInput(Key.Down) && view.SelectedIndex == 1, "down arrow moves option focus");
            view.PageOptions(1);
            Check(view.OptionPage == 1 && view.SelectedIndex == 5, "R1 advances option page and focuses its first row");
            view.HandleInput(Key.Up);
            Check(view.OptionPage == 0 && view.SelectedIndex == 4, "up arrow crosses to the preceding option page");
            view.PageOptions(1);
            view.PageOptions(1);
            Check(view.OptionPage == 2 && view.SelectedIndex == 10, "R1 pages clamp at the final page");
            view.PageOptions(-1);
            Check(view.OptionPage == 1 && view.SelectedIndex == 9, "L1 changes the page while preserving a valid focus");
            int sentId = -1, sendCount = 0;
            view.OptionSelected += id => { sentId = id; sendCount++; };
            Check(view.HandleInput(Key.Enter) && view.IsPending && sentId == 109 && sendCount == 1,
                "A confirms the focused option and raises Action<int>");
            Check(!view.ConfirmSelection() && sendCount == 1, "pending state blocks duplicate requests");
            view.PageOptions(1);
            view.HandleInput(Key.Down);
            Check(view.OptionPage == 1 && view.SelectedIndex == 9, $"pending state locks focus and paging (page={view.OptionPage}, index={view.SelectedIndex}, pending={view.IsPending})");
            Check(!view.HandleInput(Key.Escape), "B/back remains owned by the parent");
            view.SetPending(false);
            view.Close();
            view.SetPage("药铺掌柜", "[b]药品都在柜上。[/b] [url=https://invalid.example]不要打开[/url]", null,
                new List<(int id, string label)> { (7, "继续"), (0, "告辞") });
            Check(!view.IsPending && view.SelectedIndex == 0 && view.OptionPage == 0,
                "a new page resets pending state and focus");
            Check(!view.ConfirmSelection(), "hidden page cannot dispatch an option");
            view.Open();
            Check(view.HandleInput(Key.Enter) && view.IsPending && sendCount == 2 && sentId == 7,
                "new page becomes interactive after opening");
            view.SetPending(false);
            view.SetPage("驿站老板娘", new string('旅', 600), null,
                new List<(int id, string label)> { (1, "继续") });
            await ToSignal(this, SignalName.ProcessFrame);
            await ToSignal(this, SignalName.ProcessFrame);
            var body = view.GetNode<RichTextLabel>("Sheet/DialogueBody");
            var bodyScroll = body.GetVScrollBar();
            Check(bodyScroll.MaxValue > bodyScroll.Page, "long dialogue wraps and creates a scrollable body");
            view.ScrollBody(1);
            Check(bodyScroll.Value > 0, "right stick scrolls long dialogue down");
            view.ScrollBody(-1);
            Check(bodyScroll.Value == 0, "right stick scrolls long dialogue back up");

            string capture = OS.GetEnvironment("ZIRCON_NPC_DIALOGUE_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                int previousDrawRevision = view.DrawRevision;
                view.SetPage("驿站老板娘", "[color=#e2bf68]长途跋涉的客人，先歇歇脚。[/color]\n\n路上风沙很大，听说北边的旧道最近不太平。\n\n若要北行，记得带上水和干粮。", null,
                    new List<(int id, string label)> { (1, "打听北边旧道的消息，以及最近经过那里的商队是否遇到风沙和盗匪"), (2, "看看药品和补给"), (0, "先告辞") });
                Check(view.OptionCount == 3 && view.SelectedIndex == 0, "render fixture shows all dialogue choices");
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(view.DrawRevision > previousDrawRevision, "page replacement redraws the custom sheet");
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string captureDirectory = Path.GetDirectoryName(Path.GetFullPath(capture));
                Directory.CreateDirectory(captureDirectory);
                using var image = viewport.GetTexture().GetImage();
                Check(image.GetSize() == new Vector2I(640, 360), "dialogue sheet renders at 640×360");
                Check(image.SavePng(capture) == Error.Ok && new FileInfo(capture).Length > 0,
                    "handheld NPC dialogue screenshot captured");
            }

            Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            Quit(1);
        }
        finally
        {
            view?.Free();
            viewport?.Free();
        }
    }
}
