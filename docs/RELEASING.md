# GitHub Actions 构建与发行

## 本地开发构建

Windows x64，使用 global.json 固定的 .NET 10 SDK。

```powershell
dotnet run --project tests/Whoosh.Tests/Whoosh.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Version 0.1.0
```

项目专用 SDK 可通过 `-Dotnet <dotnet.exe绝对路径>` 传给 build.ps1，不改变系统 PATH。输出目录已经存在时脚本拒绝覆盖；应保留旧版本并在干净检出中重建。

## CI

`.github/workflows/ci.yml` 在 main push、PR、手动运行触发。权限 contents:read。Windows runner 安装锁定 SDK，然后调用与本地相同的构建脚本：

1. 运行可执行回归测试；非零立即停止。
2. 自包含发布 CLI、WinForms GUI。
3. 运行发布后的 CLI version / help，而不只检查文件存在。
4. 打包 ZIP、SHA256SUMS.txt、tests.json。
5. 上传 Artifact，保存 14 天。

Artifact 是一次构建的产物，Release 是长期版本分发，不要混淆。

## 正式创建发行版

GitHub → Actions → Release Windows package → Run workflow。

- version：例如 0.1.0；首版保持 prerelease=true。
- 工作流校验版本格式，不将输入直接拼进可执行 shell 源码。
- 重新运行测试与构建，生成 ZIP 和校验和。
- 仅发行 job 具有 contents:write，通过 job 专用 GITHUB_TOKEN 创建 v0.1.0 标签与发行资产。
- 已有同名版本不覆盖、不删除、不强推；修复后使用新版本号。

发行有两种显式入口：手动 Run workflow，或向 main 提交 release-version.txt 的版本变更。后者供插件工作流使用，只在该文件变化时触发，默认预发布；普通源码提交不触发发行。版本文件必须在完整代码已就绪后单独提交。无需长期 PAT、个人 SSH key 或外部云平台。

## 开箱运行包

- MyWhooshZH.exe：原生中文界面。
- whoosh-zh.exe：命令行入口。
- README / QUICKSTART / docs：使用与恢复说明。
- DOTNET-LICENSE / DOTNET-ThirdPartyNotices：运行时声明。
- SHA256SUMS.txt：文件校验值。

不含上游 PAK/字典，用户导入已有合法资源包。私有仓库发行资产需要有该仓库读取权限，不能向无权限用户承诺匿名下载。

## 验证与限制

将可复现本地测试和 Actions run 链接写入 VALIDATION。CI runner 没有真实游戏，不启动游戏，不触碰个人账户，不承诺训练硬件功能。

本项目没有购买代码签名证书。SHA-256 校验不能替代发布者身份签名；请核对下载仓库与工作流来源。不要建议用户关闭杀毒或 SmartScreen。

Actions 当前使用官方维护的 major 标签；生产长期维护建议审核并固定具体 action commit SHA，以降低上游标签变化风险。SDK 已锁定具体版本。
