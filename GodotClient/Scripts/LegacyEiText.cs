namespace ZirconClient.Scripts;

/// <summary>
/// EI 原版客户端预游戏（登录/选角/建角）文案表。
///
/// 原版这些文字来自客户端根目录的加密消息表 <c>CMList.dat</c>，由
/// <c>CMsg.pas::TCMsg.LoadMsg</c> 经 <c>EDCode.pas::Decrypt</c> 解密后按
/// <c>#id 文本</c> 装载，界面用 <c>CMsg.GetMsg(id)</c> 取用。
/// 这里把选角/建角屏实际用到的条目逐字转写进来（源文件
/// <c>MIR3_EI_ROOT/CMList.dat</c>，GBK），避免运行时依赖该文件。
///
/// id 与调用点（reference/mir3-source/Source/Client）：
///   206/207/208..210  PlayScene 选中角色详情（角色名 / 等级 / 职业 战士|法师|道士）
///   211/212 + 213/214/215  DrawNewChr 说明框首行「[ 男/女 + 职业 ]」
///   216/217/218        职业说明正文（SetCharExplain → StringDivide 折行）
///   219                「首先创建角色，才能开始游戏。」
///   223..228            建角/删角错误与确认文案
/// </summary>
public static class LegacyEiText
{
    // ---- 选中角色详情（PlayScene）----
    public const string CharacterNameLabel = "角色名";           // 206
    public const string LevelLabel = "等级";                     // 207
    private static readonly string[] JobDetailLabel =           // 208..210
    {
        "职业   战士",
        "职业   法师",
        "职业   道士",
    };

    // ---- 建角说明框（DrawNewChr）----
    public const string GenderMalePrefix = "[ 男";               // 211
    public const string GenderFemalePrefix = "[ 女";             // 212
    private static readonly string[] JobSuffix =                 // 213..215
    {
        " 战士 ]",
        " 法师 ]",
        " 道士 ]",
    };
    private static readonly string[] JobExplain =                // 216..218
    {
        "战士具有很强的体力，战斗中不易死去，能够携带沉重的武器和防御物品。"
            + "战士擅长于近距离战斗，远距离攻击就显得非常无力，值得庆幸的是，"
            + "战士可以携带专门为战士所准备的各种装备，弥补这一弱点。"
            + "战士操作简单，具有破坏能力，非常适合新手。",
        "法师虽然体力微弱，但是可以使用很多华丽而强有力的法术。"
            + "法师施展魔法念咒文的时间比较长，往往会出现一些漏洞。"
            + "建议法师在离敌人比较远的地方进行攻击。"
            + "尚未练成超强魔法的初期，法师练级比较困难，但是随着等级上升，"
            + "修炼高级魔法，法师将成长为强硬的对手。",
        "道士对武功研究颇深，精通天文地理和医术，擅长在后方支援自己的同伴。 "
            + "道士可以召唤宠物，对魔法的抵抗力也很强，但是相反，道士的攻击力不足，"
            + "等级上升速度比较慢。为了弥补这些弱点，建议道士组成小组进行打猎，"
            + "最重要的是要有助人为乐的心肠。",
    };

    // ---- 提示 / 错误 ----
    public const string CreateFirst = "首先创建角色，才能开始游戏。";               // 219
    // 原版 F51「创建角色」handler（0x459A20-0x459AC5）在**两个槽都已被占用**时
    // 弹 LoadString 802 对话框、不进入建角；Pascal 源（IntroScn.pas::SelChrNewChrClick
    // 的 else 分支）给出同一句中文文案：
    public const string TwoCharacterLimit = "您可以为每个单独的帐号建立两个角色。";
    public const string NameTooLong = "文字过多。(韩文最多6个字)";                  // 223
    public const string NameExists = "此角色名已经存在。";                          // 224
    public const string NameInvalid = "此角色名不正确。";                           // 225
    public const string TooManyCharacters = "不能创建2个以上的角色。";              // 226
    public const string DeleteFailed = "删除角色发生错误。";                        // 227
    public const string DeleteConfirm =
        "删除的角色无法还原，一定时间内不能创建同名角色，还要删除吗？";             // 228

    /// <summary>说明框首行：「[ 男 战士 ]」（对应 SetCharExplain 里 211/212 + 213/214/215 的拼接）。</summary>
    public static string GenderJobTitle(int classIndex, bool female)
        => (female ? GenderFemalePrefix : GenderMalePrefix) + JobSuffix[Clamp(classIndex)];

    /// <summary>职业说明正文（216/217/218）。</summary>
    public static string JobDescription(int classIndex) => JobExplain[Clamp(classIndex)];

    /// <summary>选中角色详情的职业行（208/209/210）。</summary>
    public static string JobDetail(int classIndex) => JobDetailLabel[Clamp(classIndex)];

    private static int Clamp(int classIndex) => classIndex < 0 ? 0 : classIndex > 2 ? 2 : classIndex;
}
