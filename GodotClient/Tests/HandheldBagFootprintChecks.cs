using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;
using ZirconClient.Formats;
using ZirconClient.Scripts;

// No server or save. Exercise full bag projection and resource-sized equipment.
public partial class HandheldBagFootprintChecks : SceneTree
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var cache = (Dictionary<(LibraryFile, int), Texture2D>)typeof(MirSkin).GetField("_textures", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var owned = new List<(int Frame, ImageTexture Texture)>();
        GameScene game = null;
        DXItemGrid grid = null;
        InventoryDialog bag = null;
        try
        {
            ClientUserItem Item(int frame, int w, int h, ItemType type)
            {
                using var pixels = Image.CreateEmpty(w * DXItemCell.CellWidth, h * DXItemCell.CellHeight, false, Image.Format.Rgba8);
                pixels.Fill(Colors.White);
                var texture = ImageTexture.CreateFromImage(pixels);
                cache.Add((LibraryFile.Inventory, frame), texture);
                owned.Add((frame, texture));
                var info = new ItemInfo();
                typeof(ItemInfo).GetField("_Image", Private).SetValue(info, frame);
                typeof(ItemInfo).GetField("_ItemType", Private).SetValue(info, type);
                typeof(ItemInfo).GetField("_ItemName", Private).SetValue(info, "fixture " + frame);
                return new ClientUserItem(info, 1) { Index = frame, Slot = frame };
            }
            const int firstFrame = 123450000;
            var small = Item(firstFrame, 1, 1, ItemType.Consumable);
            var sword = Item(firstFrame + 1, 2, 4, ItemType.Weapon);
            var armour = Item(firstFrame + 2, 3, 3, ItemType.Armour);
            game = new GameScene();
            game.Inventory = new ClientUserItem[90];
            for (int i = 0; i < game.Inventory.Length; i++)
                game.Inventory[i] = new ClientUserItem(small, 1) { Slot = i, Index = i + 1 };
            game.Inventory[88] = new ClientUserItem(sword, 1) { Slot = 88, Index = 89 };
            game.Inventory[89] = new ClientUserItem(armour, 1) { Slot = 89, Index = 90 };
            bag = new InventoryDialog();
            bag.ApplyLegacyEiLayout();
            bag.Grid.ItemGrid = game.Inventory;
            typeof(GameScene).GetField("_inventoryDialog", Private).SetValue(game, bag);
            // Deliberately undersized projection: item dimensions must not depend on placement.
            bag.Grid.GridSize = new Vector2I(6, 6);
            var rows = new List<JournalEntry>();
            game.JournalEntries(0, 1, rows);
            Check(rows.Single(x => x.Id == "88:89") is { Width: 2, Height: 4 }, "late weapon keeps its PC 2×4 footprint without an anchor");
            Check(rows.Single(x => x.Id == "89:90") is { Width: 3, Height: 3 }, "late armour keeps its PC 3×3 footprint without an anchor");

            // Five 1×1 items fill the first row, not the first column five times.
            var pack = Enumerable.Range(0, 5).Select(i => new ClientUserItem(small, 1) { Slot = i }).ToArray();
            grid = new DXItemGrid { UseLegacyFootprints = true, GridSize = new Vector2I(6, 1), GridType = GridType.Inventory, ItemGrid = pack };
            Check(grid.GetLegacyRequiredRows(1) == 1, "row sizing uses the actual first-fit column");
            grid.Free(); grid = null;
            bag.ConfigureLegacyInventoryGrid();
            Check(game.Inventory.All(i => bag.Grid.ResolveSlotCell(i.Slot) >= 0), "every saved item has a PC bag anchor after sizing");
            game.JournalEntries(0, 0, rows);
            Check(rows.Single(x => x.Id == "88:89") is { Width: 2, Height: 4 } && rows.Single(x => x.Id == "89:90") is { Width: 3, Height: 3 }, "All and Equipment categories agree on occupied cells");
            // Independently inspect PC WIL headers, not the helper under test.
            string root = OS.GetEnvironment("MIR3_EI_ROOT");
            string data = Path.Combine(root, "LegacyEI", "Data");
            string wilPath = Directory.GetFiles(data).Single(p => Path.GetFileName(p).Equals("inventory.wil", StringComparison.OrdinalIgnoreCase));
            string wixPath = Directory.GetFiles(data).Single(p => Path.GetFileName(p).Equals("inventory.wix", StringComparison.OrdinalIgnoreCase));
            using var pc = new LegacyWilLibrary(wilPath, wixPath);
            var definitions = Globals.ItemInfoList.Binding.Where(info => info.ItemType is ItemType.Weapon or ItemType.Armour or ItemType.Consumable).ToArray();
            var actual = definitions.Select((info, i) => new ClientUserItem(info, 1) { Index = 10000 + i, Slot = i }).ToArray();
            game.Inventory = actual;
            bag.Grid.ItemGrid = actual;
            bag.ConfigureLegacyInventoryGrid();
            game.JournalEntries(0, 0, rows);
            int compared = 0, multiArmour = 0;
            var report = new List<string> { "Name,Type,Image,Pixels,PC footprint,Journal footprint" };
            foreach (var item in actual)
            {
                int frame = DXItemCell.GetItemDrawIndex(item);
                var size = pc.GetSize(frame);
                if (size.X <= 0 || size.Y <= 0) continue;
                int w = (size.X + DXItemCell.CellWidth - 1) / DXItemCell.CellWidth;
                int h = (size.Y + DXItemCell.CellHeight - 1) / DXItemCell.CellHeight;
                var row = rows.Single(r => r.Id == $"{item.Slot}:{item.Index}");
                Check(row.Width == w && row.Height == h, $"PC/journal {item.Info.ItemName}: {w}×{h}");
                report.Add($"\"{item.Info.Local().Replace("\"", "\"\"")}\",{item.Info.ItemType},{frame},{size.X}x{size.Y},{w}x{h},{row.Width}x{row.Height}");
                compared++;
                if (item.Info.ItemType == ItemType.Armour && w * h > 1) multiArmour++;
            }
            Check(compared > 0 && multiArmour > 0, "real PC inventory artwork includes multi-cell armour and matches the journal");
            string reportPath = OS.GetEnvironment("ZIRCON_FOOTPRINT_REPORT");
            if (!string.IsNullOrEmpty(reportPath)) File.WriteAllLines(reportPath, report);
            GD.Print($"PASS: {compared} real PC artwork comparisons; {multiArmour} multi-cell armour definitions");
            var journal = new HandheldJournal(game);
            try
            {
                var fitHint = typeof(HandheldJournal).GetMethod("FitFooterHint", Private);
                const string longHint = "物品已变化、已锁定或当前无法操作";
                string fitted = (string)fitHint.Invoke(journal, new object[] { longHint, 76 });
                var font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
                Check(fitted.EndsWith("…", StringComparison.Ordinal) &&
                    font.GetStringSize(fitted, fontSize: MirSkin.ScaledSize(12)).X <= 76,
                    "long action hints fit before the next footer button");
                Check((string)fitHint.Invoke(journal, new object[] { "关闭", 76 }) == "关闭",
                    "short footer hints remain intact");
                var projected = (List<JournalEntry>)typeof(HandheldJournal).GetField("rows", Private).GetValue(journal);
                projected.Add(new JournalEntry("sword", "Tall weapon", "", Width: 2, Height: 6));
                projected.Add(new JournalEntry("coat", "Coat", "", Width: 2, Height: 4));
                projected.Add(new JournalEntry("coat2", "Coat", "", Width: 2, Height: 4));
                projected.Add(new JournalEntry("coat3", "Coat", "", Width: 2, Height: 4));
                typeof(HandheldJournal).GetMethod("Pack", Private).Invoke(journal, null);
                var cells = (List<Rect2I>)typeof(HandheldJournal).GetField("cells", Private).GetValue(journal);
                int lines = (int)typeof(HandheldJournal).GetProperty("Lines", Private).GetValue(journal);
                Check(cells[0].Size == new Vector2I(2, 6), "a tall PC weapon is not compressed to the page height");
                for (int i = 0; i < cells.Count; i++)
                {
                    Check(cells[i].Position.Y % lines + cells[i].Size.Y <= lines, "whole item fits on one journal page");
                    for (int j = 0; j < i; j++) Check(!cells[i].Intersects(cells[j]), "no neighbouring equipment overlap");
                }
            }
            finally { journal.Free(); }
            string capture = OS.GetEnvironment("ZIRCON_FOOTPRINT_CAPTURE");
            if (!string.IsNullOrEmpty(capture))
            {
                ClientUserItem Pick(ItemType type, int width, int height) => actual.First(i => {
                    var size = pc.GetSize(DXItemCell.GetItemDrawIndex(i));
                    return i.Info.ItemType == type && size.X > 0 && size.Y > 0 &&
                        (size.X + 35) / 36 == width && (size.Y + 35) / 36 == height;
                });
                game.Inventory = new[] { Pick(ItemType.Consumable, 1, 1), Pick(ItemType.Weapon, 2, 4), Pick(ItemType.Armour, 2, 4) }
                    .Select((i, slot) => new ClientUserItem(i, 1) { Slot = slot, Index = slot + 1 }).ToArray();
                bag.Grid.ItemGrid = game.Inventory;
                bag.ConfigureLegacyInventoryGrid();
                var viewport = new SubViewport { Size = new Vector2I(640, 360), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                Root.AddChild(viewport);
                var view = new HandheldJournal(game);
                viewport.AddChild(view);
                view.Open();
                view.Command(Key.Right); view.Command(Key.Right);
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var pixels = viewport.GetTexture().GetImage();
                Check(pixels.SavePng(capture) == Error.Ok, "captured real PC 1×1 potion and 2×4 equipment in handheld journal");
                viewport.Free();
            }
            GD.Print("PASS: handheld bag resource footprint checks");
            Quit(0);
        }
        catch (Exception error) { GD.PrintErr(error); Quit(1); }
        finally
        {
            grid?.Free(); bag?.Free(); game?.Free();
            foreach (var pair in owned) { cache.Remove((LibraryFile.Inventory, pair.Frame)); pair.Texture.Dispose(); }
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }
}
