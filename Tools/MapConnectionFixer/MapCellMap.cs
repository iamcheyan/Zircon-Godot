using System.Drawing;

namespace MapConnectionFixer;

/// <summary>
/// .map 二进制阻挡解析。判据与 ServerLibrary/Models/Map.cs Load() 逐位一致：
///   if ((flag &amp; 0x02) != 2 || (flag &amp; 0x01) != 1) continue;
/// 即 flag&amp;0x02 必须为 1、flag&amp;0x01 必须为 0 以外的可行走位组合。
/// 任何落点写入 Region/Movement 前都必须经过本类过滤，否则服务端启动会输出
/// [Safe Zone] Bad Location / [Movement] Bad Origin。
/// </summary>
internal sealed class MapCellMap
{
    public string FileName { get; }
    public int Width { get; }
    public int Height { get; }

    private readonly bool[] _walkable;   // 长度 Width*Height，按 [x*Height+y] 索引，与服务端一致

    private MapCellMap(string fileName, int width, int height, bool[] walkable)
    {
        FileName = fileName;
        Width = width;
        Height = height;
        _walkable = walkable;
    }

    private static readonly Dictionary<string, MapCellMap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static MapCellMap Load(string mapDir, string fileName)
    {
        if (Cache.TryGetValue(fileName, out var cached)) return cached;

        string path = Path.Combine(mapDir, fileName + ".map");
        if (!File.Exists(path))
            throw new FileNotFoundException($"map file missing: {path}", path);

        byte[] bytes = File.ReadAllBytes(path);

        int width = bytes[23] << 8 | bytes[22];
        int height = bytes[25] << 8 | bytes[24];
        int offSet = 28 + width * height / 4 * 3;

        var walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            byte flag = bytes[offSet + (x * height + y) * 14];
            walkable[x * height + y] = (flag & 0x02) == 2 && (flag & 0x01) == 1;
        }

        var map = new MapCellMap(fileName, width, height, walkable);
        Cache[fileName] = map;
        return map;
    }

    public static bool TryLoad(string mapDir, string fileName, out MapCellMap map)
    {
        try
        {
            map = Load(mapDir, fileName);
            return true;
        }
        catch (FileNotFoundException)
        {
            map = null;
            return false;
        }
    }

    public bool InBounds(Point p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public bool IsWalkable(Point p) => InBounds(p) && _walkable[p.X * Height + p.Y];

    /// <summary>半径 r 内所有可行走点（方形邻域，与原版安全区算法一致）。</summary>
    public List<Point> WalkableInRadius(int cx, int cy, int r)
    {
        var list = new List<Point>();
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            var p = new Point(cx + dx, cy + dy);
            if (IsWalkable(p)) list.Add(p);
        }
        return list;
    }

    /// <summary>
    /// 落点校正：若 (x,y) 不可行走，按 8 方向螺旋外扩找最近可行走点。
    /// 返回 false 表示该地图上找不到任何可行走点。
    /// </summary>
    public bool NearestWalkable(int x, int y, int maxRadius, out Point result)
    {
        // 官方坐标可能超出本库 .map 的尺寸（Zircon 的 3 号图为 350x350，
        // 而 Mud3 记录的是 364,571）。此时先把原点钳制进图内再外扩，
        // 否则越界点无论扩多大半径都找不到可行走格。
        var origin = new Point(Math.Clamp(x, 0, Width - 1), Math.Clamp(y, 0, Height - 1));
        if (IsWalkable(origin)) { result = origin; return true; }

        for (int r = 1; r <= maxRadius; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            // 只看当前外环
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            var p = new Point(x + dx, y + dy);
            if (IsWalkable(p)) { result = p; return true;
            }
        }

        result = origin;
        return false;
    }
}
