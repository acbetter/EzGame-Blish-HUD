# EzGame Blish HUD 1.4.4

基于本地 1.4.3 完整版本，包含原有窗口与输入改动，新增以下修正：

- 修正 Label 自动换行时的测量宽度，并统一测量和绘制的换行选项，避免文字截断或重复换行。
- 到期时间调整为北京时间 2026-11-01 00:00。
- 时间校验失败或到期弹窗点击“确定”后，默认浏览器打开 https://ezgame.cc/guides/ezgame-blish-hud-pathing-guide。
- 程序程序集版本、文件版本同步为 1.4.4.0，产品版本为 1.4.4。

本地交付包：`release/Blish-HUD-EzGame-1.4.4.zip`。

源码版本标签：`v1.4.4`。2026-10-10 补齐发布源码提交与标签，包含窗口与输入实现、文字换行修正、到期提示、行为测试及相关文档。此前发布直接使用工作区构建，未提交这些改动。

补录时确认线上 ZIP 与本地发布包逐字节一致，ZIP 内主程序与保留的 1.4.4 Release 构建产物逐字节一致；未重新发布程序。

2026-09-30 已上传线上，并更新安装教程的下载入口、版本号、更新日期与到期说明。

- [安装与更新教程](https://ezgame.cc/guides/ezgame-blish-hud-pathing-guide)
- [1.4.4 下载](https://ezgame.cc/packages/blish/artifacts/414c5b4ff153663b8412cefb6d0ead1ae9d828fa1086979aed3ee4e564e05fba/Blish-HUD-EzGame-1.4.4.zip)

已验证公开下载文件与本地发布包一致，MD5 sidecar 一致，浏览器页面显示 1.4.4 下载链接与新到期时间。
