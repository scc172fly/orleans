---
title: "Orleans 机表"
description: "安全部署和操作 Orleans 机板,进行现场集束视察。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-dashboard"></a>

# Orleans 机表

Orleans 机首板对 silos、 grain 激活、电话、提醒、运行计数器、日志和 grain 状态进行实时检查。这是一个行政表面,不是公开应用端点或替代留存的 OpenTelemetry 数据。

<a id="secure-the-dashboard-before-exposing-it"></a>

## 安全仪表板,在曝光前

> [!WARNING]
> <xref:Orleans.Dashboard.ServiceCollectionExtensions.MapOrleansDashboard*?displayProperty=nameWithType> 并不需要默认认证。 仪表板可以披露地形学、 grain 类型和关键信息、运行时间属性、方法活动、日志消息、提醒元数据和序列化 grain 状态。 没有认证、授权和运输安全, 永远不要将它暴露在公共互联网或无信任的应用网络上。

深度使用防御:

1. **认证操作员。** 将 ASP.NET 核心主机与贵组织的身份提供者整合。 不要依赖难以猜测的路径 。
2. **授权整个路线组 。** 将政策应用到 <xref:Microsoft.AspNetCore.Routing.RouteGroupBuilder> 返回的 `MapOrleansDashboard()`.
3. **限制网络路径。** 将行政听众绑在了安全边界, 或者把线路放在私人入侵、 VPN、防火墙或零信任访问代理的后面。
4. **使用HTPS。** 保护证书、饼干、仪表板回复和过境流传日志。
5. **限制敏感数据。** Grain 状态和日志可以包含机密或个人数据。应用正常的数据分类、保留和访问-审计要求。
6. **禁用特性不需要。** 设置 `HideTrace = true` 时, 不需要实时日志流。 避免曝光 grain- 向不需要的操作员进行状态检查 。

以下例子假设应用程序有一个由身份提供者支持的饼干签名流。 它保护每个仪表板资产和 API 端点,并使用一个政策:

:::code language="csharp" source="./snippets/secure-dashboard/Program.cs" id="SecureDashboard":::

验证授权行为, 使用未经认证的请求, 并校验操作员角色内外的用户。 同时检查代理转发和路线前置行为 。

<a id="install-and-map-the-dashboard"></a>

## 安装和映射仪表板

添加 [Microsoft.Orleans.Dashboard](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard) 到网络主机。 [Microsoft.Orleans.Dashboard.Abstractions](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard.Abstractions) 包含共享类型, 如 <xref:Orleans.Dashboard.NoProfilingAttribute> , 由主包引入 。

调调 <xref:Orleans.Dashboard.ServiceCollectionExtensions.AddDashboard*?displayProperty=nameWithType> 排在《公约》 silo 建筑工地和绘制 ASP.NET 核心应用程序。 建议使用路线前缀以避免要求网络应用程序的根:

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="map_dashboard":::

路线组包括静态仪表板资产及其支持的API。 对返回的组群应用认证、授权、费率限制、信头和其他终点协议,而不仅仅是保护 HTML 页面。 当仪表板访问仅限于操作员作用时,使用前一个安全仪表板示例中的政策配置。

<a id="configure-collection"></a>

## 配置收藏

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="configure_dashboard":::

| 选项 | 默认默认 | 实际效果 |
|---|---:|---|
| <xref:Orleans.Dashboard.DashboardOptions.HideTrace> | `false` | 当 `true`时禁用实时对流端点 。 |
| <xref:Orleans.Dashboard.DashboardOptions.CounterUpdateIntervalMs> | `1000` | 设定反取样间隔,以毫秒计;最低为1000。 |
| <xref:Orleans.Dashboard.DashboardOptions.HistoryLength> | `100` | 保存了记忆仪表板历史的控件,较大的值消耗更多的内存。 |

仪表板登记了伐木供应商,并收集了显示的运行时间度量。方法分析增加了一个输入 grain-呼叫过滤器。默认情况下,在一分钟后不询问时,特征分析就会停止。

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="configure_profiler":::

休假假 <xref:Orleans.Dashboard.GrainProfilerOptions.TraceAlways> 除非连续方法统计证明成本合理, 否则禁用。 使用您计划部署的仪表板配置装入测试代表流量。 使用此选项将显示您要使用的仪表板配置加载到您的测试代表流量中 <xref:Orleans.Dashboard.NoProfilingAttribute> 上a grain 只有在从仪表板方法统计中略去该分类或方法时,才被接受。

<a id="choose-a-deployment-boundary"></a>

## 选择部署边界

<a id="co-host-with-a-silo"></a>

### 与 silo联合主办

联合主办是最简单的设置,使仪表板能够进入当地 silo 将其HTTP路线置于行政网络边界的行政网边界上,有别于公共应用途径。 silo 本身不打开 HTTP 听众; `MapOrleansDashboard()` 地图地图的终点 ASP.NET 核心东道主。

<a id="use-a-separate-orleans-client-host"></a>

### 使用单独的 Orleans 客户端主机

Orleans 客户端可托管网络界面:

:::code language="csharp" source="../snippets/compiled/Dashboard/DashboardSnippets.cs" id="configure_dashboard_client":::

每个 silo 都必须仍然呼叫 `AddDashboard()` , 这样组合数据和剖析就可以提供。 保护客户主机的路线及其网络接入网关。 一个单独的主机可以减少 silos直接接触 HTTP, 但不会让仪表板数据变得不敏感 。

<a id="operate-it-in-production"></a>

## 生产中操作

- 使用仪表板进行短寿命互动诊断,并使用 OpenTelemetry 来进行耐久度量、跟踪、日志、警报和保留。
- 监测仪表板请求量、处理CPU/模拟和 grain-在进行可进行剖析后呼叫延迟。
- 保持仪表板和 Orleans 软件包版本一致。
- 使用周围认证/代理平台的审计操作员访问。
- 不要在不编辑的情况下将 grain 状态或活记录贴入机票或聊天。
- 在没有核准实际使用访问路径的环境中,禁用或删除仪表板制图。

<a id="troubleshooting"></a>

## 故障排查

<a id="the-dashboard-reports-lost-connectivity"></a>

### 仪表板报告失去连通性

确认仪表板主机有活动 Orleans 用户连接,可以解决/接入广告网关,使用与 silos。对于单独的主机,要核查 `AddDashboard()` 也配置在 silo. Follow the [连接客户端运行手册](../host/monitoring/troubleshooting.md#client-cant-connect).

<a id="profiling-data-is-empty"></a>

### 数据分析为空

生成 grain 方法的调用,保持页面活动,并确认该类或方法都没有 <xref:Orleans.Dashboard.NoProfilingAttribute>。如果 `TraceAlways` 是 `false`,则在 <xref:Orleans.Dashboard.GrainProfilerOptions.DeactivationTime> 之后不经查询而停止特征分析。

<a id="live-logs-return-403"></a>

### 活原数返回403

`HideTrace = true` 故意禁用跟踪端点。 如果它是 `false`, 请先检查仪表板授权政策和经认证的用户的主张,然后再修改选项 。

<a id="see-also"></a>

## 另请参阅

- [Orleans 安全](../security/index.md)
- [网络加固](../security/networking.md)
- [Orleans 易观测](../host/monitoring/index.md)
- [解释 Orleans 可观测信号](../host/monitoring/signals.md)
