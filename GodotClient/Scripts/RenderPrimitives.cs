using Godot;
using ZirconClient.Controls;
using ZirconClient.Formats;

namespace ZirconClient.Scripts;

/// <summary>
/// 小型的世界绘制原语。世界使用逻辑 48x32 格，外层 GameScene 再统一放大，
/// 所以这里不能把 UI 的缩放值混进来。
/// </summary>
internal static class RenderPrimitives
{
    // Some old ZL frames contain a shadow record, but the record is only a
    // placeholder (or a damaged/empty payload).  Drawing those textures as-is
    // produces the characteristic long 1-pixel line under an object.
    public static bool IsUsableResourceShadow(int width, int height)
    {
        if (width <= 0 || height <= 0) return false;

        // Real Mir ground shadows are short, flat shapes.  A very thin or
        // extremely wide rectangle is metadata/payload noise, not a shadow.
        return height >= 3 && width <= height * 8;
    }

    public static bool IsUsableResourceShadow(Texture2D texture, int metadataWidth, int metadataHeight)
    {
        if (!IsUsableResourceShadow(metadataWidth, metadataHeight) || texture == null)
            return false;
        int textureWidth = texture.GetWidth();
        int textureHeight = texture.GetHeight();
        return IsUsableResourceShadow(textureWidth, textureHeight)
            && textureWidth == metadataWidth
            && textureHeight == metadataHeight;
    }

    /// <summary>
    /// MirLibrary.Draw(ImageType.Shadow) 的旧 ZL fallback。Shadow payload
    /// 不可用时，原版按 ShadowType 从主体帧生成投影，而不是直接跳过。
    /// </summary>
    public static bool DrawShadowTypeFallback(CanvasItem canvas, Texture2D texture,
        ZlImage image, float alpha = 0.5f, Vector2? localOffset = null)
    {
        if (canvas == null || texture == null || image == null) return false;
        Vector2 extra = localOffset ?? Vector2.Zero;
        float x = image.ShadowOffSetX + extra.X;
        float y = image.ShadowOffSetY + extra.Y;

        if (image.ShadowType == 50)
        {
            canvas.DrawTextureRect(texture, new Rect2(x, y, image.Width, image.Height), false,
                new Color(0f, 0f, 0f, alpha));
            return true;
        }

        if (image.ShadowType is not (49 or 176 or 177)) return false;

        // Matrix3x2.CreateScale(1, .5) with M21=-.5 followed by
        // Translation(x + image.Height/2, y), as in the original renderer.
        float tx = x + image.Height * 0.5f;
        Vector2 Transform(Vector2 p) => new(tx + p.X - p.Y * 0.5f, y + p.Y * 0.5f);
        float right = image.Width;
        float bottom = image.Height;
        var points = new[]
        {
            Transform(new Vector2(0f, 0f)), Transform(new Vector2(right, 0f)),
            Transform(new Vector2(right, bottom)), Transform(new Vector2(0f, bottom)),
        };
        var uvs = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f),
        };
        canvas.DrawPolygon(points, new[] { new Color(0f, 0f, 0f, alpha) }, uvs, texture);
        return true;
    }

    /// <summary>
    /// 原版 PlayerObject.DrawShadow2：把当前帧的角色轮廓沿等距地面
    /// 斜切并压扁到脚底。这样没有 Shadow 通道时仍保留人物/怪物本身
    /// 的真实轮廓，不会把所有对象退化成同一个椭圆。
    /// </summary>
    public static bool DrawSilhouetteShadow(CanvasItem canvas, Texture2D texture,
        ZlImage image, float alpha = 0.5f, Vector2? localOffset = null,
        ZlImage anchorImage = null)
    {
        if (canvas == null || texture == null || image == null || image.Width <= 0 || image.Height <= 0)
            return false;

        float left = image.OffSetX;
        float top = image.OffSetY;
        float right = left + image.Width;
        float bottom = top + image.Height;

        // 原版 PlayerObject.DrawShadow2 把角色 scratch 纹理以
        // Matrix3x2(1, 0, -0.5, 0.5) 投影到地面。其平移不是固定的
        // (12,16)，而是逐帧使用 image.Height 与 ShadowOffSetX/Y：
        // x' = x - y/2 + Height/2 + ShadowOffSetX
        // y' = y/2 + ShadowOffSetY
        // 这样不同体型、装备和动作帧的影子都落在各自真实脚底。
        // The old client first composites body/equipment into one scratch
        // surface, then uses the body's ShadowOffset/height as the common
        // foot anchor.  Passing an anchorImage preserves that relationship
        // when Godot draws the source layers separately.
        anchorImage ??= image;
        Vector2 extra = localOffset ?? Vector2.Zero;
        Vector2 Transform(Vector2 p) => new(
            (p.X - anchorImage.OffSetX) - (p.Y - anchorImage.OffSetY) * 0.5f
                + anchorImage.Height * 0.5f + anchorImage.ShadowOffSetX + extra.X,
            (p.Y - anchorImage.OffSetY) * 0.5f + anchorImage.ShadowOffSetY + extra.Y);

        var points = new[]
        {
            Transform(new Vector2(left, top)), Transform(new Vector2(right, top)),
            Transform(new Vector2(right, bottom)), Transform(new Vector2(left, bottom)),
        };
        var uvs = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f),
        };
        canvas.DrawPolygon(points, new[] { new Color(0f, 0f, 0f, alpha) }, uvs, texture);
        return true;
    }

    public static void DrawLabel(CanvasItem canvas, string text, Vector2 baseline,
        Color colour, float size = 10f)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Font font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        if (font == null) return;
        // 世界对象不挂在 UI 缩放层；这里不能使用 UI 的反向缩放字号，
        // 否则窗口变大后角色名会被错误缩小。世界名称直接使用固定屏幕字号。
        int drawSize = MirSkin.PhysicalSize((int)size);
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, drawSize);
        Vector2 p = baseline - new Vector2(extent.X * 0.5f, 0f);
        canvas.DrawString(font, p + new Vector2(1f, 1f), text,
            HorizontalAlignment.Left, -1f, drawSize, new Color(0f, 0f, 0f, 0.85f));
        canvas.DrawString(font, p, text, HorizontalAlignment.Left, -1f, drawSize, colour);
    }

    public static float MeasureLabelWidth(string text, float size = 10f)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;
        Font font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        if (font == null) return 0f;
        return font.GetStringSize(text, HorizontalAlignment.Left, -1, MirSkin.PhysicalSize((int)size)).X;
    }

    public static void DrawLabelWithBackground(CanvasItem canvas, string text, Vector2 baseline,
        Color colour, float size = 10f,
        Color background = default, Color border = default)
    {
        if (canvas == null || string.IsNullOrWhiteSpace(text)) return;
        Font font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        if (font == null) return;
        int drawSize = MirSkin.PhysicalSize((int)size);
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, drawSize);
        const float padX = 4f, padY = 2f;
        Vector2 topLeft = baseline - new Vector2(extent.X * 0.5f + padX, font.GetAscent(drawSize) + padY);
        Vector2 boxSize = new(extent.X + padX * 2f, font.GetHeight(drawSize) + padY * 2f);
        Color fill = background == default ? new Color(0.02f, 0.02f, 0.02f, 0.82f) : background;
        Color line = border == default ? new Color(0.95f, 0.78f, 0.28f, 0.9f) : border;
        canvas.DrawRect(new Rect2(topLeft, boxSize), fill, true);
        canvas.DrawRect(new Rect2(topLeft, boxSize), line, false, 1f);
        Vector2 p = baseline - new Vector2(extent.X * 0.5f, 0f);
        canvas.DrawString(font, p + new Vector2(1f, 1f), text,
            HorizontalAlignment.Left, -1f, drawSize, new Color(0f, 0f, 0f, 0.95f));
        canvas.DrawString(font, p, text, HorizontalAlignment.Left, -1f, drawSize, colour);
    }

    /// <summary>
    /// 原版 MapObject.DrawName 的基线：NameLabel 的顶部是
    /// DrawY - (32 - labelHeight) / 2 - 6，节点原点对应 DrawX/DrawY。
    /// Godot DrawString 接收基线，因此这里把旧版顶部坐标换算成基线。
    /// </summary>
    public static float OriginalNameBaseline(float size = 9f)
    {
        Font font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        float drawSize = MirSkin.PhysicalSize((int)size);
        float height = font?.GetHeight((int)drawSize) ?? drawSize;
        return -(32f - height) / 2f - 6f + height;
    }

    /// <summary>
    /// 地图对象名称统一放在头顶血条上方。血条背景的旧版锚点是 y=-55，
    /// 名称基线放在其上方并为公会名/宠物归属名预留两行。
    /// </summary>
    public static float NameAboveHealthBarBaseline(float size = 9f) => -61f;
}
