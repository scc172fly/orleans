---
title: 解读 Orleans 可观测性信号
description: 使用 Orleans 日志、指标、跟踪、关联及告警，而不依赖过时的目录。
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="interpret-orleans-observability-signals"></a>

# 解读 Orleans 可观测性信号

从服务级症状入手，再使用 Orleans 遥测数据缩小原因范围。针对每个运行时事件或度量工具设置告警会产生噪声，并使运维工作与实现细节耦合。

<a id="logs"></a>

## 日志

Orleans 通过 [`Microsoft.Extensions.Logging`](https://learn.microsoft.com/dotnet/core/extensions/logging/overview) 写入日志。保留日志提供程序提供的结构化字段，包括类别、级别、事件 ID、异常、跟踪 ID 和跨度 ID。使用常规 .NET 日志配置设置各类别的级别。

Orleans <xref:Microsoft.Extensions.Logging.EventId> 是诊断标识符，而非完整的事件故障定义。随着运行时演进，数字范围及事件分配方式可能变化。不要维护复制出来的静态表格，而应：

- 排查已知 ID 时查询生成的 <xref:Orleans.ErrorCode> API 参考。
- 若要查看正在开发的定义，参阅 [`main` 分支中的运行时错误码源码](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core.Abstractions/Logging/ErrorCodes.cs)。
- 针对持续症状、异常类型或伴有服务影响的已知事件告警，而非针对每条警告告警。

在结构化属性或获准的 Activity 标记中记录应用关联字段。有结构化字段可用时，不要解析已渲染的日志文本。

<a id="metrics"></a>

## 指标

订阅 `Microsoft.Orleans` Meter。发现所部署版本实际发出的度量工具集合。有关安装及命令详情，参阅 [`dotnet-counters` 诊断工具](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters)：

```dotnetcli
dotnet-counters monitor -n <ProcessName> --counters Microsoft.Orleans
```

有关当前客户端与 Silo 信号、度量工具类型、单位、维度及健康状况解读，参阅[监控 Orleans 指标](metrics.md)。计数器值通常需要计算时间窗口内的速率或增量；仪表值需要有意识地选择时点聚合方式；直方图需要同时查看百分位数与请求量。将各个 Silo 与集群整体进行比较：单个异常值提示宿主或分区问题，而整个集群的变化提示共享依赖项或流量发生变化。

<a id="cardinality"></a>

### 基数

指标后端为每组唯一的属性集合创建时间序列。将属性限制为取值数量较少的维度，例如操作、方向、状态、Grain 类型、提供程序名称及 Silo 标识。绝不能将 Grain 键、跟踪 ID、请求 ID、堆栈跟踪、URL 查询参数或任意异常文本作为指标属性。

添加维度之前估算时间序列数量：

`instruments x attribute-value combinations x instances`

即使每个单独的标签看起来都合理，大量组合仍会增加内存、网络及后端成本。

<a id="traces"></a>

## 跟踪

使用应用跟踪跨度追踪 Grain 调用，并利用运行时、生命周期及存储跨度解释时间消耗的位置。有用的跟踪应回答：

1. 哪个入口点发起了调用？
2. 调用了哪项 Grain 操作？
3. 时间消耗在等待、执行应用代码、激活、放置还是访问存储？
4. 哪个异常或状态终结了操作？

如果跨度显示为独立跟踪，确认客户端已注册 <xref:Orleans.Hosting.ClientBuilderExtensions.AddActivityPropagation*?displayProperty=nameWithType>，且路径上的所有 Silo 都进行了相应注册。同时验证采样器遵循父级采样决定，代理保留 W3C `traceparent`。

默认避免记录 Grain 键及状态值。它们可能具有高基数或包含敏感信息。如果故障排查确实需要这些数据，使用受限且有时限的采集方式，并在事后移除。

<a id="baseline-alerts"></a>

## 基线告警

根据正常流量及服务目标调整告警。实用的初始告警集合包括：

- 客户端没有已连接的网关，且持续时间超过预期重连窗口。
- 请求超时率或错误率在多个窗口内超过服务目标。
- P95/P99 请求延迟较高，且请求量不为零。
- 被拒绝、丢弃或因负载削减而移除的消息增多。
- 存储错误率或延迟超过提供程序基线。
- 长时间运行的执行轮次增加并持续处于高位。
- 可用内存接近已配置的进程或容器限制，或 GC 暂停时间增加。（译注：原文使用 available memory，其字面含义与本节内存压力告警的语境存在歧义。）
- 活跃成员资格与预期部署不一致，持续时间超过滚动发布或重启所需时间。
- 优雅关闭未在编排器的终止时限内完成。
- TLS 证书即将过期。

在影响用户或数据与可用性面临迫近风险时，通知值班人员。孤立警告和容量趋势应转入工单或仪表板，除非它们持续超过阈值。

<a id="dashboard-and-telemetry-backends"></a>

## 仪表板和遥测后端

Orleans Dashboard 提供当前运维视图及方法性能分析。它不能替代留存的日志、指标、跟踪或告警。将其作为管理终结点保护，并使用 OTLP 持久保存遥测数据。参阅 [Orleans Dashboard](../../dashboard/index.md)。
