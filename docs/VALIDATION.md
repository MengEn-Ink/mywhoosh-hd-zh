# 验证记录与边界

## 本地代码验证

环境：Windows x64，.NET SDK 10.0.403（官方 SDK 包 SHA-512 已核验），GUI/CLI 均已成功编译，warnings-as-errors。

29 项基础回归全部通过；额外全量隔离缓存集成测试通过，总计 30 项、失败 0。

全量测试使用用户已有安装前备份的隔离副本，未将该备份或缓存上传 GitHub：
- 扫描 2063 份课程缓存。
- 精确匹配 2060 份，未匹配 3 份。
- 安装修改 2062 个文件（课程 + 分类 + PAK）。
- 再次规划无待变更，幂等检查通过。
- 恢复 2062 个文件，全部原有缓存 SHA-256 恢复一致。

本机真实目标只读检查识别已由旧安装器汉化的 2056 份，检测到新增可匹配 UTF-16 缓存 4 份；未在本轮强制迁移或恢复旧安装器状态。

## 已执行的构建验证

scripts/build.ps1 已本地执行：29 项基础回归全部通过，自包含发布 GUI/CLI、运行发布后 CLI 的 version/help、生成 ZIP 和 SHA256SUMS。最终改进版本为 0.1.1，未覆盖已有发行版。

## GUI与远端流程

发布后的原生 GUI 已在虚拟桌面启动，检查按钮实际完成扫描并显示与 CLI 一致的结果：2063 扫描、2060 匹配、2056 已中文、3 未匹配、4 个待更新文件。未在真实游戏目录执行更新，以保留已有安装状态。

GitHub 私有仓库： https://github.com/MengEn-Ink/mywhoosh-hd-zh

v0.1.0 已由 github-actions[bot] 创建预发布： https://github.com/MengEn-Ink/mywhoosh-hd-zh/releases/tag/v0.1.0

工作流采用提交 release-version.txt 触发，避免依赖未登录的网页端。0.1.1 增加了发行后的 assets 状态回读、release-evidence.json 和仓库中的 .github/last-release.json；实际上传资产和 run URL 以该运行时证据为准，不用静态文档代替。

## 明确未覆盖

全游戏页面、比赛/阻力/硬件控制、成绩上传、长期运行、游戏未来版本、其他 SKU、Windows ARM64、恶意同用户进程对抗、已被用户删除的备份。运行包不含第三方汉化资源，也没有代码签名证书。

CI runner 无真实游戏。即使 CI 和 Release 成功，也只能证明构建与测试流水线，不代表游戏运行时所有功能。

## 0.2.0 竞速座舱界面（本轮）

界面按用户选定的「03 竞速座舱」重写为单一自绘 `CockpitView`：左侧 104px 导航栏、切角横幅与三格遥测、总览三列切角卡片、汉化/窗口/恢复三个详情页，全部坐标经 `DeviceDpi/96` 缩放，窗口从 960×720 到 2560×1440 重排；不再使用 `Region` 圆角或子控件拼装。所有数值都来自真实调用（`Operations.Check`、`GameWindow.Inspect`、`Transactions.Status`、`PayloadStore.Validate`、备份文件 mtime），不是设计稿示例值。

自验收入口 `MyWhooshZH.exe --ui-selftest <dir>` 一次完成三件事：
- 渲染 4 个页面 + 1100×760 小窗 + 960×720 极窄窗，共 6 张 PNG，文字越界自检写入 `ui-selftest.json`。
- 执行真实 `Operations.Check()`。
- 对正在运行的游戏窗口做置顶往返：读取 `WS_EX_TOPMOST` → 置顶 → 复核 → 还原 → 复核。

本轮结果（2026-11-06，本机真实环境）：
- 6 张截图文字越界告警均为 0；逐张肉眼核对断行、相邻文本与切角边界。
- 检查：扫描 2063、匹配 2060、已是中文 2060、未匹配 3、待写入 0，`ManagedState=installed`。
- 置顶往返：`before=false → afterPin=true → restored=true`（真实游戏窗口句柄），证明置顶位真的生效且可无损还原。
- `pass=true`。

回归：基础 42 项、隔离缓存集成 43 项全部通过；集成结果为扫描 2063 / 匹配 2060 / 未匹配 3，恢复后 `AllOriginalHashesRestored=true`。

窗口最小尺寸降为 960×720，仍在 1100×760 与 960×720 实测无重叠、无裁切。
