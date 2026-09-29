using System;

namespace ZirconClient.Controls;

/// <summary>
/// 点击聊天行把「该行发送者」预填成私聊目标的文本解析。
///
/// 这是**前缀模板**，不是 @mention / autocomplete：只生成 "/名字 "，不做候选列表、
/// 不做补全、不发包。EI 原始证据：
/// - primary-static：F350 详细聊天窗 `line_recall` = 0x004142C0
///   （Mir3-Research docs/research/ei-ui-layout/chat-window-unified-model.json
///   input.line_recall：「PtInRect on line area, walk list, strip / or (!),
///   tokenize, sprintf '/%s ', write edit」），token 分隔符 space 0x20 / colon 0x3A
///   见同目录 chat-window-render-evidence.json input_parser。
/// - secondary-source（社区 Preview Delphi 源码，仅作交叉印证、不替代 primary-static）：
///   reference/mir3-source/Source/Client/FState.pas DBottomMouseDown 的
///   ExtractUserName：GetValidStr3 分隔符 ('(','!','*','/',')') 再取
///   (' ','=',':') 首 token，并拒绝以 '/','(',' ','[' 开头的 token。
///   其中 '=' 分隔符正好对应 Zircon 服务端私聊回显 "Name=> text"。
///
/// 解析是逐字符的，不限制 ASCII：中文/韩文玩家名同样可用。
/// </summary>
public static class ChatLineRecall
{
    /// <summary>从一行已渲染的聊天文本里取出发送者名；取不到返回空串。</summary>
    public static string ExtractSenderName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string s = text.TrimStart();

        // Zircon 现代 ChatTab 若带 "[Type] " 头则剥掉（当前渲染不带，防御性保留）。
        if (s.Length > 2 && s[0] == '[')
        {
            int close = s.IndexOf(']');
            if (close > 1) s = s[(close + 1)..].TrimStart();
        }

        // Zircon 服务端观察者行固定是 "(#)Name: text"（PlayerObject.ObserverChat）。
        // 参考实现的 GetValidStr3 会把该行算成 "#"，对运行时无意义，这里显式剥掉。
        if (s.StartsWith("(#)", StringComparison.Ordinal)) s = s[3..].TrimStart();

        // EI line_recall：先剥掉 "/ ! 等命令前缀，再取首 token。
        int head = 0;
        while (head < s.Length && (s[head] is '/' or '!' or '*' or '(' or ')')) head++;
        s = s[head..];

        int end = s.Length;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] is ':' or '=' or ' ' or '\t') { end = i; break; }
        }
        if (end <= 0) return string.Empty;

        string name = s[..end];
        // 与原版一致：以 '/','(',' ','[' 开头的 token 不是玩家名。
        if (name[0] is '/' or '(' or ' ' or '[') return string.Empty;
        return name;
    }

    /// <summary>原版 sprintf "/%s " 的预填格式（名字 + 尾随空格）。</summary>
    public static string FormatWhisper(string name) => $"/{name} ";
}
