---
title: Orleans 可观测性
description: 为 Orleans 配置 OpenTelemetry 日志、指标及跟踪。
ms.date: 08/02/2026
ms.topic: overview
---

<a id="orleans-observability"></a>

# Orleans 可观测性

Orleans 使用标准 .NET 可观测性 API：

- [Microsoft.Extensions.Logging](https://learn.microsoft.com/dotnet/core/extensions/logging/overview) 用于结构化日志。
- 名为 `Microsoft.Orleans` 的 <xref:System.Diagnostics.Metrics.Meter>，用于 [.NET 指标](https://learn.microsoft.com/dotnet/core/diagnostics/metrics)。
- 多个 <xref:System.Diagnostics.ActivitySource> 实例，用于 [.NET 分布式跟踪](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing)。

这些信号互为补充。指标检测变化，跟踪定位变化在请求路径中的位置，日志解释离散事件或故障。[Orleans Dashboard](../../dashboard/index.md) 适合交互式检查，但外部遥测后端才是告警、数据留存及跨服务关联的持久数据来源。

<a id="configure-opentelemetry-once"></a>

## 一次性配置 OpenTelemetry

[OpenTelemetry](https://opentelemetry.io/docs/) 为日志、指标及跟踪提供与供应商无关的管道。在运行 Orleans 的宿主中安装以下包：

- [OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting)
- [OpenTelemetry.Exporter.OpenTelemetryProtocol](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol)
- 用于外围应用的检测包，例如 `OpenTelemetry.Instrumentation.AspNetCore`、`OpenTelemetry.Instrumentation.Http` 和 `OpenTelemetry.Instrumentation.Runtime`。

以下配置适用于 Silo。在 Silo 上注册 <xref:Orleans.Hosting.CoreHostingExtensions.AddActivityPropagation*?displayProperty=nameWithType>，在 Orleans 客户端上注册 <xref:Orleans.Hosting.ClientBuilderExtensions.AddActivityPropagation*?displayProperty=nameWithType>。

:::code language="csharp" source="./snippets/observability/Program.cs" id="OpenTelemetry":::

将 `OTEL_EXPORTER_OTLP_ENDPOINT` 设置为 [OpenTelemetry 协议 (OTLP)](https://opentelemetry.io/docs/specs/otlp/) 终结点，例如 `http://localhost:4317`。OpenTelemetry .NET SDK 遵循标准的 `OTEL_*` 环境变量。在 .NET Aspire 应用中，AppHost 将 OTLP 终结点提供给引用的项目，因此此配置无需 Orleans 专用导出器即可将遥测数据发送到 Aspire 仪表板。

需要缓冲、路由、脱敏或后端专用身份验证时，在应用与最终后端之间使用 [OpenTelemetry Collector](https://opentelemetry.io/docs/collector/)。

<a id="meter-versus-activitysource"></a>

## Meter 与 ActivitySource 的区别

`AddMeter("Microsoft.Orleans")` 订阅 Orleans 的数值度量工具，不会启用跟踪。使用指标处理速率、计数、队列压力、延迟分布及告警。

`AddSource(...)` 订阅跟踪跨度，不会收集 Orleans 指标。Orleans 通过 <xref:Orleans.Diagnostics.ActivitySources> 公开以下源名称：

| 源 | 范围 |
|---|---|
| `Microsoft.Orleans.Application` | 应用 Grain 调用 |
| `Microsoft.Orleans.Runtime` | 运行时操作 |
| `Microsoft.Orleans.Lifecycle` | 激活、迁移及停用 |
| `Microsoft.Orleans.Storage` | Grain 存储操作 |
| `Microsoft.Orleans.DurableJobs` | 持久化作业调度及执行 |
| `Microsoft.Orleans.*` | 匹配所有 Orleans Activity 源的通配符 |

示例显式选择前四个源，使采集范围在代码中清晰可见。使用持久化作业时添加对应的源。通配符便于排查问题，但在 Orleans 升级后可能开始采集新源，因此应检查数据量及数据策略。

<a id="propagate-trace-context-through-grain-calls"></a>

## 在 Grain 调用中传播跟踪上下文

在参与跟踪的每个 Silo 和 Orleans 客户端上注册 `AddActivityPropagation()`。它安装 Grain 调用筛选器，通过 Orleans 消息携带当前 W3C 跟踪上下文及 baggage。如果不注册，Orleans 跨度仍可导出，但传入的 HTTP 跨度与由此产生的 Grain 调用跨度不会组成一条分布式跟踪。

将 baggage 视为需要传输的应用数据。不要在其中放入机密、凭据、个人数据或无界的用户输入。优先采用一组精简且获准的关联字段。

<a id="set-resource-identity"></a>

## 设置资源标识

每个进程都应设置稳定的资源属性：

- `service.name`：逻辑服务，例如 `orders-silo`。
- `service.version`：已部署的应用版本。
- `service.instance.id`：唯一的进程或 Pod 标识符。
- `deployment.environment.name`：环境。

不要将 Silo 地址用作 `service.name`；将会变化的实例标识放入 `service.instance.id`。仅在集群、区域或租户属性的取值集合有界且遥测数据策略允许时才配置这些属性。

<a id="control-cost-and-volume"></a>

## 控制成本及数据量

- 以适合告警目标的时间间隔持续导出指标。
- 使用基于父级的跟踪采样，使请求在 HTTP、Orleans 及下游调用中保持同一采样决定。
- 后端支持时，使用尾部采样 Collector 保留错误及慢请求。
- 在故障事件期间临时提高采样率，不要无限期执行全保真跟踪。
- 保持指标维度有界。Grain ID、请求 ID、用户 ID 及异常消息不适合作为指标属性。
- 在提供程序处筛除噪声日志类别，并避免记录完整 Grain 状态或消息有效载荷。

示例中的 10% 头部采样比例是起点，并非普适的生产环境数值。根据流量、留存成本及捕获罕见故障的概率选择该比例。

<a id="next-steps"></a>

## 下一步

- [监控 Orleans 指标](metrics.md)
- [解读 Orleans 信号](signals.md)
- [排查 Orleans 故障事件](troubleshooting.md)
- [保护及运维 Orleans Dashboard](../../dashboard/index.md)
