# EzGame Blish HUD 1.4.7

发布日期：2026-10-21。源码标签：`v1.4.7`。

- 启动校时改为仅请求 `https://ezgame.cc/api/blish/time`，删除 UDP/NTP 校时代码。
- 最多等待 3 秒；超时、网络错误或无效响应时使用电脑本地时间，不再因校时失败退出。
- 不缓存服务器时间。版本有效期保持北京时间 2026 年 11 月 30 日全天，12 月 1 日零时到期；关于页与到期提示共用实际到期时间。
- 包含前版的 EzGame 镜像和本地资源缓存修复。

上线顺序：先部署 EzGame Web 时间接口，再从已提交并推送的 `v1.4.7` 标签构建安装包，替换线上教程当前下载入口。Worker 不涉及此次改动，无需部署。

验证：`dotnet run --project tests/EzGameTime.ContractTests -c Release` 通过，覆盖服务器时间优先、请求超时、HTTP 错误、无效/缺失时间与实际挂起请求 3 秒后使用电脑时间。Release/x64/net472 构建通过（0 错误，3830 项已有警告）。

## 发布结果

- 客户端源码提交 `11e4a4f6dd6bea645003967eaee33e251ee06a03`，标签 `v1.4.7` 已推送；从此提交构建并打包。
- EzGame 时间接口源码提交 `035653918fc6286e948ba8e549a0bc58ece0a527`，标签 `blish-1.4.7` 已推送。通过 `deploy-web.bat --no-pause` 发布 Web release `20261020-165147-03565391`，未部署 Worker。
- 应用端口、源站代理与公网时间接口均返回新鲜 UTC 时间及 `Cache-Control: no-store`；服务 active，应用和源站首页 HTTP 200，发布后错误日志无新增条目。
- [线上安装包](https://ezgame.cc/packages/blish/artifacts/c4bb56ed6123f2b96222e6b8db53216c7d30caa0174e2c4ccc562a8cb03eefd9/Blish-HUD-EzGame-1.4.7.zip)：41,574,043 字节，98 个文件。公网下载与本地安装包逐字节一致，两个 MD5 附属文件正确。
- 包内版本为 1.4.7，中文关于页资源显示“当前版本有效期至 2026 年 11 月 30 日”，程序集不包含旧 `NtpClient`。
- [安装教程](https://ezgame.cc/guides/ezgame-blish-hud-pathing-guide) 已替换当前下载入口，并补充校时失败使用电脑本地时间的说明。
