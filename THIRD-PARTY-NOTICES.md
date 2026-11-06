# 第三方资源与许可证边界

## 社区汉化资源

来源：https://github.com/GourmetGodTangNiu/MyWhoosh-Chinese-Patch

本项目 v0.2.0 内置上游 v1.1.0 的三项数据：`MyWhooshChinese_P.pak`、`courses.json`、`categories.json`。程序只释放和读取 SHA-256 锁定的资源，不加载 `CoursePatch.dll`，不执行上游 EXE、PowerShell 或其他脚本。

发布者于 2026-11-06 明确确认已获得这三项资源随本项目下载包内置分发的授权，并要求发布内置版。此记录是发布者的授权确认，不是本项目独立核验过的授权书；未附具体许可文本，不能据此宣称资源开源或允许任何使用者任意公开再分发。仓库可见性及访问权限保持原状。

内置 PAK 所附字体许可与 `FONT-NOTICES.txt` 保留在 `licenses/payload-fonts/`。字体许可只覆盖对应字体，不自动覆盖所有翻译和游戏材料。未来更换资源或扩大分发范围时，应重新核对授权。

## 平台与商标

MyWhoosh 名称属于相应权利人。本项目非官方，不隶属于或代表 MyWhoosh。不包含游戏原主 PAK、账号、密钥、个人存档或资源解密能力。GUI 使用 Windows 已安装字体，不复制系统字体文件。

## 自包含运行时

Release 使用官方 Microsoft .NET 自包含发布，保留运行时 `DOTNET-LICENSE.txt` 与 `DOTNET-ThirdPartyNotices.txt`。无需用户安装 .NET 不代表不包含微软运行时及其第三方组件。

## 业务依赖与源码

无第三方 NuGet 业务依赖，不需要 Python、Node、外部窗口管理器或下载汉化资源才能运行。内置社区资源、系统 API 和微软运行时仍分别遵守对应权利及许可。

在维护者明确选定许可证前，原创源码不额外授予开放源码许可证；第三方既有权利不受仓库可见性影响。
