---
title: "解释 Orleans 可观测信号"
description: "使用 Orleans 日志、度量、跟踪、相关和警报,而不依赖简略目录。"
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="interpret-orleans-observability-signals"></a>

# 解释 Orleans 可观测信号

以服务级症状为起点,然后使用 Orleans 遥测来缩小原因。 提醒每个运行时事件或仪器会制造噪音和对夫妇操作,使其了解实施的细节。

<a id="logs"></a>

## 日志

Orleans 写作方式为 [`Microsoft.Extensions.Logging`](https://learn.microsoft.com/dotnet/core/extensions/logging/overview)。保存提供者提供的结构化字段,包括类别、级别、事件编号、例外、跟踪身份和横跨ID。使用正常的 .NET 记录配置配置配置分类等级。

Orleans <xref:Microsoft.Extensions.Logging.EventId> 是一个诊断识别符,不是完整的事件定义。数字范围与事件分配随着运行时间的演变而变化。

- 在调查已知身份时查询生成的 <xref:Orleans.ErrorCode> API 引用。
- 为了检查正在积极拟订中的定义,请查询 [`main` 分支上的运行时错误代码源](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core.Abstractions/Logging/ErrorCodes.cs)。
- 警告持续症状、例外类型或已知事件加上服务影响,而不是每次警告。

记录结构化属性或核定活动标记中的应用程序相关字段。在有结构化字段时,不要分析成日志文字。

<a id="metrics"></a>

## 计量

订阅 `Microsoft.Orleans` 仪表。 找到您所部署版本所发射的精确仪器。 关于安装和命令的细节, 请参见 [`dotnet-counters` 诊断工具](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters):

```dotnetcli
dotnet-counters monitor -n <ProcessName> --counters Microsoft.Orleans
```

见见 [监测 Orleans 指标](metrics.md) 当前客户当前客户和 silo 符号、 仪器类型、 单位、 尺寸和健康解释。 对应值通常需要时间窗口的速率或增加率或增加率,测量仪需要刻意选择的时间点汇总,直方图需要百分位数加请求量。 silo 与集群总量:一个外端表示宿主或分割问题,而整个集群的转变则表示共有依赖性或交通量变化。

<a id="cardinality"></a>

### 红心

矩阵后端为每个独有属性集创建时间序列。 运行、 方向、 状态、 grain 类型、 提供者名称和 silo 身份等小值的宽度属性。 永远不要添加 grain 密钥、 跟踪 ID、 请求 ID、 堆叠追踪、 URL 查询或任意例外文本作为参数属性 。

在添加一个维度之前估计序列数 :

`instruments x attribute-value combinations x instances`

大型组合即使每个标签似乎都合理,也增加了记忆、网络和后端成本。

<a id="traces"></a>

## 踪迹

应用程序可以用于跟踪 grain 呼叫和运行时间、生命周期和储存时间,以解释时间的用地。

1. 哪个入境点发起呼叫?
2. 援引了哪些 grain 行动?
3. 等待、执行应用代码、启动、放置或进入储存的时间是多少?
4. 哪个例外或地位终止了行动?

如果宽度显示为单独的痕迹, 则在客户端上确认 <xref:Orleans.Hosting.ClientBuilderExtensions.AddActivityPropagation*?displayProperty=nameWithType> , 相应的 silo 注册在路径中的所有 silos 上。 同时还要核实取样员是否遵守母决定,代理方是否保存了 W3C `traceparent`。

默认时避免记录 grain 键和状态值。它们可以是高心胸或敏感。如果发生事故需要,请使用有限制的、有时限的抓取并随后删除。

<a id="baseline-alerts"></a>

## 基线警报

与正常交通和服务目标有关的警戒线。

- 客户端没有连接网关的时间比预期的重新连接窗口长。
- 请求超时或错误率超过多个窗口的服务目标。
- P95/P99请求的延迟时间很高,而请求量不是零。
- 拒绝、投递或加载断层信息增加。
- 储存错误率或潜伏率高于供应商基线。
- 长途转弯会增加,并且继续上升。
- 可用内存接近配置的流程/容器限制或GC暂停时间上升。
- 现役成员不同于预期部署的时间超过推出或重新开始的期间。
- 乐团的终止预算里 优雅的关闭并没有完成
- TLS 证书即将到期 。

道路孤立警告和机票或仪表板的能力趋势,除非它们越过一个持续门槛。

<a id="dashboard-and-telemetry-backends"></a>

## 仪表板和遥测后端

下 Orleans 磁盘板提供了当前操作视图和方法特征分析。 它不是保留日志、 度量、 跟踪或警报的替代物。 安全它作为行政端点, 并使用 OTLP 进行耐用遥测 。 见 [Orleans 机表](../../dashboard/index.md).
