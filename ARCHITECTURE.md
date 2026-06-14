# PaperTodo Architecture

这份文档给后续开发者和接手项目的大模型使用，不面向普通用户。

目标是让没有上下文的人能直接理解 PaperTodo 的代码结构、产品边界、已经验证过的技术取舍、踩过的坑，以及继续开发时需要优先保护的行为。

当前文档分工：

- `README.md`：面向用户的功能说明、操作手册、设计理念和发布形态。
- `CHANGELOG.md`：面向用户的更新记录，不放太多技术细节。
- `MAINTAINING.md`：改动前必读的维护规则、禁区和验证清单。
- `ARCHITECTURE.md`：架构说明、技术取舍、历史坑点和完整程序版本演进记录。

## 1. 项目定位

PaperTodo 是一个原生 WPF 桌面纸片工具。它没有主窗口，启动后只保留桌面上的纸片窗口和系统托盘入口。

核心目标不是做任务管理系统，而是做“桌面上几张轻量纸”：

- 快速记录。
- 快速勾选。
- 桌面直接可见。
- 不引入账号、同步、分类、标签、搜索、归档、统计。
- 尽量减少管理界面，不让软件变成另一个复杂工作台。

这个边界很重要。开发新功能时要先判断它是不是会把产品推向“完整任务管理器”。如果是，默认不做，或者只做成非常轻的局部能力。

## 2. 技术栈

- 语言：C#。
- UI：WPF。
- 目标框架：`net10.0-windows`。
- Markdown 编辑 / 浏览：`AvalonEdit`。
- 托盘图标：`Hardcodet.NotifyIcon.Wpf`。
- 状态文件：程序目录下的 `data.json` 和 `data.backup.json`。
- 程序没有数据库、后台服务、WebView、Electron、Tauri 或浏览器内核。

项目文件是 `PaperTodo.csproj`。当前版本号由这里统一控制：

```xml
<Version>2.0</Version>
<AssemblyVersion>2.0.0.0</AssemblyVersion>
<FileVersion>2.0.0.0</FileVersion>
<InformationalVersion>2.0</InformationalVersion>
```

托盘菜单顶部显示的版本号来自程序集元数据，不要再做成手写字符串。

不要恢复旧的 MSBuild 后置自动递增版本号方案。版本号必须显式维护，否则普通本地构建会修改项目文件，容易污染发布和代码审查。

## 3. 运行模型

程序启动入口在 `App.xaml.cs`。

启动流程：

1. 注册全局异常处理。
2. 解析启动参数为 `StartupCommand`。
3. 通过 `SingleInstanceHelper` 抢单实例锁。
4. 如果已经有主实例，向主实例转发原始启动参数，然后当前进程退出。
5. 创建 `AppController`。
6. 设置 `ShutdownMode = OnExplicitShutdown`。
7. 如果当前命令是 `exit` / `quit`，直接同步保存并退出，不恢复窗口，也不在空数据目录里创建默认纸片。
8. 其他命令调用 `AppController.Start(createDefaultPaper: !startupCommand.CreatesPaper)`。
9. 执行当前进程的启动命令。
10. 启动命名管道监听，后续实例启动时会把参数转发给主实例执行；无参数的后续实例默认按 `show` 处理。

启动命令由 `StartupCommand.cs` 解析。参数会去掉前导 `-` 或 `/` 后再匹配，当前支持：

- `show` / `open`
- `hide`
- `toggle`
- `new-todo` / `todo`
- `new-note` / `note` / `paper`
- `exit` / `quit`

因为没有主窗口，进程生命周期由 `AppController`、托盘图标和纸片窗口共同维持。退出时不能只隐藏托盘或关闭窗口，必须保证进程真正结束。

当前退出策略：

- `AppController.Exit()` 会先同步保存。
- 释放托盘资源。
- 调用 `Application.Current.Shutdown()`。
- 最后 `Environment.Exit(0)`，用于避免“右下角托盘图标没了但进程残留”的情况。

这是一种偏硬的退出方式，但当前项目里是有意保留的，因为用户已经遇到过退出后残留进程的问题。

## 4. 主要文件职责

### `App.xaml.cs`

负责应用级生命周期：

- 单实例控制。
- 全局未处理异常处理。
- 崩溃日志 `PaperTodo.crash.log`。
- 崩溃恢复文件 `data.crash_recovery.json`。
- 退出时释放控制器和单实例资源。

崩溃日志最大约 100 KB。超过后保留末尾约 80 KB 并写入裁剪标记。不要让日志无限增长。

### `AppController.cs`

这是项目的中心协调器。它负责：

- 持有 `AppState`。
- 创建、显示、隐藏、删除纸片。
- 管理 `Dictionary<string, PaperWindow>`。
- 管理托盘图标和托盘菜单。
- 管理主题。
- 管理 MD 解析模式。
- 管理胶囊模式和胶囊自动贴边。
- 执行启动命令。
- 管理自动保存。
- 处理保存失败提示。
- 拉回屏幕外纸片。
- 处理开机自启动。
- 退出程序。

大部分跨窗口、跨纸片、跨全局状态的逻辑都应该放在这里，而不是塞进 `PaperWindow`。

从某张纸片上新建纸片时，`CreatePaper(..., sourcePaper)` 会继承源纸片的置顶状态，并在显示后通过短暂置顶把新窗口带到源纸片前面。不要改回单纯 `Show()` / `Activate()`，否则在源纸片置顶或窗口管理器没有及时调整 z-order 时，新纸片容易出现在当前纸片后面。

### `PaperWindow.cs`

这是单张纸片窗口。它负责：

- 无边框 WPF 窗口。
- 顶栏、正文、缩放手柄。
- 待办纸 UI。
- 笔记纸 UI。
- Markdown 编辑和只读浏览切换。
- 纸片右键菜单。
- 待办项拖动排序。
- 待办项拖到末尾删除区删除。
- 待办撤销/重做。
- 胶囊形态 UI、动画和自动贴边。
- 纸片大小、位置变化时通知 `AppController.UpdateGeometry()`。

注意：`PaperWindow` 直接绑定的是同一个 `PaperData` 实例，不是 ViewModel 副本。因此 UI 操作修改数据后要记得调用 `_controller.MarkDirty()` 或经过会标脏的控制器方法。

### `StateStore.cs`

负责状态读写。

状态路径：

- 主文件：`data.json`
- 备份：`data.backup.json`

读：

- 优先读主文件。
- 主文件失败时尝试备份文件。
- 两者都失败时抛出本地化错误。

写：

- 先序列化。
- 写到 `data.json.tmp`。
- 尝试把旧 `data.json` 复制为 `data.backup.json`。
- 再把临时文件移动为正式 `data.json`。

写入有版本号和锁：

- `AppController` 每次保存递增 `_saveVersion`。
- `StateStore` 用 `_latestWrittenVersion` 避免旧的异步保存覆盖新的状态。
- `SemaphoreSlim` 保证同一时间只有一个写入。

状态规范化会修复旧数据和异常恢复数据中的低风险问题：补齐缺失/重复 ID、清理不存在的笔记关联、回退非有限窗口几何、丢弃 `papers` / `items` 数组里的空元素，并收束依赖失效的胶囊设置。

读取使用 `JsonSerializerOptions.Strict`。重复属性、未知属性或非法成员应该尽早失败，避免脏数据被正常化后继续覆盖主数据和备份。启动失败时不能用空状态覆盖旧文件。

### `Models.cs`

定义持久化数据结构：

- `AppState`
  - `Papers`
  - `Theme`
  - `ColorScheme`
  - `MarkdownRenderMode`
  - `ExternalMarkdownExtension`
  - `UseCapsuleMode`
  - `UseDeepCapsuleMode`
  - `UseCapsuleCollapseAll`
  - `CapsuleCollapseAllActive`
  - `ShowDeepCapsuleWhileExpanded`
  - `EnableAnimations`
  - `EnableToolTips`
- `PaperData`
  - `Id`
  - `Type`
  - `Title`
  - `X`
  - `Y`
  - `Width`
  - `Height`
  - `IsVisible`
  - `AlwaysOnTop`
  - `IsCollapsed`
  - `TextZoom`
  - `Items`
  - `Content`
- `PaperItem`
  - `Id`
  - `Text`
  - `Done`
  - `Order`
  - `LinkedNoteId`

继续开发时要把 `data.json` 当成用户数据兼容协议。新增字段可以有默认值；删除或重命名字段要非常谨慎。

### `Theme.cs`

集中定义浅色、深色和系统主题。

主题模式保存在 `AppState.Theme`。`AppController.SetTheme()` 修改该字段后，会刷新托盘菜单和所有纸片窗口。WPF 控件里的颜色大多通过资源 key 和动态资源刷新。

### `Strings.cs` 和 `Resources/*.resx`

`Strings.cs` 是资源访问入口。

当前资源：

- `Resources/Strings.resx`：默认中文。
- `Resources/Strings.en.resx`：英文。
- `Resources/Strings.ja.resx`：日文。
- `Resources/Strings.ko.resx`：韩文。

新增任何用户可见文本，都要同步补齐这几个资源文件。

`ResourceTextVersion` 是人工检查用的资源版本标记，不是运行时逻辑。不要把它做成启动校验、版本拒绝或资源同步机制。

### `MarkdownTextBox.cs`

笔记编辑和浏览共用的 AvalonEdit 扩展，主要处理 Markdown 快捷键、轻量高亮、换行、粘贴长度限制和点击定位。

笔记纸当前把 `MaxLength` 设为 `100000`。这个上限是为了防止异常超大粘贴拖垮 WPF 测量和渲染，不要随意移除。

全局 `AppState.MarkdownRenderMode` 控制它的视觉解析强度：

- `off`：不做额外 Markdown 样式、块背景、链接命中或列表覆盖。
- `basic`：保留基础 Markdown 样式和链接命中，不淡化 Markdown 标记，不覆盖无序列表符号。
- `enhanced`：默认模式；浏览态淡化 Markdown 结构符号和原始链接 URL，并把无序列表 `-` / `+` / `*` 覆盖显示为圆点，任务列表不额外覆盖。

### `TodoTextBox.cs`

待办项编辑用 TextBox 扩展，配合 `PaperWindow` 的待办逻辑处理输入、粘贴和快捷键。

### `StartupCommand.cs`

启动命令解析器。它不执行动作，只把启动参数归一化为 `StartupCommandKind`。

`CreatesPaper` 用来区分 `new-todo` / `new-note` 这类会自己创建纸片的启动命令。首次启动且没有数据时，如果命令本身会创建纸片，就不再额外创建默认待办纸，避免一次启动生成两张纸。

### `SingleInstanceHelper.cs`

单实例工具：

- Mutex 判断是否已有实例。
- 命名管道把后续实例的原始启动参数转发给主实例。
- 主实例收到参数后重新解析为 `StartupCommand` 并执行。
- 后续实例无参数时，主实例默认按 `show` 执行。

只有实际抢到 Mutex 的主实例可以释放 Mutex。次要实例只负责发送命令并退出，不能释放主实例持有的锁，否则会复发多进程启动/退出时的崩溃和并发读写风险。

### `SystemSettingsHelper.cs`

处理开机自启动相关系统设置。

写入 Windows 启动项时，执行路径必须加引号，避免安装目录含空格时被截断。

### `ClipboardHelper.cs`

剪贴板辅助。

剪贴板读取必须隔离异常。第三方剪贴板管理器、远程桌面或系统瞬时占用都可能让 `Clipboard.GetDataObject()` 抛错，业务代码不应该直接读剪贴板。

## 5. 数据模型和保存语义

PaperTodo 的状态就是纸片本身。

重要语义：

- 删除才是真删除。
- 隐藏不是删除。
- 关闭单张纸片在产品语义上通常是隐藏。
- 重新启动时会恢复所有非删除纸片。
- 纸片位置、大小、置顶、可见性、胶囊状态都属于状态。
- 待办项顺序由 `Order` 表示，但 `StateStore.Normalize()` 会按当前列表顺序重排。

全局纸片数量上限是 100。这个限制用于防止连点或脚本创建过多 WPF 窗口耗尽 GDI/User 句柄，不是产品容量设计。

保存节流：

- `AppController.MarkDirty()` 启动 450 ms 定时器。
- 定时器触发后调用 `SaveNow()`。
- 退出时使用同步保存。

启动恢复：

- `AppController.Start()` 会先创建托盘图标。
- 如果没有纸片，创建一张待办纸。
- 如果有纸片，先用 `EnsurePapersOnScreen()` 拉回屏幕外纸片。
- 然后显示所有已有纸片。
- 启动恢复期间 `_suppressDirty = true`，避免恢复显示本身触发大量无意义保存。
- 如果确实拉回了屏幕外纸片，才保存新位置。

## 6. 托盘实现

托盘是全局入口，代码集中在 `AppController.cs`。

当前正确路径：

```csharp
_trayMenu = CreateTrayMenu();
_trayMenu.Opened += (_, _) => RebuildTrayMenu();

_trayIcon = new TaskbarIcon
{
    ToolTipText = "PaperTodo",
    IconSource = LoadTrayIconSource(),
    ContextMenu = _trayMenu,
    Visibility = Visibility.Visible
};
```

`RebuildTrayMenu()` 在菜单打开时重建内容，确保纸片状态、主题、自启动状态、胶囊状态都是最新的。

### 托盘图标规则

托盘图标加载顺序：

1. 程序目录下的外部 `PaperTodo.ico`。
2. 程序内嵌资源 `assets/icons/PaperTodo.ico`。
3. 代码绘制的 fallback vector icon。

外部 `PaperTodo.ico` 是允许用户自定义托盘图标，不是兜底文件。也就是说：如果外部文件存在，就优先用外部文件。

### Hardcodet.NotifyIcon.Wpf 的关键坑

这里曾经有一个非常隐蔽的 v1.3 回归：

- 把托盘图标从 `IconSource = LoadTrayIconSource()` 改成 `Icon = System.Drawing.Icon` 后，第一次右键托盘菜单会从桌面最右下角弹出。
- 随后第一次点击主程序纸片会被吞掉。
- 后续再打开菜单又看似正常。

根因不是删除纸片多态，也不是菜单确认态本身，而是 Hardcodet 的 WPF `IconSource` 路径和 WinForms `Icon` 路径在首次弹出 ContextMenu 时行为不同。

当前结论：

- 对这个项目，托盘图标必须走 `IconSource`。
- 不要改回 `System.Drawing.Icon`。
- 不要为了图标内嵌或自定义托盘图标重新引入 `Icon` 属性。

### 不要重新引入的托盘菜单修复

以下方案都试过，结果要么无效，要么引入更差的点击问题：

- 手动用 `PlacementMode.MousePoint` 打开托盘菜单。
- 调用 `SetForegroundWindow`。
- `PostMessage` 发送空消息。
- `ThreadFilterMessage`。
- 菜单预热。
- 启动瞬间屏幕外预打开。
- 外部点击轮询关闭菜单。
- 鼠标移到纸片上就强行关闭菜单。

这些都不要作为“第一次点击被吞”的修复重新加回来。

如果后续再次出现托盘菜单第一次位置错误或第一次点击被吞，先检查是否有人改动了：

- `TaskbarIcon.IconSource`
- `TaskbarIcon.Icon`
- `ContextMenu` 自动绑定方式
- `CreateTrayIcon()`
- `LoadTrayIconSource()`

## 7. 托盘菜单删除确认设计

托盘菜单中的纸片列表行支持内联删除确认。

当前设计：

- 普通态：左边是纸片标题和预览，右边是 `×` 删除入口。
- 点击 `×` 不直接删除，而是进入确认态。
- 确认态左侧显示警示语义，例如 `⚠ 删除`。
- 右侧有两个可点击区域：`确认` 和 `取消`。
- 两个操作都需要有 hover/pressed 预先选择态。
- 确认和取消的位置要清晰，避免误删。

相关资源 key：

- `TrayInlineConfirmDelete`
- `TrayInlineConfirmAction`
- `CommonCancel`

新增或修改这块 UI 时，必须同步四种语言资源。

## 8. 纸片窗口设计

纸片是 `PaperWindow`，每张纸都是独立的 WPF 无边框窗口。

窗口基本结构：

- 外层带圆角、边框和阴影的 `Border`。
- 顶栏。
- 内容区。
- 底部缩放手柄。
- 拖拽 overlay 层。
- 胶囊 overlay 层。

窗口属性：

- `ShowInTaskbar = false`
- `WindowStartupLocation = Manual`
- `WindowStyle = None`
- `AllowsTransparency = true`
- `Background = Transparent`
- `Topmost` 来自 `PaperData.AlwaysOnTop`，胶囊形态下会临时有效置顶

### 顶栏

顶栏负责：

- 拖动窗口。
- 左侧纸片类型图标，同时作为置顶开关。
- 右侧新建待办、新建笔记、隐藏/折叠按钮。

当前顶栏做了轻微压暗和底部分隔线，用于增强纸片层次。不要把它做成明显工具栏或重型标题栏，纸片感要保留。

### 显示和隐藏

`AppController.ShowPaper()` 显示隐藏窗口时，有一个透明首帧处理：

```csharp
double originalOpacity = window.Opacity;
window.Opacity = 0;
window.Show();
window.Dispatcher.InvokeAsync(() => window.Opacity = originalOpacity, DispatcherPriority.Render);
```

目的是避免隐藏窗口尺寸变化后重新显示时出现 DWM 缓存闪烁。不要轻易删除，除非你能验证隐藏/显示、胶囊恢复、显示全部都没有一帧错乱。

### 胶囊模式

胶囊模式由全局 `AppState.UseCapsuleMode` 控制。

单张纸还有 `PaperData.IsCollapsed`，表示这张纸当前是否折叠成胶囊。

胶囊自动贴边由 `AppState.UseDeepCapsuleMode` 控制。它是胶囊模式的附加行为：开启时会自动启用胶囊模式，并将当前可见的折叠胶囊按纸片顺序排列到屏幕右上角。

当前贴边胶囊的几何和共享常量集中在 `DeepCapsuleLayout.cs`。单张纸的贴边槽位不再由独立的 `DeepCapsuleSlotWindow` 维护，而是由 `PaperWindow` 内部的 slot host 承载；这样折叠胶囊、展开后的边缘激发态、关闭区、标题测量和描边状态复用同一套胶囊 UI 度量。

新建 `AppState` 时，`UseCapsuleMode` 和 `UseDeepCapsuleMode` 默认都是 `true`。已有数据里如果明确保存了 `false`，加载时不要强行改成 `true`。

设计要求：

- 启用胶囊模式后，右上角关闭按钮折叠成胶囊，而不是隐藏纸片。
- 折叠成胶囊时默认有效置顶；恢复普通纸片后回到用户原本的置顶状态。
- 关闭胶囊模式后，所有纸片恢复普通纸片形态。
- 隐藏全部纸片时，必须清掉 `IsCollapsed`，避免之后显示全部出现尺寸或状态 bug。
- 拖动胶囊时，待点击状态应该消失，避免拖动结束误触发。
- 胶囊左侧和右侧热区高度、视觉反馈要一致。
- 胶囊宽度按图标、标题和关闭区测量自适应，但窗口仍有最小宽度保护，避免短标题挤压热区。
- 启用胶囊自动贴边时，胶囊 UI 度量保持同源，只改变窗口位置：常态只露出图标和标题，关闭区藏在屏幕外；悬浮或激发态时滑出更多宽度。
- 从贴边胶囊展开纸片时，`ShowDeepCapsuleWhileExpanded` 决定是否保留右侧高亮胶囊并占用原槽位；关闭该设置时，展开期间隐藏边缘胶囊并释放槽位，纸片收回胶囊后再显示。
- 贴边激发态有实线外描边。描边态只是同一套胶囊 UI 的持久外移和强调状态，不应再维护一套重绘 UI。
- `UseCapsuleCollapseAll` 会显示一个主胶囊占用 slot 0，真实纸片胶囊从 slot 1 开始排列；`CapsuleCollapseAllActive` 为真时，真实胶囊会缩回主胶囊位置并隐藏点击面。
- 胶囊自动贴边的位置是运行时位置，不要把半隐藏坐标写入 `paper.X` / `paper.Y`。

这块很容易发生“隐藏纸片后再恢复显示尺寸错乱”的回归。改动后必须手动测。

## 9. 待办纸逻辑

待办纸数据来自 `PaperData.Items`。

主要行为：

- 至少保持一个空待办项。
- Enter 在当前项下方新增。
- 空项 Backspace 删除。
- 多行粘贴拆分为多条待办。
- 粘贴时清理常见列表前缀。
- 勾选完成只改变显示状态，不自动移动到底部。
- 支持拖动排序。
- 支持拖到末尾追加区变成的删除区删除。
- 支持 `Ctrl+Z` / `Ctrl+Y`。

待办撤销/重做：

- `PaperWindow` 内维护 `_undoStack` 和 `_redoStack`。
- 快照是 `List<PaperItem>` 的克隆。
- 最大深度 `MaxUndoDepth = 100`。
- 多行粘贴应该只入栈一次。不要为粘贴拆出的每一行分别推撤销快照，否则一次 `Ctrl+Z` 无法干净恢复粘贴前状态。

拖动排序相关状态：

- `TodoDragState`
- `_dragLayer`
- `_activeDropRow`
- `_appendArea`

拖动逻辑比较集中但脆弱。改动时要覆盖：

- 同一列表中上移。
- 同一列表中下移。
- 拖到最后。
- 拖到删除区。
- 拖动中取消。
- 窗口失焦时取消拖动。

## 10. 笔记纸和 Markdown 逻辑

笔记纸使用同一个 `MarkdownTextBox` 在编辑和只读浏览之间切换。

基本模型：

- 内容区只有一个 `MarkdownTextBox` 实例。
- 编辑时切换为可编辑状态。
- 浏览时切换为只读状态。
- 内容保存在 `PaperData.Content`。
- 两态共用同一份 AvalonEdit 文档、换行、缩进和滚动模型，避免浏览 / 编辑切换时文本跳动或滚动条长度变化。
- 托盘 `MD 解析` 三档设置写入 `AppState.MarkdownRenderMode`，修改后调用 `PaperWindow.UpdateMarkdownRenderMode()` 刷新所有已打开笔记纸。
- `MarkdownTextBox.RefreshVisualStyle()` 必须同时刷新 AvalonEdit 的 Background、Text 和 Caret 层；只刷新背景层会导致列表圆点、有序标号覆盖、链接标记淡化等附加渲染需要进出编辑态一次才更新。

Markdown 支持范围应该保持轻量：

- 标题。
- 加粗、斜体、删除线。
- 无序/有序列表。
- 引用。
- 行内代码。
- 代码块。
- 链接。

增强模式只做视觉补丁，不要引入完整 Markdown 文档模型。列表判断可以做少量局部解析：支持 `1.` / `2)` 作为有序列表，支持前导空格后的 `-` / `+` / `*` 作为无序列表，`- [ ]` / `- [x]` 视为任务列表，不再额外覆盖圆点。增强模式下无序列表淡化源符号并覆盖圆点；有序列表淡化源标号后覆盖显示正常颜色的数字标号，避免数字列表在渲染态消失。

不建议加入：

- 图片。
- 表格。
- HTML。
- 附件。
- 嵌入式内容。
- 复杂块编辑器。

加入这些能力会让一张纸变成文档编辑器，和产品边界冲突。

## 11. 主题与资源刷新

主题模式在 `AppState.Theme` 中保存，值通常为：

- `system`
- `light`
- `dark`

主题变化后要做三件事：

1. 将 `AppState.Theme` 设为 `system`、`light` 或 `dark`。
2. 保存状态。
3. 刷新所有 `PaperWindow.UpdateTheme()`。
4. 刷新托盘菜单。

WPF 动态资源能处理部分刷新，但不是全部。尤其是代码里动态生成的控件、AvalonEdit 文本视图、托盘菜单项，都需要主动刷新视觉样式。

MD 解析模式变化同理：保存 `AppState.MarkdownRenderMode` 后，要刷新所有笔记纸的 `MarkdownTextBox` 并重建托盘菜单。

## 12. 多语言规则

用户可见文本必须走资源文件。

新增文本时至少修改：

- `Resources/Strings.resx`
- `Resources/Strings.en.resx`
- `Resources/Strings.ja.resx`
- `Resources/Strings.ko.resx`

不要只改中文。

资源版本：

- `ResourceTextVersion` 是人工检查用。
- 它不参与运行时逻辑。
- 不要写启动时资源版本校验。
- 不要因为资源版本不匹配阻止程序运行。

## 13. 打包和发布策略

当前官方发布思路是两个直接发布的 Windows x64 单文件 exe，由 `.github/workflows/release.yml` 在 GitHub Actions 中构建。

- 推送 `v*` tag 或手动触发 workflow 时创建 / 更新 GitHub Release。
- 推送 `main` 时构建并上传 Actions artifact，用于提前检查软件包。
- Release 资产直接上传 exe，并附带 `SHA256SUMS.txt`、`.sig` 和 `.crt`，不再套 zip。
- 两个 exe 都在 GitHub Actions 中使用 Sigstore/cosign keyless 签名；这是基于 GitHub OIDC 身份的云端签名，不是 Windows Authenticode 代码签名。

### 主发布版

自包含、单文件、R2R、压缩、不 Trim。

参考参数：

```powershell
dotnet publish .\PaperTodo.csproj -c Release -r win-x64 --self-contained true -o 输出\PaperTodo-v<版本>-win-x64-self-contained-compressed\ -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false
```

特点：

- 用户不需要安装 .NET Desktop Runtime。
- 文件更大。
- 单文件压缩主要影响冷启动，运行中影响很小。
- R2R 对冷启动有一定帮助，但收益有限，需要实测。

### 轻量版

框架依赖、单文件、不 R2R、不压缩、不 Trim。

参考参数：

```powershell
dotnet publish .\PaperTodo.csproj -c Release -r win-x64 --self-contained false -o 输出\PaperTodo-v<版本>-win-x64-no-runtime-uncompressed\ -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false
```

特点：

- 用户机器需要安装对应 .NET Desktop Runtime。
- 体积更小。
- 框架依赖单文件不能用自包含那套 R2R 预期。
- 不要对它开压缩来期待收益；依赖不被一起打进单文件时，压缩意义很小。

### 不建议使用 Trim 和 Native AOT

不要给 WPF 版本开启：

- `PublishTrimmed=true`
- Native AOT

原因：

- WPF 本身和部分库依赖反射、资源、XAML 编译产物。
- 强开 Trim 已经出现过运行崩溃。
- Native AOT 对 WPF 桌面应用并不是正常支持路径。
- “Trim + Native AOT 一起强开”不是合理优化方向。

如果未来要探索，只能作为独立实验分支，不要合入正式发布参数。

## 14. 维护与验证入口

变更前必读规则、禁区、验证清单和高风险区域见 `MAINTAINING.md`。

完整程序版本演进记录见下一节，用于检查历史技术取舍、已经验证过的问题和版本间行为变化。

## 15. 完整程序版本演进记录

这些记录按功能阶段整理，不一定对应公开发布版本。`CHANGELOG.md` 是用户态简版；本节保留更多技术细节用于回查。

### v0.1 初始构建

- 创建 WPF / .NET 10 项目。
- 建立无主窗口的纸片模型。
- 实现基础纸片窗口、待办纸和本地 JSON 数据结构。
- 确认项目不使用 WebView、Tauri、Electron、MSIX / Store / AppX。

### v0.2 纸片生命周期

- 支持多张独立纸片。
- 实现启动恢复所有未删除纸片。
- 实现关闭 = 隐藏、删除 = 确认移除。
- 保存位置、大小、置顶状态和内容。
- 增加 `data.backup.json` 备份与损坏恢复。
- 启动或显示纸片时自动拉回屏幕外窗口。

### v0.3 待办纸基础交互

- 实现 checkbox 完成 / 取消完成。
- 完成项显示删除线并弱化。
- 支持 Enter 新增、Backspace 删除空项。
- 支持底部 `+` 追加区。
- 支持删除单项、清理已完成。
- 调整空事项规则：至少保留一个空白输入行。
- 允许创建并保留多个空待办项，避免输入节奏被自动整理打断。

### v0.4 托盘与纸片入口

- 接入托盘入口。
- 支持新建待办纸、新建笔记纸、显示全部、隐藏全部、退出。
- 托盘菜单列出当前所有纸片，并可切换单张纸显示 / 隐藏。
- 纸片名称使用内容预览自动生成。
- 左上角类型图标改为置顶开关。
- 托盘图标优先读取同级目录 `PaperTodo.ico` 作为自定义图标。
- 托盘纸片列表支持长标题省略，防止菜单被长文本拉宽。

### v0.5 菜单与视觉整理

- 右键菜单按场景分组：事项、格式、编辑、这张纸。
- 菜单去掉传统系统菜单左侧空白预留区。
- 滚动条改为纸片风格细滚动条。
- 删除纸片确认弹窗改为自绘弹窗。
- 纸片边框、阴影、圆角、hover 状态继续统一。
- 自定义标题栏按钮样式，消除 Windows 默认蓝色预选框与焦点框。
- 美化待办勾选框，使用暖褐色圆角边框和自绘白色对勾替代默认复选框。

### v0.6 待办拖动与粘贴

- 待办拖动排序加入落点提示。
- 右侧拖动手柄扩大为完整透明点击区。
- 拖动中不修改数据、不重建列表，松手后提交顺序。
- 优化拖拽指示线为上方 / 下方单行插入线。
- 多行粘贴加入轻清洗，处理 Markdown checkbox、项目符号、数字列表和勾选符号。
- 修复拖拽项 `Order` 字段导致的排序不生效问题。
- 拖动待办项时，底部追加区可转换为删除区，支持拖到底部删除区删除。
- 单次多行粘贴条目数上限设为 200 条，避免异常大文本拖垮 UI。

### v0.7 笔记纸与 Markdown

- 接入 Markdown 源文本的原生预览能力。
- 使用 WPF 原生控件渲染基础 Markdown 内容。
- 不转 HTML，不使用 WebView。
- 笔记纸支持编辑 / 预览切换。
- 支持标题、引用、列表、代码块、超链接等基础格式。
- 修复格式快捷键与右键操作破坏 Undo / Redo 栈的问题。

### v0.8 输入体验与基础稳定性

- 待办焦点恢复改用内部编辑器字典。
- 修复首行空待办 Backspace 后焦点丢失。
- 优化中文输入法下 Enter 行为，避免拼音输入误触新增。
- 保存失败通过托盘气泡弱提示。
- 静态 WPF Brush 冻结，减少无意义运行时噪音。
- 修复 Windows 关机或注销时被普通隐藏逻辑拦截的问题。
- 粘贴文本时捕获剪贴板并发异常，避免第三方剪贴板占用导致崩溃。

### v0.9 发布工程整理

- 引入 `Hardcodet.NotifyIcon.Wpf`，托盘从 WinForms 迁到 WPF 语境。
- 引入 Mutex 单实例运行，避免多进程并发读写。
- 曾试验 MSBuild 编译后自动递增版本号，后续改为显式维护版本号。
- 修正项目文件编码，避免输出目录乱码。
- 修复多进程启动拦截时，非 Mutex 持有者进程在退出流程中释放互斥锁导致崩溃的问题。

### v0.10 聚焦式 Markdown 预览

- 笔记纸默认以 Markdown 格式渲染预览。
- 点击笔记内容进入编辑，离开焦点后自动恢复 Markdown 渲染。
- 取消 `Ctrl+P` 等预览切换快捷键，改为聚焦 / 失焦逻辑。
- 点击笔记纸顶部标题栏或非内容区时，会主动解除编辑框焦点并触发预览。
- 修复点击预览区文本行时，由非 Visual 元素父级查找导致的崩溃问题。
- 增加统一的安全父级查找助手，覆盖 Visual、Visual3D、FrameworkContentElement 与 ContentElement。

### v0.11 主题与系统设置

- 在托盘菜单中增加浅色、深色、跟随系统三种主题模式。
- 重构静态颜色笔刷为 WPF `DynamicResource` 动态资源，支持运行时实时换肤。
- 深色模式采用暗炭 / 深褐背景与暖乳白前景，保持纸片质感。
- 主题状态保存到 `data.json`，下次启动自动恢复。
- 跟随系统主题时，监听 Windows 系统主题变化并自动刷新所有纸片。
- 增加开机自启动勾选项，安全读写当前用户注册表启动项。
- 待办与笔记文本框的光标颜色跟随主题变化，修复深色模式下光标不可见问题。

### v0.12 托盘与菜单打磨

- 优化右键菜单和任务栏托盘菜单外观，使用圆角、内边距和更紧凑的行高。
- 从笔记编辑菜单和待办项右键菜单中移除部分系统通用命令，保持菜单轻。
- 将“跟随系统 / 浅色 / 深色”从垂直菜单改为横向分段式选择器。
- 自定义托盘菜单项模板，消除系统默认悬停蓝色边框。
- 分段选择器按钮自适应撑满菜单，减少右侧留白。
- 托盘菜单和右键快捷菜单整体收紧，减少屏幕遮挡。
- 托盘纸片列表支持无弹窗二次确认删除，避免在纸片较多时拉高菜单。

### v0.13 待办撤销与编辑连续性

- 为待办纸片加入最深 100 步的全局历史记录栈。
- 支持撤销 / 重做勾选完成、清理已完成、新增、删除、拖拽排序等修改。
- 编辑待办文字时，`Ctrl+Z` 优先触发文本框局部字符撤销。
- 文本框无可撤销内容后，再次按键自动过渡到全局列表撤销。
- 右键待办事项时先聚焦当前输入框，使编辑光标和操作目标一致。
- 待办右键菜单打开期间锁定当前行 hover 背景，避免视觉焦点游离。

### v0.14 性能与代码减重

- 收拢笔记纸浏览和渲染路径，降低频繁点击带来的 UI 开销。
- 清理笔记渲染相关代码中残留的冗余样式构建器。
- 移除多次 UI 升级后不再使用的静态 Brush 和辅助代码，保持代码轻量。

### v0.15 数据保护与保存安全

- 当本地配置和备份全部损坏时，阻止程序以空白状态覆盖旧数据。
- 保存前在主线程截取稳定快照，实际 I/O 使用 `SemaphoreSlim` 在后台线程串行执行。
- 降低高速编辑时多次保存引起的读写冲突和卡顿。
- 托盘图标加载加入异常保护，图标损坏、格式不符或被锁定时自动降级到自绘图标。
- 注册全局未处理异常兜底事件，在致命崩溃前尝试写出 `data.crash_recovery.json` 抢救备份。
- 崩溃恢复文件不覆盖原有 `data.json`，避免二次破坏主数据。

### v0.16 单实例唤醒与系统隔离

- 双击启动第二个实例时，次要实例通过本地命名管道通知主实例。
- 主实例收到激活信号后，将纸片显示、置顶并唤醒聚焦。
- 次要实例发送信号后迅速退场，避免多进程同时读写数据。
- 创建 `SingleInstanceHelper`，集中处理 Mutex 持有、释放与注销。
- 创建 `SystemSettingsHelper`，隔离自启动注册表相关风险。
- 创建 `ClipboardHelper`，隔离剪贴板读取和异常防护。
- 主程序仅由抢占 Mutex 成功的进程释放锁，避免次要实例误释放。

### v0.17 拖拽中断与边界防御

- 拖拽待办项时监听 `LostMouseCapture`，意外失去鼠标捕获时自动撤回拖拽。
- 拖动过程中窗口 `Deactivated` 时同步回滚状态，防止 Alt+Tab 等操作造成拖拽卡死。
- 异常中断后彻底销毁残留的幽灵悬浮框。
- 拖动排序状态机补齐中断分支，避免 UI 与数据顺序出现不一致。
- 继续修正拖拽删除、边界判定和吸附指示线相关细节。

### v0.18 严格数据序列化与解析限制

- 启用 .NET 10 严格 JSON 选项：在 `StateStore.cs` 中启用 `JsonSerializerOptions.Strict` 模式进行数据读取校验。
- 检测到重复属性、未知属性或非法成员时严格报错，避免脏数据写入损坏正常的备份文件。

### v1.0.0 正式版发行

- 更新并合成全新应用图标 `PaperTodo.ico`，优化托盘与任务栏图标表现。
- 重构并隔离单实例命名管道唤醒、自启动注册表配置和剪贴板操作。
- 启用严格 JSON 反序列化，在致命崩溃时尝试写出 `data.crash_recovery.json`。
- 收拢笔记浏览路径和托盘菜单，以纯 WPF 原生技术栈实现动态换肤和交互。

### v1.1 胶囊模式

- 新增胶囊形态，允许将纸片折叠为 108x46 的小尺寸窗口。
- 重写基于 WPF 依赖属性的宽高双轴过渡动画。
- 通过显示前透明首帧处理减少隐藏窗口尺寸变化后的 DWM 缓存闪烁。
- 胶囊形态下临时有效置顶，恢复普通纸片后回到用户原本置顶状态。
- 托盘菜单支持一键开关全局胶囊模式。

### v1.1.1 边界防御与渲染修复

- 为 `MarkdownTextBox` 补充 10 万字 `MaxLength` 输入上限，防止异常大文本拖垮 WPF。
- 新建纸片最高 100 张，防止连点或脚本消耗过多 GDI/User 句柄。
- 修复编辑态切换主题导致浏览态配色不刷新。

### v1.1.2 性能与并发安全

- 修复后台保存任务中跨线程读写竞态，提示状态收束到 UI 线程。
- 自绘保存失败弹窗，增加“忽略本次运行”。
- 多行粘贴只入栈一次撤销快照。
- 保存前备份语义改为覆盖主文件前先 Copy。
- 启动项注册表路径加引号，避免路径含空格时截断。
- 高频动态笔刷和字体实例收拢为静态只读或冻结对象，减少 UI 刷新期分配。

### v1.2 多语言与发布工程整理

- 用户界面文本集中到 `Resources/Strings*.resx`。
- 新增英文、日文、韩文语言资源，并通过 `SatelliteResourceLanguages` 限制输出语言包。
- 加入 `ResourceTextVersion` 人工核对标记。
- 托盘菜单顶部显示程序集版本号。
- 验证 WPF 项目无法直接使用 Native AOT，保留自包含、单文件、压缩、ReadyToRun 的发布思路。

### v1.3 托盘与胶囊细节整理

- 托盘纸片列表删除入口收进纸片行右侧，删除确认在同一行内完成。
- 支持 `--show`、`--hide`、`--toggle`、`--new-todo`、`--new-note`、`--exit` 启动参数。
- 程序已运行时，后续实例通过命名管道把命令转发给当前实例执行。
- 删除确认支持确认 / 取消，并同步多语言资源。
- 纸片删除弹窗调整按钮顺序，降低误删风险。
- 托盘图标保持使用 WPF `IconSource` 路径，避免首次菜单定位异常和首次点击被吞。
- 外部 `PaperTodo.ico` 优先作为自定义托盘图标。
- 修复胶囊模式下隐藏全部再显示全部的状态同步问题。
- 加载数据时清理非法主题值和关闭胶囊模式下残留的折叠状态。
- 启动正常恢复数据时不再无条件重写 `data.json`。
- 发布策略明确为主发布版自包含压缩单文件和轻量版无运行库单文件。

### v1.4 发布流程与版本整理

- 版本号、程序集版本、文件版本、显示版本和资源文本版本统一更新。
- 修复笔记纸编辑和浏览切换时的文本锚点与滚动定位。
- GitHub Actions 默认构建两份 Windows x64 单文件 exe。
- 轻量版命名改为 `PaperTodo-v<版本>-win-x64-no-runtime-uncompressed.exe`。
- README、架构文档和 release workflow 的发布参数保持一致。

### v1.5 Markdown 编辑态与胶囊自动贴边

- 笔记纸改为同一个 AvalonEdit Markdown 文本框在编辑和只读浏览之间切换。
- 托盘新增“胶囊自动贴边”，折叠胶囊按顺序排列到屏幕右上角，常态半隐藏到右侧屏幕边缘。
- 胶囊悬浮时动画滑出，点击恢复后贴近右侧边框展开。
- 托盘纸片列表确认删除后菜单不再自动关闭。

### v1.6 优化 Markdown 显示逻辑

- Markdown 渲染态只淡化已确认属于 Markdown 结构的链接 URL 和标记符号。
- 避免任务框 `[x]`、普通括号或未闭合语法被误弱化。
- 有序列表支持 `1.` / `2)` 形式，增强模式淡化源标号并覆盖显示正常颜色的数字标号。
- 无序列表 `-` / `+` / `*` 支持更多前导空格，并在淡化源符号时覆盖显示圆点。
- 任务列表 `- [ ]` / `- [x]` 不额外显示圆点，保持正文视觉统一。
- 托盘新增 `MD 解析` 三档：不启用、启用、增强，默认增强。
- 切换 MD 解析后刷新 AvalonEdit 背景层、文本层和光标层，避免附加渲染需要进出编辑态才更新。
- 首次创建配置时默认开启胶囊模式和胶囊自动贴边。
- 从当前纸片上新建纸片时，新纸片继承当前纸片置顶状态，并被临时带到前台。
