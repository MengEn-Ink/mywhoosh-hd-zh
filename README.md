# MyWhoosh HD ZH · 竞速座舱

原生 Windows 中文助手，**v0.2.1 内置资源版**。装中文、调小窗、始终置顶、回退英文，集中在一个简单面板里。

仅支持已实测的 Windows x64 商店构建：MyWhoosh HD **6.2.3.0** / `MyWhooshTechnologyService.644173E064ED2`。包族、版本或主资源哈希不同会停止安装，不靠关闭校验硬装。

## v0.2.1 更新

检查与安装分离、真实版本/置顶状态、三条 Varied Tempo #1 精确增补、保留首次备份的增量事务、窗口多文件中断恢复、旧账本迁移回归全部进入此版。新增明确的汉化/窗口中断处理入口、完整长提示、CLI参数严格校验及发布EXE资源首次运行/修复门禁。旧v0.2.0下载包保持原样，升级请使用新的EXE并保留AppData中的备份。

## 下载和使用

到 [v0.2.1 发布页](https://github.com/MengEn-Ink/mywhoosh-hd-zh/releases/tag/v0.2.1)，下载 **mywhoosh-hd-zh-0.2.1-win-x64.zip**（不是 Source code），完整解压后双击 **MyWhooshZH.exe**。

1. 打开游戏，让训练列表加载完成，再退出游戏。
2. 打开中文助手，点「检查中文状态」→「安装 / 更新中文」。资源已内置，不用另外找汉化 ZIP。
3. 在「窗口」页选择尺寸，点「应用窗口设置」，再启动游戏。
4. 要固定在前面，打开「始终置顶」，并让中文助手保持运行。
5. 想回英文，退出游戏，在「恢复」页点「恢复原版英文」。

无需安装 .NET、Python、Node、SDK 或第三方窗口工具；不需要管理员权限。程序以当前 Windows 账户操作。

## 功能

- **03 竞速座舱界面**：深海军蓝、荧光黄、切角面板；总览、汉化、窗口、恢复四页，短句按钮与真实状态。
- **窗口模式**：应用后写入 `FullscreenMode=2`，关闭独占全屏默认配置；支持尺寸预设、标准标题栏移动和游戏允许的边缘缩放。
- **置顶守护**：每 2.5 秒重新置顶游戏窗口，面向普通桌面软件；可随时关闭。
- **汉化资源内置**：课程字典、分类字典、界面 PAK 嵌入程序；首次使用自动释放并核验 SHA-256，不执行资源包中的脚本或 DLL。
- **安全安装与回退**：精确匹配课程文字；先备份，再原子写入；失败回滚、冲突保留；恢复只撤销已记录的修改，保留收藏及训练参数。
- **旧记录迁移**：已有旧安装器备份时，可在恢复页导入旧版回退记录，核验后统一管理。没有有效备份不能凭空恢复。
- **少依赖**：C#/.NET 10、WinForms、自包含 GUI + CLI；无第三方 NuGet 业务依赖。内置汉化资源和微软运行时仍保留各自来源及许可声明，不等于没有第三方版权材料。

## 必须知道的边界

- 管理器本身可拖动缩放，布局验收覆盖 1280×860、1100×760、960×720；最小窗体为 960×720。游戏的边缘缩放仍受游戏自身实现限制，不能把写入配置当成任意尺寸缩放实测成功。
- 置顶需要管理器常开、游戏处于窗口模式；不是安全隔离机制，不能保证压过其他置顶程序、独占全屏或 Windows 安全桌面。
- 未匹配课程、动态内容可能仍为英文。匹配数不是全游戏翻译覆盖率。
- 游戏更新或构建不同会拒绝安装；未覆盖比赛、阻力设备、成绩上传、长期运行、ARM64 或未来版本。
- 代码未签名。请核对下载来源与 SHA-256，不要关闭杀毒或 SmartScreen。仓库访问权限保持不变，私有仓库下载需已有读取权限。

## 验证

发布构建跑83项基础/专项/真实子进程中断回归、发布EXE version/help、GUI/CLI各自的内嵌资源首次释放/缓存幂等/损坏修复、25张GUI布局截图和13项点击分发，再生成ZIP与SHA256SUMS。

本轮完整英文隔离集成合计84项：扫描2063、匹配2063、未匹配0、安装/恢复各2064文件、原哈希全部复原。96/120/144/192 DPI压力测试采用模拟布局尺度，不是物理多显示器跨屏实测；25图零文字越界告警还需人工复核。真实游戏自由拖边、长期置顶、硬件与成绩同步不在此轮验证范围。最终结果见[验证记录](docs/VALIDATION.md)与发布资产。

## 项目结构

```text
src/Whoosh.Core/     构建识别、内嵌资源、翻译计划、事务/窗口回退
src/Whoosh.Cli/      命令行入口
src/Whoosh.Gui/      自绘竞速座舱、异步操作、置顶守护
licenses/           内置资源附带的字体许可与来源说明
tests/Whoosh.Tests/  安全、事务、窗口、资源回归与隔离集成
scripts/build.ps1   测试 → 自包含发布 → CLI/GUI 冒烟 → 校验和 → ZIP
.github/workflows/  CI 与新版本发行、资产状态回读
docs/               新手、架构、更新、验证及发行说明
```

## 开发

使用 `global.json` 锁定的官方 .NET SDK，在 Windows 构建。

```powershell
dotnet build src/Whoosh.Gui/Whoosh.Gui.csproj -c Release
dotnet run --project tests/Whoosh.Tests/Whoosh.Tests.csproj -c Release
./scripts/build.ps1 -Version 0.2.1
./MyWhooshZH.exe --layout-selftest ./layout-test
./MyWhooshZH.exe --ui-selftest ./game-test
./whoosh-zh.exe check
./whoosh-zh.exe install --yes
./whoosh-zh.exe recover --yes
./whoosh-zh.exe window-recover --yes
./whoosh-zh.exe payload-selftest --directory ./new-isolated-payload
./whoosh-zh.exe restore --yes
```

`--layout-selftest` 仅验证布局，适合无游戏 CI；`--ui-selftest` 会额外检查本机游戏并做置顶往返，不能把 CI 布局通过等同于真机完整测试。未知参数报错；退出码 0 表示对应操作完成，不代表所有游戏功能已验证。

## 文档和声明

- [新手指南](docs/QUICKSTART.md)
- [v0.2.0 更新说明](docs/UPDATE-0.2.0.md)
- [架构](docs/ARCHITECTURE.md)
- [构建发行](docs/RELEASING.md)
- [验证结果与边界](docs/VALIDATION.md)
- [安全](SECURITY.md)
- [资源来源与许可边界](THIRD-PARTY-NOTICES.md)

本项目非官方，不包含游戏原主资源、个人缓存、存档、账号或密钥。发布者于 2026-11-06 确认已获得三项汉化资源随本项目下载包内置分发的授权；未附独立授权书，因此不将其表述为可任意公开再分发或开源许可。原创源码暂未另行授予开放源码许可证。
