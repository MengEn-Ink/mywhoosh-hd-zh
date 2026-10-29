# 架构与技术选型

## 核心决策

采用 C# / .NET 10 + WinForms + CLI + 文件型事务账本。没有 Web 服务、数据库、Electron、Python 用户依赖或运行时 AI。

WinForms 足以表达导入、检查、安装、恢复和结果日志这几个原生 Windows 动作；比 WPF 更少界面基础设施，比 Electron 更少运行时层。代价是只面向 Windows，界面视觉灵活度低于复杂 Web UI。自包含发布优先小白体验，体积增加是明确取舍。

CLI 和 GUI 只负责参数/按钮，统一调用 Whoosh.Core；不能在两个入口分别拼接 PowerShell 安装脚本。

## 分层

- Presentation：WinForms 主窗体；CLI 严格参数解析。输入校验、异步执行和结果呈现。
- Application：Operations 发现当前用户游戏、检查构建、调用资源仓库、翻译规划和事务。
- Domain：PatchPlan / PlannedChange / TextChange / Journal。计划与写入分离，计划阶段不改游戏。
- Infrastructure：WindowsTarget、SafeFiles、PayloadStore。Appx 发现、SHA-256、ZIP、原子文件写入。

唯一外部进程调用是固定内容的 Appx 只读查询，通过 ArgumentList + EncodedCommand 传入；不把用户输入插到可执行脚本中。

## 数据流

上游 ZIP → 完整路径/大小/哈希检查 → 用户 LocalAppData 的独立 payload

GUI 或 CLI → 当前账户 Appx 定位 → 身份+版本+主 PAK 哈希 → 游戏关闭检查 → 构建 PatchPlan → 备份并写 prepared → 原子写入逐项核验 → installed

Restore → 验证账本归属与备份 → 比对当前值 → 原样未改文件字节恢复 / 后续状态变更文件仅恢复匹配文字 / 未知文字保留冲突

## 通用性

通用的是引擎与测试架构，不是“同版本全部可装”的承诺。首版只有已验证构建。未来增加构建要审查资源键、格式、真实启动和回滚证据，再以代码评审纳入白名单。资源格式版本相同或包名相近都不是充分兼容性证据。

AppInfo 是当前唯一受支持身份与资源指纹来源；没有可以由普通用户随手改出的宽松 manifest。日后如引入远程配置，应加入签名、版本、防回滚以及资源许可链；首版不引入这些额外攻击面。

## 稳定性与恢复

- 先规划后改动，缓存缺失时在写入前失败。
- 严格解码 UTF-8/UTF-16，保留 BOM 和字段大小写。
- 按 Name+Description 精确匹配，未知内容不模糊替换；中文幂等检测。
- 同一状态库文件锁避免并发；游戏进程检查在规划和每次写入前执行。
- 完成全部备份才进入写入；账本先于写入落盘。
- 注入异常测试回滚；进程中断保留 prepared，下次操作先恢复。
- 未知外部修改不强行覆盖。冲突需用户看记录，不能自动丢弃。

本版本允许一次 active 安装事务；若有旧事务且出现新增待更新文件，要求先恢复，再重新安装，避免覆盖首次备份。这比复杂多代合并更保守。旧安装器遗留内容不归新账本认领。

## 发行

Windows Actions runner 使用锁定 SDK：测试 → 自包含 GUI/CLI → CLI 真入口冒烟 → 校验和 → ZIP → 手动批准的预发布 Release。CI 不含用户缓存、上游载荷或游戏文件。生成运行包和分发汉化资源是两个分开的许可问题。
