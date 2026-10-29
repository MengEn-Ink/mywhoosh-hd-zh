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
