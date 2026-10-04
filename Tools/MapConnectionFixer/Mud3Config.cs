using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MapConnectionFixer;

/// <summary>
/// Mud3 官方配置解析器。
///
/// 唯一真理源：
///   连接：Mud3-Config/Envir/Mapinfo.txt
///         <源地图> <源X>,<源Y&gt; -&gt; &lt;目标地图&gt; &lt;目标X&gt;,&lt;目标Y&gt;
///   安全区：Mud3-Config/Envir/StartPoint.txt
///         &lt;地图名&gt; &lt;中心X&gt; &lt;中心Y&gt;
///
/// 两个文件均为 GB18030 编码，必须显式解码，不能依赖 UTF-8 默认值。
/// </summary>
internal static class Mud3Config
{
    private const string ConfigRoot =
        "/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir";

    public static string MapInfoPath => Path.Combine(ConfigRoot, "Mapinfo.txt");
    public static string StartPointPath => Path.Combine(ConfigRoot, "StartPoint.txt");

    // .NET Core 默认不含 GB18030 编码，必须先注册 CodePages 提供程序，
    // 否则读取 Mud3 官方配置会抛 ArgumentException。
    private static readonly Encoding Gb18030 = CreateGb18030();

    private static Encoding CreateGb18030()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB18030");
    }

    /// <summary>一条官方连接：SourceMap(Sx,Sy) -&gt; DestMap(Dx,Dy)。</summary>
    internal sealed record Connection(string SourceMap, int SourceX, int SourceY,
                                     string DestMap, int DestX, int DestY);

    /// <summary>一个官方复活点：MapName X Y。同一张图可有多个（回城/复活分散点）。</summary>
    internal sealed record StartPoint(string MapName, int X, int Y);

    private static readonly Regex ConnectionRx =
        new(@"^([A-Za-z0-9_]+)\s+(\d+),(\d+)\s*->\s*([A-Za-z0-9_]+)\s+(\d+),(\d+)",
            RegexOptions.Compiled);

    private static readonly Regex StartPointRx =
        new(@"^\s*([A-Za-z0-9_]+)\s+(\d+)\s+(\d+)\s*$", RegexOptions.Compiled);

    public static List<Connection> LoadConnections()
    {
        var result = new List<Connection>();
        foreach (string line in File.ReadAllLines(MapInfoPath, Gb18030))
        {
            var m = ConnectionRx.Match(line.Trim());
            if (!m.Success) continue;
            result.Add(new Connection(m.Groups[1].Value,
                int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                m.Groups[4].Value,
                int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value)));
        }
        return result;
    }

    public static List<StartPoint> LoadStartPoints()
    {
        var result = new List<StartPoint>();
        foreach (string line in File.ReadAllLines(StartPointPath, Gb18030))
        {
            if (line.Trim().Length == 0) continue;
            var m = StartPointRx.Match(line);
            if (!m.Success) continue;
            result.Add(new StartPoint(m.Groups[1].Value,
                int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));
        }
        return result;
    }

    /// <summary>
    /// Mud3 的双向通道语义：回程行与去程行互为「门口格 vs 落地格」的相邻对，
    /// 故以容差 1 判定反向行是否存在。
    /// </summary>
    public static bool HasReverseLine(List<Connection> all, Connection c, int tolerance = 1)
    {
        for (int ddx = -tolerance; ddx <= tolerance; ddx++)
        for (int ddy = -tolerance; ddy <= tolerance; ddy++)
        for (int sdx = -tolerance; sdx <= tolerance; sdx++)
        for (int sdy = -tolerance; sdy <= tolerance; sdy++)
        {
            if (all.Any(o => o.DestMap == c.SourceMap
                             && Math.Abs(o.DestX - (c.SourceX + sdx)) <= tolerance
                             && Math.Abs(o.DestY - (c.SourceY + sdy)) <= tolerance
                             && o.SourceMap == c.DestMap
                             && Math.Abs(o.SourceX - (c.DestX + ddx)) <= tolerance
                             && Math.Abs(o.SourceY - (c.DestY + ddy)) <= tolerance))
                return true;
        }
        return false;
    }
}
