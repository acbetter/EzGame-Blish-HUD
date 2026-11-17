# EzGame Blish HUD 交接

整理日期：2026-11-17。依据当前源码及既有发布记录整理；本次只更新文档，没有重新部署、发布安装包或验收线上服务。线上验证结果均指对应发布日期。

## 当前交付与有效期

最近一次已记录发布为 **1.4.7（2026-10-21）**。运行源码提交 `11e4a4f6dd6bea645003967eaee33e251ee06a03`，标签 `v1.4.7`，源码与标签已推送；后续文档提交不改变该标签或安装包。

版本有效期至北京时间 **2026 年 11 月 30 日全天**，于 **12 月 1 日零时**到期，UTC 为 `2026-11-30T16:00:00Z`。如需继续提供可用版本，应在到期前明确下一版有效期再发布；本次没有延长日期。

- [安装与更新教程](https://ezgame.cc/guides/ezgame-blish-hud-pathing-guide)：用户下载入口，已记录的当前链接为 1.4.7。
- [1.4.7 完整包](https://ezgame.cc/packages/blish/artifacts/c4bb56ed6123f2b96222e6b8db53216c7d30caa0174e2c4ccc562a8cb03eefd9/Blish-HUD-EzGame-1.4.7.zip)：41,574,043 字节，98 个文件。
- 历史记录：[1.4.4](release-1.4.4.md)、[1.4.5](release-1.4.5.md)、[1.4.6](release-1.4.6.md)、[1.4.7](release-1.4.7.md)。1.4.5 修复启动网络等待并补齐缓存，1.4.6 修正关于页日期，1.4.7 删除 UDP 校时并支持电脑时间。

## 仓库与实现入口

本机外层 `D:\1Fork\EzGame-Blish-HUD` 不是 Git 仓库，客户端仓库位于其 `Blish-HUD` 子目录，当前分支为 `feat/paged-cjk-font-atlas`。关联网站仓库为 `D:\1Fork\ezgame-cc`，部署分支为 `master`。

| 职责 | 客户端源码 |
| --- | --- |
| 三项版本号 | `Blish HUD/Blish HUD.csproj`：AssemblyVersion、FileVersion、InformationalVersion |
| 启动时间来源 | `Blish HUD/Licensing/EzGameTimeClient.cs` |
| 实际到期时间与提示 | `Blish HUD/Licensing/ExpirationGate.cs` |
| 关于页日期 | `Blish HUD/GameServices/Overlay/UI/Views/AboutView.cs`、`Blish HUD/Strings/GameServices/OverlayService.zh-CN.resx` |
| Blish 服务地址 | `Blish HUD/GameServices/EzGameDistribution.cs` |
| 问题模块名单 | `Blish HUD/GameServices/Modules/SpoiledModuleList.cs`，由 `ModuleService.cs` 加载、刷新和取消 |
| 节日与构建缓存 | `Blish HUD/GameServices/Contexts/FestivalContext.cs`、`CdnInfoContext.cs` |

启动只请求 `https://ezgame.cc/api/blish/time`，最多等待 3 秒。成功使用服务器 UTC；超时、网络错误或无效响应时使用电脑当前 UTC，再判断到期。校时失败不弹错误、不阻止启动，不缓存服务器时间，不请求 UDP/NTP。离线到期判断依赖电脑时间是否准确。

到期时间只在 `ExpirationGate.ExpiresAtUtc` 定义；关于页通过 `ValidThroughDateText` 和中文资源中的 `{0}` 生成日期，到期弹窗也使用同一时间。不要在翻译资源中重新写固定日期。

Blish 官方后台请求已走 EzGame，服务器读取已发布持久快照，上游同步失败保留旧数据。客户端名单先读缓存、后台刷新，失败保留有效数据，退出取消请求；模块目录、节日和构建信息也保留副本。详细路径与失败行为见 [启动网络与缓存](startup-network-cache.md)。

镜像范围是 `pkgs.blishhud.com`、`versions.blishhud.com`、`l.blishhud.com` 等 Blish 后台服务。官方 GW2 API、官方游戏 CDN、`assets.gw2dat.com` 和模块自身外部请求不因此全部改走 EzGame。已有缓存可继续使用，无缓存的首次下载仍需要网络。

## 网站侧衔接

网站 `server/app.ts` 注册 GET `/api/blish/time`，返回 `{ "utcNow": "…Z" }`，设置 `Cache-Control: no-store`。客户端无需登录，接口处理函数直接生成时间，不查询数据库或第三方时间服务器。

网站说明位于 `ezgame-cc/docs/blish-time-api.md` 和 `ezgame-cc/docs/blish-mirror-cache.md`；部署遵循该仓库当前 AGENTS.md 与 `docs/deploy-debian-systemd.md`。

1.4.7 对应网站源码 `035653918fc6286e948ba8e549a0bc58ece0a527`、标签 `blish-1.4.7`，只部署 Web，历史 release 为 `20261020-165147-03565391`。后续网站可能已有其他发布，不应据此回切生产。

时间接口修改只部署 Web：从已提交并推送至 `origin/master` 的干净提交执行 `cmd /c deploy-web.bat --no-pause`。只有镜像同步等 Worker 实际运行逻辑变化才发布 Worker。先上线接口并确认可用，再发布依赖它的客户端。

## 后续发布

遵循 [发布约定](../AGENTS.md)，同步版本号、实际到期校验、关于页、到期提示、包内更新说明和线上教程。发布源码、必要测试及发布记录一起提交，为运行源码提交建立 `vX.Y.Z` 标签并推送，再从该干净提交构建。不得遗漏已发布源码、移动旧标签或覆盖旧内容寻址制品。

本机外层 `.codex-temp/package-main-1.4.7.py` 和 `publish-main-1.4.7.py` 是临时辅助脚本，不是仓库内正式发布工具。它们固定版本、日期、旧教程文字及本机目录，不能直接用于下一版。打包以外层 `release/Blish-HUD-EzGame` 为完整依赖与资源基底，再替换编译产物；下次需重新核对基底和产物，不能只复制 exe。

本机包位于外层 `release/Blish-HUD-EzGame-1.4.7.zip`，独立暂存目录为 `.codex-temp/blish-release-1.4.7/Blish-HUD-EzGame`。包内不含个人配置、模块或缓存。安装覆盖前退出旧程序；验收不应擅自结束用户正在运行的进程。

分发沿用 `/packages/blish/artifacts/<内容键>/<文件名>` 及既有 `.md5`、`.md5sum` 协议，不另建校验文件体系。上传后核对公网包与本地产物，替换教程下载链接和日期。当前采用教程手动下载，上传 zip 不代表已实现自动更新。凭据只从网站既有本地配置读取，不写入文档、源码或日志。

教程更新说明先交代用户问题及改动，保持简短。用户确认的基础文案为：

> 针对 Blish HUD 上游服务超时引起的启动缓慢、卡顿或未响应，改用 EzGame 镜像加速，并增加请求超时限制与本地缓存。上游不可用时，仍可使用已有缓存。

1.4.7 已追加：“启动校时改为请求 EzGame，最多等待 3 秒；请求失败时使用电脑本地时间，不再依赖 UDP 校时。”不要再将内部版本串联、关于页修正和到期弹窗细节塞进这段更新说明。

## 验证依据

下列命令供对应代码改动或发布时使用，本次文档交接不重跑构建及线上检查：

```powershell
# 工作目录：客户端仓库 Blish-HUD
dotnet run --project tests/EzGameTime.ContractTests -c Release
dotnet run --project tests/EzGameDistribution.ContractTests -c Release
dotnet build 'Blish HUD/Blish HUD.csproj' -c Release -p:Platform=x64 --no-restore -v:q -clp:ErrorsOnly
```

时间测试检测服务器时间优先、失败回退、无效/缺失时间与挂起请求是否超过 3 秒仍阻塞启动；失败时修正校时再发布。分发测试检测地址、缓存加载、刷新、保留与取消；失败时修复对应路径。构建发现编译或资源生成错误时停止打包；首次构建缺依赖则先完成还原。

1.4.7 发布时已通过时间测试、Release/x64/net472 构建（0 错误，3830 项已有警告）、包内版本和中文日期检查、移除 NTP 类型检查、公网包及附属文件验证、浏览器教程入口验收。网站接口在应用端口、源站代理和公网通过连续请求验证，返回递增 UTC 时间及禁止缓存头。

这些记录不代表所有 GW2 游戏场景或第三方 overlay 均已实机回归。下一次发布按实际改动选择检查，注明新结果及未覆盖范围；完成后检查 Git 状态，不遗漏本次文件，不提交或清理无关改动。
