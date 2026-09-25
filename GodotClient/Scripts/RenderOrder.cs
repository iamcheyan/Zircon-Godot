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
    // 四个槽位必须独立；用三个槽位时对象特效槽会等于下一行的中层地形槽。
    public const int RowStride = 4;
    // 原版 MapControl 把 DrawType.Floor 的特效画在 FLayer 底色与所有地形行
    // 之间（先于一切行列的地形和对象）。这里取 0 与底色同组：地形行从
    // TerrainMiddle(0)=0 起每行 +4，对象从每行 +2 起，因此行 0 的贴图会
    // 盖住地板特效的底座（火焰从地面升起），行 1+ 的地形和所有对象都在它
    // 之上——与旧端的绘制顺序一致。曾用常量 50，恰好落在视野中部：行 13+
    // 的地形盖住地板特效，行 0-12 的对象反而被地板特效压住。
    public const int FloorEffects = 0;
    public const int LocalPlayer = 3200;
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

    public static int TerrainMiddle(int renderY) => TerrainBase + Math.Clamp(renderY, 0, 1000) * RowStride;
    public static int TerrainFront(int renderY) => TerrainMiddle(renderY) + 1;
    public static int Object(int renderY) => TerrainMiddle(renderY) + 2;
    public static int ObjectEffect(int renderY) => TerrainMiddle(renderY) + 3;
    // Legacy MapControl draws the local player's target effects after all
    // particle emitters, so keep this above Particles.
    public static int LocalPlayerEffect => Particles + 1;
}
