using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using Server.Models;
using S = Library.Network.ServerPackets;

// No listener, game connection, live database or global config file is opened.
// Real MirDB factories, PlayerObject construction, DevSinglePlayer.Apply and
// save/reload are used. A detached SConnection only collects outgoing packets.
string root = Path.Combine(Path.GetTempPath(), "zircon-singleplayer-dev-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    SEnvir.Now = Library.Time.Now;
    SEnvir.UseLogConsole = true;
    Config.EnableDiscipline = false;
    if (args.Length == 2 && args[0] == "--database-copy")
    {
        CheckDatabaseCopy(args[1], root);
        return;
    }
    var session = OpenSession(root);
    CreateDefinitions(session);
    var account = session.GetCollection<AccountInfo>().CreateNewObject();
    account.EMailAddress = DevSinglePlayer.TargetEmail;
    account.Password = Array.Empty<byte>(); // fixture has no login credentials
    var character = session.GetCollection<CharacterInfo>().CreateNewObject();
    character.Account = account;
    character.CharacterName = "TestHero";
    character.Level = DevSinglePlayer.DevLevel; // reproduce partially initialized save
    character.Class = MirClass.Warrior;
    character.Experience = 123;

    Config.SinglePlayerDev = false;
    var unmodified = new PlayerObject(character, Connection());
    Check(character.Magics.Count == 0 && character.Items.Count == 0, "normal startup does not seed data");
    Config.SinglePlayerDev = true;
    var repaired = new PlayerObject(character, Connection());
    Check(character.Magics.Count == 2, "level255 with zero skills is repaired (disabled discipline excluded)");
    Check(character.Items.Count == 2, "missing test equipment is granted");
    Check(repaired.MagicObjects.Count == 2, "runtime magic objects are registered");
    Check(character.Magics.All(m => m.Index > 0 && m.Character == character && m.Level == Globals.MagicMaxLevel), "skills use real MirDB identity and association");
    Check(character.Magics.All(m => character.Magics.Count(x => x.Info == m.Info) == 1), "no duplicate association insert");
    Check(character.Account.Gold.Amount == 100_000_000, "test gold minimum restored");
    Check(repaired.Connection.SendList.OfType<S.ItemsGained>().Any(), "item delivery uses existing packet path");

    // Preserve existing keys, experience, amounts and already-owned definitions.
    character.Magics[0].Set1Key = SpellKey.Spell03;
    character.Magics[0].Experience = 17;
    character.Account.Gold.Amount = 100_000_100;
    int oldItems = character.Items.Count;
    var second = new PlayerObject(character, Connection());
    Check(character.Items.Count == oldItems && character.Magics.Count == 2, "second login does not duplicate skills/equipment");
    Check(character.Magics[0].Set1Key == SpellKey.Spell03 && character.Magics[0].Experience == 17,
        "existing skill binding/experience retained");
    Check(character.Account.Gold.Amount == 100_000_100, "existing gold above minimum retained");

    session.Save(true);
    session = OpenSession(root);
    character = session.GetCollection<CharacterInfo>().Binding.Single(c => c.CharacterName == "TestHero");
    var reloaded = new PlayerObject(character, Connection());
    Check(character.Level == 255 && character.Magics.Count == 2 && character.Items.Count == 2,
        "save/reload keeps full level255 character");
    Check(reloaded.MagicObjects.Count == 2 && character.Magics[0].Set1Key == SpellKey.Spell03,
        "reload keeps runtime registrations and shortcut keys");

    // Simulate interruption after one skill/item was successfully written.
    var missingMagic = character.Magics.Last();
    missingMagic.Delete();
    character.Items.Last().Delete();
    var partial = new PlayerObject(character, Connection());
    Check(character.Magics.Count == 2 && character.Items.Count == 2, "retry completes partial injection");
    Check(character.Magics.Select(x => x.Info.Index).Distinct().Count() == 2, "retry remains unique");

    var other = session.GetCollection<AccountInfo>().CreateNewObject();
    other.EMailAddress = "not-the-test-account@example.invalid";
    other.Password = Array.Empty<byte>();
    var otherCharacter = session.GetCollection<CharacterInfo>().CreateNewObject();
    otherCharacter.Account = other;
    otherCharacter.CharacterName = "Untouched";
    otherCharacter.Level = 1;
    _ = new PlayerObject(otherCharacter, Connection());
    Check(otherCharacter.Level == 1 && otherCharacter.Magics.Count == 0 && otherCharacter.Items.Count == 0,
        "non-test account is untouched");

    Config.SinglePlayerDev = false;
    var fresh = session.GetCollection<CharacterInfo>().CreateNewObject();
    fresh.Account = character.Account;
    fresh.CharacterName = "FreshDevHero";
    fresh.Level = 1;
    Config.SinglePlayerDev = true;
    var freshPlayer = new PlayerObject(fresh, Connection());
    Check(fresh.Level == DevSinglePlayer.DevLevel && fresh.Magics.Count == 2 && fresh.Items.Count == 2,
        "fresh level1 test character still gets full dev initialization");
    Check(!SEnvir.DisplayLogs.Any(x => x.Contains("注入异常")), "no swallowed injection exception");
    Console.WriteLine("PASS: SinglePlayerDevChecks (real MirDB roundtrip; no live data)");
}
finally
{
    Config.SinglePlayerDev = false;
    Directory.Delete(root, recursive: true);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

static SConnection Connection()
{
    // No sockets needed: initialization only enqueues packets. Do not call
    // Disconnect/CleanUp on this detached test sink.
    var connection = (SConnection)RuntimeHelpers.GetUninitializedObject(typeof(SConnection));
    connection.Observers = new List<SConnection>();
    connection.SendList = new ConcurrentQueue<Library.Network.Packet>();
    connection.ReceiveList = new ConcurrentQueue<Library.Network.Packet>();
    connection.Connected = true;
    return connection;
}

static Session OpenSession(string directory, bool usersOnly = false)
{
    string slash = Path.DirectorySeparatorChar.ToString();
    var session = new Session("Mode=" + (usersOnly ? "Users" : "Both") + ";Root=" + directory + slash + ";Backup=" + directory + slash + "Backup" + slash)
    { BackUp = false };
    session.Initialize(typeof(MagicInfo).Assembly, typeof(CharacterInfo).Assembly);
    SEnvir.Session = session;
    // Wire public server collections exactly as the runtime does; all are scoped
    // to this throw-away session, never the user's running save.
    foreach (var field in typeof(SEnvir).GetFields(BindingFlags.Public | BindingFlags.Static))
        if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(DBCollection<>))
            field.SetValue(null, session.GetCollection(field.FieldType.GenericTypeArguments[0]));
    SEnvir.Random = new Random(1);
    SEnvir.MagicTypes = typeof(MagicObject).Assembly.GetTypes().Where(t => !t.IsAbstract
        && t.BaseType == typeof(MagicObject) && t.IsDefined(typeof(MagicTypeAttribute))).ToList();
    return session;
}

static void CheckDatabaseCopy(string inputDirectory, string temporaryRoot)
{
    var input = Path.GetFullPath(inputDirectory);
    if (input == Path.GetFullPath(temporaryRoot)) throw new ArgumentException("Input must differ from temporary root");
    var hashes = new Dictionary<string, string>();
    foreach (string name in new[] { "System.db", "Users.db" })
    {
        string path = Path.Combine(input, name);
        hashes[name] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        File.Copy(path, Path.Combine(temporaryRoot, name));
    }
    var session = OpenSession(temporaryRoot, usersOnly: true);
    var character = session.GetCollection<CharacterInfo>().Binding.Single(c =>
        c.Account?.EMailAddress == DevSinglePlayer.TargetEmail && c.CharacterName == "TestHero");
    Console.WriteLine($"COPY BEFORE: {character.CharacterName} level={character.Level} magics={character.Magics.Count} items={character.Items.Count}");
    Config.SinglePlayerDev = true;
    var player = new PlayerObject(character, Connection());
    int expectedMagics = SEnvir.MagicInfoList.Binding.Count(m => m.School != MagicSchool.None && m.School != MagicSchool.Discipline);
    Check(character.Level >= DevSinglePlayer.DevLevel, "copied character remains level255");
    Check(character.Magics.Count(m => m.Info != null && m.Level == Globals.MagicMaxLevel) >= expectedMagics,
        "copied character receives complete dev skill set");
    Check(character.Items.Count > 0, "copied character receives test items");
    int magicCount = character.Magics.Count, itemCount = character.Items.Count;
    var second = new PlayerObject(character, Connection());
    Check(character.Magics.Count == magicCount && character.Items.Count == itemCount, "copied save second login is idempotent");
    session.Save(true);
    session = OpenSession(temporaryRoot, usersOnly: true);
    character = session.GetCollection<CharacterInfo>().Binding.Single(c => c.CharacterName == "TestHero");
    var reloaded = new PlayerObject(character, Connection());
    Check(character.Magics.Count == magicCount && character.Items.Count == itemCount, "copied save roundtrip is stable");
    // A spawned map is not needed for this offline packet projection. Supply a
    // definition-only map without invoking MapChanged/starting world services.
    typeof(MapObject).GetField("_CurrentMap", BindingFlags.NonPublic | BindingFlags.Instance)
        .SetValue(reloaded, new Map(character.CurrentMap ?? SEnvir.MapInfoList.Binding.First()));
    var start = (StartInformation)typeof(PlayerObject).GetMethod("GetStartInformation", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(reloaded, new object[] { false });
    Check(start.Level == DevSinglePlayer.DevLevel && start.Magics.Count == magicCount && start.Items.Count == itemCount,
        "real StartInformation includes full level/skills/items");
    Console.WriteLine($"COPY AFTER: {character.CharacterName} level={character.Level} magics={magicCount} items={itemCount} runtimeMagics={reloaded.MagicObjects.Count} hp={character.CurrentHP} mp={character.CurrentMP}");
    foreach (var pair in hashes)
        Check(pair.Value == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(input, pair.Key)))),
            "input " + pair.Key + " untouched");
    Check(!SEnvir.DisplayLogs.Any(x => x.Contains("注入异常")), "no swallowed injection exception on copied save");
    Console.WriteLine("PASS: device save COPY repair/reload (original files untouched)");
}

static void CreateDefinitions(Session session)
{
    var gold = session.GetCollection<CurrencyInfo>().CreateNewObject();
    gold.Name = "Gold";
    gold.Type = CurrencyType.Gold;
    var magic = session.GetCollection<MagicInfo>().CreateNewObject();
    magic.Name = "Swordsmanship";
    magic.Magic = MagicType.Swordsmanship;
    magic.School = MagicSchool.Passive;
    var second = session.GetCollection<MagicInfo>().CreateNewObject();
    second.Name = "FireBall";
    second.Magic = MagicType.FireBall;
    second.School = MagicSchool.Fire;
    var disabled = session.GetCollection<MagicInfo>().CreateNewObject();
    disabled.Name = "DisabledDiscipline";
    disabled.School = MagicSchool.Discipline;
    var sword = session.GetCollection<ItemInfo>().CreateNewObject();
    sword.ItemName = "TestSword";
    sword.ItemType = ItemType.Weapon;
    sword.StackSize = 1;
    var potion = session.GetCollection<ItemInfo>().CreateNewObject();
    potion.ItemName = "TestPotion";
    potion.ItemType = ItemType.Consumable;
    potion.StackSize = 20;
}
