using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 HelpDialog：左侧主题，右侧页标题与帮助正文。</summary>
public partial class HelpDialog : DXWindow
{
    private DXControl _menu;
    private DXControl _pageMenu;
    private DXControl _content;
    private DXVScrollBar _menuScroll;
    private DXVScrollBar _contentScroll;
    private DXLabel _contentTitle;
    private DXLabel _contentText;
    private DXButton _selectedMenuButton;
    private readonly List<HelpSectionRow> _sectionRows = new();

    public bool AuditLayout(out string details)
    {
        int helpCount = Globals.HelpInfoList?.Binding?.Count ?? 0;
        details = $"size={Size} menu={_menu.Position}/{_menu.Size} pages={_pageMenu.Position}/{_pageMenu.Size} content={_content.Position}/{_content.Size} sections={_sectionRows.Count} helpData={helpCount}";
        return Size == new Vector2I(720, 401)
            && _menu.Position == new Vector2I(13, 70)
            && _menu.Size == new Vector2I(156, 306)
            && _pageMenu.Position == new Vector2I(178, 68)
            && _pageMenu.Size == new Vector2I(535, 22)
            && _content.Position == new Vector2I(178, 90)
            && _content.Size == new Vector2I(535, 312)
            && _menuScroll.Position == new Vector2I(134, 0)
            && _contentScroll.Position == new Vector2I(514, 0)
            && (_sectionRows.Count > 0 || helpCount == 0);
    }

    public HelpDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = true;
        Size = new Vector2I(720, 401);

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 9300,
            FixedSize = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var close = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
        };
        close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        close.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(close);

        AddControl(new DXLabel
        {
            Text = Lang.MenuHelpLabel,
            FontSize = 10,
            TextColour = new Color(1f, 0.85f, 0.3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            Location = new Vector2I(0, 8),
            Size = new Vector2I(720, 18),
            IsControl = false,
        });

        _menu = new DXControl { Location = new Vector2I(13, 70), Size = new Vector2I(156, 306), Clip = true };
        _pageMenu = new DXControl { Location = new Vector2I(178, 68), Size = new Vector2I(535, 22), Clip = true };
        _content = new DXControl { Location = new Vector2I(178, 90), Size = new Vector2I(535, 312), Clip = true };
        AddControl(_menu);
        AddControl(_pageMenu);
        AddControl(_content);

        _menuScroll = CreateScrollBar(new Vector2I(134, 0), new Vector2I(20, 306), 306);
        _menu.AddControl(_menuScroll);
        _menuScroll.ValueChanged += (s, e) => UpdateMenuLocations();

        _contentScroll = CreateScrollBar(new Vector2I(514, 0), new Vector2I(20, 312), 312);
        _content.AddControl(_contentScroll);
        _contentScroll.ValueChanged += (s, e) => UpdateContentLocations();
        BuildMenu();
    }

    private static DXVScrollBar CreateScrollBar(Vector2I location, Vector2I size, int visibleSize)
    {
        return new DXVScrollBar
        {
            Location = location,
            Size = size,
            MinValue = 0,
            MaxValue = visibleSize,
            VisibleSize = visibleSize,
            Change = 23,
            HideWhenNoScroll = false,
            IsControl = true,
        };
    }

    private void BuildMenu()
    {
        var help = Globals.HelpInfoList?.Binding.OrderBy(x => x.Order).ToList();
        if (help == null || help.Count == 0)
        {
            ShowPageContent(null, Lang.MenuHelpLabel, Lang.HelpHelpLabel3);
            return;
        }
        for (int i = 0; i < help.Count; i++)
        {
            var info = help[i];
            var button = new DXButton
            {
                Text = info.Title ?? Lang.MenuHelpLabel,
                FontSize = 10,
                TextColour = new Color(1f, 0.85f, 0.3f),
                Size = new Vector2I(134, 21),
                Location = new Vector2I(0, i * 23),
                LibraryFile = LibraryFile.GameInter,
                Index = 9310,
                HoverIndex = 9310,
                PressedIndex = 9310,
                FixedSize = true,
            };
            button.MouseClick += (o, e) =>
            {
                _selectedMenuButton = (DXButton)o;
                UpdateMenuSelection();
                ShowHelp(info);
            };
            _menu.AddControl(button);
        }
        _menuScroll.MaxValue = help.Count * 23;
        if (help.Count > 0)
        {
            _selectedMenuButton = _menu.Controls.OfType<DXButton>().FirstOrDefault();
            UpdateMenuSelection();
            ShowHelp(help[0]);
        }
    }

    private void UpdateMenuLocations()
    {
        int index = 0;
        foreach (var control in _menu.Controls)
        {
            if (control is DXButton button)
                button.Location = new Vector2I(0, index++ * 23 - _menuScroll.Value);
        }
    }

    private void UpdateMenuSelection()
    {
        foreach (var control in _menu.Controls)
        {
            if (control is not DXButton button) continue;
            bool selected = button == _selectedMenuButton;
            button.Index = selected ? 9311 : 9310;
            button.HoverIndex = selected ? 9311 : 9310;
            button.PressedIndex = selected ? 9311 : 9310;
        }
    }

    private void ShowHelp(HelpInfo info)
    {
        foreach (var child in _pageMenu.GetChildren())
        {
            if (child is not Node node) continue;
            if (node is DXControl control) _pageMenu.RemoveControl(control);
            else _pageMenu.RemoveChild(node);
            node.QueueFree();
        }
        foreach (var child in _content.GetChildren())
        {
            if (child is not Node node || node == _contentScroll) continue;
            if (node is DXControl control) _content.RemoveControl(control);
            else _content.RemoveChild(node);
            node.QueueFree();
        }
        _contentTitle = null;
        _contentText = null;

        var pages = info.Pages?.OrderBy(x => x.Order).ToList();
        if (pages != null && pages.Count > 0)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var pageButton = new DXButton
                {
                    Text = page.Title ?? string.Format(Lang.HelpUi362Label, i + 1),
                    FontSize = 9,
                    TextColour = new Color(0.86f, 0.78f, 0.48f),
                    Size = new Vector2I(125, 22),
                    Location = new Vector2I(i * 128, 0),
                    LibraryFile = LibraryFile.Interface,
                    Index = -1,
                };
                pageButton.MouseClick += (o, e) => ShowPage(info, page);
                _pageMenu.AddControl(pageButton);
            }
            ShowPage(info, pages[0]);
            return;
        }

        ShowPageContent(info, info.Title ?? Lang.MenuHelpLabel, info.Description ?? string.Empty);
    }

    private void ShowPage(HelpInfo info, HelpPageInfo page)
    {
        var items = page.Items?.OrderBy(x => x.Order).ToList();
        ShowPageSections(items, page.Title ?? info.Title, info.Description);
    }

    private void ShowPageContent(HelpInfo info, string titleText, string body)
    {
        ShowPageSections(null, titleText, body);
    }

    private void ShowPageSections(System.Collections.Generic.IEnumerable<HelpItemInfo> items, string fallbackTitle, string fallbackBody)
    {
        foreach (var row in _sectionRows)
        {
            _content.RemoveControl(row);
            row.QueueFree();
        }
        _sectionRows.Clear();

        var sections = items?.OrderBy(x => x.Order).ToList() ?? new List<HelpItemInfo>();
        int y = 5;
        if (sections.Count == 0)
        {
            var row = new HelpSectionRow(fallbackTitle ?? string.Empty, fallbackBody ?? string.Empty)
            {
                Location = new Vector2I(0, y),
            };
            _content.AddControl(row);
            _sectionRows.Add(row);
            y += (int)row.Size.Y;
        }
        else
        {
            foreach (var section in sections)
            {
                var row = new HelpSectionRow(section.Title, section.Content)
                {
                    Location = new Vector2I(0, y),
                };
                _content.AddControl(row);
                _sectionRows.Add(row);
                y += (int)row.Size.Y;
            }
        }

        _contentScroll.MaxValue = Math.Max(0, y + 30);
        _contentScroll.Value = 0;
    }

    private void UpdateContentLocations()
    {
        int offset = _contentScroll?.Value ?? 0;
        int y = 5 - offset;
        foreach (var row in _sectionRows)
        {
            row.Location = new Vector2I(0, y);
            y += (int)row.Size.Y;
        }
    }
}

/// <summary>原版 HelpItem：左侧标题、右侧正文和 9315 分隔线。</summary>
public sealed partial class HelpSectionRow : DXControl
{
    private static readonly Regex ColourMarkup = new(@"\{([^:}]+):[^}]+\}", RegexOptions.Compiled);

    public HelpSectionRow(string title, string content)
    {
        const int titleWidth = 120;
        const int contentWidth = 345;
        const int contentLeft = 150;
        string body = ColourMarkup.Replace(content ?? string.Empty, "$1");
        int titleHeight = EstimateHeight(title ?? string.Empty, titleWidth, 10);
        int bodyHeight = EstimateHeight(body, contentWidth, 10);
        int textHeight = Math.Max(20, Math.Max(titleHeight, bodyHeight));
        Size = new Vector2I(500, textHeight + 10);

        AddControl(new DXLabel
        {
            Text = title ?? string.Empty,
            FontSize = 10,
            TextColour = Colors.White,
            Location = new Vector2I(20, 0),
            Size = new Vector2I(titleWidth, textHeight),
            AutoSize = false,
            IsControl = false,
        });
        AddControl(new DXLabel
        {
            Text = body,
            FontSize = 10,
            TextColour = Colors.White,
            Location = new Vector2I(contentLeft, 0),
            Size = new Vector2I(contentWidth, textHeight),
            AutoSize = false,
            IsControl = false,
        });
        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 9315,
            Location = new Vector2I(50, textHeight + 2),
            IsControl = false,
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }

    private static int EstimateHeight(string text, int width, int fontSize)
    {
        var font = MirSkin.GetFont();
        if (font == null || string.IsNullOrEmpty(text)) return 20;
        float lineWidth = 0;
        int lines = 1;
        float lineHeight = MirSkin.MeasureText("A", fontSize).Y;
        foreach (char ch in text.Replace("\r", string.Empty))
        {
            if (ch == '\n')
            {
                lines++;
                lineWidth = 0;
                continue;
            }
            float next = MirSkin.MeasureText(new string(new[] { ch }), fontSize).X;
            if (lineWidth > 0 && lineWidth + next > width)
            {
                lines++;
                lineWidth = next;
            }
            else lineWidth += next;
        }
        return Math.Max(20, (int)Math.Ceiling(lines * lineHeight));
    }
}
