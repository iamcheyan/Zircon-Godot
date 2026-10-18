using System.Text.Json;
using Library;
using Library.SystemModels;
using MirDB;

// NpcFaceWriter — 把「NPC 外观 body -> NPCface 头像帧」分配表写进 System.db 的
// NPCInfo.FaceImage（客户端 NPC 对话窗左侧头像用；原版对应 Merchant.txt 的 Face 列，
// 本机可达的三份服务端配置该列全为 0，故由 Tools/npc_faces.py 人工配对补齐）。
//
// 用法：
//   NpcFaceWriter <root> --map <facemap.json>            # dry-run，只打印将要改的项
//   NpcFaceWriter <root> --map <facemap.json> --apply    # 备份 + 写库 + 读回校验
//
//   <root>           含 System.db 的目录（如 Debug/ServerCore/Database）
//   facemap.json     {"bodies": {"<body>": <frame>, ...}, "index": {"<npcIndex>": <frame>, ...}}
//
// 纪律（见 AGENTS.md）：写库前备份；写完用新 Session 读回逐条校验；4 处 System.db
// 必须保持一致的调用方自行同步（本工具只写给定 root）。
string root = null;
string mapPath = null;
bool apply = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--map" when i + 1 < args.Length: mapPath = args[++i]; break;
        case "--apply": apply = true; break;
        default: if (root == null) root = args[i]; break;
    }
}
if (root == null || mapPath == null)
{
    Console.WriteLine("usage: NpcFaceWriter <root> --map <facemap.json> [--apply]");
    return 2;
}
if (!Path.IsPathRooted(root)) root = Path.GetFullPath(root);
if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
string backup = Path.Combine(root, "Backup") + Path.DirectorySeparatorChar;

var doc = JsonDocument.Parse(File.ReadAllText(mapPath));
var bodies = new Dictionary<int, int>();
foreach (var p in doc.RootElement.GetProperty("bodies").EnumerateObject())
    bodies[int.Parse(p.Name)] = p.Value.GetInt32();
var perIndex = new Dictionary<int, int>();
if (doc.RootElement.TryGetProperty("index", out var idxEl))
    foreach (var p in idxEl.EnumerateObject())
        perIndex[int.Parse(p.Name)] = p.Value.GetInt32();

Console.WriteLine($"root={root}");
Console.WriteLine($"map={mapPath}  body 规则 {bodies.Count} 条, 逐 NPC 覆盖 {perIndex.Count} 条");
Console.WriteLine($"模式: {(apply ? "APPLY（写库）" : "dry-run")}");

int FaceFor(NPCInfo n) => perIndex.TryGetValue(n.Index, out int f) ? f : (bodies.TryGetValue(n.Image, out int b) ? b : -1);

var session = new Session(SessionMode.System, root: root, backup: backup);
session.Initialize(typeof(NPCInfo).Assembly);
var npcs = session.GetCollection<NPCInfo>().Binding;
Console.WriteLine($"NPCInfo {npcs.Count} 条; System.db={session.SystemPath}");

var unknownBodies = new SortedSet<int>();
int changed = 0, same = 0, unmapped = 0;
var plan = new List<(NPCInfo Npc, int Face)>();
foreach (var n in npcs)
{
    int face = FaceFor(n);
    if (face < 0) { unmapped++; unknownBodies.Add(n.Image); continue; }
    if (n.FaceImage == face) { same++; continue; }
    plan.Add((n, face));
    if (changed < 40)
        Console.WriteLine($"  #{n.Index,-4} {n.NPCName,-28} body={n.Image,-5} FaceImage {n.FaceImage} -> {face}");
    changed++;
}
Console.WriteLine($"将修改 {changed} 条, 已是目标值 {same} 条, 无映射(body 未登记) {unmapped} 条");
if (unknownBodies.Count > 0)
    Console.WriteLine($"  !! 未登记的 body: {string.Join(", ", unknownBodies)}");

// 目标头像分布（确认 46 张脸都用上）
var dist = new SortedDictionary<int, int>();
foreach (var n in npcs)
{
    int face = FaceFor(n);
    if (face < 0) continue;
    dist[face] = dist.GetValueOrDefault(face) + 1;
}
Console.WriteLine($"头像帧使用分布({dist.Count} 种): {string.Join(", ", dist.Select(kv => kv.Key + "×" + kv.Value))}");

if (!apply)
{
    Console.WriteLine("dry-run 结束（加 --apply 才会写库）");
    return 0;
}
if (changed == 0)
{
    Console.WriteLine("无需修改");
    return 0;
}

string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
string bak = session.SystemPath + $".bak-npcface-{stamp}";
File.Copy(session.SystemPath, bak, overwrite: false);
Console.WriteLine($"备份: {bak}");

foreach (var (npc, face) in plan) npc.FaceImage = face;
session.Save(true);
Console.WriteLine("已保存");

// 读回校验
var check = new Session(SessionMode.System, root: root, backup: backup);
check.Initialize(typeof(NPCInfo).Assembly);
int bad = 0, ok = 0;
foreach (var n in check.GetCollection<NPCInfo>().Binding)
{
    int face = FaceFor(n);
    if (face < 0) continue;
    if (n.FaceImage != face) { bad++; if (bad < 10) Console.WriteLine($"  !! 读回不一致 #{n.Index} {n.NPCName}: {n.FaceImage} != {face}"); }
    else ok++;
}
Console.WriteLine($"读回校验: 一致 {ok}, 不一致 {bad}");
return bad == 0 ? 0 : 1;
