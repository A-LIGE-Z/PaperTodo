# PaperTodo 全量审核进度

目标：把当前版本审核到“能解释任何行为、定位任何状态来源、判断任何改动代价”的程度，并尽最大工程可能降低 bug、结构债、性能浪费、交互断点和动画缺口。

本文件是执行清单，不是总结稿。每完成一个阶段，就在这里打勾，并补充证据、结论和遗留风险。没有证据的项目不能打勾。

## 基线

- 审核起点日期：2026-06-19
- 分支：`feature/multi-master-capsule`
- 起点提交：`8e4fab8`
- 当前变更范围相对 `main...HEAD`：17 个文件，1965 insertions / 665 deletions
- 纳入范围：全部 `.cs`、`.xaml`、`.resx`、`.csproj`、`.md`，以及发布相关目录和配置
- 排除范围：`输出/`、`obj/`、缓存、截图、历史临时文件；除非它们影响发布结果

## 打勾规则

- `[ ]` 未开始或证据不足
- `[-]` 正在进行
- `[x]` 已完成，且有当前状态证据
- `[!]` 发现问题，需要修复或明确接受风险

任何 “完成” 都必须包含至少一种证据：文件行号、命令输出、测试结果、手测路径、差异核对、资源核对、构建结果或明确的代码推演。

## 总进度

- [x] 创建全量审核进度文档
  - 证据：本文件已加入仓库根目录
- [x] 阶段 0：冻结基线与审核边界
- [x] 阶段 1：建立系统地图
- [ ] 阶段 2：逐文件深读
- [ ] 阶段 3：跨模块不变量审查
- [ ] 阶段 4：高风险专项攻击
- [ ] 阶段 5：性能审查
- [ ] 阶段 6：交互、视觉、动画审查
- [ ] 阶段 7：修复循环
- [ ] 阶段 8：回归矩阵
- [ ] 阶段 9：加载用户蒸馏层做最终产品复核
- [ ] 阶段 10：发布判断和最终报告

## 阶段 0：冻结基线与审核边界

- [x] 记录当前分支和起点提交
  - 证据：`git rev-parse --abbrev-ref HEAD` -> `feature/multi-master-capsule`；`git rev-parse --short HEAD` -> `8e4fab8`
- [x] 记录相对 main 的变更规模
  - 证据：`git diff --stat main...HEAD` -> 17 files changed, 1965 insertions(+), 665 deletions(-)
- [x] 记录当前审核文件集合规模
  - 证据：`rg --files -g "*.cs" -g "*.xaml" -g "*.resx" -g "*.csproj" -g "*.md"` -> 40 个文件，其中 `.cs` 29 个
- [x] 保存完整审核文件清单
  - 证据：见下方“审核文件清单”
- [x] 完成职责草图
  - 证据：见“阶段 1：系统地图”的第一版职责图；后续逐文件深读会修正它
- [x] 运行并记录基线构建
  - 证据：`dotnet build PaperTodo.csproj -c Release` -> 0 warning / 0 error，输出 `输出\PaperTodo-v2.0\PaperTodo.dll`
- [x] 运行并记录空白 / 格式检查
  - 证据：`git diff --check` -> 无输出
- [x] 运行并记录资源 key parity
  - 证据：`ko missing=0 extra=0`，`en missing=0 extra=0`，`ja missing=0 extra=0`
- [x] 明确发布产物与版本号状态
  - 证据：`PaperTodo.csproj` -> `TargetFramework=net10.0-windows`，`Version=2.0`，`AssemblyVersion=2.0.0.0`，`FileVersion=2.0.0.0`，`InformationalVersion=2.0`，`OutputPath=输出\PaperTodo-v$(Version)\`

### 审核文件清单

- `AGENTS.md`
- `AnimationHelper.cs`
- `App.xaml`
- `App.xaml.cs`
- `AppController.cs`
- `AppController.Settings.cs`
- `AppController.Tray.cs`
- `CHANGELOG.md`
- `ClipboardHelper.cs`
- `DeepCapsuleLayout.cs`
- `FullscreenForegroundWindowDetector.cs`
- `MarkdownTextBox.cs`
- `MasterCapsuleWindow.cs`
- `Models.cs`
- `NoteTypography.cs`
- `PaperTitles.cs`
- `PaperTodo.csproj`
- `PaperWindow.Capsule.cs`
- `PaperWindow.cs`
- `PaperWindow.DeepCapsule.cs`
- `PaperWindow.Native.cs`
- `PaperWindow.Note.cs`
- `PaperWindow.Todo.cs`
- `README.en.md`
- `README.md`
- `Resources/Strings.en.resx`
- `Resources/Strings.ja.resx`
- `Resources/Strings.ko.resx`
- `Resources/Strings.resx`
- `SingleInstanceHelper.cs`
- `StartupCommand.cs`
- `StateStore.cs`
- `Strings.cs`
- `SystemSettingsHelper.cs`
- `Theme.cs`
- `TodoTextBox.cs`
- `ToolTipPreferences.cs`
- `WindowNative.cs`
- `WindowWorkAreaHelper.cs`
- `md-sample.md`

## 阶段 1：系统地图

目标：先理解系统，不急着修 bug。每个模块要能回答“谁拥有状态、谁能改变它、什么时候落盘、谁会恢复它”。

- [x] 启动、单实例、启动命令
  - 文件：`App.xaml.cs`、`SingleInstanceHelper.cs`、`StartupCommand.cs`
- [x] 数据模型、保存、加载、崩溃恢复
  - 文件：`Models.cs`、`StateStore.cs`、`App.xaml.cs`
- [x] AppController 总调度
  - 文件：`AppController.cs`
- [x] 设置、主题、资源刷新
  - 文件：`AppController.Settings.cs`、`Theme.cs`、`ToolTipPreferences.cs`、`Strings.cs`
- [x] 托盘和菜单
  - 文件：`AppController.Tray.cs`
- [x] 普通纸片窗口生命周期
  - 文件：`PaperWindow.cs`
- [x] 普通胶囊
  - 文件：`PaperWindow.Capsule.cs`
- [x] 贴边胶囊、多队列、多屏、DPI
  - 文件：`PaperWindow.DeepCapsule.cs`、`DeepCapsuleLayout.cs`、`MasterCapsuleWindow.cs`、`WindowWorkAreaHelper.cs`、`WindowNative.cs`、`PaperWindow.Native.cs`
- [x] 待办编辑、拖拽、撤销、关联笔记
  - 文件：`PaperWindow.Todo.cs`、`TodoTextBox.cs`
- [x] 笔记、Markdown、外部打开
  - 文件：`PaperWindow.Note.cs`、`MarkdownTextBox.cs`、`NoteTypography.cs`
- [x] 全屏避让和 topmost
  - 文件：`FullscreenForegroundWindowDetector.cs`、`WindowNative.cs`
- [x] 发布、更新日志、项目配置
  - 文件：`PaperTodo.csproj`、`CHANGELOG.md`、`README.md`、`README.en.md`、`AGENTS.md`

### 系统地图第一版

这张图是第一版职责草图，目标是建立“状态归属和调用方向”。它不是逐文件深读结论，不能替代阶段 2。

| 模块 | 主要文件 | 当前理解 | 状态归属 / 写入点 | 审核重点 |
| --- | --- | --- | --- | --- |
| 启动与单实例 | `App.xaml.cs`、`StartupCommand.cs`、`SingleInstanceHelper.cs` | 解析启动参数；主实例持有 Mutex；后续实例通过 named pipe 转发参数后退出。 | `_singleInstance` 属于 `App`；命令最终进入 `AppController.ExecuteStartupCommand()`。 | `exit/quit`、无参数二次启动、Mutex 释放、异常启动不覆盖数据。 |
| 数据协议与保存 | `Models.cs`、`StateStore.cs` | `AppState` / `PaperData` / `PaperItem` 是 `data.json` 协议；`StateStore` 负责加载、规范化、同步/异步写入、backup。 | `StateStore.Normalize()` 会修正 live state；`AppController.SaveNow()` 生成版本化 JSON。 | 坏数据不覆盖、未知字段兼容、旧异步保存不能覆盖新状态、字段迁移不能破坏旧数据。 |
| 控制器总调度 | `AppController.cs` | 应用状态、所有纸片窗口、托盘、保存 timer、topmost timer、多主胶囊窗口都由控制器协调。 | `State`、`_windows`、`_masterCapsules`、`_saveTimer`、`_visibilityAnimationVersions`。 | 状态变更后是否刷新 UI / 保存 / 重排；隐藏、折叠、删除语义是否清楚。 |
| 设置与主题 | `AppController.Settings.cs`、`Theme.cs`、`ToolTipPreferences.cs` | 设置页直接修改 `State`，再通知窗口、托盘、主题资源或胶囊重排。 | `State.Theme`、`ColorScheme`、功能开关、胶囊模式开关。 | 关闭模式清理状态、资源动态刷新、说明 tooltip 不被普通 tooltip 开关误关。 |
| 托盘 | `AppController.Tray.cs` | Hardcodet `TaskbarIcon` + 自绘 WPF `ContextMenu`；菜单打开时重建；纸片行支持显隐和删除确认。 | `_trayIcon`、`_trayMenu`、行内局部 confirm/suppress 状态。 | 首次点击、菜单焦点、行内按钮事件顺序、菜单重建时状态丢失。 |
| 纸片主窗口 | `PaperWindow.cs` | 单个纸片的 WPF Window，承载标题栏、主体、胶囊 shell、拖拽层、topmost、主题和几何保存。 | `_paper` 是持久数据；大量 `_deepCapsule*` / `_todo*` 是窗口瞬态状态。 | `SuppressGeometrySave`、关闭即隐藏、动画完成回调、窗口激活/topmost。 |
| 普通胶囊 | `PaperWindow.Capsule.cs` | 普通折叠胶囊 UI 和折叠/展开动画；不一定贴边。 | `_paper.IsCollapsed`、窗口 `Width/Height`、transition progress。 | 折叠时保存几何、展开恢复尺寸、不可胶囊时自动展开。 |
| 贴边胶囊与多队列 | `PaperWindow.DeepCapsule.cs`、`DeepCapsuleLayout.cs`、`MasterCapsuleWindow.cs`、`WindowWorkAreaHelper.cs`、`WindowNative.cs` | 贴边胶囊使用独立 slot-host window；队列按 `(monitor, edge)` 分组；每队列一个 master；拖单个胶囊跨边/跨屏。 | `PaperData.CapsuleSide`、`CapsuleMonitorDeviceName`、`State.CapsuleCollapseAllActiveQueues`、`State.DeepCapsuleQueueStartTopMargins`；窗口瞬态 `SlotState/VisualState/GestureState/OpenOrigin`。 | 最高风险：几何、动画、隐藏状态、持久化状态混用；多屏 DPI；slot 清理；collapse-all per-queue。 |
| 待办 | `PaperWindow.Todo.cs`、`TodoTextBox.cs` | 待办行 UI、输入、粘贴、拖拽排序、撤销/重做、关联笔记入口。 | `_paper.Items` 持久；`_undoStack` / `_redoStack` / `_todoDrag` 瞬态。 | 多行粘贴单次撤销、拖拽结束清理、关联笔记影响胶囊资格。 |
| 笔记与 Markdown | `PaperWindow.Note.cs`、`MarkdownTextBox.cs`、`NoteTypography.cs` | 笔记共用一个 `MarkdownTextBox`，在编辑/预览间切换；支持轻量 Markdown 和部分 inline HTML；外部打开写临时文件。 | `_paper.Content`、`_paper.TextZoom` 持久；`_noteBox`、`_showNotePreview` 瞬态。 | 大文本保护、滚动/选区保持、外部后缀合法性、预览点击链接。 |
| 全屏避让 / topmost | `FullscreenForegroundWindowDetector.cs`、`WindowNative.cs`、`AppController.cs` | 定时检查外部全屏窗口，必要时让纸片和胶囊退出 topmost 或插到避让窗口后。 | `_suppressTopmostForFullscreenForeground`、`_fullscreenAvoidanceWindow`。 | 200ms timer 成本、全屏误判、恢复 topmost、slot-host 和 master 一致刷新。 |
| 资源 / 发布 | `Resources/*.resx`、`PaperTodo.csproj`、`CHANGELOG.md`、`README*.md`、`AGENTS.md` | 资源四语言同步；版本号显式维护；changelog 只写用户可见行为。 | `.resx` keys、项目版本、发布输出目录。 | key parity、版本和 changelog 是否一致、发布形态是否符合 no-runtime 单文件要求。 |

### 状态所有权第一版

- `AppState`：持久化应用协议，唯一长期来源是 `data.json`；由 `AppController.State` 持有。
- `PaperData`：单纸片持久状态，包括普通窗口几何、可见性、折叠、文本、待办、胶囊队列归属。
- `PaperItem`：待办项协议，包括文本、完成状态、顺序、关联笔记 id。
- `PaperWindow` 瞬态状态：动画、slot host、拖拽、标题编辑、撤销栈、note preview，原则上不能直接成为 `data.json` 协议。
- `MasterCapsuleWindow` 瞬态状态：每队列主胶囊 UI、hover、拖动中状态；持久结果只应通过 `State.DeepCapsuleQueueStartTopMargins` 和 `State.CapsuleCollapseAllActiveQueues` 表达。
- `WindowWorkAreaHelper` / `DeepCapsuleLayout`：几何计算工具；不能拥有业务状态，除兼容旧静态 anchor 外应尽量用显式 `(monitor, edge)` 输入。

## 阶段 2：逐文件深读

每个文件记录：职责、写入状态、读取状态、外部依赖、不变量、异常路径、性能热点、动画/视觉责任、发现问题、排除问题。

- [ ] `Models.cs`
- [ ] `StateStore.cs`
- [ ] `App.xaml.cs`
- [ ] `SingleInstanceHelper.cs`
- [ ] `StartupCommand.cs`
- [ ] `AppController.cs`
- [ ] `AppController.Settings.cs`
- [ ] `AppController.Tray.cs`
- [ ] `PaperWindow.cs`
- [ ] `PaperWindow.Capsule.cs`
- [ ] `PaperWindow.DeepCapsule.cs`
- [ ] `PaperWindow.Native.cs`
- [ ] `MasterCapsuleWindow.cs`
- [ ] `DeepCapsuleLayout.cs`
- [ ] `WindowWorkAreaHelper.cs`
- [ ] `WindowNative.cs`
- [ ] `PaperWindow.Todo.cs`
- [ ] `TodoTextBox.cs`
- [ ] `PaperWindow.Note.cs`
- [ ] `MarkdownTextBox.cs`
- [ ] `NoteTypography.cs`
- [ ] `PaperTitles.cs`
- [ ] `FullscreenForegroundWindowDetector.cs`
- [ ] `Theme.cs`
- [ ] `ToolTipPreferences.cs`
- [ ] `SystemSettingsHelper.cs`
- [ ] `Strings.cs`
- [ ] `ClipboardHelper.cs`
- [ ] `AnimationHelper.cs`
- [ ] `App.xaml`
- [ ] `Resources/*.resx`
- [ ] `PaperTodo.csproj`
- [ ] `CHANGELOG.md`
- [ ] `README*.md`
- [ ] `AGENTS.md`

## 阶段 3：跨模块不变量审查

- [ ] `data.json` 损坏时不能被空状态覆盖
- [ ] 未知字段兼容和字段迁移不能破坏旧数据
- [ ] `_saveVersion`、写锁、退出同步保存必须防止旧保存覆盖新状态
- [ ] 普通纸片 `X/Y/Width/Height` 不能保存胶囊半隐藏坐标
- [ ] 删除、隐藏、折叠三种语义不能混
- [ ] 关闭胶囊 / 贴边 / 收起全部必须清理临时 slot、激发态、动画态
- [ ] `ShowDeepCapsuleWhileExpanded` 为真时展开纸片仍保留边缘胶囊槽位
- [ ] `UseCapsuleCollapseAll` 使用 slot 0 主胶囊，真实纸片从后面开始
- [ ] 每个 `(monitor, edge)` 队列独立排序、起始高度、收起状态
- [ ] `HideLinkedNotesFromCapsules` 和待办关联状态变化后胶囊资格一致
- [ ] 单实例主进程 Mutex 释放规则正确
- [ ] `exit` / `quit` 在无主实例时保存并退出，不创建默认纸片
- [ ] 托盘必须使用 `TaskbarIcon.IconSource = LoadTrayIconSource()`
- [ ] 主题变化必须刷新动态控件、托盘菜单、AvalonEdit
- [ ] 四语言资源 key 必须一致

## 阶段 4：高风险专项攻击

- [ ] 单屏 100% DPI 贴边胶囊
- [ ] 单屏 125% / 150% DPI 贴边胶囊
- [ ] 双屏同 DPI 左右侧队列
- [ ] 双屏混合 DPI 跨屏拖拽
- [ ] 左侧与右侧 hover 滑出 / 滑回视觉一致性
- [ ] 收起全部每队列独立收起 / 展开
- [ ] 拖单个胶囊上下排序和跨边磁吸阈值
- [ ] 拖拽中丢失捕获、Alt-Tab、释放到菜单外
- [ ] 隐藏全部 / 显示全部 / 关闭模式 / 重启恢复
- [ ] 托盘菜单首次点击、行点击、删除确认、菜单重建
- [ ] 新建纸片位置和来源队列继承
- [ ] 待办多行粘贴、拖拽排序、撤销重做
- [ ] 笔记 Markdown 大文本、链接、外部打开
- [ ] 全屏避让和 topmost 层级

## 阶段 5：性能审查

- [ ] 200ms topmost timer 是否做重活
- [ ] 拖拽过程中是否触发保存、全量重建或昂贵测量
- [ ] 胶囊重排复杂度是否可接受
- [ ] Markdown 渲染和大文本保护是否仍有效
- [ ] 托盘菜单重建是否只在必要时发生
- [ ] 主题切换是否重复 rebuild 过多
- [ ] 透明窗口移动 / 动画是否造成可感知压力

## 阶段 6：交互、视觉、动画审查

补动画原则：状态去向不清楚、跳变让用户误解、左右侧不一致时补；如果动画拖慢操作、制造错觉或增加风险，就不补。

- [ ] 普通纸片显示 / 隐藏
- [ ] 普通胶囊折叠 / 展开
- [ ] 贴边胶囊 hover 滑出 / 滑回
- [ ] 展开后边缘胶囊激发态
- [ ] 收起全部 retract / release
- [ ] 单胶囊跨队列拖出、松手归位
- [ ] 待办新增 / 删除 / 排序
- [ ] 关联笔记入口变化
- [ ] 托盘操作反馈
- [ ] 设置切换后的即时反馈
- [ ] 主题切换过渡
- [ ] 关闭动画开关后所有动画立即完成

## 阶段 7：修复循环

每个问题必须记录：

- 问题描述
- 影响范围
- 触发路径
- 修复方案
- 代价和风险
- 是否更新 `CHANGELOG.md`
- 验证路径

问题列表：

- [ ] 待填

## 阶段 8：回归矩阵

- [ ] `dotnet build PaperTodo.csproj -c Release`
- [ ] `git diff --check`
- [ ] 资源 key parity
- [ ] 单屏基础手测
- [ ] 多屏同 DPI 手测
- [ ] 多屏混合 DPI 手测
- [ ] 托盘菜单手测
- [ ] 待办 / 笔记核心路径手测
- [ ] 退出保存 / 重启恢复手测
- [ ] 启动命令手测：`show` / `hide` / `toggle` / `new-todo` / `new-note` / `exit`

## 阶段 9：用户蒸馏层最终复核

在代码事实审计完成后，再读取用户蒸馏文件，用它审查产品判断：

- [ ] 读取蒸馏文件
- [ ] 是否仍符合“桌面上的几张纸”
- [ ] 是否有功能膨胀
- [ ] 是否把实现复杂度转嫁给用户
- [ ] 哪些应该砍、留、延后

## 阶段 10：最终报告

- [ ] 高 / 中 / 低风险问题清单
- [ ] 已修复问题清单
- [ ] 已排除风险清单
- [ ] 结构债清单
- [ ] 性能判断
- [ ] 视觉和动画判断
- [ ] 发布前必修清单
- [ ] 发布后可缓修清单
- [ ] 最终建议：能否发 rc，能否发正式版，剩余风险是什么

## 审核日志

### 2026-06-19

- 创建本文件，建立全量审核执行框架。
- 已记录起点分支、提交、变更规模和文件集合规模。
- 完成基线构建、空白检查、资源 key parity 和版本号状态记录。
- 完成系统地图第一版，记录模块职责和状态所有权；后续逐文件深读会校正这张图。
