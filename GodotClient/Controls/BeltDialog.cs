using System;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 腰带窗口 (移植自 Client/Scenes/Views/BeltDialog.cs):
/// 无标题无边框, 10 格横向 (Globals.MaxBeltCount=10), 每格右上角数字 1-9,0。
/// 格内 QuickInfo/QuickItem 由 UpdateLinks 从 BeltLinks 恢复。
/// </summary>
public partial class BeltDialog : DXWindow
{
    public ClientBeltLink[] Links;
    public DXItemGrid Grid;
    private bool _resizing;

    /// <summary>玩家是否拖动过腰带栏; LayoutHud 不能覆盖已自定义的位置。</summary>
    public bool UserMoved { get; private set; }

    public BeltDialog()
    {
        Movable = true;
        HasTitle = false;
        HasFooter = false;
        HasTopBorder = false;
        ShowCloseButton = false;
        Size = new Vector2I(10 * (DXItemCell.CellWidth - 1) + 1, DXItemCell.CellHeight - 1 + 1);

        // 恢复上一次拖动后的位置; (-1,-1) 表示首次使用, 由 LayoutHud 给默认锚点。
        Vector2I saved = ClientSettings.BeltDialogLocation;
        if (saved.X >= 0 && saved.Y >= 0)
        {
            Position = saved;
            UserMoved = true;
        }

        // DXControl.Movable 拖动时触发 Moving; 松开时 MouseUp 持久化。
        Moving += (_, _) => UserMoved = true;
        MouseUp += (_, _) =>
        {
            if (UserMoved)
            {
                ClientSettings.BeltDialogLocation = new Vector2I((int)Position.X, (int)Position.Y);
                ClientSettings.Save();
            }
        };
        Links = new ClientBeltLink[Globals.MaxBeltCount];
        for (int i = 0; i < Globals.MaxBeltCount; i++)
            Links[i] = new ClientBeltLink { Slot = i };

        Grid = new DXItemGrid
        {
            GridSize = new Vector2I(10, 1),
            Location = Vector2I.Zero,
            GridType = GridType.Belt,
            GridPadding = 0,
            Border = false,
        };
        AddControl(Grid);
        RefreshGridLayout();
    }

    public override void _Ready()
    {
        base._Ready();
        Resized += OnResized;
        RefreshGridLayout();
    }

    private void OnResized() => RefreshGridLayout();
    /// <summary>未拖动时由 LayoutHud 调用: 锚在主面板右上侧 (底栏旁), 贴右对齐。</summary>
    public void ApplyDefaultAnchor(Vector2 logicalViewport, Vector2I mainPanelLocation, Vector2 mainPanelSize)
    {
        if (UserMoved) return;
        float x = mainPanelLocation.X + mainPanelSize.X - Size.X;
        float y = mainPanelLocation.Y - Size.Y;
        Position = new Vector2(Mathf.Max(0, x), Mathf.Max(0, y));
    }

    /// <summary>原版 AllowResize：按格子吸附尺寸，并在横向/纵向之间自动切换。</summary>
    public override Vector2I GetAcceptableResize(Vector2 requested)
    {
        int width = Math.Max(1, (int)requested.X);
        int height = Math.Max(1, (int)requested.Y);
        int columns = Math.Max(1, Math.Min(Globals.MaxBeltCount, (int)Math.Ceiling((width - 10) / (double)DXItemCell.CellWidth)));
        int rows = Math.Max(1, Math.Min(Globals.MaxBeltCount, (int)Math.Ceiling((height - 10) / (double)DXItemCell.CellHeight)));

        if (height > width)
            columns = 1;
        else
            rows = 1;

        return new Vector2I(columns * (DXItemCell.CellWidth - 1) + 1,
            rows * (DXItemCell.CellHeight - 1) + 1);
    }

    private void RefreshGridLayout()
    {
        if (Grid == null) return;

        int columns = Math.Max(1, Math.Min(Globals.MaxBeltCount,
            (int)Math.Ceiling((Size.X - 10) / (double)DXItemCell.CellWidth)));
        int rows = Math.Max(1, Math.Min(Globals.MaxBeltCount,
            (int)Math.Ceiling((Size.Y - 10) / (double)DXItemCell.CellHeight)));
        if (Size.Y > Size.X) columns = 1;
        else rows = 1;

        int count = columns * rows;
        if (Grid.GridSize == new Vector2I(columns, rows) && Grid.Cells?.Length == count)
            return;

        Grid.GridSize = new Vector2I(columns, rows);
        AddSlotLabels();
        UpdateLinks();
    }

    private void AddSlotLabels()
    {
        if (Grid?.Cells == null) return;
        for (int i = 0; i < Grid.Cells.Length; i++)
        {
            int slot = i;
            var label = new DXLabel
            {
                Text = ((slot + 1) % 10).ToString(),
                FontSize = 8,
                TextColour = new Color(1f, 0.9f, 0.5f),
                DrawOutline = true,
                OutlineColour = Colors.Black,
                Location = new Vector2I(-2, -1),
                IsControl = false,
            };
            Grid.Cells[slot].AddControl(label);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!IsEnabled)
        {
            if (e is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }
        if (e is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed && button.Position.X >= Size.X - 10 && button.Position.Y >= Size.Y - 10)
            {
                _resizing = true;
                AcceptEvent();
                return;
            }

            if (!button.Pressed && _resizing)
            {
                _resizing = false;
                AcceptEvent();
                return;
            }
        }

        if (e is InputEventMouseMotion motion && _resizing)
        {
            Size = GetAcceptableResize(motion.Position);
            RefreshGridLayout();
            AcceptEvent();
            return;
        }

        base._GuiInput(e);
    }

    /// <summary>从 BeltLinks 恢复格子链接 (登录时调用)</summary>
    public void UpdateLinks()
    {
        if (GameScene.Game == null) return;

        // 全量回包可能包含空槽；先清掉旧的 QuickInfo/QuickItem，避免
        // 重连、角色切换或服务端清空腰带后仍显示上一轮链接。
        foreach (var cell in Grid.Cells ?? Array.Empty<DXItemCell>())
        {
            cell.QuickInfo = null;
            cell.QuickItem = null;
        }

        foreach (var link in Links)
        {
            if (link.Slot < 0 || link.Slot >= Grid.Cells.Length) continue;

            if (link.LinkInfoIndex > 0)
            {
                var info = Globals.ItemInfoList.Binding.FirstOrDefault(x => x.Index == link.LinkInfoIndex);
                if (info != null) Grid.Cells[link.Slot].QuickInfo = info;
            }
            else if (link.LinkItemIndex > 0)
            {
                var item = GameScene.Game.Inventory.FirstOrDefault(x => x?.Index == link.LinkItemIndex);
                if (item != null) Grid.Cells[link.Slot].QuickItem = item;
            }
        }
        Grid.RefreshGrid();
    }
}
