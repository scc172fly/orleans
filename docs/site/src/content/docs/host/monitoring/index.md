---
title: "Orleans 易观测"
description: "配置 OpenTelemetry 记录、测量和跟踪 Orleans。"
ms.date: 08/02/2026
ms.topic: overview
---

<a id="orleans-observability"></a>

# Orleans 易观测

Orleans 使用标准 .NET 可观测性API:

- 结构化日志的[Microsoft.Extensions.Logging](https://learn.microsoft.com/dotnet/core/extensions/logging/overview) 。
- A <xref:System.Diagnostics.Metrics.Meter> 命名 `Microsoft.Orleans` 用于 [.NET 指标](https://learn.microsoft.com/dotnet/core/diagnostics/metrics).
- 几个 <xref:System.Diagnostics.ActivitySource> 用于 [.NET 分布式追踪跟踪](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing).

这些信号是互补的。 计量仪检测变化,追踪其在请求路径中的位置,并解释离散事件或故障。 [Orleans 机表](../../dashboard/index.md) 对互动检查有用,但外部遥测后端是提醒、保留和交叉服务关联的持久来源。

<a id="configure-opentelemetry-once"></a>

## 配置 OpenTelemetry 一次

[OpenTelemetry](https://opentelemetry.io/docs/) 提供了一种供货商中立的日志、度量和痕量管道。在运行 Orleans的主机安装这些包:

- [OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting)
- [OpenTelemetry.Exporter.OpenTelemetryProtocol](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol)
- 周围应用的整套仪器,例如 `OpenTelemetry.Instrumentation.AspNetCore`、 `OpenTelemetry.Instrumentation.Http`和 `OpenTelemetry.Instrumentation.Runtime`

以下配置用于 silo登记册 。 <xref:Orleans.Hosting.CoreHostingExtensions.AddActivityPropagation*?displayProperty=nameWithType> 日期 时 silos 和 <xref:Orleans.Hosting.ClientBuilderExtensions.AddActivityPropagation*?displayProperty=nameWithType> 日期 时 Orleans 客户。

:::code language="csharp" source="./snippets/observability/Program.cs" id="OpenTelemetry":::

设置 `OTEL_EXPORTER_OTLP_ENDPOINT` 到 [OpenTelemetry 议定书(欧地协定)](https://opentelemetry.io/docs/specs/otlp/) 终点,例如 `http://localhost:4317`。 OpenTelemetry .NET SDK 符合标准 `OTEL_*` 环境变量。在 .NET 寄信程序中, AppHost 提供OTLP 终端点作为参考项目,因此这种配置将遥测信号发送到Aspire仪表板上,而不指定一个 Orleans- 特定出口商。

在应用程序和最后后端之间使用 [OpenTelemetry 收集器](https://opentelemetry.io/docs/collector/) , 需要缓冲、 路由、 编辑或后端特定认证时使用此选项。

<a id="meter-versus-activitysource"></a>

## 测算器对 ActivitySource

`AddMeter("Microsoft.Orleans")` 订阅数组 Orleans 仪器。 它无法进行跟踪。 使用速率、 计数、 队列压力、 延时分配和警告的度量。

`AddSource(...)` 订阅宽度。 它不收集 Orleans 量度。 Orleans 通过 <xref:Orleans.Diagnostics.ActivitySources>暴露这些源名:

| 来源来源 | 范围范围范围 |
|---|---|
| `Microsoft.Orleans.Application` | 申请 grain 调用 |
| `Microsoft.Orleans.Runtime` | 运行时间业务 |
| `Microsoft.Orleans.Lifecycle` | 激活、迁移和停止使用 |
| `Microsoft.Orleans.Storage` | Grain 储存作业 |
| `Microsoft.Orleans.DurableJobs` | 长期工作时间安排和执行 |
| `Microsoft.Orleans.*` | 所有 Orleans 活动源的通配符 |

示例选择前四个来源, 以便收集的范围在代码中可见。 当使用时添加耐久性任务。 在调查期间通配符方便, 但可以在 Orleans 升级后开始收集新来源, 因此可以审查其数量和数据政策 。

<a id="propagate-trace-context-through-grain-calls"></a>

## 通过 grain 电话传送跟踪背景

登记册登记登记册 `AddActivityPropagation()` 上每 silo 和 Orleans 客户端参与跟踪。它安装 grain- 带现有W3C跟踪环境及行李通过 Orleans 没有它, Orleans 仍然可以导出宽度, 但可导出 HTTP 的间隔和结果 grain- 呼叫的间隔不会形成一个分布式跟踪

行李作为传送申请数据处理; 不要在行李中隐藏秘密、证书、个人资料或未受限制用户输入。

<a id="set-resource-identity"></a>

## 设定资源身份

每个进程都应设定稳定的资源属性:

- `service.name`:逻辑服务,例如 `orders-silo`。
- `service.version`:部署的应用版本。
- `service.instance.id`:一个独特的进程或舱位识别符。
- `deployment.environment.name`:环境。

不要使用 silo 地址作为 `service.name`;在 `service.instance.id`中设置更改实例身份。只有在其值组、区域或租户属性被遥测数据政策约束并允许时才配置该属性。

<a id="control-cost-and-volume"></a>

## 控制费用和数量

- 持续出口测量量,间隔时间与您的警告目标相适应。
- 使用基于母体的跟踪取样,因此请求保留了对HTTP、 Orleans和下游呼叫进行抽样决定的决定。
- 当后端支持它时, 保留错误和慢速请求, 使用尾巴取样收藏器 。
- 在事件期间临时增加取样,而不是无限期地追查全忠贞行为。
- 保持尺寸的界限。 Grain ID、要求身份证明、用户身份证明和例外信息不适宜。
- 供应商的过滤器噪音日志分类,避免记录完整的 grain 状态或信息有效载荷。

举例来说,10%的头型抽样比率是一个起点,而不是一个通用的生产价值。从交通量、保留成本和罕见的失败概率中选择它。

<a id="next-steps"></a>

## 后续步骤

- [监测 Orleans 指标](metrics.md)
- [解释 Orleans 信号](signals.md)
- [排除故障 Orleans 事件](troubleshooting.md)
- [安全操作 Orleans 机](../../dashboard/index.md)
