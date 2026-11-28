---
title: 消息传递运维指南
description: 配置容量和保留期、诊断投递、重放死信并测量吞吐量。
ms.date: 10/10/2026
ms.topic: how-to
---

<a id="operate-messaging"></a>

# 消息传递运维指南

使用本指南配置和运维
[消息传递](messaging.md)中介绍的收件箱与发件箱。有关业务重放策略，参见
[幂等性与层次键](messaging-idempotency.md)。

<a id="configure-storage-and-execution"></a>

## 配置存储与执行

在启用消息传递之前，配置 Journaling 存储及 Durable Jobs 存储。
业务状态、结果查询状态和消息传递状态通过同一个
Grain 日志持久化。收件箱与发件箱注册日志状态
`orleans.messaging.inbox` 和 `orleans.messaging.outbox`；应为应用状态使用
其他名称。消息传递使用 Grain 配置的日志格式，即 JSON 或 Orleans
二进制格式。

每个激活实例在其调度器上运行一个消息泵，每次处理一条收件箱消息。
在准备期间执行 await 的处理器会延迟该 Grain 的下一条消息，
其他 Grain 则独立推进。按业务
所有权划分工作负载，例如租户/账户、租户/SKU 或订单。少量热点 Grain 会限制
串行吞吐量；当存在独立的业务所有者可以分散部署时，增加 Silo 可扩展
容量。

有关存储配置，参见[Journaling 配置](journaling/configuration.md)、
[Azure Storage 提供程序](journaling/azure-storage.md)和
[Durable Jobs 迁移](journaling/durable-jobs-migration.md)。

<a id="capacity-retries-and-retention"></a>

## 容量、重试与保留期

<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions> 用于配置收件箱和发件箱的
行为，通过 `AddMessaging` 注册配置，并在启动时验证。

| 设置 | 默认值 | 运维影响 |
| --- | --- | --- |
| `MaxCapacity` | 1,000 | 限制待处理收件箱消息数量。每条待处理消息（包括等待其 `DeliverAt` 时间的已调度消息）从接纳至完成均计入容量。达到容量上限时，新消息收到 `Backpressured`，已有消息的重复投递收到 `Duplicate`。Grain 发送给自身的消息不受此限制地接纳，并计入容量直至完成。 |
| `DeduplicationWindow` | 7 天 | 保留已完成的标识，直至其已接纳的 `SentAt` 不晚于持久化准入下限。下限依据观测到的 Durable Jobs 时间减去此期限向前推进；已确认的快照及恢复回收符合条件的记录。高于下限的未跟踪投递时间受接收方时间加上此窗口的限制；自发送采用本地时钟或回拨后高于下限的第一个时间。 |
| `RetryInitialInterval` | 1 秒 | 收件箱首次处理重试或发件箱首次投递重试之前的延迟。每次重试使延迟加倍，最高为此间隔的 64 倍。 |
| `InboxRetryTimeout` | 6 小时 | 自首次失败起，若处理器尝试持续失败达此时长，则将消息转入收件箱死信。超时到期时执行最后一次尝试，重试消息保留其在 `MaxCapacity` 中的位置。`TimeSpan.Zero` 在第二次失败时转入死信，因为恢复时无法区分失败的尝试与尚未运行便因崩溃中断的尝试。崩溃或处理器尝试失败等非干净停止后的恢复，对每条到期消息计入一次尝试；正常停用则不计入。 |
| `OutboxRetryTimeout` | 6 小时 | 限制截至 `SentAt + OutboxRetryTimeout` 的投递尝试期限，包括背压期间。发送时的写入将 `SentAt` 记入日志，每次尝试及恢复均保留该值。必须小于 `DeduplicationWindow`。 |
| `DeadLetterRetentionPeriod` | 30 天 | 移除早于此保留期限的死信，其时间戳与保留期限均依据 Grain 的时钟。 |
| `MaxRetainedDeadLetters` | 1,000 | 分别限制收件箱及发件箱死信数量，优先移除最早的记录。 |
| `InboxBatchSize` | 32 | 限制消息泵单轮处理的收件箱消息数。 |
| `OutboxBatchSize` | 32 | 限制单个 Grain 在途投递尝试数量。每次尝试独立完成并释放其槽位，因此较慢的接收方只占用一个槽位，不会延迟发往其他接收方的消息。 |

根据可容忍的中断与背压确定重试预算，并根据历史记录的保留成本
确定准入保留期。每次尝试均携带
原始的已记入日志的 <xref:Orleans.Durable.Messaging.InboxMessage.SentAt>。接收方
优先对已跟踪标识去重：待处理或已完成记录响应
每次重复投递，并保持收件箱已接纳的发送时间。
未跟踪且发送时间不晚于下限的投递返回 `Expired`。
下限只会向前推进，并在恢复后保持有效。时钟跳变可能使新工作
提前过期或使更多历史记录保留，但发送时间和持久化下限仍保持
原始发送的准入决定。

未来时间验证对高于准入下限的未跟踪投递施加上限。
已跟踪标识无论其 `SentAt` 为何，都会收到已记录的响应，
即使时钟回拨也如此。不晚于准入下限的未跟踪时间收到
`Expired`，即使大幅回拨使该时间高于接收方时间加上窗口。
其他超出该上限的未跟踪时间都会抛出
<xref:System.ArgumentOutOfRangeException>，既不更改日志，也不将消息提供给接收器；
等于上限的时间允许准入。

Grain 发送给自身的消息使用本地暂存时间，或时钟回拨后高于
准入下限的第一个时间。对于历史压实而言，这个第一个时间仍恰好位于
既有下限之后，即使其超出
投递时间上限也是如此。

激活实例启动及每次新增死信时均应用死信保留策略。
快照将其准入下限与压实后的历史记录
原子持久化。在下限得到确认前，内存中的完成记录仍被保留。
根据处理速率及配置的保留期规划历史记录容量，并考虑
时钟变化及后续重复提交导致的额外历史记录。

收件箱容量向接收方提供背压机制；当发件箱深度增长时，应在
应用中限制发送方准入。设置每个租户的有效载荷和提交限制：
除消息数量外，字节数与序列化成本同样重要。发送及投递
会在任何持久化变更前拒绝长度超过 1,024 个 UTF-8 字节的消息 ID，以及长度超过 256 个 UTF-8
字节的主题。

每个 Grain 的一个持久化作业为在线消息泵提供崩溃保障；消息泵在到期工作已持久化后
立即处理。恢复时已到期的工作会在约一秒内恢复，
之后到期的工作则在其重试时间或
`DeliverAt` 时间唤醒 Grain。仅当没有已跟踪作业会在该一秒内到期时，写入才调度作业，
因此繁忙的 Grain 不必每次写入都执行 Durable Jobs 操作。消息泵作业
在激活失败期间无限期重试，采用有上限的退避；每个激活实例
根据恢复的状态重新安排唤醒。恢复时间取决于重试
调度、Durable Jobs 轮询及激活延迟。

已调度的消息保留在接收方日志和快照中直至到期，
期间占用 `MaxCapacity`，因此应与其他待处理消息一并规划容量、存储及恢复
成本。收件箱深度计数器也包含这些消息。

<a id="interpret-delivery-and-completion"></a>

## 理解投递与完成

| 投递状态 | 含义及应对方式 |
| --- | --- |
| `Accepted` | 接收方已将消息记入日志。等待持久化回复，或通过相同命令 ID 查询业务状态。 |
| `Duplicate` | 接收方在保留期内拥有该消息 ID 的待处理或已完成记录。发件箱移除消息；业务结果来自回复或查询。如果重复投递的是收件箱接收器已拒绝的消息，则返回原始的 `DeadLettered` 结果。 |
| `Backpressured` | 接收方达到容量上限。发件箱重试。检查接收方队列深度、最早消息的等待时长及处理器延迟。 |
| `HandlerNotFound` | 接收方没有注册处理器。发件箱记录错误并重试。检查激活期间的处理器注册和部署版本差异。 |
| `DeadLettered` | 接收方已持久化拒绝消息，包含原因及可选的应用定义代码。发件箱移除消息；由来源提供的消息以 `Rejected` 状态退出，并附带该原因及代码。 |
| `Expired` | 发送时间不晚于接收方的持久化准入下限，例如投递延迟超过 `SentAt + DeduplicationWindow`，或接收方时钟向前跳变。此前接纳结果未知。发件箱保留死信，并将由来源提供的消息按 `DeadLettered` 状态退出。授权另一项发送前，通过应用核实结果。 |
| 其他任何状态 | 发送方无法解释状态，例如来自较新版本接收方的状态，因此结果未知。发件箱重试直至 `OutboxRetryTimeout`。 |

`Accepted`、`Duplicate`、`DeadLettered` 和 `Expired` 为终态。

发送方的日志写入确立出站消息的持久性。接收方的
接纳操作确立收件箱条目的持久性。处理器的日志写入
确立业务处理的持久性。使用关联回复来实现
端到端业务确认。

<a id="diagnose-failures-by-boundary"></a>

## 按边界诊断故障

| 症状 | 边界及解决方法 |
| --- | --- |
| 反复出现处理器错误 | 验证主题注册、正文兼容性、命令 ID、回复目标及准备阶段依赖项。处理器异常导致 Grain 停用，并在恢复后重试直至 `InboxRetryTimeout`，期间占用容量槽位，因此仅将异常用于暂时性故障。使用 `Fail(reason)` 将永久无效的输入转入死信，或者发送强类型拒绝回复并调用 `Complete()`。 |
| 消息以 "No handler is registered"（未注册处理器）原因转入死信 | 已注册处理器，但没有处理器接纳该主题。消息已重试至 `InboxRetryTimeout`。为 Grain 接收的每个主题注册处理器。 |
| 待处理命令复用冲突 | 发送方或接收方抛出 `InvalidOperationException`。保持原始命令 ID、主题、目标和有效载荷；新命令使用不同的 ID。 |
| 处理器失败后 Grain 停用 | 处理器抛出异常，或者未调用 `Complete()` 或 `Fail(reason)` 就返回。日志恢复丢弃其暂存变更并计入该尝试，因此消息采用退避策略重试直至 `InboxRetryTimeout`。仅将异常用于暂时性故障，并在首次变更之前完成可能失败的工作。 |
| 死信包含 `The activation stopped without recording the outcome of an attempt.` | 恢复时计入了由非干净停止中断的尝试；该消息的重试窗口此前已开始且已经到期。在 Grain 日志中检查失败的处理器或反复发生的崩溃。单次崩溃计入一次尝试，本身绝不会使消息转入死信。 |
| 处理器返回后 Grain 停用 | 处理器以相互冲突的结果调用了 `Complete()` 与 `Fail(reason)`。确保每条分支恰好暂存一个结果。 |
| 在 `Complete()` 之后记录处理器错误 | 暂存的完成操作及出站消息会持久化。使用已提交状态诊断错误。 |
| 存储或暂存失败后 Grain 停用 | 消息传递否决该激活实例此后的每次日志写入，Grain 停用。下一个激活实例恢复已提交日志并继续待处理工作。 |
| 发件箱死信的原因说明重试超时已到期 | 原始发送的 `OutboxRetryTimeout` 内未记录终态响应。检查接收方可用性和处理器注册，再核实可能已被接纳的命令，然后才授权重放。 |
| 发送方以 `Expired` 转入死信，且接收方结果未知 | 投递延迟超过 `SentAt + DeduplicationWindow`，或接收方时钟向前跳变导致其持久化准入下限推进。通过应用对账核实业务状态，检查发送方和接收方时钟，并根据可容忍的投递延迟和时钟变化设置 `DeduplicationWindow`。保持 `OutboxRetryTimeout` 小于该窗口。 |
| 接收方警告 `SentAt` 超前过多；发送方重试 `ArgumentOutOfRangeException` | 高于准入下限的未跟踪标识投递时间超前接收方时间超过 `DeduplicationWindow`。检查双方时钟及直接调用方创建时间戳的方式。时钟回拨后，已跟踪标识仍返回已记录响应。重试时保留原始逻辑发送时间；原始重试预算耗尽后，先通过应用核实已转入死信的发送，再授权新提交。 |
| 调用方取消 | 已接纳的写入或投递会继续直至其实际结果确定。查询持久化业务状态，或使用相同命令 ID 重试。 |

处理器遵循两阶段模式：所有异步及可能失败的准备工作仅使用
局部值，最终阶段在一个同步操作块中发送消息、应用业务变更并
调用 `Complete()`。激活实例上的任何日志写入都会捕获 Grain 暂存的变更，
因此在调查非预期的部分效果时，应复查处理器的
状态变更边界。

<a id="inspect-and-replay-dead-letters"></a>

## 检查及重放死信

公开经过应用授权的诊断方法，以读取
<xref:Orleans.Durable.Messaging.IMessagingDiagnostics.InboxDeadLetters>
和 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.OutboxDeadLetters>。
每条记录均包含消息、尝试次数、失败原因及转入死信的时间。
收件箱消息接收器的拒绝属于已处理记录，而非死信；
发送方发件箱收到其原因及代码。对于由来源提供、但
<xref:Orleans.Durable.Messaging.IOutboxMessageSource> 未能提供消息而产生的发件箱死信，仅保留
消息 ID，使用空有效载荷及默认接收方；原因记录
来源的失败。

<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*> 为诊断出的永久故障创建收件箱死信，
并将消息 ID 记录为已处理，因此
已处理消息计数器报告 `dead_lettered`，重复投递返回 `Duplicate`，
只要记录仍被保留。正常业务拒绝通过强类型应用
回复与 `Complete()` 一起提交；
[库存示例](messaging-recipes.md#reserve-inventory-once-per-order-line)
明确建模了接受与拒绝的预留结果。

对于远程检查，公开一个 Grain 方法，将记录的标识、
主题、原因、时间戳及相关有效载荷字段复制到应用 DTO。
死信持有原始有效载荷与标头数组；应用需要可变缓冲区时，
应复制字节。

保留的失败记录提供有界的运维证据：识别
不兼容的主题或有效载荷，核实外部效果，修复生产者或
处理器，并授权修正后的提交。在保留策略移除这些证据之前采集它们，
并将重放及诊断访问的授权与普通
提交的授权分开。

<a id="guarantee-application-follow-up"></a>

### 保证应用后续处理

当终态投递结果需要应用采取行动时，使用 <xref:Orleans.Durable.Messaging.IOutboxMessageSource>。
在
<xref:Orleans.Durable.Messaging.IOutboxMessageSource.OnRetired*> 中，将终态结果与一条发送给自身的后续消息同步暂存到
应用日志状态中，同时暂存有效载荷的释放操作。
发件箱移除、结果及后续消息接纳原子提交。
提交前恢复会重试待处理条目及回调；
提交后恢复则通过收件箱的持久化唤醒继续后续处理。
后续处理器可以查询参与者、核实未知效果，并提交
应用的下一步决策。

`DeadLettered` 状态的退出涵盖重试耗尽、`Expired` 响应及来源
失败。此前接纳结果仍未知；`Rejected` 标识接收方的持久化拒绝。
恢复期间保持来源所有权的注册。回调
异常会使激活实例失败，并保留最后提交的待处理条目。
应用结果与工作记录的保留遵循应用业务策略；
诊断死信集合遵循配置的容量和时长。

接收方将每条收件箱死信的消息 ID 记录为已处理，因此
在完成记录保留期间原始发送收到 `Duplicate`，并在其发送时间被下限排除且其记录已通过压实移除后收到 `Expired`。
发件箱死信可能代表此前已接纳的消息，
但响应已经丢失。应在核实结果之后进行重放。

要重放发件箱死信：

1. 修复接收方可用性、处理器注册或有效载荷兼容性
   问题，并使用原始命令 ID 查询参与者业务状态。
2. 核实原始结果，仅在业务协议允许时
   授权新提交。<xref:Orleans.Durable.Messaging.IOutbox.Send*> 在原始条目退出后设置新的暂存时间。
   已跟踪的原始 ID 返回已知结果；
   在该标识的保留期结束之后，获准的新发送时间可能产生另一项
   效果。在授权修正或重复效果时使用修订 ID，
   并记录其与原始 ID 的关系。
3. 记录重放处置结果，并使用
   <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveOutboxDeadLetter*> 移除记录。
   处置结果、移除操作和重放消息在下一次日志写入中持久化。

要重放收件箱死信：

1. 修复准备阶段依赖项、处理器或有效载荷兼容性问题。
2. 使用原始消息 ID 查询参与者业务状态和外部提供程序的结果，
   并核实失败尝试已经应用的任何效果。
3. 通过已授权的 Grain 方法，以新修订 ID（例如原始键的子键）重新提交命令，
   例如 `{original}/replay/1`。
   记录修订与原始 ID 之间的关系。
4. 记录重放处置结果，并使用
   <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveInboxDeadLetter*> 移除记录。
   处置结果、移除操作及出站修订消息在下一次日志写入中持久化。

外部提供程序的保留及查询协议决定原始副作用能否安全重试；
授权修订之前，使用原始键
核实该副作用的结果。

<a id="observe-progress-and-size-the-system"></a>

## 观察进度与规划系统容量

`Microsoft.Orleans` Meter 公开以下消息传递指标工具。每个指标
均携带 `grain_type` 标签；具有结果分类的计数器还携带 `status` 标签。

| 指标 | 类型 | 解释 |
| --- | --- | --- |
| `orleans-messaging-inbox-messages-received` | Counter | 接收方的投递结果：`accepted`、`duplicate`、`backpressured`、`handler_not_found` 或 `expired`（发送时间过早）。Grain 发送给自身的消息在发送时记录 `accepted` 或 `duplicate`。收件箱接收器接手的消息记录 `accepted`，若接收器拒绝则记录 `dead_lettered`。 |
| `orleans-messaging-inbox-messages-processed` | Counter | 处理器结果：`success`、`retry` 或 `dead_lettered`。接收器接纳记录 `success`；接收器拒绝记录 `dead_lettered`。 |
| `orleans-messaging-outbox-messages-sent` | Counter | 通过 `Send` 暂存的消息，包括 Grain 发送给自身而绕过发件箱的消息。 |
| `orleans-messaging-outbox-messages-delivered` | Counter | 发送方的投递尝试结果：`accepted`、`duplicate`、`deadlettered`（接收方的持久化拒绝）、`expired`（发送时间过早）、`backpressured`、`handler_not_found` 或 `error`（调用失败且没有响应）。未经尝试即退出的消息记录 `withdrawn`（来源撤回消息）、`expired`（重试超时已到期）、`source_error`（所属来源未能提供消息）或 `unprovided`（没有已注册来源提供已恢复的消息）。发送方无法解释的状态记录其小写名称。 |
| `orleans-messaging-inbox-processing-duration` | Histogram，毫秒 | 处理器处理时长。 |
| `orleans-messaging-outbox-delivery-duration` | Histogram，毫秒 | 投递时长。 |
| `orleans-messaging-inbox-depth` | UpDownCounter | 所有活跃 Grain 的待处理收件箱消息。 |
| `orleans-messaging-outbox-depth` | UpDownCounter | 所有活跃 Grain 的待处理发件箱消息。 |

队列深度指标随消息的添加和移除报告变化，并在激活实例的消息传递停止时
释放其贡献值。这些贡献值的总和为所有活跃 Grain 当前的
待处理工作。

`Orleans.Messaging` 诊断侦听器 `MessagingEvents` 在发生转换的激活实例上
同步报告转换，并携带其 `IGrainContext`：

| 事件 | 触发时机 |
| --- | --- |
| `InboxMessageReceiving` | 新投递的发送时间高于准入下限，在收件箱暂存消息效果之前触发。观察者异常与接收器异常一样导致尝试失败，因此发送方会重试。 |
| `OutboxRetryScheduled` | 失败的投递尝试已重新调度，附带尝试次数、错误及下次尝试时间。 |
| `OutboxMessageRetired` | 消息以已投递、已拒绝或已转入死信的方式退出，并附带退出结果。 |

测试订阅 `MessagingEvents.AllEvents`，或者使用 `DiagnosticEventCollector` 来
等待状态转换而非轮询，并通过
`InboxMessageReceiving` 观察者注入投递故障。

发件箱以 `Debug` 级别记录投递轮次、已投递消息及背压；
以 `Warning` 级别记录已过期、未提供的消息及无法解释的状态；
对 `HandlerNotFound` 响应、投递错误及来源失败，以 `Error` 级别记录。

通过应用遥测管道导出指标。对持续的队列深度
增长、应用诊断中最早待处理消息的等待时长、重复失败结果
及存储延迟发出告警。将它们与 Journaling 和 Durable Jobs 健康信号结合。
记录规范命令 ID 和主题以便调查事件，并将
高基数的租户键和订单键保留在日志或跟踪中。

容量涵盖待处理消息、序列化正文、完成记录、
业务查询结果及保留的死信。快照与存储成本
取决于这些保留集合。在测量稳态处理的同时，
测量激活重放时间及存储吞吐量。

<a id="budget-allocations-and-owned-memory"></a>

<a id="budget-allocations-and-owned-memory"></a>

<a id="budget-allocations-and-managed-memory"></a>

### 规划分配量与托管内存

每条消息持有有效载荷数组和可选的命名标头数组，因此消息大小
与速率直接影响托管内存分配速率。在规划消息传递记录及应用
结果时，同时考虑待处理编码字节数、数组开销及 GC 活动。
按照日志容量与延迟预算在入口应用有效载荷大小和提交数量
限制。

发件箱状态、收件箱状态、处理器上下文及投递操作在使用
有效载荷数组期间持有其引用。移除消息后，其他引用结束时，其数组即可被
回收。参见
[有效载荷与标头数组](messaging.md#use-gc-owned-payload-arrays)。

| 预算项目 | 测量方式 |
| --- | --- |
| 处理分配量 | 预热后，整个进程分配的托管字节数除以已确认消息数。 |
| 待处理工作 | 收件箱与发件箱深度、活跃投递数量及序列化有效载荷字节数。 |
| 保留数据 | 配置保留期内的已完成消息 ID 与查询/审计结果，以及日志历史和快照。 |
| 支撑内存 | 序列化元数据的基数、序列化缓冲区容量及在途操作引用。 |
| 持久化与恢复工作 | 日志追加、快照及 Durable Jobs 唤醒调度。 |

使用串行链测量延迟，使用多目标工作负载测量
待处理工作容量。分配调用栈跟踪将成本归因于产生它们的
路径。比较采用相同有效载荷、链长度、提供程序和
保留设置的预热运行；比较延迟时使用未启用跟踪的运行。

<a id="evolve-application-payload-records"></a>

## 演进应用有效载荷记录

保持应用主题、命令 ID 及序列化标识符稳定。强类型
有效载荷记录使用 `GenerateSerializer` 和稳定的 `Id` 成员；保留对
先前编码记录的读取支持。当不兼容的有效载荷或操作策略发生变化时，
明确对业务语义与主题进行版本控制。

在生产者发出新主题之前部署其处理器。在
待处理消息、保留的死信及已授权的重放均处理完毕后，才停用主题。
协调规范键编码的变更，使现有命令保持其标识。

从受信任的入口派生租户所有权和已授权目标。在准备外部效果或
改变业务状态之前，依据该可信所有权验证声明的命令 ID、主题和正文中的
回复目标。限制
序列化有效载荷大小，不要在正文、键和诊断日志中包含秘密信息。

<a id="measure-sequential-throughput"></a>

## 测量串行吞吐量

仓库的 `Messaging.Sequential` 基准测试测量一个 Silo 上由 2 个、
4 个或 8 个相互交互的 Grain 构成的单条链。每次调用执行 1,024 次串行持久化
投递，并等待每个处理器步骤的日志确认。
每个 Grain 在构造函数中使用静态委托注册强类型处理器，
委托接收 Grain 作为参数。处理器使用强类型
`Send` 暂存下一跳，更新计数器并完成消息处理。每一跳使用
`runs/{invocation}/hops/{hop}` 命令 ID，并采用区域性无关的数值格式。一个
日志状态观察确认，清理过程检查精确的总业务效果次数及
每个 Grain 的业务效果次数。

从仓库根目录运行：

```powershell
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --job Dry --buildTimeout 600 --noOverwrite
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 --no-build -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --buildTimeout 600 --noOverwrite
```

构建超时设置容纳基准测试依赖图的隔离构建。
Dry 运行验证执行。测量报告将 `Mean` 归一化为一条
已确认消息：吞吐量为 `1 / Mean`，其中 `Mean` 以秒表示。
基准测试在每次测量迭代之外重新创建并预热集群，以
限制保留的历史记录。其易失性日志与内存作业隔离出本地
框架开销。生产容量测量应使用部署中的持久化提供程序、跨 Silo 放置，
以及独立并发链。每次比较均记录
硬件、运行时、提交、提供程序和基准测试预设。

使用 `--memory` 报告整个进程每条已确认消息分配的托管字节数。
比较时采用相同的 Grain 数量、链长度及存储阈值。
易失性存储默认以追加 100 次或写入 1 MiB 作为快照间隔阈值；参见
[开发存储](journaling/configuration.md#development-storage)，了解这两项
限制及其内存与重放方面的权衡。
