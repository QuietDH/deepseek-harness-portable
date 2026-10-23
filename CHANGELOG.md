# Changelog

## v1.0.0 — 2026-10-24

首个公开版本。

- DSH `0.2.1-rc.2` + Node.js `24.22.0`（Windows x64）
- C# WinForms 启动器（.NET Framework 4）：
  - 首次把 `payload\*.zip` 解压到本机缓存，之后快速启动
  - 自动选择可写缓存目录：`%LOCALAPPDATA%\DSHP` → `%TEMP%\DSHP` → 程序目录 `.cache`
  - 自动挑选空闲端口，读取 DSH 打印的带令牌 URL 并打开浏览器
  - 一键停止 DSH（含子进程）
  - 日志写入 `logs\`
- 便携数据：`DSH_HOME` 与 `DSH_AGENTS_HOME` 指向 U 盘 `home`
- 备用命令行启动脚本（使用系统自带 `tar` 解压）
- MIT 许可证与第三方许可证声明
