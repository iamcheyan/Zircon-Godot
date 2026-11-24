using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Library;
using S = Library.Network.ServerPackets;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

public partial class NPCDialog
{
    internal bool HandheldPageValid => _page != null;
    internal string HandheldBody { get; private set; } = "";
    internal string HandheldNpcTitle { get; private set; } = "";
    internal Texture2D HandheldPortrait { get; private set; }
    internal readonly List<(int id, string label)> HandheldOptions = new();
    internal bool IsHandheldReplyControl(DXControl control) => _buttons.Contains(control as DXButton);

    private void ClearHandheldPage()
    {
        HandheldOptions.Clear();
        HandheldBody = "无法读取此对话页，请关闭对话后重新交谈。";
        HandheldNpcTitle = "对话暂不可用";
        HandheldPortrait = null;
    }

    // Parse only presentation. Button IDs are still checked by the server's current page.
    private void PrepareHandheldPage(S.NPCResponse response, string resolved)
    {
        HandheldOptions.Clear();
        var npc = Scripts.GameScene.Game?.GetNpcInfo(response.ObjectID);
        HandheldNpcTitle = npc?.Local() ?? "交谈";
        string body = Regex.Replace(resolved ?? "", @"\[(?<text>.*?):(?<id>-?\d+)\]", match =>
        {
            if (int.TryParse(match.Groups["id"].Value, out int id) && id >= 0)
            {
                if (!HandheldOptions.Exists(option => option.id == id))
                    HandheldOptions.Add((id, StripHandheldMarkup(match.Groups["text"].Value)));
                return "";
            }
            return match.Value;
        });
        // Plain text is intentionally used; do not execute script markup as BBCode.
        HandheldBody = StripHandheldMarkup(body);
        HandheldPortrait = _legacyNpcFaceFrame >= 0 ? MirSkin.GetTexture(LibraryFile.NPCImage, _legacyNpcFaceFrame) : null;
        if (HandheldOptions.Count == 0 && _page.Buttons != null)
            foreach (var option in _page.Buttons)
                HandheldOptions.Add((option.ButtonID, $"选项 {option.ButtonID}"));
    }

    private static string StripHandheldMarkup(string value)
    {
        string result = Regex.Replace(value ?? "", @"\{([^{}]*):(?:#[0-9a-fA-F]+|[A-Za-z]+)\}", "$1");
        return result.Replace("\r", "").Trim();
    }
}
