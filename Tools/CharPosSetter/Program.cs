using Library;
using Library.SystemModels;
using MirDB;

// CharPosSetter — 把角色直接放到指定地图/坐标（写 Users.db）。
//
// 用途：本机 NPC 对话/头像逐 NPC 截图取证时，避免依赖客户端输入（xdotool 无法把
// '@' 送进 Godot 的聊天输入框），改为直接改测试角色坐标后重启服务端。
// 只在**自建隔离测试服务端**的 DB 副本上使用，不要对正在运行的服务端 DB 写。
//
// 用法：
//   CharPosSetter <root> --char <角色名> --map <MapInfo.Index> --x <n> --y <n> [--apply]
using System.Drawing;
string root = null, name = null;
int mapIndex = -1, px = -1, py = -1;
bool apply = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--char" when i + 1 < args.Length: name = args[++i]; break;
        case "--map" when i + 1 < args.Length: mapIndex = int.Parse(args[++i]); break;
        case "--x" when i + 1 < args.Length: px = int.Parse(args[++i]); break;
        case "--y" when i + 1 < args.Length: py = int.Parse(args[++i]); break;
        case "--apply": apply = true; break;
        default: if (root == null) root = args[i]; break;
    }
}
if (root == null || name == null || mapIndex < 0 || px < 0 || py < 0)
{
    Console.WriteLine("usage: CharPosSetter <root> --char <name> --map <index> --x <n> --y <n> [--apply]");
    return 2;
}
if (!Path.IsPathRooted(root)) root = Path.GetFullPath(root);
if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

var session = new Session(SessionMode.Users, root: root, backup: Path.Combine(root, "Backup") + Path.DirectorySeparatorChar);
session.Initialize(typeof(MapInfo).Assembly, typeof(Server.DBModels.AccountInfo).Assembly);

var maps = session.GetCollection<MapInfo>().Binding;
MapInfo map = maps.FirstOrDefault(x => x.Index == mapIndex);
if (map == null) { Console.WriteLine($"找不到地图 Index={mapIndex}"); return 1; }

var chars = session.GetCollection<Server.DBModels.CharacterInfo>().Binding;
var ch = chars.FirstOrDefault(x => string.Equals(x.CharacterName, name, StringComparison.OrdinalIgnoreCase));
if (ch == null) { Console.WriteLine($"找不到角色 {name}（共 {chars.Count} 个）"); return 1; }

Console.WriteLine($"{ch.CharacterName}: 现在 map={ch.CurrentMap?.Index}({ch.CurrentMap?.FileName}) loc=({ch.CurrentLocation.X},{ch.CurrentLocation.Y})");
Console.WriteLine($"目标: map={map.Index}({map.FileName}) loc=({px},{py})  模式={(apply ? "APPLY" : "dry-run")}");
if (!apply) return 0;

string bak = Path.Combine(root, "Users.db.bak-possetter-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
File.Copy(Path.Combine(root, "Users.db"), bak, overwrite: false);
Console.WriteLine($"备份: {bak}");

ch.CurrentMap = map;
ch.CurrentLocation = new Point(px, py);
session.Save(false);

var check = new Session(SessionMode.Users, root: root, backup: Path.Combine(root, "Backup") + Path.DirectorySeparatorChar);
check.Initialize(typeof(MapInfo).Assembly, typeof(Server.DBModels.AccountInfo).Assembly);
var ch2 = check.GetCollection<Server.DBModels.CharacterInfo>().Binding
    .First(x => string.Equals(x.CharacterName, name, StringComparison.OrdinalIgnoreCase));
Console.WriteLine($"读回: map={ch2.CurrentMap?.Index} loc=({ch2.CurrentLocation.X},{ch2.CurrentLocation.Y})");
return (ch2.CurrentMap?.Index == mapIndex && ch2.CurrentLocation.X == px && ch2.CurrentLocation.Y == py) ? 0 : 1;
