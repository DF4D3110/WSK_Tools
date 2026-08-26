# WSK Tools v1.0.4 — Windows System Kit 工具集

一套面向 Windows System Kit (WSK) / FactoryOS / Windows Core OS 镜像构建与设备布局管理的综合工具集。v1.0.4 版本重点重构了设备布局管理工具链，引入 imageapp 兼容的原生存储池创建流程，并将 wsktool / ffuext 重写为 .NET 8 跨架构版本。

---

## 版本亮点 (v1.0.4)

- **DeviceLayoutExplorer** 全新设备布局浏览器，替代旧版 DeviceLayout_Exchanger
- **DeviceLayoutGeneratorV2** imageapp 兼容流程，使用 ADK 原生 `ImageStorageService.dll` 创建存储池
- **wsktool / ffuext** 从 C++ Win32 重写为 .NET 8 WinForms，支持 x86/amd64/arm64
- **自动卸载** 虚拟磁盘创建完成后无论成功失败均自动卸载
- **CAB 持久化** 从 CAB 加载的设备布局 XML 自动保存到程序目录，避免临时文件丢失

---

## 工具列表

### 设备布局管理

| 工具 | 说明 | 平台 |
|------|------|------|
| **DeviceLayoutExplorer.exe** | 设备布局浏览器：加载 XML/CAB，树状视图预览分区/存储池，调用 GeneratorV2 创建虚拟磁盘，内置磁盘管理 | x86 / amd64 / arm64 |
| **DeviceLayoutGeneratorV2.exe** | 设备布局生成器 V2：imageapp 兼容流程，原生 API 创建存储池与虚拟磁盘 | **仅 x86**（依赖 32 位 ADK DLL） |
| **dl2vhd.exe** | 设备布局转 VHD 命令行工具（旧版流程） | x86 / amd64 / arm64 |

### 镜像构建与应用

| 工具 | 说明 | 平台 |
|------|------|------|
| **wsktool.exe** | WSK 构建半自动化工具：自动检测 WSK 路径，选择工作区/架构/产品/物理或VM模式，实时构建日志 | x86 / amd64 / arm64 |
| **ffuext.exe** | FFU 镜像释放工具：选择 FFU 文件与目标磁盘，调度 DISM `/apply-ffu`，实时进度 | x86 / amd64 / arm64 |
| **wcosstagetool.exe** | WCOS 阶段工具：WCOS 构建、驱动注入、CAB 注入、BCD 编辑 | x86 / amd64 / arm64 |

### 镜像浏览与分析

| 工具 | 说明 | 平台 |
|------|------|------|
| **virtualdiskexplorer.exe** | 虚拟磁盘浏览器：挂载/浏览 VHD/VHDX，查看分区与文件 | x86 / amd64 / arm64 |
| **ffuexplorer.exe** | FFU 镜像浏览器：查看 FFU 内部结构与分区 | x86 / amd64 / arm64 |
| **ffuinfo.exe** | FFU 结构分析命令行工具 | x86 / amd64 / arm64 |
| **wimexplorer.exe** | WIM 镜像浏览器 | x86 / amd64 / arm64 |

### 包管理与辅助工具

| 工具 | 说明 | 平台 |
|------|------|------|
| **MobilePackageGen.exe** | CBS/SPKG/Driver 包提取命令行 | x86 / amd64 / arm64 |
| **MobilePackageGen.GUI.exe** | 包提取图形界面 | x86 / amd64 / arm64 |
| **exttools.exe** | FFU/VHDX 进阶诊断工具 | x86 / amd64 / arm64 |
| **IUSpacesHelper.exe** | IU 存储空间助手（32 位子进程辅助） | x86 / amd64 / arm64 |
| **StoragePoolHelper.exe** | 存储池创建助手（32 位子进程辅助） | x86 / amd64 / arm64 |
| **createdump.exe** | 进程转储创建工具 | x86 / amd64 / arm64 |

---

## 核心功能详解

### DeviceLayoutExplorer

**设备布局加载**
- 点击「打开 XML」直接加载 `DeviceLayout.xml`
- 点击「打开 CAB」从 CAB 包中自动提取设备布局（支持多 XML 选择）
- 从 CAB 加载的 XML 自动复制到 `程序目录\cab_extracted\` 持久保存

**树状视图预览**
- 左侧树状视图显示：存储 (Store) → 分区 (Partition) → 存储池 (Storage Pool) → 虚拟磁盘 (Space)
- 右侧详情面板显示选中节点的详细属性（名称、类型、大小、GUID 等）
- 支持存储池内虚拟磁盘的分区预览

**创建虚拟磁盘**
- 点击「创建虚拟磁盘」按钮
- 选择输出 VHD/VHDX 路径
- 弹出 DeviceLayoutGeneratorV2 真实运行窗口，实时显示构建进度
- 完成后弹出日志查看窗口，显示完整构建日志
- **成功**：保留输出文件，显示文件大小
- **失败**：自动删除不完整的输出文件
- 无论成功失败，创建完成后自动卸载虚拟磁盘

**磁盘管理**
- 切换到「磁盘管理」标签页
- 查看物理磁盘、分区、存储池信息
- 挂载/卸载 VHD 虚拟磁盘
- 分配/移除盘符
- 格式化分区
- 磁盘联机/脱机

### DeviceLayoutGeneratorV2

**imageapp 兼容流程**
1. 解析 `DeviceLayout.xml`（扇区大小、块大小、分区、存储池）
2. 创建 ImageStorageService（ADK 原生 API）
3. 创建虚拟硬盘（VHD/VHDX）
4. 写入 GPT 分区表（顶层分区）
5. 更新分区属性（名称、类型 GUID）
6. 格式化并标记顶层分区（FAT/NTFS/exFAT）
7. 创建存储池（Storage Pool）
8. 创建存储池内虚拟磁盘（Space）并分区格式化
9. 自动卸载虚拟磁盘

**支持的设备布局版本**
- 17686 / 17705 等rs5阶段系统带存储池的设备布局
- 支持 Talkman (AndromedaOS)、GenericGPT 等多种设备布局

**命令行用法**
```
DeviceLayoutGeneratorV2.exe <DeviceLayout.xml> <output.vhd>
```

### wsktool (.NET 版)

**功能**
- WSK 路径自动检测（扫描所有固定磁盘）
- 手动浏览选择 WSK 目录
- 工作区目录选择
- 架构选择：amd64 / arm64 / x86 / arm
- 产品选择：FactoryOS / AndromedaOS / WindowsCoreOS
- 机器类型：物理机 / 虚拟机 (VM)
- 调用 WSK 脚本执行构建
- 实时输出构建日志（黑底绿字控制台风格）

### ffuext (.NET 版)

**功能**
- FFU 镜像文件选择
- 目标磁盘自动枚举（WMI 查询，含型号、容量）
- 刷新磁盘列表
- 调用 DISM `/apply-ffu` 释放镜像
- 实时进度条（解析 DISM 输出百分比）
- 危险操作二次确认
- 完成后显示成功/失败结果

---

## 系统要求

- **操作系统**：Windows 10 1809 / Windows 11 / Windows Server 2019 或更高
- **权限**：部分功能（创建虚拟磁盘、应用 FFU、磁盘管理）需要管理员权限
- **磁盘空间**：建议至少 50GB 可用空间（用于虚拟磁盘创建）
- **.NET 运行时**：所有工具均为 self-contained，无需额外安装 .NET 运行时

---

## 支持的平台

| 平台 | 目录 | 说明 |
|------|------|------|
| **x86 (32位)** | `release\x86\` | 包含 DeviceLayoutGeneratorV2（依赖 32 位 ADK DLL） |
| **amd64 (64位)** | `release\amd64\` | 完整工具集（无 GeneratorV2） |
| **arm64** | `release\arm64\` | 完整工具集（无 GeneratorV2） |

> **注意**：DeviceLayoutGeneratorV2 仅提供 x86 版本，因为它依赖 ADK 的 32 位 `ImageStorageService.dll`。在 amd64/arm64 系统上可通过 WoW64 运行 x86 版本。

---

## 快速开始

### 1. 从设备布局创建虚拟磁盘

1. 运行 `DeviceLayoutExplorer.exe`
2. 点击「打开 XML」或「打开 CAB」加载设备布局
3. 在左侧树状视图中预览分区结构
4. 点击「创建虚拟磁盘」
5. 选择输出路径（.vhd 或 .vhdx）
6. 等待 DeviceLayoutGeneratorV2 运行完成
7. 查看构建日志，确认成功

### 2. 构建 WSK 镜像

1. 运行 `wsktool.exe`
2. 确认或手动选择 WSK 安装路径
3. 选择工作区目录
4. 选择架构、产品、机器类型
5. 点击「Build Image」开始构建
6. 查看实时构建日志

### 3. 释放 FFU 到磁盘

1. **以管理员身份**运行 `ffuext.exe`
2. 点击「Browse」选择 FFU 镜像文件
3. 从下拉列表选择目标磁盘
4. 点击「Apply FFU」
5. 确认危险操作提示
6. 等待释放完成，查看进度

---

## 目录结构

```
WSK_Tools\
├── release\                  编译产物
│   ├── x86\                  32 位版本（含 GeneratorV2）
│   ├── amd64\                64 位版本
│   └── arm64\                ARM64 版本
├── Source\                    源码
│   └── v1.0.4\               v1.0.4 版本源码
│       ├── DeviceLayoutExplorer\    设备布局浏览器 (.NET 8)
│       ├── DeviceLayoutGeneratorV2\ 设备布局生成器 V2 (.NET 8)
│       ├── wsktool.net\             WSK 构建工具 (.NET 8)
│       ├── ffuext.net\              FFU 释放工具 (.NET 8)
│       ├── dl2vhd\                  设备布局转 VHD
│       ├── virtualdiskexplorer\     虚拟磁盘浏览器
│       ├── wcosstagetool\           WCOS 阶段工具
│       ├── MobilePackageGen\        移动包生成器
│       └── ...
└── readme_1.0.4.md           本文档
```

---

## 编译说明

### .NET 8 工具（DeviceLayoutExplorer / GeneratorV2 / wsktool / ffuext）

```bash
# 编译单个项目
dotnet build Source\v1.0.4\DeviceLayoutExplorer\DeviceLayoutExplorer.csproj -c Release

# 发布（self-contained，包含运行时）
dotnet publish Source\v1.0.4\DeviceLayoutExplorer\DeviceLayoutExplorer.csproj -c Release -r win-x64 --self-contained true -o release\amd64
dotnet publish Source\v1.0.4\DeviceLayoutExplorer\DeviceLayoutExplorer.csproj -c Release -r win-x86 --self-contained true -o release\x86
dotnet publish Source\v1.0.4\DeviceLayoutExplorer\DeviceLayoutExplorer.csproj -c Release -r win-arm64 --self-contained true -o release\arm64
```

### C++ 工具（wcosstagetool / dl2vhd / virtualdiskexplorer 等）

使用 Visual Studio 2022 打开 `Source\v1.0.4\WSK_Tools.sln`，选择配置和平台后生成。

---

## 已知限制

1. **DeviceLayoutGeneratorV2 仅 x86**：依赖 32 位 ADK `ImageStorageService.dll`，amd64/arm64 系统需通过 WoW64 运行
2. **存储池创建需要管理员权限**：创建 Storage Spaces 存储池需要管理员权限
3. **动态 VHD 限制**：当前使用动态扩展 VHD，固定大小 VHD 创建较慢但兼容性更好
4. **部分特殊分区类型**：某些 Windows Core OS 专用分区类型（如 DPP、SEC、TZ）可能无法通过标准 API 格式化，需手动处理

---

## 第三方开源项目致谢

本项目在开发过程中使用了以下优秀的开源项目和工具，在此向各项目的贡献者表示衷心感谢：

### 集成到发布产物中的开源库

| 项目 | 用途 | 许可证 |
|------|------|--------|
| **DiscUtils** | .NET 虚拟磁盘与文件系统操作库（ISO/UDF/FAT/NTFS/VHD/VHDX 等） | MIT |
| **Microsoft.Deployment.Compression.Cab** | CAB 压缩/解压库（来自 WiX 工具集） | MS-RL |
| **DotnetPackaging.Msix** | MSIX/Appx 包创建与操作库 | MIT |
| **DeflateBlockCompressor** | Deflate 块压缩算法实现 | MIT |
| **Img2Ffu** | FFU 镜像格式读写库（基于 [github.com/riverar/img2ffu](https://github.com/riverar/img2ffu)） | MIT |
| **Ffu2Vhdx** | FFU 转 VHDX 转换工具（基于 [github.com/riverar/Ffu2Vhdx](https://github.com/riverar/Ffu2Vhdx)） | MIT |
| **LibSxS** | SxS (Side-by-Side) Delta 清单解析库 | MIT |
| **StorageSpace** | Windows Storage Spaces / OSPool 存储池解析库 | MIT |
| **EDLProgram2VHDX** | EDL (Early Launch) 程序转 VHDX 转换工具 | MIT |
| **SevenZipExtractor** | 7-Zip 压缩格式解压封装（包含 7z.dll） | LGPL-2.1 |

### 逆向分析与研究工具（不包含在发布产物中）

| 工具 | 用途 | 许可证 |
|------|------|--------|
| **dnSpy** | .NET 程序集反编译与调试工具（用于分析 ADK 中 .NET 组件） | GPL-3.0 |
| **Ghidra** | NSA 开源软件逆向工程框架（用于分析 ADK 原生 DLL 和 EXE） | Apache-2.0 |
| **Adoptium Temurin JDK 21** | OpenJDK 发行版（Ghidra 运行环境） | GPL-2.0 + Classpath Exception |

### Microsoft 官方工具（非开源，免费使用）

- **Windows ADK**（评估和部署工具包）：提供 DISM、ImageApp、UpdateApp、ImageStorageService.dll 等核心组件
- **Windows WSK**（系统工具包）：提供 FactoryOS / Windows Core OS 构建环境
- **Windows SDK**：提供 Windows API 头文件和库

### 参考项目

- **[DevImgGen](https://github.com/mediaexplorer74/DevImgGen)**：设备镜像生成工具，界面设计参考
- **MobilePackageGen**：移动设备包生成工具，本项目包提取功能的基础

---

## 版本历史

### v1.0.4 (2026-08-27)
- 新增 DeviceLayoutExplorer（替代 DeviceLayout_Exchanger）
- 新增 DeviceLayoutGeneratorV2（imageapp 兼容流程）
- wsktool 重写为 .NET 8 WinForms
- ffuext 重写为 .NET 8 WinForms
- 虚拟磁盘创建后自动卸载
- CAB 加载 XML 持久化保存
- 创建失败自动删除不完整文件
- 创建完成后弹出日志查看窗口

### v1.0.3 (2026-08-25)
- DeviceLayout_Exchanger 多创建选项
- 原生存储池 API 集成
- 磁盘管理功能

### v1.0.2 (2026-08-20)
- 初始工具集发布
- ffuext / wsktool / wcosstagetool C++ 版本
- MobilePackageGen 包提取工具

---

## 组织信息

- **组织**：WinStory 2026
- **Wiki**：https://wiki.win-story.cn


---

*本文档最后更新：2026-08-27*
