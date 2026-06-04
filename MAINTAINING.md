# PaperTodo Maintaining Notes

本文件是改动前必读。详细架构见 `ARCHITECTURE.md`，用户说明见 `README.md`，用户态更新日志见 `CHANGELOG.md`。

## 产品边界

- PaperTodo 是桌面纸片工具，不是任务管理系统。
- 不做主窗口、统一管理页、大型设置页。
- 不做账号、同步、分类、标签、搜索、归档、统计、提醒、日历。
- 不做内置数据库、WebView、Electron、Tauri、MSIX、AppX 托管。
- 不把 Markdown 纸扩展成完整文档编辑器。
- 新功能必须保持局部、轻量、低层级；如果会让项目长出管理器，默认不做。

## 硬规则

- 版本号在 `PaperTodo.csproj` 显式维护，不恢复 MSBuild 自动递增。
- 用户可见文本必须同步 `Resources/Strings.resx`、`Strings.en.resx`、`Strings.ja.resx`、`Strings.ko.resx`。
- `ResourceTextVersion` 只作手动刷新标记，不写运行时资源版本检查。
- `data.json` 是用户数据；新增字段要给默认值并在 `Normalize()` 兼容旧数据。
- 保存逻辑保留 `_saveVersion`、`StateStore` 写入锁和退出同步保存，避免旧保存覆盖新保存。
- 单实例锁只由主实例释放，不让转发启动参数的临时实例释放主实例 `Mutex`。
- 剪贴板读取走 `ClipboardHelper`，不要把 WPF 剪贴板异常散落到 UI 代码。
- 笔记输入的 `MaxLength = 100000` 不要删除，除非替换为等价保护。
- 纸片数量上限 100 不要删除，除非替换为等价的 WPF/GDI/User handle 保护。
- 托盘图标使用 `TaskbarIcon.IconSource = LoadTrayIconSource()`，不要改回 `System.Drawing.Icon` 路径。
- 不启用 `PublishTrimmed` 或 Native AOT；WPF 发布参数不要为了体积牺牲稳定性。
- 正式发布资产不要混入 `data.json`、`data.backup.json` 等开发运行数据。

## 高风险区域

### 托盘菜单

- 优先保持 Hardcodet 默认 ContextMenu 路径和 `IconSource`。
- 不手动模拟右键菜单弹出。
- 不加全局鼠标轮询关闭菜单。
- 不用“鼠标移到纸片就关闭菜单”这类粗糙方案。
- 改完必须检查首次右键位置、首次点击是否被吞、删除确认是否可正常点击。

### 胶囊模式

- `paper.Width` / `paper.Height` 只保存普通纸片尺寸。
- 胶囊尺寸来自 `PaperLayoutDefaults.CapsuleWidth` / `CapsuleHeight`。
- 折叠、展开、隐藏、显示之间不要把胶囊尺寸写回普通尺寸。
- 胶囊自动贴边不要把屏幕边缘隐藏坐标持久化成普通窗口坐标。

### Markdown 解析

- `MD 解析` 三档语义要保持清楚：
  - 不启用：无视 Markdown 标签，不做额外渲染解析。
  - 启用：不淡化 Markdown 标签，只保留基础显示。
  - 增强：淡化可隐藏标记，并用轻量覆盖体现渲染态。
- 修复列表、勾选框、链接、标题等规则时，要优先避免破坏主体文字的视觉统一性。
- 特别注意 `- [ ]`、`- [x]`、缩进列表、`1.`、`2)`、`-`、`+`、`*` 这类边界。

### 主题和资源刷新

- 主题切换要同时刷新纸片、托盘菜单和动态生成控件。
- 新增资源文本后要确认四种语言都有键值。
- 不要把资源缺失处理成用户可见的空白或异常。

### 发布打包

- 正式发行版保留两个 Windows x64 exe 资产：主发布版和轻量版。
- 主发布版是自包含压缩单文件；轻量版是不携带 .NET Runtime 的框架依赖单文件。
- 普通 `dotnet build` 只证明编译通过，不代表发布包行为正确。
- 发布后关注 SHA256、Sigstore/cosign、GitHub Release 资产名称和数量。

## 验证清单

普通编译检查：

```powershell
dotnet build -p:OutputPath=输出\_codex-build-check\
```

改 UI、托盘、保存、发布参数后，至少检查：

1. 首次启动没有数据文件时，会创建一张待办纸。
2. 新建待办纸和笔记纸正常。
3. 退出后重新启动，纸片位置、大小、内容、置顶、主题恢复。
4. 隐藏单张纸片后，托盘能重新显示；隐藏全部后，显示全部正常。
5. 胶囊模式和胶囊自动贴边的折叠、悬浮、展开、隐藏全部、显示全部正常。
6. 待办项 Enter、Backspace、多行粘贴、拖动排序、拖动删除、撤销、重做正常。
7. 笔记纸编辑和 Markdown 预览正常，`MD 解析` 三档切换后能立即刷新。
8. 主题在系统、浅色、深色之间切换，纸片和托盘都刷新。
9. 托盘首次右键菜单位置正确，首次点击纸片不会被吞。
10. 托盘纸片列表删除确认中，确认和取消都能点击，hover/pressed 状态明显。
11. 删除最后一张纸片后，会自动补一张待办纸。
12. 外部 `PaperTodo.ico` 存在时优先作为托盘图标，不存在时使用内嵌图标，损坏时不阻止启动。
13. 退出后没有残留 `PaperTodo.exe` 进程。
14. `PaperTodo.crash.log` 不会无限增长。
15. 正式发布包内没有 `data.json`、`data.backup.json` 这类开发运行数据。

## 可以考虑的改进

- 发布后自动校验两个正式 exe 的 SHA256 和 cosign 签名。
- 发布 workflow 继续防止运行数据混入正式资产。
- 更轻的首次启动性能记录脚本。
- 对 `data.json` 增加显式 schema/version 字段，用于未来迁移。
- 给外部自定义图标加载失败增加一次性日志。
- 增加“复制当前纸片内容”或“复制全部待办纯文本”的轻量操作。
- 增加极简导出为 Markdown，但不要做导入管理器。
- 给 Markdown 渲染失败提供更明确的错误提示。
- 把过大的 `PaperWindow.cs` 拆成 partial 文件，但只按真实边界拆，例如 Todo、Note、Capsule、Menus。

## 协作节奏

- 先读当前代码和相关历史，不要凭空改。
- 简短说明要改哪里，再直接实施。
- 做最相关的验证，不用空泛保证代替检查。
- 明确说明改了什么、为什么、有没有未验证的点。
