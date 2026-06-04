# PaperTodo / 一张纸

PaperTodo 是一个极简 Windows 桌面纸片工具

只负责让桌面上有几张安静、可用、不会打扰人的纸。

---

## 设计原则

- **纸片优先**：每张纸都是一个独立的无边框窗口，直接存在于桌面上。
- **即时使用**：想记就写；完成就勾掉。
- **拒绝管理器**：不做分类、标签、搜索、归档、同步、账号、统计。
- **原生实现**：基于 WPF，不使用 WebView、Tauri、Electron。
- **交互优先**：轻不是低占用洁癖，而是操作路径轻、认知负担轻、界面干扰少。可以使用必要的现代依赖，但不让产品长出复杂系统。

---

## 项目边界

为了保持极简与纯粹，PaperTodo 不做会把纸片工具推向完整管理系统的功能：

- 主窗口 / 统一管理页
- 账号体系 / 提醒 / 日历 / 数据统计
- 回收站 / 归档 / 插件扩展
- 图片 / 表格 / 附件插入
- 分类标签 / 搜索过滤 / 数据同步
- WebView / Tauri / Electron
- MSIX / Microsoft Store / AppX 托管

如果某个功能会让项目长出管理器，默认不做。
如果某个功能会明显增加交互层级，默认不做。

---

## 项目特色

- **没有主窗口**：启动后只留下桌面纸片和托盘入口，减少管理界面带来的打扰。
- **纸片即状态**：位置、尺寸、置顶、内容、胶囊形态都直接保存，不需要额外确认。
- **原生纸片体验**：Markdown 编辑 / 浏览、托盘菜单、拖拽排序和主题切换都使用 WPF 原生控件实现。
- **外部快捷键友好**：通过启动参数即可显示、隐藏、切换和新建纸片，不需要在应用内增加复杂快捷键配置。
- **数据保护优先**：保存前备份、临时文件写入、严格 JSON 解析和崩溃恢复文件共同降低数据损坏风险。
- **分发清晰，允许自定义**：主发布版是自包含压缩单文件；轻量版是不携带 .NET Runtime 的框架依赖单文件。程序自带内嵌图标，如果同目录存在 `PaperTodo.ico`，托盘会优先读取它作为自定义图标。

---

## v1.6 更新重点

- **Markdown 双模式**：笔记纸保留编辑态 / 浏览态双模式，编辑时专注源文本输入，浏览时按当前 MD 解析模式呈现轻量 Markdown 视觉。
- **MD 解析三档模式**：托盘支持“不启用 / 启用 / 增强”三档，默认增强；增强模式会淡化 Markdown 标记并优化列表圆点显示。
- **胶囊动画优化**：折叠 / 展开时将真实窗口尺寸变化与可见动画解耦，减少透明窗口缩放过程中的硬裁切碎帧。
- **胶囊过渡细节优化**：胶囊文字独立于缩放纸片层淡入淡出，避免缩小过程中脱离窗口；接近胶囊尺寸时动态增强外壳圆角。
- **默认胶囊行为调整**：首次创建配置时默认开启胶囊模式和胶囊自动贴边。

---

## 当前能力

- 多张独立纸片，每张纸是一个独立窗口。
- 两种纸片：
  - **待办纸**：一行一个事项，可以勾选、编辑、删除、清理已完成。
  - **笔记纸**：普通文本 + AvalonEdit Markdown 轻量高亮 + 只读浏览，支持三档 MD 解析模式。
- 纸片支持移动、缩放、置顶、隐藏、删除。
- 胶囊模式：默认启用，可将纸片折叠成置顶小胶囊，减少桌面占用，并可从托盘统一启用 / 关闭。
- 胶囊自动贴边：默认启用，折叠胶囊可自动排列到屏幕右上角，半隐藏到屏幕边缘，悬浮时动画滑出。
- 显示模式切换：支持系统、浅色、深色三种模式，免重启实时切换。
- 多语言界面：支持中文、英文、日文、韩文资源文本，随系统界面语言加载。
- 开机自启动：支持一键勾选随 Windows 自动启动。
- 托盘提供最低限度入口：新建、显示/隐藏全部、切换单张纸显示状态、退出。
- 托盘纸片列表支持单行内联删除确认，确认态提供独立的“确认 / 取消”操作，避免纸片较多时菜单过高。
- 自动保存到程序目录下的 `data.json`，并保留 `data.backup.json`。
- 临时文件写入，降低异常退出时的数据损坏风险。
- 启动时自动拉回屏幕的纸片。
- 托盘图标支持自定义：程序目录下存在 `PaperTodo.ico` 时优先作为托盘图标，否则使用内嵌图标。

---

## 纸片功能与操作手册

### 待办 (Todo)

适合当天任务、临时事项、桌面小清单。

### 基本操作

- **勾选完成**：点击事项左侧 checkbox，切换完成 / 未完成。
- **编辑内容**：点击事项文字，直接编辑。
- **新增事项**：在事项中按 `Enter`，在当前事项下方新增一行。
- **删除空项**：当前事项为空时按 `Backspace`，删除该空事项。
- **粘贴多行**：粘贴多行文本时，自动拆成多条事项。
- **拖动排序**：按住右侧 `≡` 手柄上下拖动。
- **末尾追加**：点击底部 `＋` 区域，在列表末尾新增一行。
- **拖拽删除**：拖动事项时，底部追加区会变为删除区，把事项拖到这里即可删除。
- **撤销与重做**：`Ctrl+Z` 与 `Ctrl+Y` 可以撤销 / 重做上个操作。

### 完成状态

- 完成项显示删除线，文字弱化。
- 完成项仍可编辑、拖动和取消完成。
- 不自动沉底，避免变成任务管理系统。

### 粘贴清洗

粘贴多行时会尽量清理常见列表前缀，例如：

- `- 项目`
- `* 项目`
- `+ 项目`
- `1. 项目`
- `1、项目`
- `- [ ] 项目`
- `- [x] 项目`
- `☐ 项目`
- `☑ 项目`
- `✓ 项目`

### 右键菜单

右键待办事项文本，可使用：

- 复制
- 粘贴
- 删除这一项
- 清理已完成

---

### 笔记纸 (Paper)

短笔记、临时文字、简单说明。

支持 Markdown。

### 格式快捷键

- `Ctrl+B`：加粗。
- `Ctrl+I`：斜体。
- `Ctrl+K`：插入超链接。
- `Ctrl+Z` / `Ctrl+Y`：使用编辑器撤销 / 重做。

### 支持的 Markdown

- 标题：`#` 到 `######`
- 加粗：`**文本**`
- 斜体：`*文本*`
- 删除线：`~~文本~~`
- 无序列表：`- 项目`
- 有序列表：`1. 项目`
- 引用：`> 文本`
- 行内代码：`` `code` ``
- 代码块：使用 ` ```代码 ``` `包裹
- 超链接：`[文本](URL)`

### 不支持

- 图片
- 表格
- 附件
- 嵌入内容
- 复杂块编辑器

笔记纸不是 Markdown 编辑器，只是让一张纸能写得稍微清楚一点。

### 右键菜单

- **编辑状态右键**：包含格式区（加粗、斜体、删除线、标题、引用、列表、代码块、插入链接）和纸片操作区（隐藏、删除）。
- **浏览状态右键**：包含新建纸片、折叠 / 恢复胶囊、隐藏、删除等纸片操作；点击浏览内容可进入编辑。

---

## 纸片

### 移动与缩放

- 拖动纸片顶部空白区域：移动纸片。
- 拖动纸片右下角：调整大小。

### 置顶

每张纸左上角的类型图标同时也是置顶开关：

- 待办纸图标：`☑`
- 笔记纸图标：`✎`
- 点击左上角图标：置顶 / 取消置顶。
- 图标变实表示当前置顶。
- 置顶状态会自动保存。

### 新建和删除

右上角可新建待办和笔记纸

顶栏右键可删除纸片




---

## 托盘入口

PaperTodo 没有主窗口。托盘是唯一的全局入口。

### 托盘操作

- 双击托盘图标：显示并拉回全部纸片。
- 右键托盘图标：打开托盘菜单。
- 托盘菜单顶部显示当前程序版本号。
- **切换显示模式**：支持系统、浅色、深色三种主题实时免重启切换。
- **切换 MD 解析**：支持不启用、启用、增强三档；默认增强。增强模式会在浏览态淡化 Markdown 标记，并将无序列表标记显示为圆点。
- **切换胶囊模式**：可一键启用 / 关闭全局胶囊折叠模式。
- **胶囊自动贴边**：启用后，已折叠胶囊会自动排列到屏幕右上角，并以半隐藏状态贴在屏幕右侧边缘，悬浮时动画滑出。
- **配置开机自启动**：可一键勾选自启动，随 Windows 系统开机自动后台运行。
- **删除纸片**：纸片列表行右侧的 `×` 会先切换为确认态；确认态下左侧显示警示，右侧提供“确认 / 取消”两个操作。

### 启动参数

可以把这些命令配置到外部快捷键工具、脚本或 Windows 快捷方式中：

- `PaperTodo.exe --show`：显示并拉回全部纸片。
- `PaperTodo.exe --hide`：隐藏全部纸片，程序继续留在托盘运行。
- `PaperTodo.exe --toggle`：有纸片显示时隐藏全部；全部隐藏时显示全部。
- `PaperTodo.exe --new-todo`：新建一张待办纸。
- `PaperTodo.exe --new-note`：新建一张笔记纸。
- `PaperTodo.exe --exit`：保存当前状态并退出程序。

这些参数也支持去掉 `--`，并支持少量别名，例如 `open` 等同于 `show`，`quit` 等同于 `exit`。

程序已经运行时，再次带参数启动不会创建第二个常驻进程，而是把命令转发给当前实例执行。无参数再次启动时保持原行为：显示并拉回全部纸片。



---

## 数据文件

数据放在程序目录下：

```text
PaperTodo/
├─ PaperTodo.exe
├─ data.json
└─ data.backup.json
```

`data.json` 是主数据文件。  
`data.backup.json` 是保存前备份，用于主文件损坏时恢复。  
`PaperTodo.ico` 不是必需文件；如果放在同目录，程序会优先读取它作为自定义托盘图标，否则使用内嵌图标。

不要把程序放在只读目录，否则可能无法保存数据。

---

## 环境与依赖

- Windows
- .NET 10
- WPF
- AvalonEdit
- Hardcodet.NotifyIcon.Wpf

## 发布形态

GitHub Actions 会构建两个 Windows x64 单文件 exe，并直接作为 GitHub Release 资产发布，不再套 zip。Release 还会附带 `SHA256SUMS.txt`、Sigstore 签名文件 `.sig` 和证书文件 `.crt`。

GitHub Release 的发行说明会从 `CHANGELOG.md` 中自动提取对应 tag 的版本小节，例如 `v1.6` 会使用 `### v1.6 ...` 下方内容。

- `PaperTodo-v<版本>-win-x64-self-contained-compressed.exe`
  - 面向普通用户。
  - 自包含，包含 .NET Desktop Runtime。
  - 单文件，开启 ReadyToRun 和单文件压缩。
- `PaperTodo-v<版本>-win-x64-no-runtime-uncompressed.exe`
  - 面向已安装对应 .NET Desktop Runtime 的环境。
  - 框架依赖，不携带运行库。
  - 单文件，不开启 ReadyToRun，不开启单文件压缩。

每个 exe 都在 GitHub Actions 中通过 Sigstore/cosign keyless 方式签名。下载后可以用 cosign 验证：

```powershell
cosign verify-blob --certificate .\PaperTodo-v<版本>-win-x64-self-contained-compressed.exe.crt --signature .\PaperTodo-v<版本>-win-x64-self-contained-compressed.exe.sig --certificate-identity-regexp "^https://github[.]com/testsnow0722/PaperTodo/[.]github/workflows/release[.]yml@refs/(heads/main|tags/v.*)$" --certificate-oidc-issuer "https://token.actions.githubusercontent.com" .\PaperTodo-v<版本>-win-x64-self-contained-compressed.exe
cosign verify-blob --certificate .\PaperTodo-v<版本>-win-x64-no-runtime-uncompressed.exe.crt --signature .\PaperTodo-v<版本>-win-x64-no-runtime-uncompressed.exe.sig --certificate-identity-regexp "^https://github[.]com/testsnow0722/PaperTodo/[.]github/workflows/release[.]yml@refs/(heads/main|tags/v.*)$" --certificate-oidc-issuer "https://token.actions.githubusercontent.com" .\PaperTodo-v<版本>-win-x64-no-runtime-uncompressed.exe
```

这个签名用于证明产物来自本仓库的 GitHub Actions 构建；它不是 Windows Authenticode 代码签名，所以 Windows 仍可能显示未知发布者。

校验 SHA256：

```powershell
Get-FileHash .\PaperTodo-v<版本>-win-x64-self-contained-compressed.exe -Algorithm SHA256
Get-FileHash .\PaperTodo-v<版本>-win-x64-no-runtime-uncompressed.exe -Algorithm SHA256
```

---

## 后续计划

- 暂无

## 其他

致谢——https://linux.do/
