# MyWhoosh HD ZH · 竞速座舱

原生 Windows 中文助手，**v0.3.1 标准安装＋快捷浮窗版**。装中文、调小窗、始终置顶、回退英文，集中在一个简单面板里。

仅支持已实测的 Windows x64 商店构建：MyWhoosh HD **6.2.3.0** / `MyWhooshTechnologyService.644173E064ED2`。包族、版本或主资源哈希不同会停止安装，不靠关闭校验硬装。

## v0.3.1 更新

修复v0.3.0操作后全量备份校验阻塞UI：慢状态读取/日志改后台，浮窗与主面板共享轻量运行态，草稿不被置顶抹掉，隐藏确认/错误可见。新增6项慢后端UI响应性门禁；安全校验与备份不删除。游戏账号/成绩网络同步不属于助手刷新修复。

新增标准安装向导：当前用户安装到硬盘、可选独立目录、桌面/开始菜单快捷图标、系统卸载入口。新增快捷浮窗和系统托盘，快速开关游戏置顶、选择尺寸、打开主面板；关闭面板收起，托盘明确退出才停止助手。安装/卸载不改变游戏，卸载保留汉化备份，默认不开机自启。v0.2.1的增量事务与安全回退全部保留。

## 下载和使用

到 [v0.3.1 发布页](https://github.com/MengEn-Ink/mywhoosh-hd-zh/releases/tag/v0.3.1)，普通用户下载 **mywhoosh-hd-zh-0.3.1-win-x64-setup.exe**，双击按向导安装。默认 `%LOCALAPPDATA%\Programs\MyWhooshHdZh`，无需管理员权限，可选独立安装目录。

桌面/开始菜单的「MyWhoosh 中文助手」打开主面板，「MyWhoosh 快捷浮窗」打开小面板。也保留 **mywhoosh-hd-zh-0.3.1-win-x64.zip** 便携版；完整解压后运行EXE。Source code不是运行包。

1. 打开游戏，让训练列表加载完成，再退出游戏。
2. 打开中文助手，点「检查中文状态」→「安装 / 更新中文」。资源已内置，不用另外找汉化 ZIP。
3. 在「窗口」页选择尺寸，点「应用窗口设置」，再启动游戏。
4. 要固定在前面，打开「始终置顶」，并让中文助手保持运行。
5. 想回英文，退出游戏，在「恢复」页点「恢复原版英文」。

无需安装 .NET、Python、Node、SDK 或第三方窗口工具；不需要管理员权限。程序以当前 Windows 账户操作。

## 功能

- **标准安装/卸载**：仅当前账户安装，桌面/开始菜单两个入口，不自动汉化、不设置开机自启；卸载保留AppData备份和游戏当前状态。
- **快捷浮窗＋托盘**：独立可拖小面板、实际置顶状态、尺寸草稿与确认应用。关闭面板收起到托盘；托盘退出停止助手。重复打开激活同一实例。
- **03 竞速座舱界面**：深海军蓝、荧光黄、切角面板；总览、汉化、窗口、恢复四页，短句按钮与真实状态。
- **窗口模式**：应用后写入 `FullscreenMode=2`，关闭独占全屏默认配置；支持尺寸预设、标准标题栏移动和游戏允许的边缘缩放。
- **置顶守护**：500ms轻量后台刷新，句柄验证、单飞、防过期结果；只在置顶位缺失时重试，不等待游戏线程。面向普通桌面软件，可随时关闭。
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

安装编译使用Inno Setup 6，仅为开发/CI工具，不是用户依赖。CI额外验证安装器的隔离AppId、快捷方式目标/浮窗参数、版本元数据升级、重复安装、卸载与未知文件保留，并运行安装目录下的GUI浮窗自检。测试不把生产安装器安装到用户电脑。

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
./scripts/build.ps1 -Version 0.3.1 -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
./MyWhooshZH.exe --floating
./MyWhooshZH.exe --quick-selftest ./quick-test
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
