# 特效透明度、屏幕混合与选角/建角站位层级问题：终局裁决与完整修复文档

> **记录时间**：2026-10-03 ~ 10-04  
> **状态**：**已彻底解决并结案（Closed & Verified）**  
> **关键提交**：`2f0119ae`（修复建角与选角界面特效死黑及人物层级遮挡）

---

## 一、问题背景与现象

在 Legend of Mir 3（复古 EI / Legacy UI 模式）下，用户反馈了建角与选角界面的多处严重视觉缺陷：

1. **创建人物（建角界面 Phase 2）**：
   - 法师手掌上方的动画（青色雷电光球，`Interface1c F1384..F1391`）原本应是半透明柔和发光，但画面中周围出现了一大片锯齿状的不透明死黑/灰黑色底层（颜色键抠像未生效产生的多边形锯齿云）；
   - 人物脚下的投影是纯黑硬边，死黑遮盖了地面岩石沙地；
   - 尝试修复半透明混合后，女法师腰部以上（头、胸、手臂）被背后的石壁裁切遮挡，只露出下半身裙子。
2. **选择人物（选角界面 Phase 0）**：
   - 选人洞窟中法师释放的青色雷电不仅有大片死黑背景覆盖地面，且特效动画与人物动作脱节（帧跳跃）；
   - 洞窟右下角经常莫名冒出一只巨大的女法师立绘杂影；
   - 阴影为实体死黑。

---

## 二、底层原理剖析与根本原因（四大根因）

### 2.1 根因一：素材暗部的本质是 Screen Blend，而非抠像（ColorKey）

#### (1) 素材像素数据确证
对 `Interface1c F1385`（法师女青光雷电，512×256）做逐像素解码与统计：
- 原始不透明像素数：**52,119**；
- 其中暗像素（`max(RGB) < 40`）有 **42,215** 个（占 81%）；
- 严格近黑（`RGB=(8,8,8), Alpha=255`）有 **32,717** 个；
- 这说明素材自带了均匀渐变的暗色外发光晕，**其 Alpha 通道全部是 255（不透明）**。

#### (2) 为什么颜色键（ColorKey / UseEffectTexture）和连通域（FloodFill）都是错的？
- **颜色键阈值切除**：若采用阈值（例如亮度 < 32 剔除），渐暗边缘中大于 32 的近黑像素依然保留，形成尖锐锯齿状的不透明黑多边形云；
- **四角连通域抠像**：实测 42,215 个暗像素中有 42,142 个（99.8%）与图像边缘四角直接连通。一旦执行连通域扩散，整个特效主体被抠掉 98%，只剩零星亮斑。

#### (3) 原版 Direct3D / Delphi 的真实实现：Screen Blend
逆向查阅 Delphi 原版客户端（`IntroScn.pas` 及 `Mir3.exe`）：
原版在绘制此类特效时调用的是：
```pascal
DrawBlend(..., 1); // 1 = fxAnti / D3D 屏幕混合 (Screen Blending)
```
其数学公式为：
$$\text{Output} = \text{Destination} + \text{Source} \times (1 - \text{Destination})$$

**数学美感与本质**：
- 当源像素为近黑暗色时（$\text{Source} \approx 0$）：
  $$\text{Output} \approx \text{Destination} + 0 = \text{Destination}$$
  **暗像素在数学计算后结果严格等于背景本身！等效于 100% 绝对透明！** 任何暗色晕影都不会遮盖背景与人物。
- 当源像素为青色电弧或金色烈焰等亮部时（$\text{Source} > 0$）：
  $$\text{Output} = \text{Destination} + \text{Source} \times (1 - \text{Destination})$$
  产生完美的屏幕提亮融合，高光自然发光，通透无边。

因此，**特效控件必须启用 `Blend = true, UseEffectTexture = false`**，接入 `LegacyBlendMaterial.Create()`（即 `LegacyScreenBlend.gdshader`）。

---

### 2.2 根因二：Godot 4 CanvasItem 屏幕采样缺陷与人物“被腰斩”真相

在开启 `Blend = true` 后，建角界面出现了诡异的现象：闪电确实通透了，但**女法师腰部以上的上半身凭空消失，被石壁背景遮挡，只留下一条水平截断的裙子**。

#### (1) 测量切线坐标
- 女法师锚点：`LegacyCreateAnchors[4] = (420, 115)`；
- 身体帧 `F1340` 偏移 `(-24, 82)`，实际绘制矩形为：`X: 396..548, Y: 197..447`；
- 特效帧 `F1385`（512×256）偏移 `(-27, 7)`，实际绘制矩形为：`X: 393..905, Y: 122..378`；
- **惊人巧合**：特效的底部边界正是 $122 + 256 = 378$！被遮挡的区域正是 `Y: 197..378`（女法师头部到腰部），而 $Y > 378$ 未被特效覆盖的区域正是露出的裙子！

#### (2) Godot 4 `hint_screen_texture` 运行机制
`LegacyScreenBlend.gdshader` 中使用了屏幕采样：
```glsl
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
vec4 destination = textureLod(screen_texture, SCREEN_UV, 0.0);
```
**致命陷阱**：
在 Godot 4 的 2D 渲染管线中，CanvasItem 对 `hint_screen_texture` 的捕获**不是每画一个节点就自动全屏拷贝一次**的！在同一个 CanvasLayer 下，如果节点顺序为：
1. `_selectBackground`（石壁背景）
2. `body`（女法师身体）
3. `aura`（闪电特效，带有 `LegacyScreenBlend` 着色器）

在没有显式屏障的情况下，Godot 在绘制整个图层前只拷贝了一次屏幕。当 `aura` 节点去采样 `screen_texture` 时，**采到的不是刚刚画上去的 `body`，而是还没有画人物时的石壁背景！**  
结果：`aura` 着色器计算出的 $\text{Output} = \text{石壁} + 0 = \text{石壁}$，然后以 Alpha=1 强行写入屏幕缓冲区，把女法师上半身无情地覆盖成了石壁！

#### (3) 终局方案：插入 `BackBufferCopy`
在 `body` 绘制之后、`aura` 绘制之前，插入 Godot 原生 `BackBufferCopy` 节点：
```csharp
_legacyCreateLayer.AddControl(body);
// 强制将已绘制的人物体刷新进 screen_texture 缓冲区
_legacyCreateLayer.AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
_legacyCreateLayer.AddControl(aura);
```
这样，当 `aura` 着色器采样时，`destination` 完美包含女法师身体，手托雷电与身体自然叠加融合，遮挡问题彻底根除！

---

### 2.3 根因三：选角洞窟的动画段绑定与帧号严格同步

#### (1) 原版 Delphi 证据（`IntroScn.pas:1320-1340`）
在原版选角界面中，**根本不存在所谓独立的特效循环定时器**！
```pascal
// Mir3.exe 反汇编 / IntroScn.pas:
// esi = 角色槽指针; [esi-2] + 0x28 存储特效帧偏移
// 角色身体推进时，特效帧严格满足：
AuraIndex := BodyFrameIndex + 40;
```
实测 Interface1c 各职业动画块：
- 男法师（`F1040..F1054` 15帧）：+40 对应 `F1080..F1094`（15帧橙色烈火）；
- 女法师（`F1340..F1356` 17帧）：+40 对应 `F1380..F1396`（其中 `F1384` 为手掌电球，`F1385..F1391` 为爆发雷电，其余帧为 4×2 空白占位）；
- 女道士（`F1940..F1954` 15帧）：+40 对应 `F1980..F1994`（15帧蓝色星光）；
- 男战士、男道士：+40 对应全空（无特效）。

#### (2) 修复措施
1. 将选角特效 `_slotAura0/1` 从旧的 `DXAnimatedControl`（独立计时器）改为 `DXImageControl`；
2. 在 `_Process` 调用的 `SyncLegacySlotGeometry` 中，每帧随角色身体当前帧同步赋值：
   ```csharp
   int auraIndex = idx + 40;
   Vector2I auraSize = MirSkin.GetSize(LibraryFile.Interface1c, auraIndex);
   bool hasAura = auraSize.X > 4 || auraSize.Y > 2; // 排除 4x2 等空白占位
   bool showAura = isSelected && hasAura;           // 仅在当前选中的槽显示
   aura.Visible = showAura;
   if (showAura) aura.Index = auraIndex;
   ```
   消除了特效帧乱跳、错位问题。

---

### 2.4 根因四：残留的现代 Zircon 叠加层导致洞窟右下角立绘杂影

- **现象**：在选角界面，当选中法师时，洞窟右下角常驻出现一个手持法杖的大法师半身立绘；
- **排查**：发现现代版 Zircon 客户端有 `_characterOverlay1` 和 `_characterOverlay2`（将身体帧 +100 / +130 偏移后绘制在 (450, 200)）；
- **原版对比**：EI 原版选角屏（F50）只有三层（阴影 +20、身体、特效 +40），根本没有 +100/+130 这两层；在 `Interface1c` 里 `+100/+130` 恰巧是其他职业的大立绘帧，由于 `LegacyUi` 早期只是在 `_Process` 提前 return 而未把已显示的对象隐藏，导致立绘被常驻刷在右下角；
- **修复措施**：在 `AutoLoginArgs.LegacyUi` 激活时，不创建、不添加、并显式隐藏 `_characterOverlay1/2`。

---

### 2.5 阴影纯黑硬边修复

原版 `0x457BF0` 使用 `DrawBlend(..., 0)` 以 50%~60% 的透明度绘制地面投影。
- 将 `_caveShadow0/1` 及 `_legacyCreateShadow0/1` 均显式指定：
  ```csharp
  ImageOpacity = 0.6f;
  ```
  地面投影柔和通透，透出石板沙地纹理。

---

## 三、修改文件清单与实现要点

### 1. `GodotClient/Scripts/SelectScene.cs`
- **槽位特效控件**：`_slotAura0/1` 与 `_legacyCreateAura0/1` 均设为 `Blend = true, UseEffectTexture = false`；
- **BackBufferCopy 屏幕屏障**：
  - 选角槽：在 `background.AddControl(_characterAnimation2)` 之后、`background.AddControl(_slotAura0)` 之前插入 `new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport }`；
  - 建角槽：在 `_legacyCreateLayer.AddControl(body)` 之后、`_legacyCreateLayer.AddControl(aura)` 之前插入 `new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport }`；
- **选角几何与帧同步**：在 `SyncLegacySlotGeometry` 中绑定 `idx + 40`，过滤 4×2 空白占位，仅对选中槽激活；
- **清理叠加层**：在 `LegacyUi` 下阻断 `_characterOverlay1/2` 的挂载与可见性；
- **阶段互斥**：进入创建（Phase 2）时隐藏 Phase 0 洞窟槽位与信息框，并重建 Phase 2 槽位。

### 2. `GodotClient/Scripts/AutoLoginArgs.cs`
- 开放 `--legacy-preview-create`、`--legacy-preview-class=wizard`、`--legacy-preview-female`、`--legacy-preview-slot1` 等命令行参数，支持离线全组合渲染验证。

---

## 四、实机验证与测试配方

可在 debian 82 开发机上通过 Xvfb 无头环境一键验证：

### 4.1 建角界面（女法师手托青雷 + 身体完整）：
```bash
DISPLAY=:100 godot-mono --path GodotClient -- --window --legacy-slot-preview --legacy-preview-create --legacy-preview-class=wizard --legacy-preview-female &
PID=$! && sleep 6 && DISPLAY=:100 scrot -o /tmp/verify_create.png && kill $PID 2>/dev/null
```
- **视觉判定标准**：女法师手掌上方青色电弧流转，无黑边无黑块；女法师头脸发饰、衣服、肚脐、法杖完整呈现，无水平截断。

### 4.2 选角界面（男法师挥舞烈焰 / 女法师施放雷电）：
```bash
# 男法师金色烈火
DISPLAY=:100 godot-mono --path GodotClient -- --window --legacy-slot-preview --legacy-preview-class=wizard &
PID=$! && sleep 6 && DISPLAY=:100 scrot -o /tmp/verify_cave_male.png && kill $PID 2>/dev/null

# 女法师青色雷电
DISPLAY=:100 godot-mono --path GodotClient -- --window --legacy-slot-preview --legacy-preview-class=wizard --legacy-preview-slot1 &
PID=$! && sleep 6 && DISPLAY=:100 scrot -o /tmp/verify_cave_female.png && kill $PID 2>/dev/null
```
- **视觉判定标准**：
  1. 男法师双手烈火熊熊燃烧，通透自然；
  2. 女法师雷电炸裂，与洞窟地面沙石完美融合；
  3. 洞窟右下角无任何残留立绘；
  4. 角色脚下阴影半透明透出地表纹理。

---

## 五、开发与渲染经验总结（防踩坑备忘）

1. **暗底素材绝不能用连通域抠像或 Alpha 阈值硬切**：
   - 带有黑底的发光素材是专为 **Screen Blend（屏幕混合）** 设计的，数学公式 `out = dst + src*(1-dst)` 天然使得黑底（src=0）等效于 100% 透明；
   - 强行抠像只会破坏半透明光晕和细节边缘。
2. **Godot 4 CanvasItem 中使用 `hint_screen_texture` 必须警惕 `BackBufferCopy`**：
   - 着色器内读 `screen_texture` 时，必须保证被采样的底层节点与当前特效节点之间存在 `BackBufferCopy`；
   - 缺少拷贝屏障会导致采到更早以前的底图，从而发生“上层把底层盖成更旧背景”的诡异裁切 bug。
3. **严格尊重原版帧映射规则**：
   - 原版 Interface1c 特效是固定的 `身体帧 + 40`、阴影是 `身体帧 + 20`，切勿自行发明计时器或多余的叠加层。
