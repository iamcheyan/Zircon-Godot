using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 贴图控件 (移植自 Client/Controls/DXImageControl.cs)。
/// 用 .Zl 图库的一张图绘制自己; 支持普通/悬停/按下三态与偏移。
/// </summary>
public partial class DXImageControl : DXControl
{
    private ShaderMaterial _blendMaterial;
    private ShaderMaterial _grayMaterial;
    private ShaderMaterial _grayBlendMaterial;
    public LibraryFile LibraryFile = LibraryFile.Interface;

    private int _index = -1;
    public int Index
    {
        get => _index;
        set
        {
            if (_index == value) return;
            _index = value;
            QueueRedraw();
            if (!FixedSize)
                Size = MirSkin.GetSize(LibraryFile, value);
        }
    }

    public int HoverIndex = -1;
    public int PressedIndex = -1;

    /// <summary>true: 尺寸固定为 Size, 不随图自动调整 (贴图偏移仍生效)</summary>
    public bool FixedSize;

    /// <summary>
    /// true 时把完整贴图拉伸到控件 Size。旧版网页模拟器的普通窗口背景
    /// 使用 object-fit: fill；FixedSize 本身只固定控件尺寸，不会改变绘制尺寸。
    /// 默认 false，避免影响现有精灵和普通 UI 控件。
    /// </summary>
    public bool StretchImage;

    public bool DrawImage = true;

    /// <summary>原版 Blend 标记；图库 UI 光效使用高亮混合。</summary>
    public bool Blend;

    /// <summary>绘制图库的 Overlay 层，而不是普通 Image 层。</summary>
    public bool UseOverlayTexture;

    /// <summary>true: 绘制时加上图的 OffSetX/OffSetY 偏移（原版默认 false）</summary>
    public bool UseOffSet;

    public float ImageOpacity = 1f;
    public bool GrayScale;

    public override void _Ready()
    {
        // 原版 DXWindow/DXImageControl 对 Interface[15] 关闭按钮统一提供
        // CommonControlClose Hint；窗口若有更具体提示，可在此之前覆盖。
        if (LibraryFile == LibraryFile.Interface && Index == 15 && string.IsNullOrEmpty(TooltipText))
            TooltipText = Lang.CommonControlClose;
        base._Ready();
    }

    protected override void DrawControl()
    {
        if (!DrawImage) return;

        int idx = GetCurrentIndex();
        if (idx < 0) return;

        var tex = UseOverlayTexture
            ? MirSkin.GetOverlayTexture(LibraryFile, idx)
            : MirSkin.GetTexture(LibraryFile, idx);
        if (tex == null) return;

        // 旧版 DXImageControl 的 Blend 标记通过
        // RenderingPipelineManager.SetBlend(true, ImageOpacity, BlendMode)
        // 绘制：ImageOpacity 落在 NORMAL 混合的 blendRate 参数上并被忽略
        // （AppliesBlendRateToVertexColour 不含 NORMAL），顶点 Alpha 保持
        // ForeColour 全不透明。只有非 Blend 路径 SetOpacity(ImageOpacity)
        // 才会衰减顶点 Alpha。
        if (GrayScale)
        {
            Material = Blend ? (_grayBlendMaterial ??= CreateGrayMaterial(true))
                : (_grayMaterial ??= CreateGrayMaterial(false));
        }
        else
        {
            Material = Blend ? (_blendMaterial ??= LegacyBlendMaterial.Create()) : null;
        }

        Vector2I off = UseOffSet ? MirSkin.GetOffset(LibraryFile, idx) : Vector2I.Zero;
        Rect2 dest = StretchImage
            ? new Rect2(Vector2.Zero, Size)
            : new Rect2(off, tex.GetSize());

        // 旧版 PresentTexture 会把 ForeColour（禁用时为灰色）直接乘到
        // 贴图，并把 ImageOpacity 作为源 Alpha；在贴图上再盖半透明灰块
        // 会改变透明边缘和黑色有效像素，不能作为等价实现。
        Color tint = IsEnabled ? ForeColour : new Color(0.29f, 0.29f, 0.29f, 1f);
        if (!Blend)
            tint.A *= Mathf.Clamp(ImageOpacity, 0f, 1f);
        // 灰度 shader 用 uniform vcolor 接收顶点色（COLOR 已被模板预乘 texel，
        // 无法无损恢复 Col；见 CreateGrayMaterial 注释）。tint 每次绘制可变
        // （禁用变灰、非 Blend 衰减 ImageOpacity），必须每帧写入。
        if (GrayScale && Material is ShaderMaterial sm)
            sm.SetShaderParameter("vcolor", tint);
        DrawTextureRect(tex, dest, false, tint);
    }

    internal static ShaderMaterial CreateGrayMaterial(bool blend)
    {
        // 原版 GrayscaleD3D11.hlsl（默认管线）：
        //   gray = dot(texel.rgb, (0.299, 0.587, 0.114))   // 直通 texel
        //   out.rgb = gray * Col.rgb * texel.a * Col.a     // 预乘两个 Alpha
        //   out.a   = texel.a * Col.a
        // Blend 变体再叠加 NORMAL 屏幕混合 out = src*(1-dst)+dst。
        //
        // Godot 4 canvas 管线实测（MapTestScene 混合审计，字节级验证）：
        //   1. texture(TEXTURE, UV) 返回直通 texel（rgb 与 a 均未预乘）。
        //   2. 片元 shader 的 COLOR 输入 = 顶点色 × 贴图：canvas 模板在用户
        //      代码之前执行 color *= texture(color_texture, uv)。即
        //      COLOR = Col × texel —— 旧实现 0.125 而非 0.25 的根因：
        //      source = l*COLOR.rgb*texel.a*COLOR.a 把 texel.rgb 与 texel.a
        //      各多乘了一次（灰 texel 0.5/白顶点色 → 0.25×0.5×0.5 = 0.0625
        //      → 0.125）。
        //   3. screen_texture = 本 item 绘制前的同帧帧缓冲；默认 MIX 的混合
        //      因子是 shader 输出的 COLOR.a（不是 TEXTURE alpha）。本 shader
        //      输出 alpha=1 → 因子=1 → 写出的颜色就是 COLOR.rgb，字节级可验证。
        //   4. 原版 Col 无法从 COLOR 无损恢复（texel.rgb 含 0 通道时除法
        //      发散且信息已丢失），必须用 uniform vcolor 显式传入顶点色；
        //      DrawControl 每次绘制前写入 tint（见下）。
        // 因此：src.rgb = l * vcolor.rgb * texel.a * vcolor.a = 原版精确数学。
        // 非 blend 变体（普通 alpha 混合 out = src + dst*(1-src.a)）同样输出
        // alpha=1、在 shader 内用 screen_texture 完成 dst 项：实测 Godot 对
        // alpha<1 的 shader 输出混入额外预乘（常量 a=0.5 的 shader 在黑底上
        // 渲染出 0.75 而非 0.5），无法按标准公式预测；alpha=1 + 显式 dst 项
        // 则字节级可验证。
        var shader = new Shader
        {
            Code = "shader_type canvas_item;\nuniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;\nuniform vec4 vcolor = vec4(1.0);\n" +
                "void fragment() {\n" +
                "    vec4 texel = texture(TEXTURE, UV);\n" +
                "    float l = dot(texel.rgb, vec3(0.299, 0.587, 0.114));\n" +
                (blend
                    ? "    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n" +
                      "    vec3 source = vec3(l) * vcolor.rgb * texel.a * vcolor.a;\n" +
                      "    vec3 out_rgb = destination.rgb + source * (vec3(1.0) - destination.rgb);\n" +
                      "    COLOR = vec4(out_rgb, 1.0);\n"
                    : "    vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);\n" +
                      "    float a = texel.a * vcolor.a;\n" +
                      "    vec3 source = vec3(l) * vcolor.rgb * texel.a * vcolor.a;\n" +
                      "    COLOR = vec4(source + destination.rgb * (1.0 - a), 1.0);\n") +
                "}"
        };
        return new ShaderMaterial { Shader = shader };
    }

    /// <summary>当前应显示的图索引 (按下 > 悬停 > 普通)</summary>
    public int GetCurrentIndex()
    {
        if (IsPressed && PressedIndex >= 0) return PressedIndex;
        if (IsHovered && HoverIndex >= 0) return HoverIndex;
        return Index;
    }
}
