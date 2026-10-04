using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>EI id 8 chat popup (GameInter F350), separate from friends/mail CommunicationDialog.</summary>
public sealed partial class LegacyChatDialog : DXWindow
{
    private const int VisibleRows = 19;
    private const int LineStep = 14;
    private const int MaxMessages = 250;
    private readonly List<(string Text, Color Colour)> _messages = new();
    private readonly List<DXLabel> _rows = new();
    private readonly DXControl _historyClip;
    private readonly DXTextInput _input;
    private readonly DXImageControl _background;
    private readonly ChatScrollBar _scrollBar;
    private readonly DXButton _close;
    private readonly List<int> _linkedItems = new();
    private int _scrollOffset;

    /// <summary>
    /// C12 聊天窗右侧的锁链滚动条。
    ///
    /// **证据链（2026-10-03 全面更正，之前的「素材缺失/不可交互」判断是错的）**：
    /// - 原版是共享 gauge 控件（ctor `0x417960`、paint `0x4179B0`、vtable `0x476654`）。
    ///   聊天窗构造 `0x41428F`：
    ///     `0x417960(this+0x6D4, lib, frame=0x17C, pad=0x13, w=0x0C, h=0x104, mode=0, 0, 0x0C)`
    ///   → 轨道 **12 宽 × 260 高**，帧 380。
    /// - 绘制 `0x414822` 以 `(window.x+0x215, window.y−0xD0)`、value=`[this+0x68]`、
    ///   max=`[this+0x6D0]`；鼠标分派 `0x44197F` 对 `this+0x6D4` 调 `vtable+0xC`
    ///   命中测试并写回 `this+0x64/0x68` → **滑块可拖动**。
    /// - 上下箭头 `this+0x558`(帧 380/381) / `this+0x60C`(帧 382/383)，
    ///   位于窗口相对 (539,25)/(539,311)。**381/382/383 在本机 GameInter.wil
    ///   确实缺失（WIX offset=0）**，但 F350 已把箭头美术烘焙进背景 → 只留命中区。
    ///
    /// **滑块不是另画的方块**：GameInter 的锁链帧自带**宝石珠**，
    /// `F380`(16×502) 珠在 y≈249、`F631/F632`(16×286) 珠在 y≈141（即中点）。
    /// 用户原版截图里的金边宝石就是它。所以这里用 `F631` 作轨道，
    /// 滑块 = 同一张图上**裁出珠子那一段**，随 offset 上下移动 —— 不臆造美术。
    /// </summary>
    private sealed partial class ChatScrollBar : DXControl
    {
        private readonly ChainView _chain;
        private readonly BeadHit _thumb;
        private readonly DXButton _up, _down;
        private float _dragging;
        private bool _dragActive;

        // 证据几何：F350 聊天窗口右侧黑槽 X=531..547 (宽16)，Y=40..295 (高255)，完全对齐左侧 19 行聊天文字 (Y=28..294)。
        private const int TrackX = 531;
        private const int TrackY = 40;
        private const int TrackH = 255;
        private const int TrackW = 16;
        private const int UpY = 24;
        private const int DownY = 300;
        private const int ButtonW = 19;
        private const int ButtonH = 14;
        /// <summary>原版聊天历史可见行数（unified-model max_rows=19）。</summary>
        private const int VisibleRowCount = 19;
        /// <summary>滑块命中区高度：珠子本体 16px，上下各留 6px 便于点按。</summary>
        private const int ThumbHitH = 28;

        public ChatScrollBar()
        {
            Size = new Vector2I(572, 388);
            MouseFilter = MouseFilterEnum.Pass;
            MouseWheel += (s, e) => ScrollByRows?.Invoke(e.Delta > 0 ? 1 : -1);

            // 轨道 = F631 的一段（无珠子的链身）。珠子由滑块单独画。
            _chain = new ChainView
            {
                Location = new Vector2I(TrackX, TrackY),
                Size = new Vector2I(TrackW, TrackH),
                MouseFilter = MouseFilterEnum.Stop,
            };
            _chain.MouseWheel += (s, e) => ScrollByRows?.Invoke(e.Delta > 0 ? 1 : -1);
            _chain.MouseDown += (_, _) =>
            {
                Vector2 localMouse = GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
                int travel = Math.Max(1, TrackH - ThumbHitH);
                float t = Mathf.Clamp((localMouse.Y - TrackY - ThumbHitH / 2f) / travel, 0f, 1f);
                ScrollToFraction?.Invoke(t);
                BeginDrag();
            };
            AddControl(_chain);

            // 滑块 = 宝石珠子（F631 的 y138..144 段），随 offset 移动；命中区比珠子大。
            _thumb = new BeadHit
            {
                Location = new Vector2I(TrackX, TrackY),
                Size = new Vector2I(TrackW, ThumbHitH),
                MouseFilter = MouseFilterEnum.Stop,
            };
            _thumb.MouseWheel += (s, e) => ScrollByRows?.Invoke(e.Delta > 0 ? 1 : -1);
            AddControl(_thumb);

            // 箭头美术已烘焙进 F350（帧 381/382/383 本机缺失）→ 只建透明命中区。
            _up = HitButton(new Vector2I(TrackX, UpY));
            _down = HitButton(new Vector2I(TrackX, DownY));
        }

        private DXButton HitButton(Vector2I at)
        {
            var b = new DXButton
            {
                Name = "ChatScrollArrow",
                LibraryFile = LibraryFile.GameInter,
                Index = -1,
                HoverIndex = -1,
                PressedIndex = -1,
                Location = at,
                Size = new Vector2I(ButtonW, ButtonH),
                Modulate = new Color(1, 1, 1, 0),
                CanBePressed = true,
            };
            AddControl(b);
            return b;
        }

        public event Action<int>? ScrollByRows;
        public event Action<float>? ScrollToFraction;

        public void Configure(int messageCount)
        {
            int maxOffset = Math.Max(0, messageCount - VisibleRowCount);
            _thumb.Visible = maxOffset > 0;
            if (maxOffset == 0)
                _thumb.Position = new Vector2I(TrackX, TrackY + TrackH - ThumbHitH);
        }

        public void SetOffset(int offset, int messageCount)
        {
            int maxOffset = Math.Max(0, messageCount - VisibleRowCount);
            int travel = Math.Max(1, TrackH - ThumbHitH);
            if (maxOffset <= 0)
            {
                _thumb.Position = new Vector2I(TrackX, TrackY + travel);
                return;
            }
            float t = 1f - Mathf.Clamp((float)offset / maxOffset, 0f, 1f);
            _thumb.Position = new Vector2I(TrackX, TrackY + (int)Math.Round(t * travel));
        }

        public void Wire()
        {
            _up.MouseClick += (_, _) => ScrollByRows?.Invoke(VisibleRowCount);
            _down.MouseClick += (_, _) => ScrollByRows?.Invoke(-VisibleRowCount);
            _thumb.MouseDown += (_, _) => BeginDrag();
        }

        private void BeginDrag()
        {
            if (!_thumb.Visible) return;
            _dragActive = true;
            Vector2 localMouse = GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
            _dragging = localMouse.Y - (_thumb.Position.Y + ThumbHitH / 2f);
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (_dragActive && !Input.IsMouseButtonPressed(MouseButton.Left))
                _dragActive = false;
            if (!_dragActive) return;
            Vector2 localMouse = GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
            int travel = Math.Max(1, TrackH - ThumbHitH);
            float t = Mathf.Clamp((localMouse.Y - _dragging - TrackY) / travel, 0f, 1f);
            ScrollToFraction?.Invoke(t);
        }
    }

    /// <summary>
    /// 锁链轨道：只画 F631 的**纯链身**（源 y 0..132，不含珠子段 133..149），
    /// 逐行平铺到轨道高度。珠子由 <see cref="BeadHit"/> 单独画一次，
    /// 避免出现第二颗珠子。
    /// </summary>
    private sealed partial class ChainView : DXControl
    {
        /// <summary>珠子段起点（F631 实测 138..144，取 133..149 覆盖整颗）。</summary>
        private const int BeadBandStart = 133;
        private const int BeadBandEnd = 150;

        protected override void DrawControl()
        {
            var tex = MirSkin.GetTexture(LibraryFile.GameInter, 631);
            if (tex == null) return;
            int w = Math.Min((int)Size.X, (int)tex.GetWidth());
            int h = (int)Size.Y;
            if (w <= 0 || h <= 0) return;
            int bandH = Math.Max(1, BeadBandStart);
            for (int y = 0; y < h; y++)
            {
                int srcY = y % bandH;   // 0..132 循环，永远不取到珠子段
                DrawTextureRectRegion(tex,
                    new Rect2(0, y, w, 1),
                    new Rect2(0, srcY, w, 1), Colors.White);
            }
        }
    }

    /// <summary>
    /// 宝石滑块：直接裁 F631 的珠子段（y133..149）绘制 —— 原版没有独立滑块美术，
    /// 滑块就是锁链图上那颗金橙宝石（用户原版截图即此物）。
    /// </summary>
    private sealed partial class BeadHit : DXControl
    {
        protected override void DrawControl()
        {
            var tex = MirSkin.GetTexture(LibraryFile.GameInter, 631);
            if (tex == null) return;
            int w = Math.Min((int)Size.X, (int)tex.GetWidth());
            if (w <= 0) return;
            const int srcY = 133, srcH = 16;
            DrawTextureRectRegion(tex,
                new Rect2(0, (Size.Y - srcH) / 2f, w, srcH),
                new Rect2(0, srcY, w, srcH), Colors.White);
        }
    }
    public LegacyChatDialog()
    {
        HasTitle = false;
        HasTopBorder = false;
        HasFooter = false;
        DrawChrome = false;
        ShowCloseButton = false;
        DropShadow = false;
        Movable = true;
        Size = new Vector2I(572, 388);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 350,
            Location = new Vector2I(-226, -62),
            FixedSize = true,
            Size = MirSkin.GetSize(LibraryFile.GameInter, 350),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _scrollBar = new ChatScrollBar();
        _scrollBar.ScrollByRows += ScrollBy;
        _scrollBar.ScrollToFraction += f => SetScrollOffset((int)Math.Round((1f - f) * Math.Max(0, _messages.Count - VisibleRows)));
        _scrollBar.Wire();
        AddControl(_scrollBar);

        // C4/4.5 证据 chat_window_evidence.refresh.draw_geometry：
        // 文字原点 (40,29)，但**首行裁剪框**是 (35,28)-(520,43) = 485x15
        // （比文字原点左多 5px），按 14px 步进；19 行 -> 高 266。
        // 此前用 (40,29) 491x279，右边多出 6px、左边少 5px。
        _historyClip = new DXControl
        {
            Location = new Vector2I(35, 28),
            Size = new Vector2I(485, 14 * 19),
            Clip = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _historyClip.MouseWheel += OnHistoryWheel;
        _historyClip.MouseClick += OnHistoryClick;
        AddControl(_historyClip);
        MouseWheel += OnHistoryWheel;

        // 固定 19 行标签池：只复用、不增删（见 RebuildRows 的说明）。
        for (int i = 0; i < VisibleRows; i++)
        {
            var row = new DXLabel
            {
                Name = $"ChatRow{i}",
                FontSize = 9,
                TextColour = Colors.White,
                DrawOutline = true,
                AutoSize = true,
                Size = new Vector2I(491, LineStep),
                Location = new Vector2I(0, i * LineStep),
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            _historyClip.AddControl(row);
            _rows.Add(row);
        }

        var channelFrames = new[] { (360, 361), (362, 363), (364, 365), (366, 367), (368, 369), (370, 371) };
        var channelText = new[] { "@拒绝 ", "!", "!!", "!~", "@拒绝私聊", "@拒绝行会聊天" };
        var tips = new[]
        {
            "拒绝和 某人 私聊(@拒绝 某人名)",
            "大喊话(!喊话)",
            "编组 喊话(!!喊话)",
            "行会 喊话(!~喊话)",
            "拒绝 私聊(@拒绝私聊)",
            "拒绝 行会 聊天(@拒绝行会聊天)",
        };
        for (int i = 0; i < channelFrames.Length; i++)
        {
            int index = i;
            var button = CreateSpriteButton(channelFrames[i].Item1, channelFrames[i].Item2,
                new Vector2I(25 + 40 * i, 332));
            // 原版频道键三态（ctor 实参 + 绘制状态机 0x417640 / 鼠标 0x417780-0x4177F0）：
            //   arg8 = -1  -> 普通态**不画**（灰白图标由背景 F350 烘焙；实测 F350 在
            //                  窗口相对 (25+40k, 332) 处就是那 6 个图标）
            //   arg2 = 360+2k -> "屏蔽/激活"绿态（该键 arg9/+0x30 = 0，悬停态不画帧，只画文字）
            //   arg3 = 361+2k -> 按下金态
            // 故此处不常显绿帧：普通态画空、悬停不画帧（提示文字走 Tooltip）、按下画 arg3。
            button.Size = MirSkin.GetSize(LibraryFile.GameInter, channelFrames[i].Item1);
            button.Index = -1;
            button.HoverIndex = -1;
            button.PressedIndex = channelFrames[i].Item2;
            button.TooltipText = tips[i];
            button.MouseClick += (_, _) => InsertChannelTemplate(channelText[index]);
            AddControl(button);
        }

        _input = new DXTextInput
        {
            Position = new Vector2(25, 311),
            Size = new Vector2(499, 15),
            MaxLength = Globals.MaxChatLength,
        };
        _input.TextSubmitted += Submit;
        // Escape 取消：原版 ChatTextBox 的输入框 KeyPress Escape 分支
        // （旧 Client/Scenes/Views/ChatTextBox.cs:302-309）清空文本、解绑链接物品、
        // 并隐藏聊天条。DXTextInput 已有 Canceled 事件但**此前无人订阅**，
        // 且 LineEdit 会消费 Escape，导致「聊天框打开后 Esc 无法关闭」
        // （真机复现：R 开聊天窗后按 Esc 无任何变化，其它热键同样被
        // GameScene._Input 的 LineEdit 焦点早退吞掉）。
        _input.Canceled += CancelChatInput;
        AddControl(_input);

        // 原版关闭钮 arg8=-1、arg9=0（普通/悬停都不画帧）；F161(✕) 已烘焙进 F350
        // （模板搜索命中窗口(532,350)+背景偏移(-226,-62) = 帧(758,412)，diff 31.8）→ 只保留按下 arg3=162。
        _close = CreateSpriteButton(161, 162, new Vector2I(532, 350));
        _close.Index = -1;
        _close.HoverIndex = -1;
        _close.PressedIndex = 162;
        _close.Modulate = new Color(1, 1, 1, 0);  // 不绘制（含 fallback 底色框）
        _close.Size = new Vector2I(28, 26);
        _close.TooltipText = Lang.CommonControlClose;
        _close.MouseClick += (_, _) => CloseChat();
        AddControl(_close);
        Visible = false;
    }

    private static DXButton CreateSpriteButton(int frame, int hover, Vector2I location, Vector2I? fallbackHitSize = null)
    {
        var button = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = frame,
            HoverIndex = hover,
            PressedIndex = hover,
            Location = location,
            CanBePressed = true,
        };
        if (fallbackHitSize.HasValue
            && (MirSkin.GetSize(LibraryFile.GameInter, frame) == Vector2I.Zero
                || MirSkin.GetSize(LibraryFile.GameInter, hover) == Vector2I.Zero))
        {
            GD.Print($"[LegacyChat] scroll button frames {frame}/{hover} unavailable; using {fallbackHitSize.Value} hit zone without sprite");
            button.Index = -1;
            button.HoverIndex = -1;
            button.PressedIndex = -1;
            button.Size = fallbackHitSize.Value;
        }
        return button;
    }

    public void OpenChat(Node parent)
    {
        WindowManager.Open(this, parent);
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
        CallDeferred(nameof(RefreshVisuals));
    }

    private void RefreshVisuals()
    {
        if (!Visible) return;
        QueueRedraw();
        foreach (Node child in GetChildren())
            if (child is CanvasItem item)
                item.QueueRedraw();
    }
    public bool InputHasFocus => _input.HasFocus;

    public void CloseChat()
    {
        _input.ReleaseFocus();
        WindowManager.Close(this);
    }

    public override void Close()
    {
        _input.ReleaseFocus();
        Visible = false;
    }

    public bool HandleGlobalKey(InputEventKey key, Node parent)
    {
        if (!key.Pressed) return false;
        if (_input.HasFocus) return true;
        if (!Visible) return false;
        if (ClientSettings.ShiftOpenChat && key.ShiftPressed && key.Keycode is >= Key.Key0 and <= Key.Key9)
        {
            _input.GrabFocus();
            GetViewport()?.SetInputAsHandled();
            return true;
        }
        if (key.Keycode is Key.Enter or Key.Space or Key.Slash || key.Unicode == '@' || key.Unicode == '!')
        {
            _input.GrabFocus();
            if (key.Keycode == Key.Slash) _input.Text = "/";
            else if (key.Unicode == '@' || key.Unicode == '!') _input.Text = ((char)key.Unicode).ToString();
            _input.CaretColumn = _input.Text.Length;
            GetViewport()?.SetInputAsHandled();
            return true;
        }
        return false;
    }

    public void AddMessage(string text, MessageType type, Color colour)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _messages.Add((text, colour));
        if (_messages.Count > MaxMessages) _messages.RemoveAt(0);
        // 原版：底部视图（offset==0）自动跟随新消息；用户往上翻历史时**不**打断，
        // 保持当前浏览位置。此前"仅 offset==0 才重建"会让用户翻上去后新消息
        // 完全不出现，且滑块位置也不更新。
        if (_scrollOffset == 0) RebuildRows();
        else
        {
            _scrollBar.Configure(_messages.Count);
            _scrollBar.SetOffset(_scrollOffset, _messages.Count);
        }
    }

    public void LinkItem(ClientUserItem item)
    {
        if (item?.Info == null || _linkedItems.Count >= Globals.MaxChatItemLinks) return;
        _input.Text += $"[{item.Info.Local()}]";
        _linkedItems.Add(item.Index);
        _input.CaretColumn = _input.Text.Length;
        _input.GrabFocus();
    }


    /// <summary>
    /// EI F350 的 line_recall（primary-static 0x004142C0）：点击历史行 ->
    /// 剥前缀、取发送者、sprintf "/%s " 写进编辑框并置焦点。
    ///
    /// 事件挂在**裁剪区**而不是每一行上：DXControl 收到鼠标事件必定
    /// AcceptEvent()（DXControl.cs _GuiInput），若给每行设 MouseFilter.Stop
    /// 会把滚轮一并吞掉，直接破坏本窗的滚轮滚动。行本身保持 Ignore。
    /// </summary>
    private void OnHistoryClick(object sender, EventArgs e)
    {
        Vector2 local = _historyClip.GetLocalMousePosition();
        if (local.Y < 0) return;
        int row = (int)local.Y / LineStep;
        if (row < 0 || row >= VisibleRows) return;

        int end = Math.Max(0, _messages.Count - _scrollOffset);
        int start = Math.Max(0, end - VisibleRows);
        int index = start + row;
        if (index < 0 || index >= end) return;

        string name = ChatLineRecall.ExtractSenderName(_messages[index].Text);
        if (string.IsNullOrEmpty(name)) return;
        _input.Text = ChatLineRecall.FormatWhisper(name);
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
    }

    /// <summary>
    /// 输入框 Escape：原版 ChatTextBox.TextBox_KeyPress 的 Escape 分支
    /// （旧 Client/Scenes/Views/ChatTextBox.cs:302-309）——丢弃未提交文本、
    /// 清链接物品、关闭聊天条。用 CloseChat 走 WindowManager，保证 Z 序
    /// 与 OpenWindows 列表同步（否则会留下「不可见但仍在列表」的残留）。
    /// </summary>
    private void CancelChatInput()
    {
        _linkedItems.Clear();
        _input.Text = string.Empty;
        CloseChat();
    }

    private void Submit(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            GameScene.Game?.SendChat(text, new List<int>(_linkedItems));
        _linkedItems.Clear();
        _input.Text = string.Empty;
        _input.ReleaseFocus();
    }

    private void InsertChannelTemplate(string text)
    {
        if (text is "@拒绝私聊" or "@拒绝行会聊天")
            _input.Text = string.IsNullOrEmpty(_input.Text) ? text : _input.Text + " " + text;
        else if (string.IsNullOrEmpty(_input.Text))
            _input.Text = text;
        else if (text is "!" or "!!" or "!~")
            _input.Text = text + _input.Text.TrimStart('!', '~');
        else
            _input.Text = text + _input.Text;
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
    }

    private void OnHistoryWheel(object sender, MouseWheelEventArgs mouseEvent)
    {
        // 滚轮按**单行**翻（此前 ±19 行 = 整页跳，一滚就跳过所有中间内容）。
        // 滚轮向上 = 看更早 = 增大 offset。
        ScrollBy(mouseEvent.Delta > 0 ? 1 : -1);
    }

    public void ScrollBy(int rows) => SetScrollOffset(_scrollOffset + rows);

    private void SetScrollOffset(int value)
    {
        _scrollOffset = Mathf.Clamp(value, 0, Math.Max(0, _messages.Count - VisibleRows));
        RebuildRows();
    }

    private void RebuildRows()
    {
        // **不能用 QueueFree 重建**：释放是延迟的，QueueFree 过的行在当帧仍挂在
        // _historyClip 上，与新建的行叠在一起 —— 这就是「聊天内容经常消失」
        // 的根因（点滚动/来新消息时旧行压在新行上，看起来像内容没了）。
        // 改为**固定 19 行标签池**：只改 Text/位置，永不增删节点。
        int end = Math.Max(0, _messages.Count - _scrollOffset);
        int start = Math.Max(0, end - VisibleRows);
        int shown = 0;
        for (int i = start; i < end && shown < _rows.Count; i++, shown++)
        {
            var row = _rows[shown];
            if (row.Text != _messages[i].Text)
                row.Text = _messages[i].Text;
            row.TextColour = _messages[i].Colour;
            row.Position = new Vector2I(0, shown * LineStep);
            row.Visible = true;
        }
        for (int i = shown; i < _rows.Count; i++)
            _rows[i].Visible = false;

        _scrollBar.Configure(_messages.Count);
        _scrollBar.SetOffset(_scrollOffset, _messages.Count);
    }
}
