---
title: Orleans Dashboard
description: 安全部署和运行 Orleans Dashboard，以实时检查集群。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-dashboard"></a>

# Orleans Dashboard

Orleans Dashboard 提供对 Silo、Grain 激活实例、调用、提醒器、运行时计数器、日志及 Grain 状态的实时检查。它是管理入口，而非公共应用终结点，也不能替代留存的 OpenTelemetry 数据。

<a id="secure-the-dashboard-before-exposing-it"></a>

## 对外公开仪表板之前确保其安全

> [!WARNING]
> <xref:Orleans.Dashboard.ServiceCollectionExtensions.MapOrleansDashboard*?displayProperty=nameWithType> 默认不要求身份验证。仪表板可能公开拓扑、Grain 类型及键信息、运行时属性、方法活动、日志消息、提醒器元数据及序列化的 Grain 状态。未实施身份验证、授权及传输安全时，绝不能将其公开到公共互联网或不可信的应用网络。

采用纵深防御：

1. **验证运维人员身份。** 将 ASP.NET Core 宿主与组织的身份提供程序集成。不要依赖难以猜测的路由。
2. **对整个路由组实施授权。** 对 `MapOrleansDashboard()` 返回的 <xref:Microsoft.AspNetCore.Routing.RouteGroupBuilder> 应用策略。
3. **限制网络路径。** 绑定管理监听器，或将路由置于专用入口、VPN、防火墙或零信任访问代理之后。路由前缀并非安全边界。
4. **使用 HTTPS。** 保护传输中的凭据、Cookie、仪表板响应及流式日志。
5. **限制敏感数据。** Grain 状态和日志可能包含机密或个人数据。应用常规数据分类、留存及访问审计要求。
6. **禁用不需要的功能。** 不需要实时日志流时，设置 `HideTrace = true`。不要向无须检查 Grain 状态的运维人员开放该功能。

以下示例假设应用具有由其身份提供程序支持的 Cookie 登录流程。它使用一个策略保护仪表板的每项资源和 API 终结点：

:::code language="csharp" source="./snippets/secure-dashboard/Program.cs" id="SecureDashboard":::

使用未通过身份验证的请求，以及属于和不属于运维人员角色的用户来验证授权行为。还要在已部署的拓扑中检查代理转发及路由前缀行为。

<a id="install-and-map-the-dashboard"></a>

## 安装并映射仪表板

将 [Microsoft.Orleans.Dashboard](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard) 添加到 Web 宿主。[Microsoft.Orleans.Dashboard.Abstractions](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard.Abstractions) 包含 <xref:Orleans.Dashboard.NoProfilingAttribute> 等共享类型，由主包引入。

在 Silo 构建器上调用 <xref:Orleans.Dashboard.ServiceCollectionExtensions.AddDashboard*?displayProperty=nameWithType>，并在构建 ASP.NET Core 应用后映射路由。建议使用路由前缀，避免占用 Web 应用的根路径：

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="map_dashboard":::

路由组包含仪表板静态资源及其后端 API。将身份验证、授权、速率限制、标头及其他终结点约定应用到返回的组，而不是只保护 HTML 页面。当仪表板访问限于运维人员角色时，使用前面的安全仪表板示例中基于策略的配置。

<a id="configure-collection"></a>

## 配置数据采集

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="configure_dashboard":::

| 选项 | 默认值 | 运维影响 |
|---|---:|---|
| <xref:Orleans.Dashboard.DashboardOptions.HideTrace> | `false` | 为 `true` 时禁用实时日志流终结点。 |
| <xref:Orleans.Dashboard.DashboardOptions.CounterUpdateIntervalMs> | `1000` | 设置计数器采样间隔，单位为毫秒；最小值为 1000。 |
| <xref:Orleans.Dashboard.DashboardOptions.HistoryLength> | `100` | 控制保留在内存中的仪表板历史记录。较大的值消耗更多内存。 |

仪表板注册日志提供程序，并采集运行时指标用于显示。方法性能分析会添加入站 Grain 调用筛选器。默认情况下，一分钟没有仪表板查询后，性能分析会转为非活动状态。也可启用持续性能分析，但它具有持续开销：

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="configure_profiler":::

保持 <xref:Orleans.Dashboard.GrainProfilerOptions.TraceAlways> 禁用，除非持续方法统计值得付出开销。使用计划部署的仪表板配置对代表性流量进行负载测试。仅在允许某 Grain 类或方法不计入仪表板方法统计时，才对其应用 <xref:Orleans.Dashboard.NoProfilingAttribute>。

<a id="choose-a-deployment-boundary"></a>

## 选择部署边界

<a id="co-host-with-a-silo"></a>

### 与 Silo 共同托管

共同托管是最简单的配置方式，使仪表板可以访问本地 Silo 服务。将其 HTTP 路由置于与公共应用路由不同的管理网络边界中。将仪表板添加到 Silo 本身不会打开 HTTP 监听器；`MapOrleansDashboard()` 在 ASP.NET Core 宿主上映射终结点。

<a id="use-a-separate-orleans-client-host"></a>

### 使用独立的 Orleans 客户端宿主

Orleans 客户端可以托管 Web UI：

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="configure_dashboard_client":::

每个 Silo 仍必须调用 `AddDashboard()`，才能提供集群数据及性能分析。保护客户端宿主的路由及其到网关的网络访问。独立宿主减少 Silo 直接暴露 HTTP 的情况，但不会使仪表板数据变得不敏感。

<a id="operate-it-in-production"></a>

## 在生产环境中运行

- 使用仪表板进行短时交互式诊断，使用 OpenTelemetry 持久保存指标、跟踪、日志，并处理告警及数据留存。
- 启用性能分析后，监控仪表板请求量、进程 CPU 与内存以及 Grain 调用延迟。
- 保持仪表板与 Orleans 包版本一致。
- 使用外围身份验证或代理平台审计运维人员访问。
- 未经脱敏，不要将 Grain 状态或实时日志粘贴到工单或聊天中。
- 在没有获准运维访问路径的环境中，禁用或移除仪表板映射。

<a id="troubleshooting"></a>

## 故障排查

<a id="the-dashboard-reports-lost-connectivity"></a>

### 仪表板报告连接丢失

确认仪表板宿主具有活动的 Orleans 客户端连接，能够解析并访问通告网关，且使用与 Silo 相同的集群及服务标识符和集群成员资格提供程序。对于独立宿主，还要验证每个 Silo 都配置了 `AddDashboard()`。遵循[客户端连接运行手册](../host/monitoring/troubleshooting.md#client-cant-connect)。

<a id="profiling-data-is-empty"></a>

### 性能分析数据为空

发起对 Grain 方法的调用，保持页面活动，并确认类及方法都没有 <xref:Orleans.Dashboard.NoProfilingAttribute>。如果 `TraceAlways` 为 `false`，没有仪表板查询的持续时间达到 <xref:Orleans.Dashboard.GrainProfilerOptions.DeactivationTime> 后，性能分析停止。

<a id="live-logs-return-403"></a>

### 实时日志返回 403

`HideTrace = true` 会有意禁用跟踪终结点。如果该选项为 `false`，应先检查仪表板授权策略及已通过身份验证用户的声明，然后再更改该选项。

<a id="see-also"></a>

## 另请参阅

- [Orleans 安全](../security/index.md)
- [网络加固](../security/networking.md)
- [Orleans 可观测性](../host/monitoring/index.md)
- [解读 Orleans 可观测性信号](../host/monitoring/signals.md)
