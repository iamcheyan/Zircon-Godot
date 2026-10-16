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

    // 每行 4 个槽位，严格照原版 Client/Scenes/Views/MapControl.cs 的逐行画家
    // 顺序（DrawObjects 的 for y 循环内）：
    //     middle(y) → front(y) → 该行对象 → 该行对象特效 → middle(y+1) → …
    // 由此得到两条用户可见规则，缺一不可：
    //   1) middle(y) < object(y)  ：自己脚下的中层底图不压自己（否则"地砖截腰"）
    //   2) object(y) < middle(y+1)：**前面那一行的中层/前景会盖住本行角色**
    //      —— 树林、墙体遮挡角色的来源。棕榈树就是中层整帧
    //      （`smobjectsc[943]` 48×480，锚点 4 行之外，树身正好穿过上游角色）。
    // 历史教训（两次都改坏过这里，勿再动）：
    //   · 212cc387 把中层单独压到所有对象之下（TerrainMiddle=row<1001），
    //     于是行 y+1 的中层永远画在行 y 角色之前 → 棕榈树挡不住人；
    //   · 0d20010d 把对象整体挪到 Object(renderY+1)（ObjectAtFoot），
    //     于是行 y+1 的前景层也画在角色之前 → 前景树/悬崖切不住人。
    private static int Row(int renderY) => Math.Clamp(renderY, 0, 1000);
    public static int TerrainMiddle(int renderY) => TerrainBase + Row(renderY) * 4;
    public static int TerrainFront(int renderY) => TerrainMiddle(renderY) + 1;
    public static int Object(int renderY) => TerrainMiddle(renderY) + 2;
    public static int ObjectEffect(int renderY) => TerrainMiddle(renderY) + 3;
    // 世界层之上（对象/特效之后）：Row 上限 1000 → 行带最大 4003，必须留在
    // 这些槽位之下，也要留在 Godot CanvasItem.ZIndex 上限 4096 之下。
    // 自身 Buff 光环/护盾在角色身体后方（由 MirEffectNode 按角色行取
    // ObjectEffect(player.RenderY)，不落在这个常量上）。
    public const int PlayerBuffEffect = 4050;
    public const int Particles = 4060;
    public const int FinalEffects = 4070;
    // 原版 MapControl.OnBeforeDraw 在 DrawObjects()（地形+对象+天气粒子）之后
    // 才绘制 LLayer 光纹理做全屏合成：夜晚环境光应覆盖包括对象在内的全部世界
    // 内容。此处取世界画布最后一个槽位。
    public const int LightOverlay = 4071;
    // Legacy MapControl draws the local player's target effects after all
    // particle emitters, so keep this above Particles.
    public static int LocalPlayerEffect => Particles + 1;
}
