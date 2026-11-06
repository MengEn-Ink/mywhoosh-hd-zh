# GitHub Actions 构建与发行

## 本地

使用 global.json 固定的官方 .NET SDK，在 Windows x64 构建：

```powershell
dotnet run --project tests/Whoosh.Tests/Whoosh.Tests.csproj -c Release
./scripts/build.ps1 -Version 0.3.0 -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

可以传 `-Dotnet <dotnet.exe绝对路径>`，不修改系统 PATH。版本格式先校验；`artifacts/build-<版本>` 存在时拒绝覆盖，保留历史产物。

## CI 门禁

`.github/workflows/ci.yml` 在 main push、PR、手动运行触发，contents:read。构建版本来自 release-version.txt，不再固定 0.1.1。

1. 基础回归（含三项内嵌资源释放/幂等/修复）非零停止。
2. 发布 GUI 和 CLI：Windows x64、自包含、单文件。
3. 跑发布后的 CLI version/help。
4. 跑发布后GUI `--layout-selftest`：九张基础尺寸图与16张长文本/96–192 DPI模拟压力图，共25图；13项点击分发、零越界告警，以及GUI/CLI发布EXE各自的隔离内嵌资源首次释放/缓存/修复均需通过。DPI模拟不等于物理显示器迁移测试。
5. 跑浮窗8项隔离测试、生成ICO图标；Inno Setup 6编译生产Setup和单独测试AppId安装器。CI测试安装、快捷方式、旧元数据升级、重装、安装目录EXE自检、卸载和未知用户文件保留。
6. 保留文档/资源许可/微软及Inno声明，生成ZIP、Setup.exe、两个SHA-256、tests.json、gui-layout.json、quick-layout.json、installer-tests.json。
7. 上传Artifact，保留14天。生产Setup、ZIP和报告进入Release，隔离测试安装器不发布。

内置 PAK 必须真正跟踪到 Git（`.gitignore` 只为固定 payload 路径开例外），否则远端无法编译，不能只看本机成功。

## 发行

先提交已测试的源码、资源、说明和工作流，再单独提交 release-version.txt 的版本变更到 main。该文件变化才自动发行；普通源码提交只触发 CI。也可手动运行 Release Windows package，填写新版本号。

v0.2.0 默认为非预发布版本；手动 prerelease=true 时才标为预发布。已有版本不覆盖、不删除、不强推。

发行 job 使用专用 GITHUB_TOKEN（contents:write），调用同一构建脚本，创建不可变版本标签及 Release，上传：

- mywhoosh-hd-zh-<版本>-win-x64-setup.exe
- quick-layout.json
- installer-tests.json
- mywhoosh-hd-zh-<版本>-win-x64.zip
- SHA256SUMS.txt
- tests.json
- gui-layout.json
- release-evidence.json（资产状态回读后生成）

每个必需资产要回读 state=uploaded，证据记录 run URL、commit、asset name/size/digest。`.github/last-release.json` 由 Actions 保存；不把静态说明或本地 ZIP 当成远端发布成功。

## 下载包

- MyWhooshZH.exe：竞速座舱 GUI，内置三项汉化资源。
- whoosh-zh.exe：共享核心 CLI，同样内置资源。
- README / QUICKSTART / docs：功能、步骤、回退和边界。
- licenses/payload-fonts：上游资源所附字体许可与声明。
- DOTNET-LICENSE / DOTNET-ThirdPartyNotices：官方运行时许可。
- SHA256SUMS.txt：包内文件校验值。

发布者确认三项汉化资源获随本项目下载包内置分发授权，见 THIRD-PARTY-NOTICES；没有另授无限制再分发或开源权利。仓库权限保持不变；私有仓库下载需读取权限，不承诺匿名公开下载。

默认开发机需提供Inno编译器路径，用户无需Inno。安装集成只在GITHUB_ACTIONS=true的runner执行；隔离AppId/安装目录/快捷方式不触真实用户系统。升级测试使用0.2.9元数据配同版载荷，并非实际旧包；托盘测试不创建真实系统托盘图标。

## 能证明什么

CI 无真实游戏。`--layout-selftest` 只证明发布 GUI 能渲染及布局门禁通过，不做真实游戏检查或置顶操作。本地 `--ui-selftest` 才做本机检查及可恢复的置顶往返。隔离缓存测试不上传个人缓存。

全游戏页面、比赛、阻力硬件、成绩上传、长期运行、未来版本和游戏任意边缘拖动缩放不由 CI 保证。未签名，请核对仓库与 SHA-256，不关闭杀毒或 SmartScreen。Actions major 标签仍需后续维护审核，SDK 已锁定版本。
