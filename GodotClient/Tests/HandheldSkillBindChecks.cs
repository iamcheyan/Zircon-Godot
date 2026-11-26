using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using Library;
using Library.Network;
using Library.SystemModels;
using ZirconClient.Controls;
using ZirconClient.HandheldActions;
using ZirconClient.Network;
using ZirconClient.Scripts;
using C = Library.Network.ClientPackets;

/// <summary>
/// Handheld skill binding: the skill page must offer the handheld action slots
/// (page X/Y/dpad and fixed triggers), and A on a slot must commit and persist it.
/// </summary>
public partial class HandheldSkillBindChecks : SceneTree
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); GD.Print("PASS: " + text); }

    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var game = new GameScene();
        var player = new PlayerRenderer();
        var net = new NetworkManager();
        HandheldJournal journal = null;
        try
        {
            GameScene.Game = game;
            var connection = (ServerConnection)RuntimeHelpers.GetUninitializedObject(typeof(ServerConnection));
            connection.Connected = true; connection.SendList = new ConcurrentQueue<Packet>();
            typeof(NetworkManager).GetField("<Connection>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(net, connection);
            var privateField = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(GameScene).GetField("_net", privateField)!.SetValue(game, net);
            typeof(GameScene).GetField("_player", privateField)!.SetValue(game, player);
            typeof(GameScene).GetField("_startGameShown", privateField)!.SetValue(game, true);
            typeof(GameScene).GetField("_playerLevel", privateField)!.SetValue(game, 255);
            game.StartInfo = new StartInformation { Index = 1, Class = MirClass.Warrior, Gender = MirGender.Male };
            game.MagicBarSpellSet = 1;

            var info = Globals.MagicInfoList.Binding.First(m =>
                m.School is not (MagicSchool.None or MagicSchool.Discipline or MagicSchool.Passive) &&
                m.MatchesClass(MirClass.Warrior) && m.Magic is not (MagicType.Swordsmanship or MagicType.SpiritSword));
            var magic = new ClientUserMagic { Info = info, InfoIndex = info.Index, Level = 3 };
            game.UserMagics[info] = magic;

            var actions = game.JournalActions(2, 0, info.Index.ToString(), false);
            Check(actions.Count == 1, "skill page offers one binding action");
            var slotList = actions[0].Children();
            if (AutoLoginArgs.Handheld)
            {
                Check(actions[0].Label == "绑定到掌机动作栏", "handheld offers the handheld action bar, not the PC spell sets");
                Check(slotList.Count == 18, $"slot list covers 2 pages x 6 buttons plus 6 fixed triggers (got {slotList.Count})");
                Check(slotList[0].Label.StartsWith("第 1 页 · X"), "first slot is page 1 X");
                Check(slotList[17].Label.StartsWith("固定 · L2+R2"), "last slot is the L2+R2 chord");

                slotList[0].Execute();
                var x = game.HandheldActionConfig.Pages[0].Bindings[HandheldButton.X];
                Check(x.Kind == ActionKind.Skill && x.StableId == info.Index.ToString(),
                    $"A on 第 1 页 X binds the skill to that slot (got {x.Kind}/{x.StableId})");

                // The journal path a handheld player actually uses: A, A, Down, A.
                var viewport = new SubViewport
                {
                    Size = new Vector2I(640, 360),
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                };
                Root.AddChild(viewport);
                journal = new HandheldJournal(game);
                viewport.AddChild(journal);
                journal.Open();
                journal.SelectTab(2);
                journal.Command(Key.Enter);
                Check(journal.HasActionOverlay, "A opens the binding action");
                journal.Command(Key.Enter);
                Check(journal.HasActionOverlay, "A opens the handheld slot list");
                string capture = System.Environment.GetEnvironmentVariable("ZIRCON_SKILL_BIND_CAPTURE");
                if (!string.IsNullOrEmpty(capture))
                {
                    await ToSignal(this, SignalName.ProcessFrame);
                    await ToSignal(this, SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var shot = viewport.GetTexture().GetImage();
                    Check(shot.SavePng(capture) == Error.Ok, "captured the handheld slot list at 640x360");
                }
                journal.Command(Key.Down);
                journal.Command(Key.Enter);
                var y = game.HandheldActionConfig.Pages[0].Bindings[HandheldButton.Y];
                Check(y.Kind == ActionKind.Skill && y.StableId == info.Index.ToString(),
                    $"A commits the selected slot and it takes effect immediately (got {y.Kind}/{y.StableId})");
                Check(!journal.HasActionOverlay, "committing closes the slot list");
            }
            else
            {
                Check(actions[0].Label.Contains("共用快捷栏"), "desktop keeps the PC spell-set action");
                Check(slotList.Count == 25, "desktop slot list is clear plus every spell key");
                slotList[(int)SpellKey.Spell01].Execute();
                Check(magic.Set1Key == SpellKey.Spell01 && connection.SendList.OfType<C.MagicKey>().Count() == 1,
                    "desktop binding still writes the PC spell key and sends one packet");
            }
            Quit(0);
        }
        catch (Exception e) { GD.PrintErr(e); Quit(1); }
        finally { GameScene.Game = null; journal?.Free(); net?.Free(); player?.Free(); game?.Free(); }
    }
}
