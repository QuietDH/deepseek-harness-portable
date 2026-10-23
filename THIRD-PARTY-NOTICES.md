# 第三方组件声明

本项目的成品包（Release 附件）重新分发了以下第三方组件。它们各自的许可证全文
随包放在 `licenses\` 目录中，本仓库的 `licenses\` 目录也保留了副本。

## DeepSeek Harness（`@deepseek-ai/dsh`）

- 版权：Copyright (c) 2026 DeepSeek
- 许可证：MIT
- 上游：https://github.com/deepseek-ai/deepseek-harness
- 许可证全文：`licenses/dsh-LICENSE.txt`

成品包中的 `payload\app.zip` 内含 DSH 及其 npm 依赖；每个依赖自身的 LICENSE
文件保留在 zip 内对应的包目录中。

## Node.js

- 版权：Copyright Node.js contributors
- 许可证：MIT，并包含 OpenSSL、V8、libuv 等第三方组件的许可证
- 上游：https://nodejs.org/
- 许可证全文：`licenses/node-LICENSE.txt`

## npm

- 版权：Copyright (c) npm, Inc.
- 许可证：Artistic License 2.0
- 上游：https://github.com/npm/cli
- 许可证全文：`licenses/npm-LICENSE.txt`

## 其他依赖

DSH 的 npm 依赖各自带有 LICENSE 文件，随 `payload\app.zip` 一起分发。
本项目的启动器使用 .NET Framework 的 WinForms 与 `System.IO.Compression`，
运行于目标电脑自带的 .NET Framework 上，不再单独分发运行时。
