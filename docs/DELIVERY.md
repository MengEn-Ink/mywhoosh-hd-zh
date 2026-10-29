# 本次交付与运行说明

## 完成状态

本地完整项目：D:\workspace\mywhoosh-hd-zh

私有 GitHub 仓库：https://github.com/MengEn-Ink/mywhoosh-hd-zh

已通过 GitHub Actions 自动创建的推荐预发布：
https://github.com/MengEn-Ink/mywhoosh-hd-zh/releases/tag/v0.1.1

实际发行工作流：
https://github.com/MengEn-Ink/mywhoosh-hd-zh/actions/runs/40920266641

工作流对应源码提交：a4e461f341f28065cc5b5506a2b1c69ccbe6ca01

## 远端发行资产证据

通过 GitHub 插件回读 .github/last-release.json，确认以下资产均为 uploaded：

- mywhoosh-hd-zh-0.1.1-win-x64.zip，78,559,840 字节。
  SHA-256：b8518094e87f325c001d62f5cee59e04b66c77d610615adab858850a74a07293
- SHA256SUMS.txt，99 字节。
- tests.json，55 字节。

发行状态：prerelease=true，draft=false，发布者 github-actions[bot]。

Release 工作流是在测试、构建和发布后 CLI 冒烟检查成功后创建发行版，再回读上传状态，并生成 release-evidence.json 与 .github/last-release.json。此回读不是对 ZIP 文件存在的猜测。

已确认远端资产上传状态，未在此电脑下载远端私有 Release ZIP 并再次运行；本机实际运行、GUI验收和 ZIP 完整性验证针对本地构建包，不混淆两种来源。

## 本地运行包

ZIP：D:\workspace\mywhoosh-hd-zh\artifacts\build-0.1.1\mywhoosh-hd-zh-0.1.1-win-x64.zip

大小：78,191,035 字节。
SHA-256：d555e0a460890b417d7a74fc1ef54c64b9125265665de008e096383063ba418f

本地与远端 ZIP 不是逐字节相同产物：编译环境、时间戳、文档版本和压缩元信息可能不同，不能用本地哈希代替远端校验和。

打开方式：完整解压，双击 MyWhooshZH.exe；命令行入口为 whoosh-zh.exe。自包含 .NET 运行时，不需安装 Python、Node 或 SDK。

已为当前用户添加“ MyWhoosh HD 中文管理器 ”开始菜单快捷方式，指向上述 0.1.1 解压目录。本轮在虚拟桌面完成 GUI 验收，任务窗口已关闭。

本机已将上游 v1.1.0 ZIP 的三个资源导入 %LOCALAPPDATA%\MyWhooshHdZh\payload\v1.1.0。它们只在本地，不上传仓库、Release 或 CI。

## 实际验证结果

- 29 项基础回归成功，失败 0。
- 加全量隔离缓存集成共 30 项成功，失败 0。
- 隔离副本扫描 2063 份缓存、匹配 2060 份、未匹配 3 份。
- 安装与恢复 2062 个文件；全部原缓存 SHA-256 恢复一致。
- 已运行发布后 CLI version 和 help。
- 最终 0.1.1 GUI 实际启动，中文摘要、检查按钮和 CLI 返回一致。
- 真实游戏当前检查：2056 份已汉化，4 份额外 UTF-16 缓存可匹配，3 份仍未匹配；没有在这轮重构中强行更新真实游戏。
- ZIP 完整性、必需 EXE/说明/许可证条目及内部 12 个文件校验和全部复核。
- 源码提交范围不包含本地账号目录、缓存、备份、存档、PAK、字典或访问凭据。

## 已有汉化如何使用

本机游戏上一轮汉化状态保留。新软件检查显示 unmanaged 是因为旧汉化归原安装器的账本管理，不是中文丢失。

先用新软件检查即可，不必立即恢复或重装。若要完整迁移备份归属，需用旧版恢复器先恢复英文，再由新工具安装；本轮没有这么做，避免改变正在使用的游戏状态。

## 项目与 Git

本地新目录已初始化 Git 并保存完整原创源码快照，origin 指向私有仓库。远端写入通过 GitHub 插件完成，所以本地根提交与插件创建的远端提交历史不同；没有强推或重置历史。不要直接强推本地根提交覆盖远端。

正常协作可另外克隆远端作为后续工作目录，或经明确确认再整合两端历史。本次已有项目文件保持在指定的新目录。未来需要统一历史时不得用 reset --hard 抹掉当前工作。

## 架构和发行使用

C#/.NET 10 + WinForms + CLI，共用 Whoosh.Core；资源导入、精确构建校验、翻译计划、事务备份恢复与状态记录在核心层。

CI：提交/PR → Windows runner → 固定 SDK → 29 项回归 → 自包含发布 → CLI 冒烟 → Artifact。

发行：手动运行 release.yml 或单独提交 release-version.txt → 测试与打包 → 新标签与预发布 → 上传资产回读与证据记录。已有版本不覆盖。

完整说明分别在 README.md、docs/QUICKSTART.md、docs/ARCHITECTURE.md、docs/RELEASING.md、SECURITY.md 和 THIRD-PARTY-NOTICES.md。

## 不承诺的范围

仅已验证的本机 x64 6.2.3.0 构建；未知版本拒绝安装。未实测全游戏页面、阻力、比赛、成绩上传、长时间运行、ARM64和未来更新。没有代码签名证书，不建议关闭杀毒或 SmartScreen。第三方资源再分发授权未确认，因此不内置运行包，需要用户自行导入。

本版本是可运行且完成核心验收的预发布，不是所有平台、所有版本或所有在线功能都验证的稳定正式版。
