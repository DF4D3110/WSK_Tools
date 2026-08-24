# WSK Tools — Windows System Kit 工具集

一套基于纯 Win32 C++ 开发的 Windows System Kit (WSK) 相关工具，目前仅包括 FFU 镜像释放、WSK 自动化构建、WCOS 处理三个功能。

## 工具列表

### 1. ffuext — FFU 镜像释放工具

调度系统 DISM 将 FFU 镜像释放到物理磁盘。

- 启动时自动检测当前 DISM 是否支持 `/apply-ffu`
- 自动枚举宿主机所有物理磁盘（含容量、型号、可访问性）
- 文件对话框选择 FFU 镜像
- 后台线程执行 DISM `/apply-ffu`，管道实时捕获输出并解析进度百分比
- 释放完成反馈成功 / 失败（含退出码与错误信息）
- 管理员权限检测与提示

### 2. wsktool — WSK 构建半自动化工具

自动化完成 Windows System Kit 镜像构建的完整流程。

- 优先自动检测挂载的 WSK 光盘，未检测到时提示手动选择目录
- 自动检测 `SetImagGenEnv.cmd` 并初始化构建环境
- 自动枚举 `<WSK>\Program Files\Windows Kits\10\FMFiles\<架构>` 下的 SKU
- 用户选择工作区目录、架构（x86 / AMD64 / Arm / Arm64）、SKU、机器类型（实体机 FFU / 虚拟机 VHDX）
- 两阶段构建：`PrepWSKWorkspace` → `BuildWSKImage`
- 构建过程中实时输出控制台内容，非阻塞轮询避免管道死锁
- 构建完成自动定位并打开产物（FFU / VHDX）所在目录
- 选择 ARM / ARM64 架构时弹出设备布局提醒

### 3. wcosstagetool — WCOS 辅助工具

集成 WCOS 构建与镜像后期处理的多选项卡工具。

| 选项卡 | 功能 | 调度工具 |
|--------|------|----------|
| WCOS 构建 | 调用 ImgGen 生成 FFU，支持自定义输出文件名（默认 `flash.ffu`），输出目录非空时即时提示另存为 | `imggen.cmd` |
| 驱动注入 | 对已有 FFU/VHDX 注入驱动文件夹 | `imageapp.exe` |
| CAB 注入 | 挂载 VHD 并安装 CAB 包 | `UpdateApp.exe` |
| BCD 编辑 | 勾选式配置调试端口、波特率、测试签名等启动项 | 系统 `bcdedit.exe` |

## 项目内容

- 支持 **x86 / amd64 / arm64** 三种架构编译（arm32 平台配置保留，当前 Windows SDK 已移除 32 位 ARM 支持）
- **多语言支持**：简体中文、繁体中文、英语（美国）、日语、俄语、韩语，语言文件以独立 DLL 形式存放于 `language\` 目录，运行时可切换
- 简洁的 GUI 界面，统一的菜单栏（语言切换 / 关于）

## 编译要求

- Visual Studio 2022（Community / Professional / Enterprise）
- Windows 10 SDK（10.0.26100.0 或更高）
- MSVC v143 工具集，C++17 标准

## 编译方法

1. 用 Visual Studio 2022 打开 `Source\WSK_Tools.sln`
2. 选择配置（Release / Debug）和平台（x86 / x64 / ARM64）
3. 生成解决方案

编译产物位于各项目 `bin\<平台>\<配置>\` 目录下，运行时需将 `language\` 目录与 exe 放在同一级。

## 目录结构

```
WSK_Tools\
├── Source\              源码
│   ├── WSK_Tools.sln    解决方案
│   ├── common\          公共模块（多语言管理器）
│   ├── ffuext\          FFU 释放工具
│   ├── wsktool\         WSK 构建工具
│   └── wcosstagetool\   WCOS 阶段工具
└── Files\               编译成品（x64 Release）
    ├── ffuext.exe
    ├── wsktool.exe
    ├── wcosstagetool.exe
    └── language\        18 个语言 DLL（3 程序 × 6 语言）
```

---

## .NET 工具集（ffuview / ffuinfo / exttools）

除上述 C++ 工具外，本项目还包含三个基于 .NET 8 的 FFU 分析工具：

### ffuview — FFU 镜像分区与文件浏览器（GUI）

图形化 FFU 镜像浏览器，支持不挂载、不释放直接查看 FFU 内容。

- 支持所有 FFU 格式：V1（无压缩）、V1.1（压缩，DISM 默认）、V2（多 Store）
- 列出 GPT 分区表（分区名、类型 GUID、LBA、大小、文件系统）
- 直接浏览 NTFS / FAT32 分区文件系统，树形目录 + 文件列表
- **OSPool / Storage Spaces 支持**：解析 Windows Core OS / Andromeda OS 的 OSPool 分区，列出内部虚拟磁盘并深入浏览其分区和文件系统（含 slab 块映射磁盘）

### ffuinfo — FFU 结构分析工具（命令行）

命令行 FFU 镜像结构分析工具。输出 FFU 头部信息、Store 列表、每个 Store 的磁盘几何信息、分区表（含分区名、类型、LBA、大小、文件系统检测），以及 OSPool 深度分析。

### exttools — FFU/VHDX 进阶诊断工具集（命令行，默认不编译）

进阶 FFU/VHDX 诊断工具集，用于深度分析和逆向调试。包含以下子命令：

| 命令 | 说明 |
|------|------|
| `broadscan` | 广泛签名扫描（GPT/NTFS/FAT/SPACEDB/SDBB/SDBC） |
| `btreedump` | Dump OSPool 所有 SDBB B-tree 条目、Volume、Slab 分配 |
| `ospooldiag` | OSPool 结构诊断（SPACEDB 头、各类条目统计） |
| `ospooldump` | OSPool 完整 dump（所有虚拟磁盘及分区） |
| `ospoolscan` | 扫描 FFU 中所有 OSPool 分区 |
| `ospoolpartdiag` | OSPool 虚拟磁盘分区详细诊断 |
| `vhxddump` | VHDX 文件头部和元数据 dump |
| `diag2` | 通用 FFU 诊断（头部、Store、分区表） |

> exttools 默认不在解决方案中编译，需在 Visual Studio 配置管理器中手动勾选，或使用 `dotnet build Source\exttools\exttools.csproj -c Release`。

## 多语言支持

本项目所有工具均支持多语言：

- **C++ 工具**（ffuext / wsktool / wcosstagetool）：语言文件以独立 DLL 形式存放于 `language\` 目录，运行时通过菜单栏切换。支持 6 种语言：简体中文（zh-cn）、繁體中文（zh-tw）、English（en-us）、日本語（ja-jp）、Русский（ru-ru）、한국어（ko-kr）。
- **ffuview**：菜单栏新增"语言"菜单，6 种语言可即时切换，切换后更新标题、菜单、工具栏按钮文本。
- **ffuinfo / exttools**：默认中文输出，通过 `-l` 或 `-language` 选项指定输出语言。
  - 用法示例：`ffuinfo file.ffu -l en-us`
  - 用法示例：`exttools diag2 file.ffu -l ja-jp`

## 开源致谢

本项目的 .NET 工具集成了以下开源项目的代码（均已包含在 `Source\` 目录中）：

- **[Img2Ffu](https://github.com/MobileTooling/img2ffu)** — FFU 读取/写入库，作者 Gustave Monce (gus33000)，MIT License。提供 `FullFlashUpdateReaderStream`，自动处理所有 FFU 格式（V1/V1.1/V2）的解析和解压。
- **[StorageSpace](https://github.com/MobileTooling/StorageSpace)** — Windows Storage Spaces / OSPool 解析库，作者 Gustave Monce (gus33000)，MIT License。提供 SPACEDB/SDBC/SDBB B-tree 解析和 slab 块映射 Stream，支持 Windows Core OS / Andromeda OS 的 OSPool 虚拟磁盘浏览。
- **[LTRData.DiscUtils](https://github.com/LTRData/DiscUtils)** — .NET 磁盘工具库，MIT License。提供 GPT 分区表解析和 NTFS/FAT32 文件系统读取。

## 组织信息

- **WinStory 2026**
- 网站：https://wiki.win-story.cn
- 编译者：DF4D3110
