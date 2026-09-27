using System;
using Godot;
using Library;
using ZirconClient.Formats;

namespace ZirconClient.Controls;

/// <summary>
/// 创建角色预览 (移植自原版 NewCharacterDialog.PreviewPanel_AfterDraw，
/// Client/Scenes/SelectScene.cs:1307)。
///
/// 原版**不是动画帧**，而是静态分层合成，全部画在同一锚点
/// (预览面板左上角 + (70, 160)) 上，每层各自加 WIL OffSetX/Y：
///   1. 身体   ProgUse 0(男)/1(女)      Image,   白色
///   2. 盔甲   Equip 941/951/2000/2010  Image,   白色
///   3. 武器   Equip 1042/2200          Image,   白色
///   4. 盔甲色 Equip 同盔甲帧           Overlay,  染 ArmourColour
///   5. 头发   ProgUse hairBase+发型-1  Overlay,  染 HairColour
///      (刺客女 发型1 额外叠 ProgUse 1160, Image, 染 HairColour)
/// 每次外观状态变化 SetAppearance -> QueueRedraw 即可，无时间驱动。
/// </summary>
public partial class DXCreatePreviewControl : DXControl
{
    // 原版 lib.Draw(..., x + 70, y + 160, ...)，x/y 为预览面板 DisplayArea 左上角。
    private static readonly Vector2 PreviewAnchor = new(70f, 160f);

    private MirClass _class = MirClass.Warrior;
    private MirGender _gender = MirGender.Male;
    private int _hairNumber = 1;
    private Color _hairColour = Colors.Black;
    private Color _armourColour = Colors.White;

    public override void _Ready()
    {
        IsControl = false; // 纯显示层，不拦截鼠标
        base._Ready();
    }

    public void SetAppearance(MirClass cls, MirGender gender, int hairNumber,
        Color hairColour, Color armourColour)
    {
        _class = cls;
        _gender = gender;
        _hairNumber = hairNumber;
        _hairColour = hairColour;
        _armourColour = armourColour;
        QueueRedraw();
    }

    protected override void DrawControl()
    {
        var progUse = LibraryCache.Get(LibraryFile.ProgUse);
        var equip = LibraryCache.Get(LibraryFile.Equip);
        if (progUse == null || equip == null) return;

        // 1. 身体 (ProgUse: 0=男, 1=女)
        DrawLayer(progUse, _gender == MirGender.Male ? 0 : 1, Colors.White, false);

        // 2. 刺客女 + 发型1 的专属帧 (原版在盔甲/武器之前绘制，垫在最底层)
        if (_class == MirClass.Assassin && _gender == MirGender.Female && _hairNumber == 1)
            DrawLayer(progUse, 1160, _hairColour, false);

        // 3. 盔甲 + 武器 (原版: 战士/法师/道士共用 weapon 1042)
        int armour, weapon;
        switch (_class)
        {
            case MirClass.Assassin:
                weapon = 2200;
                armour = _gender == MirGender.Male ? 2000 : 2010;
                break;
            default:
                weapon = 1042;
                armour = _gender == MirGender.Male ? 941 : 951;
                break;
        }
        DrawLayer(equip, armour, Colors.White, false);
        DrawLayer(equip, weapon, Colors.White, false);

        // 4. 盔甲染色 (Overlay, 染 ArmourColour)
        DrawLayer(equip, armour, _armourColour, true);

        // 5. 头发 (Overlay, 染 HairColour)
        if (_hairNumber > 0)
        {
            int hairBase = _class == MirClass.Assassin
                ? (_gender == MirGender.Male ? 1100 : 1120)
                : (_gender == MirGender.Male ? 60 : 80);
            DrawLayer(progUse, hairBase + _hairNumber - 1, _hairColour, true);
        }
    }

    private void DrawLayer(ZlLibrary lib, int frame, Color tint, bool overlay)
    {
        if (lib == null || frame < 0 || frame >= lib.Images.Length) return;
        var img = lib.Images[frame];
        if (img == null) return;
        int w = overlay ? img.OverlayWidth : img.Width;
        int h = overlay ? img.OverlayHeight : img.Height;
        if (w <= 0 || h <= 0) return;
        var texture = overlay ? lib.GetOverlayTexture(frame) : lib.GetImageTexture(frame);
        if (texture == null) return;
        DrawTextureRectRegion(texture,
            new Rect2(PreviewAnchor.X + img.OffSetX, PreviewAnchor.Y + img.OffSetY, w, h),
            new Rect2(0, 0, w, h),
            tint);
    }
}
