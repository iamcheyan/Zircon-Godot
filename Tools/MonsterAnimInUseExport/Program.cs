// 单文件 C# 工具: 离线读 System.db 导出 MonsterInfo + RespawnInfo,
// 同时与网站 monsters.json 做"实际在用"交集, 落到 out 参数指定的 JSON.
//
// 用法:
//   dotnet run --project Tools/MonsterAnimInUseExport -- <dbRoot> <websiteJson> <out>
//
//   dbRoot     = 指向 System.db 所在目录 (例: Debug/ServerCore/Database)
//   websiteJson= 指向 mir3-website/data/monsters.json
//   out        = 输出 JSON 路径
//
// 读 db: 复制 System.db 到 /tmp 临时目录, 用 Session(SessionMode.System) 读.
// 这是 read-only, 不动运行中的 System.db.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Library.SystemModels;
using MirDB;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: MonsterAnimInUseExport <dbRoot> <websiteJson> <out>");
            return 2;
        }
        string dbRoot = Path.GetFullPath(args[0]);
        string websiteJson = Path.GetFullPath(args[1]);
        string outPath = Path.GetFullPath(args[2]);

        if (!File.Exists(Path.Combine(dbRoot, "System.db")))
        {
            Console.Error.WriteLine($"System.db not found in {dbRoot}");
            return 2;
        }
        // 不挂载 AccountInfo/Users.db 集合, 避免无关路径出错.
        string tempRoot = Path.Combine(Path.GetTempPath(), "monster_anim_db_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        File.Copy(Path.Combine(dbRoot, "System.db"), Path.Combine(tempRoot, "System.db"));
        string srcSha = ShaOf(Path.Combine(dbRoot, "System.db"));
        Console.WriteLine($"[InUseExport] System.db sha256={srcSha} tempRoot={tempRoot}");

        var session = new Session("MODE=3;ROOT=" + tempRoot + "/;BACKUP=" + tempRoot + "_backup/");
        session.Initialize(typeof(MonsterInfo).Assembly, typeof(RespawnInfo).Assembly,
            typeof(MapInfo).Assembly, typeof(CompanionInfo).Assembly);
        var monsters = session.GetCollection<MonsterInfo>().Binding
            .OrderBy(m => m.Index)
            .Select(m => new
            {
                index = m.Index,
                name = m.MonsterName,
                image = m.Image.ToString(),
                ai = m.AI,
                level = m.Level,
                viewRange = m.ViewRange,
                isBoss = m.IsBoss,
                respawnCount = m.Respawns?.Count ?? 0,
            })
            .ToList();

        var respawns = session.GetCollection<RespawnInfo>().Binding
            .Where(r => r.Region?.Map != null)
            .GroupBy(r => r.Monster?.Index ?? -1)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new
                {
                    map = r.Region.Map.FileName ?? "",
                    mapIndex = r.Region.Map.Index,
                    count = r.Count,
                    delay = r.Delay,
                    eventSpawn = r.EventSpawn,
                }).ToList());

        var companionRefs = session.GetCollection<CompanionInfo>().Binding
            .Where(c => c.MonsterInfo != null)
            .GroupBy(c => c.MonsterInfo!.Index)
            .ToDictionary(g => g.Key, g => g.Count());

        // 用 GodotClient/translations/db_names.json 把 DB 英文名翻译成中文, 与站点对齐.
        var zhGlossaryPath = Path.GetFullPath(Path.Combine("GodotClient", "translations", "db_names.json"));
        var db2zh = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(zhGlossaryPath))
        {
            using var gfs = File.OpenRead(zhGlossaryPath);
            using var gdoc = JsonDocument.Parse(gfs);
            foreach (var section in new[] { "monsters", "items" })
            {
                if (!gdoc.RootElement.TryGetProperty(section, out var ms)) continue;
                foreach (var prop in ms.EnumerateObject())
                {
                    if (db2zh.ContainsKey(prop.Name)) continue;
                    if (prop.Value.TryGetProperty("zh", out var zh))
                        db2zh[prop.Name] = zh.GetString() ?? "";
                }
            }
            Console.WriteLine($"[InUseExport] db_names.json loaded entries={db2zh.Count}");
        }
        var website = new List<Dictionary<string, object>>();
        {
            using var fs = File.OpenRead(websiteJson);
            using var doc = JsonDocument.Parse(fs);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                int id = -1;
                if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    && int.TryParse(idProp.GetString()!.Substring(4), out var parsed))
                    id = parsed;
                else if (el.TryGetProperty("id", out var num) && num.ValueKind == JsonValueKind.Number)
                    id = num.GetInt32();

                string name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string category = el.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                string image = el.TryGetProperty("image", out var im) ? im.GetString() ?? "" : "";
                string desc = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

                var spawns = new List<Dictionary<string, object>>();
                if (el.TryGetProperty("spawns", out var sp) && sp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sp.EnumerateArray())
                    {
                        spawns.Add(new Dictionary<string, object>
                        {
                            ["mapId"] = s.TryGetProperty("mapId", out var mid) ? mid.ToString() : "",
                            ["mapNameZh"] = s.TryGetProperty("mapNameZh", out var mn) ? mn.GetString() ?? "" : "",
                            ["count"] = s.TryGetProperty("count", out var ct) && ct.ValueKind == JsonValueKind.Number ? ct.GetInt32() : 0,
                            ["delay"] = s.TryGetProperty("delay", out var dl) ? dl.GetString() ?? "" : "",
                        });
                    }
                }
                website.Add(new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["category"] = category,
                    ["image"] = image,
                    ["description"] = desc,
                    ["spawns"] = spawns,
                });
            }
        }

        // 网站 id 与 MonsterInfo.Index 已经不对齐 (经典化清洗后), 改用归一化中文名
        // 做主键. 多个 DB 行同名时, 第一个匹配 website, 其余标 duplicate-name.
        static string NormZh(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c) || c == '　' || c == ',' || c == '，') continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
        var websiteByName = website
            .Where(w => w["name"] is string n && !string.IsNullOrWhiteSpace(n))
            .GroupBy(w => NormZh((string)w["name"]))
            .ToDictionary(g => g.Key, g => g.First());
        string DbZh(string en) => db2zh.TryGetValue(en, out var z) && !string.IsNullOrEmpty(z) ? z : en;
        var dbZhLookup = monsters.ToLookup(m => NormZh(DbZh(m.name)));
        var nameAssigned = new HashSet<string>(StringComparer.Ordinal);

        var inUse = new List<Dictionary<string, object>>();
        foreach (var m in monsters)
        {
            var key = NormZh(DbZh(m.name));
            string match;
            int websiteId = -1;
            string websiteName = null;
            string websiteCategory = null;
            if (websiteByName.TryGetValue(key, out var w) && !nameAssigned.Contains(key))
            {
                match = dbZhLookup[key].Count() > 1 ? "name-exact-first" : "name-exact";
                nameAssigned.Add(key);
                websiteId = (int)w["id"];
                websiteName = (string)w["name"];
                websiteCategory = (string)w["category"];
            }
            else
            {
                match = dbZhLookup[key].Count() > 1 ? "duplicate-name" : "name-miss";
            }
            inUse.Add(new Dictionary<string, object>
            {
                ["index"] = m.index,
                ["name"] = m.name,
                ["image"] = m.image,
                ["ai"] = m.ai,
                ["level"] = m.level,
                ["isBoss"] = m.isBoss,
                ["respawnCount"] = m.respawnCount,
                ["respawnMaps"] = respawns.TryGetValue(m.index, out var rs)
                    ? rs.Select(x => (object)new { x.map, x.count, x.delay, x.eventSpawn }).ToList()
                    : new List<object>(),
                ["companionRefs"] = companionRefs.TryGetValue(m.index, out var n) ? n : 0,
                ["match"] = match,
                ["websiteId"] = websiteId,
                ["websiteName"] = websiteName,
                ["websiteCategory"] = websiteCategory,
            });
        }
        var matchedNames = nameAssigned;
        var onlyDb = inUse.Where(m => m["match"] is string s && (s == "name-miss" || s == "duplicate-name")).ToList();
        var matched = inUse.Where(m => m["match"] is string s && (s == "name-exact" || s == "name-exact-first")).ToList();
        var onlyWebsite = websiteByName.Where(kv => !matchedNames.Contains(kv.Key))
            .Select(kv => new { name = (string)kv.Value["name"], category = (string)kv.Value["category"], id = (int)kv.Value["id"] })
            .OrderBy(x => x.id)
            .ToList();

        var doc2 = new Dictionary<string, object>
        {
            ["generatedAt"] = DateTime.UtcNow.ToString("o"),
            ["dbRoot"] = dbRoot,
            ["dbSha"] = srcSha,
            ["websiteJson"] = websiteJson,
            ["websiteCount"] = website.Count,
            ["currentDbCount"] = monsters.Count,
            ["matched"] = matched.Count,
            ["onlyDb"] = onlyDb.Count,
            ["onlyWebsite"] = onlyWebsite.Count,
            ["monsters"] = inUse,
            ["onlyDbList"] = onlyDb,
            ["onlyWebsiteList"] = onlyWebsite,
        };

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(doc2, opts));
        Console.WriteLine($"[InUseExport] monsters={monsters.Count} matched={matched.Count} onlyDb={onlyDb.Count} onlyWebsite={onlyWebsite.Count} -> {outPath}");

        // 清理临时目录
        try { Directory.Delete(tempRoot, recursive: true); } catch { }
        return 0;
    }

    private static string ShaOf(string path)
    {
        if (!File.Exists(path)) return "";
        using var s = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(s));
    }
}
