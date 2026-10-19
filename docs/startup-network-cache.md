# Blish 官方服务镜像与启动缓存

2026-10-19 修复。起因是 1.4.4 启动时同步等待 `pkgs.blishhud.com/spoiled.json`，网络异常后约 100 秒才继续。

## 请求与缓存

程序对 Blish 官方后台服务的请求统一改到 `https://ezgame.cc/packages/blish/`，不再直接请求 `pkgs.blishhud.com`、`versions.blishhud.com` 或 `l.blishhud.com`。

| 内容 | EzGame 路径 | 客户端缓存与失败行为 |
| --- | --- | --- |
| 稳定/预览模块目录 | `packages.gz`、`preview-packages.gz` | 成功解析后保存压缩目录，请求失败使用本机副本 |
| 问题模块名单 | `spoiled.json` | 启动先读 `cache/blish-spoiled.json`，后台刷新，失败保留有效名单，退出取消请求 |
| 节日信息 | `general/getactivefestivals` | EzGame 请求失败后使用 `cache/blish-festivals.json` |
| 构建信息 | `general/getbuildinfo` | 先使用 `cache/blish-build-info.json`，后台更新；没有副本时保留原有官方游戏 CDN 查询 |
| 主程序更新 | `versions.json` | 继续走 EzGame 自有版本入口 |

以上客户端镜像请求限时 5 秒。官方 GW2 API、官方游戏 CDN、`assets.gw2dat.com` 图片服务，以及模块自身的外部功能不在本次 `*.blishhud.com` 镜像范围内。浏览器打开的帮助链接也不属于后台 HTTP 请求。

EzGame 的目录和问题名单、节日、构建信息保存为数据库快照，公开请求只读取已发布快照。上游同步失败不清空历史数据。操作类信息同步成功后自动发布，不等待模块翻译或包目录人工发布。

上游不可用时继续使用已保存数据；缺失缓存的数据不能凭空生成。已安装模块不会因目录网络失败被删除。NTP 启动校验沿用现有策略，本次未改成离线时间校验。

## 验证与交付状态

- Release/x64/net472 构建通过。
- 分发行为测试通过：镜像 URL、慢请求时读取旧名单、刷新后重启读取、超时/无效 JSON 保留有效数据、取消刷新、缺失/损坏缓存。
- 独立配置的完整程序在 HTTP 不可用、存在缓存时仍进入更新循环，并从缓存显示万圣节；没有退出或替换用户正在运行的进程。
- 网站端相关 18 项测试、目标文件 ESLint、全项目类型检查通过；三个本地公开接口与持久化快照一致。
- 本轮未发布新版本，未修改线上安装包，未部署网站或 Worker。网站新增两个目录键及路由应先于新版客户端上线，详见 `ezgame-cc/docs/blish-mirror-cache.md`。
