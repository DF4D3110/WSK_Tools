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
| 驱动注入 | 对已有 FFU/VHDX 注入驱动文件夹 | `imageapp.exe /Patch` |
| CAB 注入 | 挂载 VHD 并安装 CAB 包 | `UpdateApp.exe` |
| BCD 编辑 | 勾选式配置调试端口、波特率、测试签名等启动项 | 系统 `bcdedit.exe` |

## 项目内容

- 支持 **x86 / amd64 / arm64** 三种架构编译（arm32 平台配置保留，当前 Windows SDK 已移除 32 位 ARM 支持）
- **多语言支持**：简体中文、繁体中文、英语（美国）、日语、俄语、韩语，语言文件以独立 DLL 形式存放于 `language\` 目录，运行时可切换
- 简洁的 GUI 界面，统一的菜单栏（语言切换 / 关于）

## 编译要求

- Visual Studio 2022（Community / Professional / Enterprise）
- Windows 11 SDK（10.0.26100.0 或更高）
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

## 组织信息

- **WinStory 2026**
- 网站：https://wiki.win-story.cn
- 编译者：DF4D3110

## 额外声明

部分内容已经经过调整，可能存在异常情况，如果您遇到了异常情况，请到issues反馈，感谢您对本工具的支持！
如果你有什么想法或者希望引入的功能也可以在issues反馈！
