using System;

namespace ZirconClient.Scripts;

/// <summary>
/// Global canvas ordering equivalent to Client/Scenes/Views/MapControl.DrawObjects.
/// One map row is: middle terrain, front terrain, objects, object effects.
/// The local player is drawn by the legacy client after every map object.
/// </summary>
public static class RenderOrder
{
    // Godot CanvasItem.ZIndex is limited to [-4096, 4096].  The previous
    // values copied the old client's unrestricted painter-order integers and
    // were rejected by Godot at runtime, which could make the layer order
    // undefined.  The visible map range only needs a compact relative order.
    public const int TerrainBase = 0;
    // 原版 MapControl 把 DrawType.Floor 的特效画在 FLayer 底色与所有地形行
    // 之间（先于一切行列的地形和对象）。这里取 0 与底色同组：中层地形从
    // TerrainMiddle(0)=0 起按地图行递增，因此行 0 的贴图会盖住地板特效的
    // 底座（火焰从地面升起），行 1+ 的地形和所有对象都在它
    // 之上——与旧端的绘制顺序一致。曾用常量 50，恰好落在视野中部：行 13+
    // 的地形盖住地板特效，行 0-12 的对象反而被地板特效压住。
    public const int FloorEffects = 0;
    // 自身持续 Buff 光环/护盾在角色身体后方，避免特效帧的透明键或矩形
    // 覆盖腿部；仍高于所有地图前景地形。
    public const int PlayerBuffEffect = 3198;
    public const int Particles = 3300;
    public const int FinalEffects = 3400;
    // 原版 MapControl.OnBeforeDraw 在 DrawObjects()(地形+对象+天气粒子)
    // 之后才绘制 LLayer 光纹理做全屏合成: 夜晚环境光应覆盖包括对象在内的
    // 全部世界内容, 光源光斑再在覆盖层上恢复亮度。此处取 FinalEffects 之后
    // 最后一个世界槽位。光照层实际挂在独立 CanvasLayer(Layer=1), 在世界
    // 画布完整绘制后触发一次新的 hint_screen_texture 整屏拷贝, 采样必然
    // 完整——世界画布内首个 screen_texture 用户(地形 Blend 行/施法特效,
    // 低 ZIndex)会劫持拷贝点, 只靠本槽位无法保证采样含全部对象。
    public const int LightOverlay = FinalEffects + 1;

    // 中层地面是脚下底图，不能因为它属于更靠后的地图行就把对象整层
    // 压住。Godot 每个对象是独立 CanvasItem，而旧端是在同一张画布里逐像素
    // 合成；把中层地面单独放在前景/对象之前，避免怪物被“地板贴土”截腰。
    public static int TerrainMiddle(int renderY) => TerrainBase + Math.Clamp(renderY, 0, 1000);
    // 原版 MapControl.DrawObjects 的真实顺序是：中层地面 -> 前景树/悬崖
    // -> 该行对象 -> 该行对象特效。前景贴图必须先于同一 RenderY 的对象，
    // 但其透明区域不会遮挡对象；如果把前景放到对象之后，悬崖/树根会错误
    // 地盖住人物和怪物的腿（见 Client/Scenes/Views/MapControl.cs）。
    private static int Row(int renderY) => Math.Clamp(renderY, 0, 1000);
    public static int TerrainFront(int renderY) => 1001 + Row(renderY) * 3;
    public static int Object(int renderY) => 1002 + Row(renderY) * 3;
    // MapView.CellToScreen(..., objectBaseline:true) 与地图中/前景的 drawY
    // 都落在 CellY+1 的脚底基线。对象的深度也必须使用同一基线，否则对象
    // 虽然画在下一行，Z 顺序却仍停留在上一行，前景切口会错误压住脚和影子。
    public static int ObjectAtFoot(int renderY) => Object(renderY + 1);
    public static int ObjectEffect(int renderY) => 1003 + Row(renderY) * 3;
    public static int ObjectEffectAtFoot(int renderY) => ObjectEffect(renderY + 1);
    // Legacy MapControl draws the local player's target effects after all
    // particle emitters, so keep this above Particles.
    public static int LocalPlayerEffect => Particles + 1;
}
