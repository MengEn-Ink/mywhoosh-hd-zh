# v0.2.0 正式发行验证

发布页：https://github.com/MengEn-Ink/mywhoosh-hd-zh/releases/tag/v0.2.0

成功的发行工作流：https://github.com/MengEn-Ink/mywhoosh-hd-zh/actions/runs/42141277742

发行提交：523804779181945f0b1203b44a4ed01559edff98。draft=false、prerelease=false。

正式 ZIP 已回读 state=uploaded，101,909,322 字节；SHA-256：2bbdf4a1be9cb2504110e86ed82760c4039836d10b9bb20685b9819423da082c。

工作流重新下载该 ZIP，验证哈希一致（redownloadHashMatches=true），解压后 CLI version=0.2.0、GUI layout pass=true。45 项基础回归通过；本机含隔离集成共 46 项通过。six-page 布局报告、tests.json、SHA256SUMS.txt 和 release-evidence.json 已随版本提供，完整回读证据在 .github/last-release.json。

保留明确边界：任意游戏边缘缩放未完整真机验证、置顶不对抗安全桌面或其他置顶程序，比赛/硬件/成绩上传/长期运行和未来版本未覆盖。Actions 有一条旧 action Node 20 退役告警，实际强制使用 Node 24，本次运行成功，后续维护应审核升级 action。

首次使用请完整解压 win-x64.zip 并双击 MyWhooshZH.exe；资源与 .NET 已内置。详见 README 与 QUICKSTART。
