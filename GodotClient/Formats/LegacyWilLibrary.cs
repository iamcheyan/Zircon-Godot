using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace ZirconClient.Formats;

/// <summary>
/// Read-only loader for the original Mir3 EI WIL/WIX image format.
/// In EI UI mode it provides the original source frames; converted .Zl files
/// remain the fallback for libraries without a matching WIL/WIX pair.
/// </summary>
public sealed class LegacyWilLibrary : IDisposable
{
    private const int WixHeaderSize = 26;
    private const int WixMagic = 0xB13A;
    private const int WilFrameHeaderSize = 17;

    private readonly byte[] _wilData;
    private readonly int[] _frameOffsets;
    private readonly Dictionary<int, ImageTexture> _textures = new();
    // 特效颜色键纹理（黑=透明），与 .Zl 侧 `ZlLibrary.GetEffectTexture` 同语义。
    private readonly Dictionary<int, ImageTexture> _effectTextures = new();
    private readonly Dictionary<int, (int Width, int Height, int OffsetX, int OffsetY)> _headers = new();

    public string WilPath { get; }
    public int Count => _frameOffsets.Length;

    public LegacyWilLibrary(string wilPath, string wixPath)
    {
        WilPath = wilPath ?? throw new ArgumentNullException(nameof(wilPath));
        if (string.IsNullOrWhiteSpace(wixPath)) throw new ArgumentNullException(nameof(wixPath));

        _wilData = File.ReadAllBytes(wilPath);
        byte[] wixData = File.ReadAllBytes(wixPath);
        int indexOffset = GetIndexOffset(wixData);
        int count = (wixData.Length - indexOffset) / sizeof(int);
        _frameOffsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            uint offset = ReadUInt32(wixData, indexOffset + i * sizeof(int));
            _frameOffsets[i] = offset <= int.MaxValue ? (int)offset : -1;
        }
    }

    public Vector2I GetSize(int index)
    {
        if (!TryGetHeader(index, out var header)) return Vector2I.Zero;
        return new Vector2I(header.Width, header.Height);
    }

    public Vector2I GetOffset(int index)
    {
        if (!TryGetHeader(index, out var header)) return Vector2I.Zero;
        return new Vector2I(header.OffsetX, header.OffsetY);
    }

    public ImageTexture GetImageTexture(int index)
    {
        if (index < 0 || index >= _frameOffsets.Length) return null;
        if (_textures.TryGetValue(index, out ImageTexture cached)) return cached;
        if (!TryGetHeader(index, out var header)) return null;

        int frameOffset = _frameOffsets[index];
        int pixelWordCount = ReadInt32(_wilData, frameOffset + 13);
        if (pixelWordCount <= 0) return null;

        int compressedByteCount;
        try { compressedByteCount = checked(pixelWordCount * sizeof(ushort)); }
        catch (OverflowException) { return null; }
        int compressedStart = frameOffset + WilFrameHeaderSize;
        if (!HasRange(_wilData, compressedStart, compressedByteCount)) return null;

        byte[] rgba = DecodeRgb565Rle(_wilData, compressedStart, compressedByteCount, header.Width, header.Height);
        if (rgba == null) return null;

        Image image = Image.CreateFromData(header.Width, header.Height, false, Image.Format.Rgba8, rgba);
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        _textures[index] = texture;
        return texture;
    }

    /// <summary>
    /// 特效帧纹理：按原客户端的**黑色透明键**把近黑像素清成透明，并让键色之外的
    /// 暗部辉光按亮度折算成半透明。
    /// 元素特效帧（法师男 F1080 火球、法师女 F1385 闪电、道士女 F1984 光球）
    /// 四周都有一圈**不透明纯黑**（F1080 实测 13999 个不透明像素里 6797 个是
    /// alpha=255 / RGB&lt;12 的黑），普通 Image 层会把它当实体整块盖住底下的人物。
    /// 纯黑之外还有一圈**暗色辉光**（F1080 残留 4365 像素，(40,16,0)…(80,28,0)）
    /// 逐通道都 &gt; 32，键色抠不掉，会变成一块不透明暗斑盖住场景；故再按亮度
    /// 线性折算 alpha（见循环内注释）。
    /// 与 `ZlLibrary.GetEffectTexture` 同语义。
    public ImageTexture GetEffectTexture(int index)
    {
        if (index < 0 || index >= _frameOffsets.Length) return null;
        if (_effectTextures.TryGetValue(index, out ImageTexture cached)) return cached;
        ImageTexture baseTexture = GetImageTexture(index);
        if (baseTexture == null) return null;

        Image source = baseTexture.GetImage();
        byte[] pixels = source.GetData();
        if (pixels == null || pixels.Length < 4) return baseTexture;
        // 原版透明键：逐通道 <= 32 视为键色（与 ZL 侧
        // `ZlReader.EffectTransparentKeyTolerance = 32` 同一判据）。
        const byte KeyTolerance = 32;
        // 辉光保留区间：带**色彩**的暗像素（青光/蓝光的柔和外圈）要留下并按
        // 亮度渐变，而不是被键色一刀切掉。
        // 依据（逐帧解码 Interface1c.wil）：
        //   F1385 512x256（法师女 +40 特效）52119 个不透明像素中，
        //     **46519 个是近灰黑**（饱和度 max(RGB)-min(RGB) < 20，mean≈(3,4,9)），
        //     **5600 个带色**（青光，mean≈(87,117,115)）。
        //   F1080（法师男）同理：6797 纯黑 + 4365 暗色辉光 (40,16,0)…(80,28,0)。
        // 原版靠绘制顺序让辉光落在人物上被挡住；落到空处就露馅。旧实现按
        // **亮度**线性折算 alpha（F1385 剩 978/52119、青色剩 61，光球几乎被抹平，
        // 边缘硬切 —— 就是用户报的「创建人物时青光周围没有黑色半透明、很难看」），
        // 改为按**饱和度**判定键色：灰黑抠掉，带色辉光保留。
        const int GrayChromaTolerance = 24;   // max-min < 24 视为无彩色（键色）
        const int GlowFloor = 32;             // 低于此亮度完全透出
        const int GlowCeiling = 255;          // 达到即保持原 alpha
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            int red = pixels[i], green = pixels[i + 1], blue = pixels[i + 2];
            int level = Math.Max(red, Math.Max(green, blue));
            int chroma = level - Math.Min(red, Math.Min(green, blue));

            // (1) 近黑 → 完全键色，透出。
            if (red <= KeyTolerance && green <= KeyTolerance && blue <= KeyTolerance)
            {
                pixels[i + 3] = 0;
                continue;
            }
            // (2) 无彩色（灰/黑）→ 仍是键色，透出。带彩色的辉光不走这里。
            if (chroma < GrayChromaTolerance)
            {
                pixels[i + 3] = 0;
                continue;
            }
            // (3) 带色辉光：按亮度渐变 alpha，核心保持实、外围渐隐。
            if (level <= GlowFloor)
            {
                pixels[i + 3] = 0;
                continue;
            }
            if (level >= GlowCeiling) continue;
            int span = GlowCeiling - GlowFloor;
            int scaled = span <= 0 ? 0 : (level - GlowFloor) * 255 / span;
            pixels[i + 3] = (byte)Math.Min(pixels[i + 3], Math.Max(scaled, 0));
        }
        ImageTexture texture = ImageTexture.CreateFromImage(
            Image.CreateFromData(source.GetWidth(), source.GetHeight(), false, Image.Format.Rgba8, pixels));
        _effectTextures[index] = texture;
        return texture;
    }

    private bool TryGetHeader(int index, out (int Width, int Height, int OffsetX, int OffsetY) header)
    {
        header = default;
        if (index < 0 || index >= _frameOffsets.Length) return false;
        if (_headers.TryGetValue(index, out header)) return header.Width > 0 && header.Height > 0;

        int offset = _frameOffsets[index];
        if (offset <= 0 || !HasRange(_wilData, offset, WilFrameHeaderSize)) return false;
        int width = ReadInt16(_wilData, offset);
        int height = ReadInt16(_wilData, offset + 2);
        int offsetX = ReadInt16(_wilData, offset + 4);
        int offsetY = ReadInt16(_wilData, offset + 6);
        if (width <= 0 || height <= 0) return false;
        header = (width, height, offsetX, offsetY);
        _headers[index] = header;
        return true;
    }

    private static int GetIndexOffset(byte[] wixData)
    {
        if (wixData.Length < 24) throw new InvalidDataException("WIX header is truncated.");
        if (wixData.Length >= WixHeaderSize + sizeof(ushort)
            && ReadUInt16(wixData, WixHeaderSize) == WixMagic)
            return WixHeaderSize + sizeof(ushort);
        return 24;
    }

    private static byte[] DecodeRgb565Rle(byte[] file, int dataStart, int dataLength, int width, int height)
    {
        int pixelCount;
        try { pixelCount = checked(width * height); }
        catch (OverflowException) { return null; }
        if (width > short.MaxValue || height > short.MaxValue || pixelCount <= 0
            || pixelCount > int.MaxValue / 4) return null;

        byte[] rgba = new byte[pixelCount * 4];
        int dataEnd = dataStart + dataLength;
        int rowWord = 0;
        int streamWord = 0;

        // EI stores each scanline's pixels in display order. GameInter WIL/ZL
        // frame cross-checks confirm that this is already Godot's top-to-bottom
        // row order; the legacy converter's two vertical reversals cancel.
        for (int y = 0; y < height; y++)
        {
            int rowHeader = dataStart + streamWord * sizeof(ushort);
            if (rowHeader < dataStart || rowHeader + sizeof(ushort) > dataEnd) return null;
            int rowEnd = rowWord + ReadUInt16(file, rowHeader);
            if (rowEnd < rowWord + 1) return null;

            streamWord++;
            int commandWord = streamWord;
            int x = 0;
            while (commandWord < rowEnd)
            {
                int commandOffset = dataStart + commandWord * sizeof(ushort);
                if (commandOffset < dataStart || commandOffset + 4 > dataEnd) return null;
                byte opcode = file[commandOffset];
                int count = ReadUInt16(file, commandOffset + 2);
                commandWord += 2;
                int colorOffset = dataStart + commandWord * sizeof(ushort);

                switch (opcode)
                {
                    case 0xC0: // transparent run
                        x = Math.Min(width, x + count);
                        break;

                    case 0xC1: // solid colour run
                    case 0xC2: // overlay-colour run; the mask plane is not used by UI images
                    case 0xC3: // solid colour run variant
                        if (colorOffset < dataStart || (long)colorOffset + (long)count * 2 > dataEnd) return null;
                        for (int p = 0; p < count; p++)
                        {
                            ushort pixel = ReadUInt16(file, colorOffset + p * 2);
                            if (x < width && pixel != 0)
                                WriteRgb565(rgba, (y * width + x) * 4, pixel);
                            x++;
                        }
                        commandWord += count;
                        break;

                    default:
                        return null;
                }

            }

            // The original reader loops while nX < End and accepts a final
            // command that advances nX one word past End. End is the
            // scanline's exclusive boundary after the trailing increment,
            // so requiring equality here rejects valid wide runs.
            rowWord = rowEnd + 1;
            streamWord = rowWord;
        }

        return rgba;
    }

    private static void WriteRgb565(byte[] rgba, int offset, ushort value)
    {
        rgba[offset] = (byte)((value & 0xF800) >> 8);
        rgba[offset + 1] = (byte)((value & 0x07E0) >> 3);
        rgba[offset + 2] = (byte)((value & 0x001F) << 3);
        rgba[offset + 3] = 255;
    }

    private static bool HasRange(byte[] data, int offset, int length)
        => offset >= 0 && length >= 0 && (long)offset + length <= data.Length;

    private static short ReadInt16(byte[] data, int offset)
        => unchecked((short)ReadUInt16(data, offset));

    private static ushort ReadUInt16(byte[] data, int offset)
        => (ushort)(data[offset] | data[offset + 1] << 8);

    private static uint ReadUInt32(byte[] data, int offset)
        => (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);

    private static int ReadInt32(byte[] data, int offset)
        => unchecked((int)ReadUInt32(data, offset));

    public void Dispose()
    {
        foreach (ImageTexture texture in _textures.Values)
            texture?.Dispose();
        _textures.Clear();
        _headers.Clear();
    }
}
