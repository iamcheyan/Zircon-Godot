using System;
using System.IO;
using Godot;

namespace ZirconClient.Formats;

// .map 文件读取器（移植自 Client/Scenes/Views/MapControl.cs:484-545）
// 格式: 头部22字节 → Width/Height → 背景层(半分辨率) → 全分辨率单元格(每格14字节)
public sealed class MirMap
{
    public int Width;
    public int Height;
    public MapCell[,] Cells;

    public MirMap(string fileName)
    {
        using var stream = File.OpenRead(fileName);
        using var reader = new BinaryReader(stream);

        // 头部 22 字节（跳过）
        reader.ReadBytes(22);

        // Width, Height
        Width = reader.ReadInt16();
        Height = reader.ReadInt16();

        // 数据从偏移 28 开始（原客户端 mStream.Seek(28, SeekOrigin.Begin)）
        reader.BaseStream.Seek(28, SeekOrigin.Begin);

        // 单元格步长：发行数据里存在 **13 字节/格** 的 .map（39 张：D601–D619、0_00xx、d60x–d61x，
        // 空间连通性检验 + 三副本 MD5 一致，是另一种格大小而不是拷贝损坏）。原来按 14 字节顺序读，
        // 读这些图会在流末尾抛 EndOfStreamException → 地形不加载。这里按文件实际长度自动识别；
        // 两种布局前 13 字节字段相同（14 字节布局尾部那 1 字节本来就是跳过不用的），
        // 所以只需把"跳过 1 字节"改成"补到步长"。
        int cellStride = 14;
        {
            long cellBytes = reader.BaseStream.Length - 28 - (long)(Width / 2) * (Height / 2) * 3;
            long perCell = (Width > 0 && Height > 0) ? cellBytes / ((long)Width * Height) : 0;
            if (perCell is 13 or 14) cellStride = (int)perCell;
        }

        Cells = new MapCell[Width, Height];

        // 第一段: 背景层（半分辨率，只存偶数格）
        for (int x = 0; x < Width / 2; x++)
        {
            for (int y = 0; y < Height / 2; y++)
            {
                byte backFile = reader.ReadByte();
                ushort backImage = reader.ReadUInt16();
                Cells[x * 2, y * 2].BackFile = backFile;
                Cells[x * 2, y * 2].BackImage = backImage;
            }
        }

        // 第二段: 全分辨率单元格，每格 14 字节
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                byte flag = reader.ReadByte();
                byte middleAnimationFrame = reader.ReadByte();
                byte value = reader.ReadByte();
                byte frontFile = reader.ReadByte();
                byte middleFile = reader.ReadByte();

                // FrontAnimationFrame: 255→0, 再 &= 0x8F
                int frontAnimationFrame = value == 255 ? 0 : value;
                frontAnimationFrame &= 0x8F;

                ushort middleImage = (ushort)(reader.ReadUInt16() + 1); // +1
                ushort frontImage = (ushort)(reader.ReadUInt16() + 1);  // +1

                reader.BaseStream.Seek(3, SeekOrigin.Current); // 跳过 3 字节
                byte light = (byte)((reader.ReadByte() & 0x0F) * 2);  // 低4位 ×2
                reader.BaseStream.Seek(cellStride - 13, SeekOrigin.Current); // 补到单元步长（14 时=原行为，13 时=0）

                // Flag: (flag & 0x01) != 1 || (flag & 0x02) != 2
                bool cellFlag = ((flag & 0x01) != 1) || ((flag & 0x02) != 2);

                ref var cell = ref Cells[x, y];
                cell.MiddleAnimationFrame = middleAnimationFrame;
                cell.FrontAnimationFrame = frontAnimationFrame;
                cell.FrontFile = frontFile;
                cell.MiddleFile = middleFile;
                cell.MiddleImage = middleImage;
                cell.FrontImage = frontImage;
                cell.Light = light;
                cell.Flag = cellFlag;
            }
        }
    }

    public bool IsValid => Width > 0 && Height > 0 && Cells != null;

    public static bool TryGetDimensions(string fileName, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName)) return false;
        try
        {
            using var stream = File.OpenRead(fileName);
            using var reader = new BinaryReader(stream);
            reader.BaseStream.Seek(22, SeekOrigin.Begin);
            width = reader.ReadInt16();
            height = reader.ReadInt16();
            return width > 0 && height > 0;
        }
        catch
        {
            width = height = 0;
            return false;
        }
    }
}

public struct MapCell
{
    public int BackFile;
    public int BackImage;
    public int MiddleFile;
    public int MiddleImage;   // 已 +1，绘制时 -1
    public int FrontFile;
    public int FrontImage;    // 已 +1，绘制时 -1
    public int MiddleAnimationFrame;
    public int FrontAnimationFrame;
    public int Light;
    public bool Flag;         // 阻挡标志
}
