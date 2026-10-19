# EzGame Blish HUD 1.4.5

发布日期：2026-10-19。源码标签：`v1.4.5`。

- 修复启动同步等待上游问题模块名单导致约 100 秒未响应的问题。
- Blish 官方包库、名单、节日与构建信息统一使用 EzGame 镜像；服务器保留持久快照，客户端保留有效缓存，上游故障不清空已有数据。
- 问题名单在后台刷新，退出取消请求；客户端镜像请求超时为 5 秒。
- 有效期至北京时间 **2026-11-30 全天**，在 **2026-12-01 00:00** 到期。时间校验失败或到期弹窗仍引导到安装与更新教程。

EzGame Web/Worker 对应源码提交：`3ad97356`。先部署镜像路由和同步逻辑，再发布客户端。详细缓存行为见 [启动缓存说明](startup-network-cache.md)。

交付包：工作区根目录 `release/Blish-HUD-EzGame-1.4.5.zip`。压缩包保留完整依赖和资源，不包含个人配置、模块或缓存。

此前已完成分发缓存行为测试、HTTP 不可达时的独立配置启动验证，以及网站端 18 项测试、类型检查、接口与持久快照比对。本次发布另核对 Release/x64 构建、程序集版本、到期边界、线上包内容和 MD5 sidecar。真实 GW2 与第三方注入 overlay 不在本次新增现场验证范围。

## 发布结果

- 源码提交：`736873bcc6246f679fd43f84c42b6a961de4ad00`；标签 `v1.4.5` 已推送。
- EzGame Web/Worker 已部署 `3ad97356`，两端 release 为 `20261019-131054-3ad97356`；网站对应标签 `blish-1.4.5` 已推送。
- [1.4.5 完整安装包](https://ezgame.cc/packages/blish/artifacts/3a703994cf334e3a8fec85c0d2ae0a05bf7890bcd46b293d23f7e6a9b4670785/Blish-HUD-EzGame-1.4.5.zip)，41,574,568 字节。公开下载与本地包逐字节一致，MD5 sidecar 一致。
- [安装与更新教程](https://ezgame.cc/guides/ezgame-blish-hud-pathing-guide) 已更新版本、下载链接、更新日期及有效期。页面用“12 月 1 日零时”表述，避免现有渲染器截断带冒号的时间。
- 验证两个 systemd 服务均 active、两个 current 路径正确、近期错误日志为空，三个 Blish 镜像接口公开返回 200。原生软件包验收覆盖实际制品 HEAD/Range、两个 MD5 sidecar、Blish gzip、旧域名兼容入口及静态页面资源。
