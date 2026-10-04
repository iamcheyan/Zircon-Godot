using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Library;
using Library.SystemModels;
using MirDB;
using S = Library.Network.ServerPackets;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>NPC 对话：现代布局使用 GameInter 380/381/382；legacy EI 使用 F1100/F1101/F1102 三段式。</summary>
public partial class NPCDialog : DXWindow
{
    private readonly DXControl _textArea;
    private readonly NPCTextControl _text;
    private DXImageControl _legacyNpcFace;
    private int _legacyNpcFaceFrame = -1;
    private readonly DXVScrollBar _scroll;
    private readonly DXButton _scrollUp;
    private readonly DXButton _scrollDown;
    private readonly List<DXButton> _buttons = new();
    private readonly List<DXImageControl> _rowBackgrounds = new();
    private readonly DXImageControl _headerBackground;
    private readonly DXImageControl _footerBackground;
    private DXButton _closeButton;
    private NPCPage _page;
    private readonly NPCGoodsPanel _goods;
    private readonly NPCRepairPanel _repair;
    private readonly NPCAdvancedPanel _advanced;
    private bool _legacyLayout;
    private int _scrollLine;

    // 原版 Legacy 三段式素材几何实测值:
    // F1100 (Header 顶段): 512x256, bbox=(64, 59, 448, 197), 可见尺寸=384x138, 锚点=(-64, -59)
    // F1101 (Middle 中段行): 512x32, bbox=(64, 7, 447, 25), 可见尺寸=384x18, 锚点=(-64, -7)
    // F1102 (Footer 底段): 512x64, bbox=(64, 10, 448, 54), 可见尺寸=384x44, 锚点=(-64, -10)
    // F1102 自带右下角圆形关闭按钮 (中心约 355, 20)
    private const int LegacyHeaderHeight = 138;
    private const int LegacyRowHeight = 18;
    private const int LegacyFooterHeight = 44;
    private const int LegacyWidth = 384;
    private const int LegacyFontSize = 10;
    private const int LegacyLinePitch = 21;
    private const int LegacyLinePitchCompact = 14;
    private const int LegacyScannerHeaderSegments = 6;
    private const int LegacyScannerOverflowLimit = 16;

    /// <summary>
    /// legacy 行距：按原版扫描器规则从正文推导（21 为默认，14 为「有 NPCIMG 且段数溢出」）。
    /// </summary>
    private static int ComputeLegacyLinePitch(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf("{NPCIMG", StringComparison.Ordinal) < 0)
            return LegacyLinePitch;
        int segments = 1;
        foreach (char c in raw)
            if (c == '\n' || c == '\r') segments++;
        return (segments - LegacyScannerHeaderSegments) > LegacyScannerOverflowLimit
            ? LegacyLinePitchCompact
            : LegacyLinePitch;
    }

    private int _legacyPitch = LegacyLinePitch;

    public NPCDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = false;
        Size = new Vector2I(380, 204);

        _headerBackground = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 380,
            FixedSize = true,
            Size = new Vector2I(380, 140),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddControl(_headerBackground);

        _footerBackground = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 382,
            FixedSize = true,
            Size = new Vector2I(380, 64),
            Location = new Vector2I(0, 140),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddControl(_footerBackground);

        _closeButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
            Location = new Vector2I(350, 3)
        };
        _closeButton.MouseClick += (o, e) => CloseNpc();
        AddControl(_closeButton);

        _textArea = new DXControl
        {
            Location = new Vector2I(15, 45),
            Size = new Vector2I(350, 95),
            Clip = true
        };
        AddControl(_textArea);

        _text = new NPCTextControl
        {
            Size = new Vector2I(340, 1000)
        };
        _textArea.AddControl(_text);

        _scroll = new DXVScrollBar
        {
            Location = new Vector2I(350, 45),
            Size = new Vector2I(14, 95),
            VisibleSize = 95,
            Change = 1,
            HideWhenNoScroll = false,
            BackColour = Colors.Transparent,
            Border = false
        };
        _scroll.UpButton.LibraryFile = LibraryFile.GameInter;
        _scroll.UpButton.Index = 387;
        _scroll.DownButton.LibraryFile = LibraryFile.GameInter;
        _scroll.DownButton.Index = 385;
        _scroll.PositionBar.LibraryFile = LibraryFile.None;
        _scroll.PositionBar.Index = -1;
        _scroll.ValueChanged += (o, e) => _text.Position = new Vector2(0, -_scroll.Value);
        AddControl(_scroll);

        _scrollUp = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 52,
            HoverIndex = 53,
            PressedIndex = 53,
            FixedSize = true,
            Size = new Vector2I(12, 8),
            Visible = false,
            Sound = SoundIndex.None
        };
        _scrollUp.MouseClick += (o, e) => ScrollLegacy(-1);

        _scrollDown = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 54,
            HoverIndex = 55,
            PressedIndex = 55,
            FixedSize = true,
            Size = new Vector2I(12, 8),
            Visible = false,
            Sound = SoundIndex.None
        };
        _scrollDown.MouseClick += (o, e) => ScrollLegacy(1);

        AddControl(_scrollUp);
        AddControl(_scrollDown);

        _goods = new NPCGoodsPanel { Location = new Vector2I(0, 204), Visible = false };
        AddControl(_goods);
        _repair = new NPCRepairPanel { Location = new Vector2I(0, 204), Visible = false };
        AddControl(_repair);
        _advanced = new NPCAdvancedPanel { Location = new Vector2I(0, 204), Visible = false };
        AddControl(_advanced);
    }

    /// <summary>
    /// 旧版 EI NPC 对话框三段式初始化 (F1100 顶段 + F1101 中段平铺 + F1102 底段带关闭钮)。
    /// </summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyLayout = true;
        Size = new Vector2I(LegacyWidth, LegacyHeaderHeight + LegacyFooterHeight);

        _headerBackground.LibraryFile = LibraryFile.GameInter;
        _headerBackground.Index = 1100;
        _headerBackground.Location = new Vector2I(-64, -59);
        _headerBackground.Size = new Vector2I(LegacyWidth, LegacyHeaderHeight);
        _headerBackground.StretchImage = false;
        _headerBackground.Visible = true;

        _footerBackground.LibraryFile = LibraryFile.GameInter;
        _footerBackground.Index = 1102;
        _footerBackground.Location = new Vector2I(-64, LegacyHeaderHeight - 10);
        _footerBackground.Size = new Vector2I(LegacyWidth, LegacyFooterHeight);
        _footerBackground.StretchImage = false;
        _footerBackground.Visible = true;

        // F1102 自带右下角圆圈关闭按钮，透明热区精准覆盖
        _closeButton.LibraryFile = LibraryFile.None;
        _closeButton.Index = -1;
        _closeButton.HoverIndex = -1;
        _closeButton.PressedIndex = -1;
        _closeButton.Location = new Vector2I(342, LegacyHeaderHeight + 8);
        _closeButton.Size = new Vector2I(28, 28);
        _closeButton.Visible = true;

        _scroll.Visible = false;
        _scrollUp.Visible = false;
        _scrollDown.Visible = false;

        _scrollLine = 0;
        _text.Position = Vector2.Zero;

        UpdateClientAreaForLegacySkin();
        _goods.ApplyLegacyEiLayout();
        _legacyGoodsPlaced = true;
        PlaceGoodsPanel();
    }

    private void PlaceGoodsPanel()
    {
        _goods.Location = new Vector2I(0, (int)Size.Y);
    }

    public static readonly Vector2I LegacyStoreScreen = new(0, 184);
    private bool _legacyGoodsPlaced;

    private void ScrollLegacy(int delta)
    {
        if (!_legacyLayout) return;
        int maxScroll = GetLegacyMaxScroll();
        _scrollLine = Mathf.Clamp(_scrollLine + delta, 0, maxScroll);
        _text.Position = new Vector2(0, -_scrollLine * _legacyPitch);
        UpdateLegacyScrollEnabled();
    }

    private int GetLegacyMaxScroll()
    {
        int visibleLines = Math.Max(1, (int)_textArea.Size.Y / _legacyPitch);
        return Math.Max(0, _text.LineCount - visibleLines);
    }

    private void UpdateLegacyScrollEnabled()
    {
        int maxScroll = GetLegacyMaxScroll();
        _scrollUp.Enabled = maxScroll > 0 && _scrollLine > 0;
        _scrollDown.Enabled = maxScroll > 0 && _scrollLine < maxScroll;
    }

    public bool AuditLegacyGoods(out string details)
    {
        bool own = _goods.AuditLegacyEiLayout(out string ownDetails);
        details = $"placed={_legacyGoodsPlaced} | {ownDetails}";
        return own;
    }

    public void ShowGoodsForTest()
    {
        _goods.ApplyLegacyEiLayout();
        _goods.Visible = true;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        int rowCount = _rowBackgrounds.Count;
        int expectedH = LegacyHeaderHeight + rowCount * LegacyRowHeight + LegacyFooterHeight;
        bool ok = (int)Size.X == LegacyWidth
            && (int)Size.Y >= LegacyHeaderHeight + LegacyFooterHeight
            && _headerBackground.Index == 1100
            && _footerBackground.Index == 1102;
        details = $"size={Size} expectedH={expectedH} bg=F{_headerBackground.Index} footer=F{_footerBackground.Index} rows={rowCount}";
        return ok;
    }

    public (bool Ok, string Details, int Line, int MaxLine, float TextOffsetY) LegacyEiSelfState()
    {
        bool ok = AuditLegacyEiLayout(out string details);
        return (ok, details, _scrollLine, GetLegacyMaxScroll(), _text.Position.Y);
    }

    public DXButton LegacyCloseButton => _closeButton;
    public DXButton LegacyScrollUpButton => _scrollUp;
    public DXButton LegacyScrollDownButton => _scrollDown;
    public NPCTextControl LegacyText => _text;

    public void ShowPage(S.NPCResponse response)
    {
        _page = response?.Page;
        if (_page == null) return;

        bool selling = _page.DialogType == NPCDialogType.BuySell && _page.Types is { Count: > 0 };
        if (!selling) GameScene.Game?.EndInventoryNpcSale();

        string raw = _page.Say ?? string.Empty;
        raw = Regex.Replace(raw, @"\<(?<Text>.*?):(?<Default>.+?)\>", match =>
        {
            string id = match.Groups["Text"].Value;
            var value = response.Values?.Find(x => x.ID.ToString() == id);
            return value?.Value ?? match.Groups["Default"].Value;
        });
        var buttonMatches = Regex.Matches(raw, @"\[(?<Text>.*?):(?<ID>.+?)\]");

        // 头像判定
        _legacyNpcFaceFrame = -1;
        if (_legacyLayout)
        {
            raw = ApplyLegacyFColor(raw);
            raw = ExtractLegacyNpcImg(raw, out _legacyNpcFaceFrame);
            // FaceImage 是 NPCface.wil 的原始帧号，帧 0 也是有效头像。
            // 旧判断 > 0 会把最常见的零号头像误判成“没有头像”。
            if (_legacyNpcFaceFrame < 0 && GameScene.Game?.CurrentNPCInfo?.FaceImage >= 0)
            {
                _legacyNpcFaceFrame = GameScene.Game.CurrentNPCInfo.FaceImage;
            }
        }

        bool hasFace = _legacyLayout && _legacyNpcFaceFrame >= 0;
        ApplyLegacyNpcFace(hasFace);

        // 排版参数：左对齐
        _legacyPitch = _legacyLayout ? ComputeLegacyLinePitch(raw) : 18;
        int textLeft = _legacyLayout ? (hasFace ? 135 : 20) : 15;
        int textTop = _legacyLayout ? 20 : 45;
        int textWidth = _legacyLayout ? (hasFace ? 229 : 344) : 350;

        _textArea.Location = new Vector2I(textLeft, textTop);
        // 新页面一律回到顶部（原版 0x440630: [0x3BC]=0），否则沿用上一页的滚动偏移，正文被推出可视区。
        _scrollLine = 0;
        _text.Position = Vector2.Zero;
        _scroll.Value = 0;
        _text.SetContent(raw, textWidth, _legacyLayout ? LegacyFontSize : 10, _legacyPitch);

        int pageTextHeight = _text.ContentHeight;

        // 按钮处理（兼容）
        foreach (var button in _buttons) { RemoveControl(button); button.QueueFree(); }
        _buttons.Clear();
        int btnY = textTop + pageTextHeight + 10;
        if (buttonMatches.Count == 0 && _page.Buttons != null)
        {
            foreach (var option in _page.Buttons)
            {
                var button = new DXButton
                {
                    Text = string.Format(Lang.NPCUi357Label, option.ButtonID),
                    FontSize = 10,
                    TextColour = new Color(1f, .85f, .3f),
                    LibraryFile = LibraryFile.GameInter,
                    Index = -1,
                    Location = new Vector2I(textLeft, btnY),
                    Size = new Vector2I(textWidth, 20)
                };
                int id = option.ButtonID;
                button.MouseClick += (o, e) => GameScene.Game?.SendNPCButton(id);
                AddControl(button);
                _buttons.Add(button);
                btnY += 22;
            }
            pageTextHeight = btnY - textTop;
        }

        // 清理旧的中段行
        foreach (var row in _rowBackgrounds) { RemoveControl(row); row.QueueFree(); }
        _rowBackgrounds.Clear();

        if (_legacyLayout)
        {
            // 基础可用文本高度 (避开底边框)
            int baseTextH = hasFace ? 110 : 85;
            int overflow = pageTextHeight - baseTextH;
            int rowCount = overflow > 0 ? Math.Clamp((overflow + LegacyRowHeight - 1) / LegacyRowHeight, 0, 6) : 0;

            int totalH = LegacyHeaderHeight + rowCount * LegacyRowHeight + LegacyFooterHeight;
            Size = new Vector2I(LegacyWidth, totalH);

            _headerBackground.Location = new Vector2I(-64, -59);
            _headerBackground.Size = new Vector2I(LegacyWidth, LegacyHeaderHeight);
            _headerBackground.Visible = true;

            for (int i = 0; i < rowCount; i++)
            {
                var row = new DXImageControl
                {
                    LibraryFile = LibraryFile.GameInter,
                    Index = 1101,
                    FixedSize = true,
                    Size = new Vector2I(LegacyWidth, LegacyRowHeight),
                    Location = new Vector2I(-64, LegacyHeaderHeight + i * LegacyRowHeight - 7),
                    MouseFilter = MouseFilterEnum.Ignore,
                    ZIndex = -10
                };
                AddControl(row);
                _rowBackgrounds.Add(row);
            }

            int footerY = LegacyHeaderHeight + rowCount * LegacyRowHeight;
            _footerBackground.Location = new Vector2I(-64, footerY - 10);
            _footerBackground.Size = new Vector2I(LegacyWidth, LegacyFooterHeight);
            _footerBackground.Visible = true;

            // 关闭按钮定位在 F1102 右下角圆钮处
            _closeButton.Location = new Vector2I(342, footerY + 8);
            _closeButton.Size = new Vector2I(28, 28);
            _closeButton.Visible = true;

            const int textBottomMargin = 42;
            int areaH = Math.Max(0, totalH - textTop - textBottomMargin);
            _textArea.Size = new Vector2I(textWidth, areaH);
            int maxScroll = GetLegacyMaxScroll();
            _scroll.Visible = false;
            _scrollUp.Location = new Vector2I(300, footerY + 14);
            _scrollDown.Location = new Vector2I(318, footerY + 14);
            _scrollUp.Visible = maxScroll > 0;
            _scrollDown.Visible = maxScroll > 0;
            UpdateLegacyScrollEnabled();
        }
        else
        {
            int rowCount = Math.Clamp((pageTextHeight - 124) / 20, 0, 6);
            int footerY = 140 + rowCount * 20;
            Size = new Vector2I(380, footerY + 64);
            for (int i = 0; i < rowCount; i++)
            {
                var row = new DXImageControl
                {
                    LibraryFile = LibraryFile.GameInter,
                    Index = 381,
                    FixedSize = true,
                    Size = new Vector2I(380, 20),
                    Location = new Vector2I(0, 140 + i * 20),
                    MouseFilter = MouseFilterEnum.Ignore,
                    ZIndex = -10
                };
                AddControl(row);
                _rowBackgrounds.Add(row);
            }
            _footerBackground.Location = new Vector2I(0, footerY);
            _footerBackground.Visible = true;
            _closeButton.Location = new Vector2I(350, 3);
            _textArea.Size = new Vector2I(350, Math.Max(0, (int)Size.Y - 59));
            _scroll.Size = new Vector2I(14, Math.Max(0, (int)Size.Y - 59));
            _scroll.VisibleSize = (int)_textArea.Size.Y;
            _scroll.MaxValue = Math.Max(0, pageTextHeight - (int)_textArea.Size.Y + 14);
            _scroll.Visible = _scroll.MaxValue > 0;
        }

        // 子面板垂直吸附定位
        int panelY = (int)Size.Y;
        _goods.Location = new Vector2I(0, panelY);
        _repair.Location = new Vector2I(0, panelY);
        _advanced.Location = new Vector2I(0, panelY);

        _goods.SetGoods(_page.Goods, _page.Currency, _page.Types?.Select(x => x.ItemType));
        _goods.Visible = _page.DialogType == NPCDialogType.BuySell && _page.Goods != null && _page.Goods.Count > 0;
        if (selling)
        {
            GameScene.Game?.ShowInventoryForNpcSale(_page.Currency, _page.Types.Select(x => x.ItemType));
            _goods.Visible = true;
        }

        _repair.AllowedTypes = _page.Types?.Select(x => x.ItemType);
        _repair.Visible = _page.DialogType == NPCDialogType.Repair;
        if (_repair.Visible)
            GameScene.Game?.SetInventoryLegacyMode(InventoryMode.Repair);

        _advanced.HidePanel();
        GameScene.Game?.CloseNPCCompanionStorage();
        if (_page.DialogType != NPCDialogType.None && _page.DialogType != NPCDialogType.BuySell && _page.DialogType != NPCDialogType.Repair &&
            _page.DialogType != NPCDialogType.Socketing && _page.DialogType != NPCDialogType.SocketCombine &&
            (_page.DialogType != NPCDialogType.CompanionManage || GameScene.IsCompanionEnabled) &&
            _page.DialogType != NPCDialogType.Consignment)
        {
            _advanced.Configure(_page.DialogType);
            if (_page.DialogType == NPCDialogType.CompanionManage)
                GameScene.Game?.OpenNPCCompanionStorage();
        }
        if (_page.DialogType == NPCDialogType.Consignment && GameScene.IsConsignmentEnabled)
            GameScene.Game?.OpenConsignmentDialog();

        WindowManager.Open(this, GameScene.Game?.UILayer ?? GetParent());

        if (_page.DialogType == NPCDialogType.None)
            GameScene.Game?.OpenNPCQuestList(GameScene.Game.NPCObjectId);
        else
        {
            GameScene.Game?.CloseNPCQuestDialogs();
            GameScene.Game?.CloseNPCSocketDialogs();
            if (_page.DialogType == NPCDialogType.Socketing)
                GameScene.Game?.OpenNPCSocketDialog();
            else if (_page.DialogType == NPCDialogType.SocketCombine)
                GameScene.Game?.OpenNPCSocketCombineDialog();
        }
    }

    public void RepairResult(Library.Network.ServerPackets.NPCRepair packet) => _repair.RepairResult(packet);
    public void ClearAdvancedLinks(IEnumerable<CellLinkInfo> links) => _advanced.CompleteLinks(links);
    public void CancelPendingLinks()
    {
        var links = _repair.CancelLinks();
        links.AddRange(_advanced.CancelLinks());
        links.AddRange(_goods.CancelAllLinks());
        foreach (var link in links)
            GameScene.Game?.UnlockItemLink(link);
    }
    public void CancelUnsubmittedLinks()
    {
        var links = _repair.CancelDisplayedLinks();
        links.AddRange(_advanced.CancelUnsubmittedLinks());
        links.AddRange(_goods.CancelUnsubmittedLinks());
        foreach (var link in links)
            GameScene.Game?.UnlockItemLink(link);
    }
    public void ItemsChanged(IEnumerable<CellLinkInfo> links) => _goods.ItemsChanged(links);
    public void SetRefineList(IEnumerable<ClientRefineInfo> list) => _advanced.SetRefineList(list);
    public void RemoveRefine(int index) => _advanced.RemoveRefine(index);
    public void RefreshRefineList() => _advanced.RefreshRefineList();
    public void ShowRollResult(int type, int result) => _advanced.ShowRollResult(type, result);

    public bool TryRouteItem(DXItemCell source)
    {
        if (!Visible || source?.Item == null) return false;
        if (GameScene.Game?.TryRouteItemToSocket(source) == true) return true;
        if (_repair.Visible && _repair.TryRouteItem(source)) return true;
        if (_goods.Visible && _goods.TrySelectForSale(source)) return true;
        if (_advanced.Visible && _advanced.TryRouteItem(source)) return true;
        return false;
    }

    public bool TrySelectItemForSale(DXItemCell source)
        => Visible && _goods.TrySelectForSale(source);

    public bool CanAcceptAdvancedLink(DXItemCell source, DXItemCell target)
        => Visible && _advanced.Visible && _advanced.CanAcceptLink(source, target);

    public bool CanAcceptRepairLink(DXItemCell source)
        => Visible && _repair.Visible && _repair.CanAcceptSource(source);

    private void CloseNpc()
    {
        CancelUnsubmittedLinks();
        _advanced.HidePanel();
        if (GameScene.Game != null)
            GameScene.Game.CloseNPCDialog();
        else
            WindowManager.Close(this);
    }

    private string ApplyLegacyFColor(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("FCOLOR", StringComparison.Ordinal)) return text;
        var output = new List<string>();
        Color? current = null;
        foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("FCOLOR ", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed[7..].Trim(), out int index) && index >= 0 && index < LegacyFColorPalette.Length)
                {
                    current = LegacyFColorPalette[index];
                }
                continue;
            }
            output.Add(current == null || trimmed.Length == 0
                ? line
                : $"{{{line}:{current.Value.ToHtml(false)}}}");
        }
        return string.Join("\n", output);
    }

    private string ExtractLegacyNpcImg(string raw, out int frame)
    {
        frame = -1;
        if (string.IsNullOrEmpty(raw)) return raw;
        var lines = raw.Replace("\r", string.Empty).Split('\n');
        var output = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (TryParseLegacyNpcImg(line.Trim(), out int f))
            {
                frame = f;
            }
            else
            {
                output.Add(line);
            }
        }
        return string.Join("\n", output);
    }

    private bool TryParseLegacyNpcImg(string line, out int frame)
    {
        frame = -1;
        int at = line.IndexOf("NPCIMG", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;
        int i = at + "NPCIMG".Length;
        while (i < line.Length && (line[i] == '/' || line[i] == ' ' || line[i] == ':' || line[i] == '=')) i++;
        int start = i;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        if (i == start) return false;
        if (!int.TryParse(line[start..i], out frame)) { frame = -1; return false; }
        return true;
    }

    private void ApplyLegacyNpcFace(bool hasFace)
    {
        if (!hasFace || _legacyNpcFaceFrame < 0)
        {
            if (_legacyNpcFace != null) _legacyNpcFace.Visible = false;
            return;
        }
        _legacyNpcFace ??= new DXImageControl
        {
            LibraryFile = LibraryFile.NPCImage,
            FixedSize = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _legacyNpcFace.Index = _legacyNpcFaceFrame;
        var size = MirSkin.GetSize(LibraryFile.NPCImage, _legacyNpcFaceFrame);
        _legacyNpcFace.Size = size;
        // 原版 NPCIMG 帧绘制在 NPC 对话窗局部坐标 (40,30)。
        _legacyNpcFace.Location = new Vector2I(40, 30);
        if (_legacyNpcFace.GetParent() == null) AddControl(_legacyNpcFace);
        _legacyNpcFace.Visible = true;
    }

    private static readonly Color[] LegacyFColorPalette =
    {
        Color.Color8(0x00, 0x00, 0x00),
        Color.Color8(0xFF, 0x00, 0x00),
        Color.Color8(0x00, 0x80, 0x00),
        Color.Color8(0x80, 0x80, 0x00),
        Color.Color8(0x80, 0x80, 0x80),
        Color.Color8(0x80, 0x00, 0x00),
        Color.Color8(0x00, 0x80, 0x80),
        Color.Color8(0x00, 0x00, 0x80),
        Color.Color8(0xC0, 0xC0, 0xC0),
        Color.Color8(0x80, 0x00, 0x80),
        Color.Color8(0x00, 0xFF, 0x00),
        Color.Color8(0x00, 0x00, 0xFF),
        Color.Color8(0xFF, 0xFF, 0xFF),
        Color.Color8(0xFF, 0x00, 0xFF),
        Color.Color8(0x00, 0xFF, 0xFF),
        Color.Color8(0xFF, 0xFF, 0x00),
    };

    public override void Close()
    {
        base.Close();
        GameScene.Game?.SendNPCClose();
    }
}
