using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;
using ZirconClient.Network;
using ZirconClient.Scripts;
using S = Library.Network.ServerPackets;

// Real renderer, real System.db. No network and no simulated service acceptance.
public partial class HandheldNpcCatalogChecks : SceneTree
{
    private static readonly Regex Options = new(@"\[([^\[\]\r\n]*):(-?[0-9]+)\]");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        GameScene game = null;
        NPCDialog dialog = null;
        CanvasLayer overlay = null;
        try
        {
            var args = OS.GetCmdlineUserArgs();
            int outputArg = Array.IndexOf(args, "--catalog-out");
            if (outputArg < 0 || outputArg + 1 >= args.Length)
                throw new ArgumentException("Provide --catalog-out <directory> after --");
            string output = args[outputArg + 1];
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("Catalog capture requires a display, for example Xvfb with gl_compatibility");
            if (!AutoLoginArgs.Handheld || (Globals.NPCInfoList == null && !DatabaseLoader.Load()))
                throw new InvalidOperationException("Handheld mode and System.db required");
            Directory.CreateDirectory(output);
            Root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
            Root.Size = new Vector2I(640, 360);
            if (Root.GetTexture().GetSize() != new Vector2(640, 360)) await ToSignal(this, SignalName.ProcessFrame);

            var pages = Globals.NPCPageList.Binding.OrderBy(p => p.Index).ToArray();
            var npcs = Globals.NPCInfoList.Binding.OrderBy(n => n.Index).ToArray();
            var coverage = new List<object>();
            var allReachable = new HashSet<int>();
            int failureCount = 0;
            game = new GameScene();
            GameScene.Game = game;
            var lookup = (Dictionary<uint, NPCInfo>)typeof(GameScene).GetField("_npcInfos", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            dialog = new NPCDialog();
            dialog.ApplyLegacyEiLayout();
            Root.AddChild(dialog);
            dialog.Hide();
            overlay = new CanvasLayer { Layer = 98 };
            Root.AddChild(overlay);
            var view = new HandheldNpcDialogue();
            overlay.AddChild(view);
            view.Open();

            foreach (var npc in npcs)
            {
                lookup[1] = npc;
                var visited = new HashSet<int>();
                var queue = new Queue<NPCPage>();
                var records = new List<object>();
                if (npc.EntryPage != null) queue.Enqueue(npc.EntryPage);
                while (queue.TryDequeue(out var page))
                {
                    if (!visited.Add(page.Index)) continue;
                    allReachable.Add(page.Index);
                    var links = DescribeLinks(page).ToArray();
                    foreach (var edge in links)
                        if (edge.Target != null && edge.Kind != "service-success") queue.Enqueue(edge.Target);
                    // SuccessPage can also be requested by service completion; record it as conditional.
                    if (page.SuccessPage != null) queue.Enqueue(page.SuccessPage);
                    var errors = new List<string>();
                    string shot = null;
                    if (!string.IsNullOrEmpty(page.Say))
                    {
                        dialog.ShowPage(new S.NPCResponse { ObjectID = 1, Page = page });
                        view.SetPage(dialog.HandheldNpcTitle, dialog.HandheldBody, dialog.HandheldPortrait, dialog.HandheldOptions);
                        var expected = links.Where(e => e.Kind == "inline" || e.Kind == "fallback").Select(e => e.Id.Value).Distinct();
                        var actual = dialog.HandheldOptions.Select(e => e.id);
                        if (!actual.SequenceEqual(expected)) errors.Add($"choices expected [{string.Join(',', expected)}] actual [{string.Join(',', actual)}]");
                        var fired = new List<int>();
                        void OnSelected(int id) => fired.Add(id);
                        view.OptionSelected += OnSelected;
                        foreach (int id in actual)
                        {
                            int before = fired.Count;
                            view.HandleInput(Key.Enter);
                            view.HandleInput(Key.Enter);
                            if (fired.Count != before + 1 || fired[^1] != id) errors.Add($"focus/pending {id}");
                            view.SetPending(false);
                            view.HandleInput(Key.Down);
                        }
                        view.OptionSelected -= OnSelected;
                        view.SetPage(dialog.HandheldNpcTitle, dialog.HandheldBody, dialog.HandheldPortrait, dialog.HandheldOptions);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var image = Root.GetTexture().GetImage();
                        if (image.GetSize() != new Vector2I(640, 360)) throw new InvalidOperationException($"Capture size {image.GetSize()}");
                        shot = $"n{npc.Index}-p{page.Index}.png";
                        if (image.SavePng(Path.Combine(output, shot)) != Error.Ok) throw new IOException(shot);
                    }
                    failureCount += errors.Count;
                    records.Add(new { page = page.Index, type = page.DialogType.ToString(), say = page.Say, shot,
                        portrait = shot != null && dialog.HandheldPortrait != null, edges = links.Select(e => new { kind = e.Kind, id = e.Id, target = e.Target?.Index, e.Note }), errors });
                }
                var points = npc.Region?.PointRegion;
                coverage.Add(new { index = npc.Index, name = npc.NPCName, mapFile = npc.Region?.Map?.FileName, mapDesc = npc.Region?.Map?.Description,
                    x = points?.FirstOrDefault().X, y = points?.FirstOrDefault().Y, pointCount = points?.Length, bitRegion = npc.Region?.BitRegion != null,
                    face = npc.FaceImage, category = npc.Category.ToString(), entryPage = npc.EntryPage?.Index,
                    requirements = npc.Requirements.Select(r => new { type = r.Requirement.ToString(), quest = r.QuestParameter?.Index, r.IntParameter1, @class = r.Class.ToString(), days = r.DaysOfWeek.ToString() }), pages = records });
            }
            var pageTable = pages.Select(p => new { index = p.Index, description = p.Description, type = p.DialogType.ToString(), say = p.Say, args = p.Arguments,
                goods = p.Goods.Count, itemTypes = p.Types.Select(t => t.ItemType.ToString()),
                edges = DescribeLinks(p).Select(e => new { kind = e.Kind, id = e.Id, target = e.Target?.Index, e.Note }),
                actions = p.Actions.Select(a => new { type = a.ActionType.ToString(), map = a.MapParameter1?.FileName, item = a.ItemParameter1?.Index, a.IntParameter1, a.IntParameter2 }),
                structuralReach = allReachable.Contains(p.Index) });
            File.WriteAllText(Path.Combine(output, "catalog.json"), JsonSerializer.Serialize(new { npcs = coverage, pages = pageTable }, Json));
            GD.Print($"Catalog: {npcs.Length} NPCs, {pages.Length} pages, {allReachable.Count} structurally reachable, {failureCount} interactive check errors");
            Quit(failureCount == 0 ? 0 : 1);
        }
        catch (Exception e) { GD.PrintErr(e); Quit(1); }
        finally { GameScene.Game = null; overlay?.Free(); dialog?.Free(); game?.Free(); }
    }

    private static IEnumerable<(string Kind, int? Id, NPCPage Target, string Note)> DescribeLinks(NPCPage page)
    {
        var buttons = page.Buttons.ToLookup(b => b.ButtonID);
        // Resolve default value substitutions before catalog comparison, without user state.
        string text = Regex.Replace(page.Say ?? "", @"<([^<>]*?):(.+?)>", "$2");
        var seen = new HashSet<int>();
        foreach (Match match in Options.Matches(text))
        {
            if (!int.TryParse(match.Groups[2].Value, out int id) || id < 0) continue;
            var button = buttons[id].FirstOrDefault();
            yield return ("inline", id, id == 0 ? null : button?.DestinationPage,
                id == 0 ? "client-close" : !seen.Add(id) ? "repeated-inline-id" :
                button == null ? "missing-binding" : button.DestinationPage == null ? "missing-destination" : "");
        }
        bool fallback = !Options.Matches(text).Cast<Match>().Any(m => int.TryParse(m.Groups[2].Value, out int id) && id >= 0);
        foreach (var group in buttons)
        {
            var first = group.First();
            if (fallback || (group.Key != 0 && !seen.Contains(group.Key)))
                yield return (fallback ? "fallback" : "not-exposed", group.Key, fallback && group.Key != 0 ? first.DestinationPage : null,
                    group.Key == 0 ? "client-close" : first.DestinationPage == null ? "missing-destination" : "");
            foreach (var duplicate in group.Skip(1))
                yield return ("shadowed-binding", group.Key, null, $"server takes first binding; shadowed target {duplicate.DestinationPage?.Index}");
        }
        foreach (var check in page.Checks)
            yield return ("check-failure", null, check.FailPage, $"{check.CheckType} {check.Operator} {check.IntParameter1} {check.IntParameter2}");
        if (page.SuccessPage != null)
            yield return (string.IsNullOrEmpty(page.Say) ? "auto-success" : "service-success", null, page.SuccessPage, "");
    }
}
