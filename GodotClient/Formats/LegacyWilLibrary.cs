using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace ZirconClient.Formats;

/// <summary>
/// Read-only loader for the original Mir3 EI WIL/WIX image format.
/// It is used only when a legacy UI library has no converted .Zl counterpart.
/// </summary>
public sealed class LegacyWilLibrary : IDisposable
{
    private const int WixHeaderSize = 26;
    private const int WixMagic = 0xB13A;
    private const int WilFrameHeaderSize = 17;

    private readonly byte[] _wilData;
    private readonly int[] _frameOffsets;
    private readonly Dictionary<int, ImageTexture> _textures = new();
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
