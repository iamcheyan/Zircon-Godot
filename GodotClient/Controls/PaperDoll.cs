using System.Linq;
using Godot;
using Library;
using ZirconClient.Scripts;
using ZirconClient.Formats;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Controls;

/// <summary>
/// 角色面板人物纸娃娃 (移植自 Client/Scenes/Views/CharacterDialog.cs
/// CharacterTab_BeforeChildrenDraw, 行 2555-2700)。
///
/// 逐层叠加绘制 (从底到顶):
///   1. ProgUse[1160]  刺客女特殊发型 (Class=Assassin & Female & HairType=1 & 无头盔)
///   2. ProgUse[0男/1女] 裸身肤色
///   3. Equip[costume.Image] 时装 / Equip[armour.Image](+overlay 染色) 衣服
///   4. Equip[weapon.Image](+overlay) 武器
///   5. Equip[shield.Image](+overlay) 盾
///   6. Equip[helmet.Image](+overlay) 头盔
/// 坐标 (130, 270) 相对窗口（原版 CharacterTab_BeforeChildrenDraw 使用的绘制锚点）。
///
/// 数据来源: GameScene.StartInfo (Gender/Class/HairType/HairColour) +
///           GameScene.Equipment (装备数组) + HideBody/HideWeapon (骑马时)。
/// </summary>
public partial class PaperDoll : Control
{
    private const int DollX = 130;
    private const int DollY = 270;

    private ZlLibrary _progUse;
    private ZlLibrary _equip;
    private ZlLibrary _equipEffect;
    private ZlLibrary _gameInter;
    private bool _inspect;
    private MirGender _inspectGender;
    private MirClass _inspectClass;
    private int _inspectHairType;
    private int _inspectFame;
    private System.Drawing.Color _inspectHairColour;
    private ClientUserItem[] _inspectItems;
    private readonly System.Collections.Generic.List<BlendImageLayerNode> _effectLayers = new();

    public void SetInspect(S.Inspect info, ClientUserItem[] items)
    {
        _inspect = info != null;
        if (_inspect)
        {
            _inspectGender = info.Gender;
            _inspectClass = info.Class;
            _inspectHairType = info.Hair;
            _inspectHairColour = info.HairColour;
            _inspectFame = info.Fame;
            _inspectItems = items;
        }
        QueueRedraw();
    }

    public void ClearInspect()
    {
        _inspect = false;
        _inspectItems = null;
        _inspectFame = 0;
        QueueRedraw();
    }

    public PaperDoll()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(DollX, DollY);
        // 给个尺寸避免被裁; 实际靠贴图 OffSet 定位
        Size = new Vector2(180, 220);
    }

    public override void _Ready()
    {
        _progUse = LibraryCache.Get(LibraryFile.ProgUse);
        _equip = LibraryCache.Get(LibraryFile.Equip);
        _equipEffect = LibraryCache.Get(LibraryFile.EquipEffect_UI);
        _gameInter = LibraryCache.Get(LibraryFile.GameInter);
    }

    public override void _Process(double delta)
    {
        if (Visible) QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var layer in _effectLayers) layer.Visible = false;
        var game = GameScene.Game;
        if (game == null) return;
        var info = game.StartInfo;
        if (!_inspect && info == null) return;
        var eq = _inspect ? _inspectItems : game.Equipment;
        if (eq == null) return;

        var gender = _inspect ? _inspectGender : info.Gender;
        var playerClass = _inspect ? _inspectClass : info.Class;
        var hairType = _inspect ? _inspectHairType : info.HairType;
        var hairColour = _inspect ? _inspectHairColour : info.HairColour;

        // 原版 CharacterTab_BeforeChildrenDraw 在人物层最先绘制声望光效。
        // GameInter 的帧号和偏移沿用 FameEffectDecider，动画节拍为 100ms。
        DrawFameEffect(_inspect ? _inspectFame : game.PlayerStats[Stat.Fame]);

        bool hideBody = !_inspect && info.Horse != Library.HorseType.None;   // 骑马时只露头
        bool hideWeapon = hideBody;
        bool hideHead = !_inspect && info.HideHead;

        var weapon = eq[(int)EquipmentSlot.Weapon];
        var armour = eq[(int)EquipmentSlot.Armour];
        var helmet = eq[(int)EquipmentSlot.Helmet];
        var shield = eq[(int)EquipmentSlot.Shield];
        var costume = eq[(int)EquipmentSlot.Costume];

        // 旧版护甲特效在裸身/装备主体之前绘制，且时装存在时不绘制
        // 护甲特效；这是 EquipEffectDecider 的独立 Image + Blend 层。
        if (!hideBody && costume == null && armour != null)
            DrawEquipmentEffect(armour, gender, true);

        // 1. 刺客女特殊发型
        if (!hideBody && playerClass == MirClass.Assassin && gender == MirGender.Female
            && hairType == 1 && helmet == null)
        {
            DrawImage(_progUse, 1160, ToGodot(hairColour));
        }

        // 2. 裸身 (男0/女1)
        if (!hideBody)
        {
            int bodyIndex = gender == MirGender.Male ? 0 : 1;
            DrawImage(_progUse, bodyIndex, Colors.White);
        }

        // 3. 衣服 / 时装
        if (_equip != null)
        {
            if (costume != null)
            {
                DrawImage(_equip, costume.Info.Image, Colors.White);
            }
            else if (armour != null)
            {
                DrawImage(_equip, armour.Info.Image, Colors.White);
                DrawImageOverlay(_equip, armour.Info.Image, ToGodot(armour.Colour));
            }

            // 4. 武器
            if (!hideWeapon && weapon != null)
            {
                DrawImage(_equip, weapon.Info.Image, Colors.White);
                DrawImageOverlay(_equip, weapon.Info.Image, ToGodot(weapon.Colour));
                DrawEquipmentEffect(weapon, gender, false);
            }

            // 5. 盾
            if (!hideWeapon && shield != null)
            {
                DrawImage(_equip, shield.Info.Image, Colors.White);
                DrawImageOverlay(_equip, shield.Info.Image, ToGodot(shield.Colour));
                DrawEquipmentEffect(shield, gender, false);
            }

        // 6. 头盔 / 普通头发。旧版 CharacterTab 在没有头盔时仍按
        // 职业和性别绘制头发；不能只处理刺客女的 1160 特例。
        if (!hideHead && helmet != null)
        {
            DrawImage(_equip, helmet.Info.Image, Colors.White);
            DrawImageOverlay(_equip, helmet.Info.Image, ToGodot(helmet.Colour));
        }
        else if (!hideHead && hairType > 0 && _progUse != null)
        {
            int hairBase = playerClass == MirClass.Assassin
                ? (gender == MirGender.Male ? 1100 : 1120)
                : (gender == MirGender.Male ? 60 : 80);
            DrawImage(_progUse, hairBase + hairType - 1, ToGodot(hairColour));
        }
        }
    }

    /// <summary>画一帧普通图 (用贴图自带 OffSet 定位)。</summary>
    private void DrawImage(ZlLibrary lib, int index, Color colour)
    {
        if (lib == null || index < 0) return;
        var tex = lib.GetImageTexture(index);
        if (tex == null) return;
        var img = lib.Images[index];
        // 贴图 OffSet 是相对"锚点"的; 我们锚点在 Position (130,270)
        Vector2 pos = new(img.OffSetX, img.OffSetY);
        // Godot DrawTextureRect 不支持染色; 用 DrawTexture (单色乘) 替代
        if (colour == Colors.White)
            DrawTextureRect(tex, new Rect2(pos, img.Width, img.Height), false);
        else
            DrawTextureRect(tex, new Rect2(pos, img.Width, img.Height), false, colour);
    }

    /// <summary>画 overlay 层 (原版 ImageType.Overlay 的独立图库帧)。</summary>
    private void DrawImageOverlay(ZlLibrary lib, int index, Color colour)
    {
        if (lib == null || index < 0) return;
        var img = lib.Images[index];
        var tex = lib.GetOverlayTexture(index);
        if (tex == null) return;
        Vector2 pos = new(img.OffSetX, img.OffSetY);
        DrawTextureRect(tex, new Rect2(pos, img.Width, img.Height), false, colour);
    }

    private void DrawEquipmentEffect(ClientUserItem item, MirGender gender, bool behind)
    {
        if (_equipEffect == null || item?.Info == null) return;
        int tick = (int)(Godot.Time.GetTicksMsec() / 100);
        int frame = item.Info.ExteriorEffect switch
        {
            ExteriorEffect.A_GreenFeatherWings => 2100,
            ExteriorEffect.A_RedFeatherWings => 2101,
            ExteriorEffect.A_BlueFeatherWings => 2102,
            ExteriorEffect.A_WhiteFeatherWings => 2103,
            ExteriorEffect.A_AngelicWings => 3000,
            ExteriorEffect.A_BlueAura => gender == MirGender.Male ? 602 : 622,
            ExteriorEffect.A_FlameAura => gender == MirGender.Male ? 601 : 621,
            ExteriorEffect.A_WhiteAura => gender == MirGender.Male ? 600 : 620,
            ExteriorEffect.A_SmallYellowWings => gender == MirGender.Male ? 1800 : 1820,
            ExteriorEffect.A_PurpleTentacles => 2200 + tick % 11,
            ExteriorEffect.A_LionWings => 2300 + tick % 15,
            ExteriorEffect.A_BlueDragonWings => (gender == MirGender.Male ? 2400 : 2500) + tick % 14,
            ExteriorEffect.A_RedWings2 => (gender == MirGender.Male ? 2600 : 2700) + tick % 15,
            ExteriorEffect.A_FlameAura2 => (gender == MirGender.Male ? 1700 : 1720) + tick % 10,
            ExteriorEffect.A_GreenWings => (gender == MirGender.Male ? 400 : 420) + tick % 15,
            ExteriorEffect.A_FlameWings => (gender == MirGender.Male ? 300 : 320) + tick % 15,
            ExteriorEffect.A_BlueWings => (gender == MirGender.Male ? 200 : 220) + tick % 15,
            ExteriorEffect.A_RedSinWings => (gender == MirGender.Male ? 500 : 520) + tick % 13,
            ExteriorEffect.A_FireDragonWings => (gender == MirGender.Male ? 100 : 120) + tick % 10,
            ExteriorEffect.W_ChaoticHeavenBlade => 2000 + tick % 10,
            ExteriorEffect.W_JanitorsScimitar => 1900 + tick % 12,
            ExteriorEffect.W_JanitorsDualBlade => 1920 + tick % 12,
            _ => ResolvePresetEquipmentEffect(item.Info.Image),
        };
        if (frame < 0 || frame >= _equipEffect.Images.Length || _equipEffect.Images[frame] == null) return;
        var img = _equipEffect.Images[frame];
        // EquipEffect_UI is selected as ImageType.Image by the legacy
        // EquipEffectDecider; its black pixels are valid UI art, not an
        // effect color key.
        if (_equipEffect.GetImageTexture(frame) == null) return;

        BlendImageLayerNode layer = null;
        foreach (var candidate in _effectLayers)
        {
            if (!candidate.Visible) { layer = candidate; break; }
        }
        if (layer == null)
        {
            layer = new BlendImageLayerNode();
            _effectLayers.Add(layer);
            AddChild(layer);
        }
        layer.Configure(_equipEffect, frame, new Color(1f, 1f, 1f, 0.8f), behind ? -1 : 1);
    }

    private void DrawFameEffect(int fame)
    {
        if (_gameInter == null || fame <= 0) return;
        int frame = 0, count = 0, offsetX = 0, offsetY = 0;
        switch (fame)
        {
            case 1: frame = 1870; count = 10; break;
            case 2: frame = 1890; count = 10; break;
            case 3: frame = 1910; count = 11; break;
            case 4: frame = 1930; count = 10; break;
            case 5: frame = 1950; count = 10; break;
            case 6: frame = 1970; count = 10; offsetX = -11; offsetY = -10; break;
            case 7: frame = 1990; count = 12; offsetX = -17; offsetY = -15; break;
            case 8: frame = 2270; count = 18; offsetX = -7; offsetY = -5; break;
            case 9: frame = 2250; count = 18; offsetX = -7; offsetY = -5; break;
            default: return;
        }
        int index = frame + (int)(Godot.Time.GetTicksMsec() / 100) % count;
        if (index < 0 || index >= _gameInter.Images.Length || _gameInter.Images[index] == null) return;
        var image = _gameInter.Images[index];
        var texture = _gameInter.GetImageTexture(index);
        if (texture == null) return;

        // CharacterTab 的人物锚点为 (130,270)，而本节点锚点是 (130,315)，
        // 因此把原版声望坐标 (257,76) 换算成本节点局部坐标。
        var position = new Vector2(127 + offsetX + image.OffSetX, -239 + offsetY + image.OffSetY);
        DrawTextureRect(texture, new Rect2(position, image.Width, image.Height), false,
            new Color(1f, 1f, 1f, 0.8f));
    }

    private static int ResolvePresetEquipmentEffect(int image)
        => image switch
        {
            942 => 700, 952 => 720,
            961 => 1600, 971 => 1620,
            982 => 800, 992 => 820,
            983 => 1200, 993 => 1220,
            984 => 1100, 994 => 1120,
            1022 => 900, 1032 => 920,
            1023 => 1300, 1033 => 1320,
            1002 => 1000, 1012 => 1020,
            1003 => 1400, 1013 => 1420,
            _ => -1,
        };

    private static Color ToGodot(System.Drawing.Color c)
        => new Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
}
