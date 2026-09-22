using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>将旧版 GameInter/Interface 帧拉伸到当前窗口尺寸的迁移底图。</summary>
public partial class LegacyUiFrame : DXControl
{
    public LibraryFile LibraryFile { get; set; } = LibraryFile.GameInter;
    public int Index { get; set; } = -1;
    public bool PreserveAspect { get; set; }

    protected override void DrawControl()
    {
        if (Index < 0 || Size.X <= 0 || Size.Y <= 0) return;
        Texture2D texture = MirSkin.GetTexture(LibraryFile, Index);
        if (texture == null) return;
        Rect2 destination = new(Vector2.Zero, Size);
        if (PreserveAspect)
        {
            float scale = Mathf.Min(Size.X / texture.GetWidth(), Size.Y / texture.GetHeight());
            Vector2 scaled = texture.GetSize() * scale;
            destination = new Rect2((Size - scaled) / 2f, scaled);
        }
        DrawTextureRect(texture, destination, false);
    }
}
