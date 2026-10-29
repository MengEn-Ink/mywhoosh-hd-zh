# MyWhoosh HD ZH

Windows 原生中文汉化管理器。GUI 与 CLI 共用 C#/.NET 10 核心，提供资源导入、版本检查、事务安装、内容核验与有边界的恢复。

**当前：0.1.0 预发布。仅允许已验证的 Windows x64 商店构建 6.2.3.0 / `MyWhooshTechnologyService.644173E064ED2`。不支持所有同版本 SKU。**

## 小白从哪里开始

1. 在本仓库 Releases 下载 `mywhoosh-hd-zh-0.1.0-win-x64.zip`，完整解压。
2. 双击 `MyWhooshZH.exe`。运行包自带 .NET 运行时，不需要安装 Python、Node、SDK。
3. 从[上游项目](https://github.com/GourmetGodTangNiu/MyWhoosh-Chinese-Patch)自行取得 `MyWhoosh_HD_6.2.3.0_Chinese_Package_v1.1.0.zip`，点击“导入汉化资源”。本软件只读取其中三个哈希锁定的数据文件，不运行 EXE、DLL 或 PowerShell 载荷。
4. 用玩游戏的同一 Windows 账户启动游戏、进入训练列表，然后完全退出。
5. 点击“检查安装状态”，再“安装 / 更新”。最后打开游戏验证中文。

详细说明：[新手指南](docs/QUICKSTART.md)、[架构与技术选型](docs/ARCHITECTURE.md)、[构建与发行](docs/RELEASING.md)、[安全边界](SECURITY.md)。

## 已有旧版汉化怎么办

已匹配的中文会跳过，已知同哈希 PAK 不覆盖。新工具不会伪造旧版备份或删除不归自己管理的文件。若已经用旧安装器完成汉化，恢复旧版请继续使用原来的恢复程序；需要完整迁移时先通过原恢复器恢复，再用新工具安装。

新工具能保留 UTF-8/UTF-16 字节序与 BOM，并保持课程顶层字段名大小写。它只修改精确匹配字典的 Name/Description，其他课程数据保持 JSON 语义不变。未知课程不猜译，不改变训练功率或时长。

## 技术选择

- .NET 10 + C#：Windows 文件操作、JSON、ZIP、SHA-256、进程和异步任务均使用标准库。
- WinForms：原生桌面窗口，适合检查/安装/恢复这类少量操作；不引入 Electron、浏览器或 Web 后端。
- 共享 `Whoosh.Core`：GUI/CLI 不重复实现安装逻辑。
- 自包含 x64 ZIP：开箱即用；体积大于框架依赖部署，但不要求用户装运行时。
- 静态受支持配置编译进核心：未知构建默认拒绝，避免用户只改 manifest 哈希就误认兼容。
- 本地事务账本：先备份与 prepared 日志，再写入；失败回滚，中断后可恢复；遇到后续未知修改保留并报冲突。

## 项目结构

```text
src/Whoosh.Core/       安全路径、平台发现、资源导入、翻译计划、事务恢复
src/Whoosh.Cli/        diagnose/check/import/install/restore/version
src/Whoosh.Gui/        原生中文界面，异步执行，操作互斥
 tests/Whoosh.Tests/   无外部测试框架依赖的可执行回归测试
scripts/build.ps1     测试、自包含发布、CLI冒烟、ZIP与校验和
.github/workflows/    PR/push CI、手动发行
 docs/                架构、新手、发行与验证边界
```

## 命令行

```powershell
.\whoosh-zh.exe diagnose
.\whoosh-zh.exe import --zip "C:\Downloads\MyWhoosh_HD_6.2.3.0_Chinese_Package_v1.1.0.zip"
.\whoosh-zh.exe check
.\whoosh-zh.exe install --yes
.\whoosh-zh.exe restore --yes
```

未知参数会报错。`check` 为只读游戏检查；它会创建本工具日志和状态锁目录，但不会改游戏文件。退出码 0 表示命令完成，不等于每个游戏页面或在线功能都已经验证。恢复冲突返回 3，错误返回 1。

## 本地开发

安装 `global.json` 指定的官方 .NET SDK。项目本身没有 NuGet 第三方依赖。

```powershell
dotnet build src/Whoosh.Gui/Whoosh.Gui.csproj -c Release
dotnet run --project tests/Whoosh.Tests/Whoosh.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Version 0.1.0
```

`artifacts/build-0.1.0/` 输出运行包。构建脚本遇到既有目录会停止，不会默默删除历史产物。必须在 Windows 构建 GUI。CI 使用 Windows runner。

## 资源与授权

仓库与发行 ZIP **不包含**第三方 PAK、课程翻译字典、DLL、用户缓存、存档、密钥或个人截图。不因仓库私有就假定有再分发授权。上游授权状态见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md)。本仓库原创源码暂未授予开放源码许可证，后续公开前由维护者决定。

## 验证范围

回归覆盖精确恢复、收藏保留、未知文字冲突、注入失败回滚、并发锁、账本归属、备份损坏、路径穿越、ZIP 缺失、UTF-8/UTF-16、JSON Pointer 和重复操作。`dotnet run tests` 的真实结果才是权威，不把固定徽章当测试结论。

本机原补丁曾实际加载出中文；本工具的全量隔离缓存验收、GUI检查和流水线结果见 docs/VALIDATION.md。CI 不具备你的游戏、账户或训练硬件，不能证明比赛、阻力或成绩上传安全。没有代码签名证书，Windows 可能提示未知发布者；请核对来源与 SHA-256，不要关闭安全软件。
