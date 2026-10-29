# MyWhoosh HD ZH

原生 Windows 汉化管理器，C#/.NET 10 + WinForms + CLI，共享安装核心。当前推荐版本 **0.1.1 预发布**。

首版仅支持已验证的 x64 商店构建：6.2.3.0 / MyWhooshTechnologyService.644173E064ED2。未知版本只诊断、不安装；不承诺所有同版本 SKU 通用。

## 小白快速开始

1. Releases 下载 mywhoosh-hd-zh-0.1.1-win-x64.zip，完整解压。
2. 双击 MyWhooshZH.exe，不需要 Python、Node、SDK 或手动安装 .NET。
3. 从上游 https://github.com/GourmetGodTangNiu/MyWhoosh-Chinese-Patch 取得 MyWhoosh_HD_6.2.3.0_Chinese_Package_v1.1.0.zip，点击“导入汉化资源”。
4. 用当前 Windows 账户打开游戏训练列表，随后完全退出。
5. 点击检查、安装，然后启动游戏看中文。无需管理员权限。

资源包不内置。导入器只读取三个 SHA-256 锁定的数据文件，不运行包内 EXE、DLL 或 PowerShell。

## 功能和安全

- 自动发现当前账户 Appx 包，精确检查包族、版本与主资源哈希。
- UTF-8/UTF-16 严格解码、保留 BOM 和 Description 字段大小写。
- 课程 Name + Description 精确匹配，不改训练参数、不猜译未知课程。
- 先规划、备份和 prepared 账本，再原子写回核验；失败回滚、中断恢复、并发锁。
- 恢复保留后续收藏等非文字状态，未知文字改动报冲突。
- 不改 WindowsApps 权限，不覆盖原主 PAK，不关闭安全软件。
- 已有旧安装器汉化不自动认领，恢复旧版仍用原恢复程序；本工具只恢复自己备份过的内容。

## 技术与结构

WinForms 适合少量原生文件操作，比引入 Electron/Web 后端更少运行时层；代价是 Windows 专用。自包含 ZIP 体积较大，但小白不必安装运行时。无第三方 NuGet 业务依赖。

```text
src/Whoosh.Core/     Appx发现、安全路径、资源导入、翻译计划、事务恢复
src/Whoosh.Cli/      严格命令行入口
src/Whoosh.Gui/      中文原生界面和异步执行
tests/Whoosh.Tests/  可执行回归测试与可选隔离缓存集成
scripts/build.ps1   测试→自包含发布→冒烟→ZIP和校验和
.github/workflows/  push/PR CI及显式版本发行
docs/               新手、架构、发行、验证边界
```

## 开发和命令行

使用 global.json 指定的官方 SDK，在 Windows 构建。

```powershell
dotnet build src/Whoosh.Gui/Whoosh.Gui.csproj -c Release
dotnet run --project tests/Whoosh.Tests/Whoosh.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Version 0.1.1
.\whoosh-zh.exe diagnose
.\whoosh-zh.exe import --zip "C:\Downloads\MyWhoosh_HD_6.2.3.0_Chinese_Package_v1.1.0.zip"
.\whoosh-zh.exe check
.\whoosh-zh.exe install --yes
.\whoosh-zh.exe restore --yes
```

未知参数报错。退出码 0 是命令完成，不等于所有游戏功能验证；错误 1、恢复冲突 3。check 只读游戏，但写本工具日志。构建目录已有文件时停止，保留历史产物。

## Actions 发行

CI：Windows runner → 锁定 SDK → 回归 → GUI/CLI 自包含打包 → CLI 真入口冒烟 → Artifact。

发行：手动 Run workflow 或单独提交 release-version.txt → 同一构建脚本 → 创建新版本 Release → 回读上传资产 → 保存 .github/last-release.json。旧版本不覆盖。私有仓库下载需要访问权限。

## 文档

- [新手指南](docs/QUICKSTART.md)
- [架构与技术选型](docs/ARCHITECTURE.md)
- [Actions构建发行](docs/RELEASING.md)
- [真实验证结果](docs/VALIDATION.md)
- [安全边界](SECURITY.md)
- [第三方声明](THIRD-PARTY-NOTICES.md)

29 项基础回归和全量隔离集成共 30 项通过。隔离缓存扫描 2063 份、匹配 2060 份；安装/恢复 2062 个文件，全部原文件哈希复原。原生窗口与检查按钮已实测；真实游戏安装状态未被这轮重构覆盖。

不含个人缓存、存档、密钥或上游汉化载荷；整体载荷再分发授权尚未确认，因此用户自行导入。原创源码暂未授予开放源码许可证。代码未签名，请核对下载来源和 SHA-256，别关杀毒。比赛、阻力、成绩上传、长期运行和未来版本未验证。
