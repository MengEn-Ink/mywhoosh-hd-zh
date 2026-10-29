# 第三方资源与许可证边界

## 上游

汉化资源来源：https://github.com/GourmetGodTangNiu/MyWhoosh-Chinese-Patch

本仓库只实现原创的导入、诊断、安装与恢复引擎。运行时从用户本地导入上游 v1.1.0 ZIP 中的 MyWhooshChinese_P.pak、courses.json、categories.json；不加载 CoursePatch.dll，不执行 Installer.ps1 或其他脚本。

现有材料尚不足以确认整体汉化载荷、字典和嵌入资源的再分发许可证，因此**不复制进 Git 仓库、不放入 GitHub Release、不由 Actions 下载或打包这些载荷**。公开可下载不等于可以随意再分发。

上游 ZIP 中的字体许可文件只说明对应字体的授权，并不自动覆盖整体 PAK、所有翻译或游戏资源。使用者应保留上游原包及其 Licenses 目录。若维护者获得明确授权，再以独立变更记录加入许可与分发机制。

## 平台与商标

MyWhoosh 名称属于相应权利人。本项目非官方，不隶属于或代表 MyWhoosh。没有提供游戏原文件、账号、密钥、系统字体、存档或资源解密能力。

## 自包含运行时

Release 使用官方 Microsoft .NET 自包含发布，运行时许可遵循 Microsoft .NET 对应发行物的许可。项目 SDK 来自官方渠道，不使用未知下载站。维护者发布前应保留运行时发行要求的许可证及第三方声明。

## 原创源码

在维护者明确选定许可证前，原创源码不授予开放源码许可证。仓库当前为私有，第三方的原有权利不受其可见性影响。
