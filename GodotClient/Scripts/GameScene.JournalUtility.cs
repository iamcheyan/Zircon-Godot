using System;
using System.Collections.Generic;
using System.Linq;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private string JournalSocialUnavailable => _net?.Connection?.Connected != true ? "尚未连接服务器。"
        : IsObserver ? "观察模式下不可使用社交服务。" : null;

    internal void JournalUtilityEntries(int tab, int cat, List<JournalEntry> rows)
    {
        if (tab == 4) JournalSocialEntries(cat, rows);
        if (tab == 5) JournalSystemEntries(cat, rows);
    }

    private void JournalSocialEntries(int cat, List<JournalEntry> rows)
    {
        void Empty(string detail) => rows.Add(new JournalEntry("unavailable", "不可用", detail));
        if (JournalSocialUnavailable is { } reason) { Empty(reason); return; }
        switch (cat)
        {
            case 0 when _communicationDialog != null:
                rows.Add(new JournalEntry("friends", "好友管理", _communicationDialog.Friends.Count == 0
                    ? "尚无好友。可在通讯窗口添加好友。" : $"好友：{_communicationDialog.Friends.Count} 人"));
                foreach (var friend in _communicationDialog.Friends)
                    rows.Add(new JournalEntry($"friend:{friend.Index}", friend.Name, friend.State switch
                    {
                        OnlineState.Online => "在线", OnlineState.Away => "离开", OnlineState.Busy => "忙碌", _ => "离线"
                    }));
                break;
            case 1 when _groupDialog != null:
                rows.Add(new JournalEntry("group", "队伍管理", _groupDialog.Members.Count == 0
                    ? "当前没有队伍成员。可打开组队窗口邀请玩家。" : $"队伍成员：{_groupDialog.Members.Count} 人"));
                rows.Add(new JournalEntry("invites", "接收组队邀请", $"{JournalToggleLabel(_groupDialog.AllowsInvites)}\n更改此设置可能离开当前队伍。"));
                foreach (var member in _groupDialog.Members)
                    rows.Add(new JournalEntry($"group:{member.Key}", member.Value, "当前队伍成员。队伍权限由服务器决定。"));
                break;
            case 2 when _guildDialog != null:
                var guild = _guildDialog.Guild;
                if (guild == null) { Empty("当前没有加入行会。"); break; }
                rows.Add(new JournalEntry("guild", guild.GuildName, $"成员：{guild.Members?.Count ?? 0}/{guild.MemberLimit}\n资金：{guild.GuildFunds:N0}\n{guild.Notice}"));
                if (guild.Members == null) break;
                foreach (var member in guild.Members)
                    rows.Add(new JournalEntry($"guild:{member.Index}", member.Name, $"职务：{member.Rank}\n总贡献：{member.TotalContribution:N0}"));
                break;
            case 3 when _communicationDialog != null:
                rows.Add(new JournalEntry("inbox", "收件箱", _communicationDialog.Mails.Count == 0
                    ? "当前没有邮件。" : $"邮件：{_communicationDialog.Mails.Count} 封"));
                rows.Add(new JournalEntry("compose", "写邮件", "通过现有邮件窗口填写收件人、正文和附件。"));
                foreach (var mail in _communicationDialog.Mails)
                    rows.Add(new JournalEntry($"mail:{mail.Index}", string.IsNullOrWhiteSpace(mail.Subject) ? "（无主题）" : mail.Subject,
                        $"发件人：{mail.Sender}\n日期：{mail.Date:g}\n物品附件：{mail.Items?.Count ?? 0} · 金币：{mail.Gold:N0}\n\n{mail.Message}",
                        mail.Opened ? "已读" : "未读"));
                break;
            case 4 when _rankingDialog != null:
                rows.Add(new JournalEntry("rankings", "角色排行", !_rankingDialog.HasRankResponse ? "尚未收到排行数据。可请求刷新或打开排行窗口。"
                    : _rankingDialog.Ranks.Count == 0 ? "服务器未返回当前筛选条件的排行记录。" : "显示最近一次服务器返回的排行结果。"));
                foreach (var rank in _rankingDialog.Ranks)
                    if (rank != null) rows.Add(new JournalEntry($"rank:{rank.Index}", $"#{rank.Rank} {rank.Name}",
                        $"等级：{rank.Level} · {(rank.Online ? "在线" : "离线")}"));
                break;
            default: Empty("此窗口尚未就绪。"); break;
        }
    }

    private static string JournalToggleLabel(bool enabled) => enabled ? "开启" : "关闭";

    private void JournalSystemEntries(int cat, List<JournalEntry> rows)
    {
        void Add(string id, string name, string detail) => rows.Add(new JournalEntry(id, name, detail));
        void Toggle(string id, string name, bool value) => Add(id, name, JournalToggleLabel(value));
        void Volume(string id, string name, int value, bool muted) => Add(id, name, $"{value}%{(muted ? " · 静音" : "")}");
        switch (cat)
        {
            case 0:
                Toggle("effects", "显示特效", ClientSettings.DrawEffects);
                Toggle("particles", "显示粒子", ClientSettings.DrawParticles);
                Toggle("smooth", "平滑移动", ClientSettings.SmoothMove);
                if (AutoLoginArgs.LegacyUi) Toggle("shadows", "阴影混合", ClientSettings.LegacyShadowBlendEnabled);
                break;
            case 1:
                if (AutoLoginArgs.LegacyUi)
                {
                    Add("bgm", "背景音乐", $"{ClientSettings.LegacyBgmLevel} dB · {JournalToggleLabel(ClientSettings.LegacyBgmEnabled)}");
                    Add("sfx", "音效", $"{ClientSettings.LegacyEffectSoundLevel} dB · {JournalToggleLabel(ClientSettings.LegacyEffectSoundEnabled)}");
                }
                else
                {
                    Volume("music", "音乐音量", ClientSettings.MusicVolume, ClientSettings.MusicVolumeMuted);
                    Volume("system", "界面音量", ClientSettings.SystemVolume, ClientSettings.SystemVolumeMuted);
                    Volume("player", "人物音量", ClientSettings.PlayerVolume, ClientSettings.PlayerVolumeMuted);
                    Volume("monster", "怪物音量", ClientSettings.MonsterVolume, ClientSettings.MonsterVolumeMuted);
                    Volume("magic", "魔法音量", ClientSettings.MagicVolume, ClientSettings.MagicVolumeMuted);
                }
                Toggle("background-sound", "后台播放声音", ClientSettings.SoundInBackground);
                break;
            case 2:
                Add("controls-journal", "手札操作", "Menu：打开或关闭手札\nL1 / R1：切换主标签\nL2 / R2：切换子页\n方向键：移动焦点\nA：操作 · B：返回 · X：次要操作");
                Add("action-bindings", "操作绑定", "配置两页 X/Y 与十字键动作，以及肩键短按、长按和组合键。");
                Add("controls-world", "世界操作", "左摇杆：移动 · 右摇杆：选目标\nA：攻击、交谈或拾取 · B：取消\nX / Y / 十字键：执行当前页绑定\nL1 / R1：松开切页 · L2 / R2：固定快捷\nStart：切换 HUD · Select：地图");
                break;
            case 3:
                Toggle("item-names", "显示物品名称", ClientSettings.ShowItemNames);
                Toggle("monster-names", "显示怪物名称", ClientSettings.ShowMonsterNames);
                Toggle("player-names", "显示人物名称", ClientSettings.ShowPlayerNames);
                Toggle("health", "显示生命条", ClientSettings.ShowUserHealth);
                Toggle("damage", "显示伤害数字", ClientSettings.ShowDamageNumbers);
                Add("auto-potion", "自动喝药设置", JournalSocialUnavailable ?? (AutoPotionBox == null ? "自动喝药窗口尚未就绪。" : "配置已有药品链接及触发条件。"));
                Add("config", "完整游戏设置", _configDialog == null ? "设置窗口尚未就绪。" : "打开原有设置窗口，编辑其他选项。");
                break;
            case 4:
                Add("help", "游戏帮助", _helpDialog == null ? "帮助窗口尚未就绪。" : "打开现有帮助窗口。");
                Add("live-world", "世界不会暂停", "打开手札时，服务器仍在运行，角色仍可能受到攻击。物品及社交请求以服务器处理结果为准。");
                break;
            case 5:
                Add("logout", "返回角色选择", JournalLogoutUnavailable ?? "需确认。等待服务器响应后返回角色选择。");
                Add("exit", "退出客户端", "需确认。掌机启动器会在客户端退出后接回屏幕。");
                break;
        }
    }

    private string JournalLogoutUnavailable => _leavingGame ? "正在等待服务器退出角色。"
        : _net?.Connection?.Connected != true ? "尚未连接服务器。" : null;

    internal IReadOnlyList<JournalAction> JournalUtilityActions(int tab, int cat, string id, bool secondary)
    {
        if (tab == 4) return JournalSocialActions(cat, id, secondary);
        if (tab != 5 || secondary) return Array.Empty<JournalAction>();
        return (cat, id) switch
        {
            (0, "effects") => JournalSetting(() => ClientSettings.DrawEffects = !ClientSettings.DrawEffects),
            (0, "particles") => JournalSetting(() => ClientSettings.DrawParticles = !ClientSettings.DrawParticles),
            (0, "smooth") => JournalSetting(() => ClientSettings.SmoothMove = !ClientSettings.SmoothMove),
            (0, "shadows") when AutoLoginArgs.LegacyUi => JournalSetting(() => ClientSettings.LegacyShadowBlendEnabled = !ClientSettings.LegacyShadowBlendEnabled),
            (1, "bgm") when AutoLoginArgs.LegacyUi => JournalLegacyVolume(() => ClientSettings.LegacyBgmLevel, x => ClientSettings.LegacyBgmLevel = x,
                () => ClientSettings.LegacyBgmEnabled = !ClientSettings.LegacyBgmEnabled),
            (1, "sfx") when AutoLoginArgs.LegacyUi => JournalLegacyVolume(() => ClientSettings.LegacyEffectSoundLevel, x => ClientSettings.LegacyEffectSoundLevel = x,
                () => ClientSettings.LegacyEffectSoundEnabled = !ClientSettings.LegacyEffectSoundEnabled),
            (1, "music") => JournalVolume(() => ClientSettings.MusicVolume, x => ClientSettings.MusicVolume = x, () => ClientSettings.MusicVolumeMuted, x => ClientSettings.MusicVolumeMuted = x),
            (1, "system") => JournalVolume(() => ClientSettings.SystemVolume, x => ClientSettings.SystemVolume = x, () => ClientSettings.SystemVolumeMuted, x => ClientSettings.SystemVolumeMuted = x),
            (1, "player") => JournalVolume(() => ClientSettings.PlayerVolume, x => ClientSettings.PlayerVolume = x, () => ClientSettings.PlayerVolumeMuted, x => ClientSettings.PlayerVolumeMuted = x),
            (1, "monster") => JournalVolume(() => ClientSettings.MonsterVolume, x => ClientSettings.MonsterVolume = x, () => ClientSettings.MonsterVolumeMuted, x => ClientSettings.MonsterVolumeMuted = x),
            (1, "magic") => JournalVolume(() => ClientSettings.MagicVolume, x => ClientSettings.MagicVolume = x, () => ClientSettings.MagicVolumeMuted, x => ClientSettings.MagicVolumeMuted = x),
            (1, "background-sound") => JournalSetting(() => ClientSettings.SoundInBackground = !ClientSettings.SoundInBackground),
            (3, "item-names") => JournalSetting(() => ClientSettings.ShowItemNames = !ClientSettings.ShowItemNames),
            (3, "monster-names") => JournalSetting(() => ClientSettings.ShowMonsterNames = !ClientSettings.ShowMonsterNames),
            (3, "player-names") => JournalSetting(() => ClientSettings.ShowPlayerNames = !ClientSettings.ShowPlayerNames),
            (3, "health") => JournalSetting(() => ClientSettings.ShowUserHealth = !ClientSettings.ShowUserHealth),
            (3, "damage") => JournalSetting(() => ClientSettings.ShowDamageNumbers = !ClientSettings.ShowDamageNumbers),
            (3, "auto-potion") => [new JournalAction("打开设置", () => JournalSocialUnavailable ?? JournalOpenUtilityWindow(AutoPotionBox, () => WindowManager.Open(AutoPotionBox, _uiLayer)))],
            (3, "config") => [new JournalAction("打开完整设置", () => JournalOpenUtilityWindow(_configDialog, OpenConfigDialog))],
            (2, "action-bindings") => [new JournalAction("编辑掌机按键", () => { OpenHandheldActionBindings(); return null; })],
            (4, "help") => [new JournalAction("打开帮助", () => JournalOpenUtilityWindow(_helpDialog, OpenHelpDialog))],
            (5, "logout") => [JournalConfirmUtility("返回角色选择？", () => JournalLogoutUnavailable ?? JournalRun(LeaveGame))],
            (5, "exit") => [JournalConfirmUtility("退出客户端？", () => JournalRun(ExitClient))],
            _ => Array.Empty<JournalAction>()
        };
    }

    private IReadOnlyList<JournalAction> JournalSocialActions(int cat, string id, bool secondary)
    {
        if (JournalSocialUnavailable != null || id == null || id == "unavailable") return Array.Empty<JournalAction>();
        if (cat == 4 && secondary)
            return [new JournalAction("刷新等级排行", () => JournalSocialUnavailable ?? JournalRun(() => RequestRankings(0, false), "已请求排行，等待服务器响应。"))];
        if (secondary) return Array.Empty<JournalAction>();
        switch (cat)
        {
            case 0 when id == "friends":
                return [new JournalAction("打开好友管理", () => JournalCommunication(() => _communicationDialog.ShowFriendsPage()))];
            case 0 when JournalUtilityId(id, "friend:", out int friendId):
                return [new JournalAction("私聊", () => JournalSocialUnavailable ?? JournalWhisperFriend(friendId)),
                    new JournalAction("邀请入队", () => JournalSocialUnavailable ?? JournalInviteFriend(friendId)),
                    new JournalAction("打开好友管理", () => JournalCommunication(() => _communicationDialog.ShowFriendsPage()))];
            case 1 when id == "invites":
                // The server may remove the character from its current party when this changes.
                return [JournalConfirmUtility("切换邀请设置？可能离开当前队伍。", () => JournalSocialUnavailable ?? (_groupDialog == null ? "组队窗口尚未就绪。"
                    : JournalRun(() => SendGroupSwitch(!_groupDialog.AllowsInvites), "已请求更改邀请设置。")))];
            case 1 when id == "group" || id.StartsWith("group:", StringComparison.Ordinal):
                return [new JournalAction("打开组队窗口", () => JournalSocialUnavailable ?? JournalOpenUtilityWindow(_groupDialog, OpenGroupDialog))];
            case 2:
                return [new JournalAction("打开行会窗口", () => JournalSocialUnavailable ?? (!HasGuild ? "当前没有加入行会。" : JournalRun(() =>
                {
                    _guildDialog.SelectTab(id == "guild" ? 0 : 1);
                    EnsureGuildDialogOpen();
                })))];
            case 3 when id == "inbox" || id == "compose":
                return [new JournalAction(id == "inbox" ? "打开收件箱" : "写邮件", () => JournalCommunication(() =>
                {
                    if (id == "inbox") _communicationDialog.ShowInboxPage(); else _communicationDialog.ShowSendPage();
                }))];
            case 3 when JournalUtilityId(id, "mail:", out int mailId):
                return [new JournalAction("阅读与处理附件", () => JournalSocialUnavailable ?? (_communicationDialog?.FindMail(mailId) == null
                    ? "邮件已不存在。" : JournalCommunication(() => _communicationDialog.ShowMail(mailId))))];
            case 4:
                return [new JournalAction("打开服务器排行", () => JournalSocialUnavailable ?? JournalOpenUtilityWindow(_rankingDialog, OpenRankingDialog)),
                    new JournalAction("刷新等级排行", () => JournalSocialUnavailable ?? JournalRun(() => RequestRankings(0, false), "已请求排行，等待服务器响应。"))];
            default: return Array.Empty<JournalAction>();
        }
    }

    private string JournalInviteFriend(int index)
    {
        var friend = _communicationDialog?.Friends.FirstOrDefault(x => x.Index == index);
        if (friend == null) return "该好友已不在列表中。";
        if (friend.State == OnlineState.Offline) return "该好友不在线。";
        if (_groupDialog?.Members.Values.Contains(friend.Name) == true) return "该好友已经在当前队伍中。";
        SendGroupInvite(friend.Name);
        return "已发送组队邀请请求，等待回应。";
    }

    private string JournalWhisperFriend(int index)
    {
        var friend = _communicationDialog?.Friends.FirstOrDefault(x => x.Index == index);
        if (friend == null) return "该好友已不在列表中。";
        if (friend.State == OnlineState.Offline) return "该好友不在线。";
        if (_chatTextBox == null) return "聊天输入尚未就绪。";
        CloseJournal();
        StartPrivateMessage(friend.Name);
        return null;
    }

    private string JournalCommunication(Action showPage) => JournalSocialUnavailable ?? JournalOpenUtilityWindow(_communicationDialog, () =>
    {
        showPage();
        WindowManager.Open(_communicationDialog, _uiLayer);
    });

    private static bool JournalUtilityId(string id, string prefix, out int index)
    {
        index = -1;
        return id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), out index) && index >= 0;
    }

    private static string JournalOpenUtilityWindow(DXWindow window, Action open) => window == null ? "窗口尚未就绪。" : JournalRun(open);

    private static string JournalRun(Action action, string feedback = null)
    {
        action();
        return feedback;
    }

    private static JournalAction JournalConfirmUtility(string question, Func<string> confirm) => new(question, Children: () =>
        [new JournalAction("取消", () => "已取消。"), new JournalAction("确认", confirm)]);

    private static IReadOnlyList<JournalAction> JournalSetting(Action change, bool audio = false) =>
        [new JournalAction("切换", () => JournalSaveSetting(change, audio))];

    private static IReadOnlyList<JournalAction> JournalLegacyVolume(Func<int> get, Action<int> set, Action toggle) =>
        [new JournalAction("降低 5 dB", () => JournalSaveSetting(() => set(Math.Max(-100, get() - 5)), true)),
         new JournalAction("提高 5 dB", () => JournalSaveSetting(() => set(Math.Min(0, get() + 5)), true)),
         new JournalAction("切换声音", () => JournalSaveSetting(toggle, true))];

    private static IReadOnlyList<JournalAction> JournalVolume(Func<int> get, Action<int> set, Func<bool> muted, Action<bool> setMuted) =>
        [new JournalAction("降低 10%", () => JournalSaveSetting(() => set(Math.Max(0, get() - 10)), true)),
         new JournalAction("提高 10%", () => JournalSaveSetting(() => set(Math.Min(100, get() + 10)), true)),
         new JournalAction(muted() ? "取消静音" : "静音", () => JournalSaveSetting(() => setMuted(!muted()), true))];

    private static string JournalSaveSetting(Action change, bool audio)
    {
        change();
        if (audio) ClientSettings.ApplyAudioSettings();
        ClientSettings.Save();
        return "设置已保存。";
    }
}
