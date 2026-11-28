---
title: "消息传递"
description: "了解收件箱和发件箱的保证、执行模型和运行限制。"
ms.date: 10/10/2026
ms.topic: conceptual
---

<a id="messaging"></a>

# 消息传递

`Microsoft.Orleans.Durable` 包中的消息传递机制基于 Orleans Journaling（日志化）和 Durable Jobs（持久化作业），为每个 Grain 提供持久化收件箱与发件箱。Grain 在自己的日志中一并提交收到的消息、业务变更及待发送消息，因此处理器产生的效果和待发送消息在激活实例丢失后仍可恢复。

> `Microsoft.Orleans.Durable` 是面向 .NET 11（`net11.0`）的 Alpha 预发布包。由于 API 和日志格式在稳定之前可能发生变化，评估时应制定明确的升级、回滚、备份与恢复计划。

本页介绍消息、提交与执行模型。可进一步阅读：

- [幂等性与分层操作键](messaging-idempotency.md)：应用命令标识、保留的完成记录与外部副作用。
- [实践示例](messaging-recipes.md)：库存预留、支付服务商对账、乱序投影与通知扇出。
- [运维与吞吐量](messaging-operations.md)：配置、诊断、死信重放、升级以及顺序消息传递基准测试。
- [持久化任务](durable-tasks.md)：用顺序代码编写多步骤工作流，并在相同的收件箱和发件箱之上执行。

<a id="message-and-routing-model"></a>

## 消息与路由模型

每个 <xref:Orleans.Durable.Messaging.Envelope> 都携带由应用程序提供的 <xref:Orleans.Durable.Messaging.MessageId>（封装了 <xref:Orleans.Durable.Messaging.HierarchicalKey>）、必需的 `byte[]` 有效载荷、可选的按序数规则比较的主题、可选的命名字节数组标头，以及可选的 `DeliverAt` 时间。允许空有效载荷。

<xref:Orleans.Durable.Messaging.OutboxMessage> 将信封与目标（<xref:Orleans.Runtime.GrainId>）配对。投递时，将携带同一信封的 <xref:Orleans.Durable.Messaging.InboxMessage> 传递给该 Grain 的收件箱。信封的 ID 标识逻辑命令。回复目标、领域版本以及其他业务数据由应用有效载荷携带。

发送和投递操作会在发生任何持久化变更之前验证每条消息：

- 消息 ID 必须已设置，其规范形式的 UTF-8 编码长度不得超过 1,024 字节。
- 有效载荷不得为 null。
- 如果提供主题，则其不得为空，且 UTF-8 编码长度不得超过 256 字节。
- 标头键必须是非空且格式正确的 UTF-16 字符串，标头值不得为 null。
- 出站消息必须具有目标。

重试以及重新构造的提交必须复用相同的 ID、主题、目标、正文和 `DeliverAt` 时间。

<a id="register-handlers"></a>

### 注册处理器

向 Grain 注入 <xref:Orleans.Durable.Messaging.IInbox>，并通过 <xref:Orleans.Durable.Messaging.IInbox.Register*> 添加处理器。<xref:Orleans.Durable.Messaging.IInboxHandler> 有两个成员：<xref:Orleans.Durable.Messaging.IInboxHandler.CanHandle*> 接收 <xref:Orleans.Durable.Messaging.InboxMessage> 并选择处理器能够接受的消息；<xref:Orleans.Durable.Messaging.IInboxHandler.HandleAsync*> 处理一条消息。收件箱按照注册顺序，将每条消息分派给第一个 `CanHandle` 返回 `true` 的处理器。选中的处理器在 `HandleAsync` 中暂存消息的终结结果。

通过 <xref:Orleans.Durable.Messaging.InboxExtensions.Register*> 扩展方法注册强类型处理器。每个重载接受一个 <xref:Orleans.Durable.Messaging.MessageType`1> 绑定和一个委托，按序数比较规则匹配绑定的主题，解码有效载荷，并将解码后的正文与处理器上下文传给委托：

| 委托 | 用途 |
| --- | --- |
| `Action<T, IInboxHandlerContext>` | 同步处理。 |
| `Func<T, IInboxHandlerContext, CancellationToken, ValueTask>` | 最终更新前的异步准备。 |
| `Action<T, TArg, IInboxHandlerContext>` | 带显式参数的同步处理，适用于 `static` 委托。 |
| `Func<T, TArg, IInboxHandlerContext, CancellationToken, ValueTask>` | 带显式参数的异步处理，适用于 `static` 委托。 |

接受 `TArg` 参数的重载允许 `static` Lambda 表达式在不分配闭包的情况下接收 Grain 或其他状态。同步强类型处理器会在执行委托之前立即检查处理取消令牌。`Register` 返回收件箱，因此可以链式注册。

处理器可以随时注册。建议在构造 Grain 时或在 <xref:Orleans.Grain.OnActivateAsync*> 中注册，使收件箱开始处理恢复的消息时处理器已经可用。收件箱中已有的消息会等待处理器注册。

没有注册任何处理器的接收方会以 `DeliveryResult.HandlerNotFound` 响应投递，发送方随后重试该消息。如果已经注册了处理器，但没有任何处理器接受消息，则该消息的处理会报告处理器失败，并遵循重试与死信策略。

<a id="encode-ordinary-application-values"></a>

### 编码普通应用数据

消息正文是普通的 Orleans 可序列化类型：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_payload" language="csharp":::

<xref:Orleans.Durable.Messaging.MessageType`1> 将主题与正文类型的 <xref:Orleans.Serialization.Serializer`1> 绑定。<xref:Orleans.Durable.Messaging.MessageType`1.Decode*> 在读取有效载荷之前验证信封的主题。将应用的绑定集中定义在一个类中，通过 <xref:Orleans.Serialization.Serializer> 服务构造绑定，将该类注册为单例，再注入 Grain：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_messages" language="csharp":::

<xref:Orleans.Hosting.MessageTypeExtensions.AddMessageType*> 将单个绑定注册为键控单例，以便通过 `FromKeyedServices` 进行注入。

主题采用精确的序数比较规则，例如 `inventory.reserve.v1`。不同主题可以绑定同一个 CLR 类型。当主题使用基类型时，应在序列化契约中定义受支持的多态行为。

<xref:Orleans.Durable.Messaging.OutboxExtensions.Send*> 接受绑定、稳定的命令 ID、目标和正文。目标可以是 <xref:Orleans.Runtime.GrainId>，也可以是 <xref:Orleans.Runtime.IAddressable> Grain 引用。该辅助方法编码正文并暂存出站消息。序列化先于暂存完成，因此序列化错误不会改变发件箱。批量重载在暂存任何消息之前编码所有消息，若编码失败则不暂存任何消息；全部编码成功后按顺序暂存。

<xref:Orleans.Durable.Messaging.OutboxExtensions.SendReply*> 接受绑定、处理器上下文、目标和结果正文。它在接收到的命令 ID 后追加字面量 `result` 段以派生回复 ID。应用程序负责提供回复目标，通常取自请求正文的某个字段（例如 `ResponseDestination`）。

<xref:Orleans.Durable.Messaging.MessageType`1.Create*> 根据命令 ID、目标和正文构造出站消息。可通过其 <xref:Orleans.Durable.Messaging.OutboxMessage.ToInboxMessage*> 视图显式投递消息。

<a id="own-and-borrow-payload-slices"></a>

<a id="use-gc-owned-payload-arrays"></a>

<a id="use-payload-and-header-slices"></a>

<a id="use-payload-and-header-arrays"></a>

### 使用有效载荷和标头数组

信封是只读值，持有有效载荷数组、主题和可选的标头字典。结构体赋值会共享这些对象；RPC 复制和反序列化会生成独立副本。发送前准备好有效载荷和标头，发送后保持内容不变：发件箱状态、投递重试、处理器上下文和诊断会读取同一批数组。

对于原始协议，设置编码后的有效载荷及协议使用的标头。接收方按序数比较规则查找标头键，并使用约定的编码解码。下面示例使用普通序列化器编码货运有效载荷和两个应用标头：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_byte_payload" language="csharp":::

标头与有效载荷一起经历持久化、重试、恢复以及死信检查。重复提交待处理或已暂存的命令时，保留首次提交的标头。将决定命令含义的参数放在有效载荷和主题中，标头用于携带跟踪标识等附加上下文。

<a id="carry-independently-encoded-items"></a>

<a id="carry-related-values-in-one-message"></a>

### 在同一消息中携带相关数据

对于复合消息，使用可序列化的 record 类型。字段可包含其他应用记录、集合及二进制数据。此货运示例在同一个正文中携带预留请求和 `byte[]` 清单：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_shipment" language="csharp":::

强类型 `Send` 辅助方法序列化整个 `Shipment`，强类型处理器接收解码后的记录。在消息传递机制之外进行序列化时，使用 <xref:Orleans.Serialization.Serializer`1.SerializeToArray*> 和 <xref:Orleans.Serialization.Serializer`1.Deserialize*>。

<a id="schedule-processing"></a>

### 计划处理时间

设置 <xref:Orleans.Durable.Messaging.Envelope.DeliverAt>，使消息保留在接收方收件箱中，直至 Durable Jobs 时钟达到指定时间。用于单条消息的强类型 <xref:Orleans.Durable.Messaging.OutboxExtensions.Send*> 辅助方法和 <xref:Orleans.Durable.Messaging.MessageType`1.Create*> 接受可选的 `deliverAt` 参数。应根据 Durable Jobs 时钟计算 `deliverAt`；Grain 可以注入键为 `DurableJobTimeProviderNames.DurableJobs` 的 <xref:System.TimeProvider> 服务来获得该时钟。发件箱立即投递消息；接收方接受消息、去重并写入日志，到期后处理器才执行。如果 Grain 为自身安排消息，则接收记录在发送时的日志写入中提交。计划消息在处理前始终保留在接收方的日志和快照中，从被接受到完成期间均计入 `MaxCapacity`。

<a id="keep-payloads-in-application-state"></a>

### 将有效载荷保存在应用状态中

默认情况下，发送的有效载荷由发件箱写入日志，接收的有效载荷由收件箱写入日志。如果应用自身的日志化状态已经持有该有效载荷，可以使用发件箱消息源或收件箱消息接收端，让该状态成为日志中唯一的有效载荷副本：

- <xref:Orleans.Durable.Messaging.IOutboxMessageSource> 通过 <xref:Orleans.Durable.Messaging.IOutbox.Register*> 注册，为通过 <xref:Orleans.Durable.Messaging.IOutbox.Send*> 按 ID 发送的消息提供内容。发件箱记录消息 ID 和投递状态，并应用普通消息的所有投递保证。它通过 <xref:Orleans.Durable.Messaging.IOutboxMessageSource.TryGetMessage*> 获取完整消息，以验证发送请求，并在每次投递尝试前再次获取；消息源在负责提供该消息期间必须返回相同的消息，对不属于自己的消息返回 `false`。第一个返回消息的源拥有该消息。
  - **撤回。** <xref:Orleans.Durable.Messaging.IOutbox.Withdraw*> 移除待处理消息，例如应用程序已判定超时的消息。应在消息源释放有效载荷的同一次日志写入中暂存撤回操作。发件箱不再尝试投递，也不调用 `OnRetired`；但已经在进行的尝试仍可能成功投递。
  - **终结。** 消息终结时，发件箱在暂存移除操作的同时调用 <xref:Orleans.Durable.Messaging.IOutboxMessageSource.OnRetired*>，使消息源释放有效载荷、应用终结结果及后续工作在同一次日志写入中提交。<xref:Orleans.Durable.Messaging.OutboxMessageRetirement> 包含三种情况：接收方接受消息或报告重复时为 `OutboxMessageRetirement.Delivered`；接收方收件箱接收端以持久化方式拒绝时为 `OutboxMessageRetirement.Rejected`，包含接收端的 `Reason` 和应用定义的 `Code`；`OutboxMessageRetirement.DeadLettered` 表示发件箱因重试超时、消息源失败或收到 `Expired` 响应而将消息移入死信集合，包含其 `Reason`。`DeadLettered` 表示先前是否被接收方接受仍未知。`OutboxMessageRetirement` 和 `DeliveryResult` 是 C# 联合类型，各分支为类，因此应使用类型模式与属性模式匹配，例如 `retirement is OutboxMessageRetirement.Rejected { Code: var code }`。发件箱死信会先获得完整消息的独立副本，因此消息源可在 `OnRetired` 中释放有效载荷。可在该回调中暂存发给自身的后续消息，经由收件箱的持久化唤醒和处理器路径执行应用恢复工作。提交之前失败会恢复待处理的发件箱条目，并再次调用回调；已提交的终结结果会与后续工作一起恢复。回调异常使激活失败，从而丢弃其未提交的变更。外部 I/O 应放在后续处理器的准备阶段执行。
  - **失败。** 发送后，如果拥有该消息的消息源抛出异常、返回无效消息或对自身消息返回 `false`，发件箱仅将该条目移入死信集合，并在同一次日志写入中以 `DeadLettered` 终结该消息；Grain 继续处理其他消息。此死信仅携带消息 ID，有效载荷为空，接收方为默认值，原因记录此次失败。恢复出的条目若没有任何已注册的消息源提供，也以同样方式移入死信集合，但没有所有者可供通知。

  在 `Send` 期间，消息源异常或无效消息会传播给调用方，且不暂存任何内容。再次发送待处理 ID 会合并；如果该 ID 已作为普通消息处于待处理状态，则抛出 <xref:System.InvalidOperationException>。由消息源提供的消息不能发给发送 Grain 自身。
- <xref:Orleans.Durable.Messaging.IInboxMessageSink> 通过 <xref:Orleans.Durable.Messaging.IInbox.Register*> 注册，在收件箱接受每条已去重的消息之前接收该消息，包括计划消息和 Grain 发给自身的消息。接管消息的接收端从 <xref:Orleans.Durable.Messaging.IInboxMessageSink.TryAccept*> 返回 `true`，并将其 `out` <xref:Orleans.Durable.Messaging.DeliveryResult> 参数设置为以下某种情况；拒绝接管消息的接收端返回 `false` 并将结果设为 `default`：
  - `DeliveryResult.Accepted.Instance`：接收端已暂存消息的效果。收件箱仅在同一次写入中记录已处理记录，并在写入完成后返回结果。
  - `DeliveryResult.Duplicate.Instance`：接收端已持有该消息。
  - `DeliveryResult.Backpressured.Instance`：接收端容量已满，发送方稍后重试。
  - `DeadLettered`：使用 `new DeliveryResult.DeadLettered(reason, code)` 创建，是唯一的永久拒绝结果。收件箱记录消息已处理，保存原因与可选的应用定义代码（例如 `orders/duplicate-order`），并返回给发送方，发送方的消息源在 `OutboxMessageRetirement.Rejected` 终结结果中收到它们。重复消息获得相同拒绝结果。

  接收端异常或其他任何结果都意味着结果未知：收件箱不暂存任何内容，投递失败，发送方的发件箱重试直至 `OutboxRetryTimeout`。当接收端抛出异常或报告 `Backpressured` 时，Grain 发给自身的消息的 `Send` 会失败。由接收端拥有的消息不占用收件箱容量，由接收端遵守其 `DeliverAt` 时间。所有接收端都不接管的消息走普通收件箱路径。
- 通过 <xref:Orleans.Durable.Messaging.IInbox.RegisterWakeup*> 注册的唤醒回调报告某个组件（如接收端）保存在 Grain 日志中的持久化工作。回调返回已停用 Grain 必须在多久之后唤醒以继续工作；没有待处理工作时返回 `null`。消息传递唤醒机制会重新激活 Grain，直到这些工作全部处理完毕；组件在激活时继续其工作。

消息源、接收端以及唤醒回调应在构造或激活初始化期间、日志恢复之前注册。如果恢复出一条由消息源提供的消息，却没有注册任何消息源，激活就会失败。收件箱先对已跟踪的标识去重，再交给接收端；对于发送时间不晚于持久化接纳时间下限且未被跟踪的消息，会在接收端产生任何效果之前将其判定为过期。

<a id="commit-and-delivery-guarantees"></a>

## 提交与投递保证

- <xref:Orleans.Durable.Messaging.IOutbox.Send*> 在 Grain 的日志化状态中暂存出站消息。下一次日志写入将其与 Grain 的其他暂存变更一起持久化。发件箱只有在该写入完成之后才投递消息。
- 发给发送 Grain 自身的消息从不进入发件箱。`Send` 将接受记录暂存在该 Grain 自身的收件箱中，因此同一次日志写入会将接受记录和 Grain 的其他暂存变更一并持久化；收件箱只在写入完成后处理消息。如果写入在收集暂存变更之前失败（例如安排唤醒失败），接受记录仍与其他变更一起保持暂存，重试写入即可将其持久化。如果存储写入失败，则接受记录与其他暂存变更一并丢弃，下次激活恢复已提交的状态。
- 使用同一 ID 重复发送等价消息（目标、主题、有效载荷和 `DeliverAt` 时间相同）会保留原消息。使用同一 ID 发送不同内容将抛出 <xref:System.InvalidOperationException>。
- 接收方收件箱在返回 `DeliveryResult.Accepted` 之前，会将已接受的消息或由收件箱接收端接受的消息处理记录写入日志；只有在接受记录持久化之后才处理消息。
- 处理器的业务变更、出站消息及收件箱完成记录一并暂存，并通过一次日志写入持久化。
- 传输保证为**至少一次**。如果接收方接受消息后、发件箱记录投递之前，发送方的激活实例丢失，发件箱会使用原始 `SentAt` 再次发送。通过保留的完成记录和持久化接纳时间下限，每次逻辑发送的效果仅应用一次；同一条消息的处理器可能执行多次，因此准备阶段必须允许重复执行。
- 接收方按精确的 `MessageId` 去重。重复投递待处理消息时，返回 `DeliveryResult.Duplicate` 的条件是主题、有效载荷和 `DeliverAt` 时间均相同；内容不同时投递失败。重复投递已完成的消息时，只要完成记录仍被保留，就返回 `Duplicate`；如果此前被收件箱接收端拒绝，则返回原始 `DeadLettered` 的原因和代码。无论重复消息携带何种发送时间，两种记录都保留收件箱首次接受时的 `SentAt`。完成记录通过压实移除后，发送时间不晚于持久化接纳时间下限的未跟踪消息会收到 `Expired`，此时无法确定此前是否已接受。
- 带有 <xref:Orleans.Durable.Messaging.Envelope.DeliverAt> 的消息会立即投递并被接受；只有在接受记录已持久化且 Durable Jobs 时钟达到 `DeliverAt` 后才进行处理。Grain 发送给自身的消息在发送时的日志写入中被接受，因此 Grain 可以在一次写入中将为自身安排的持久化工作与其他日志化变更一并提交。
- 投递**不保证顺序**。需要顺序的应用程序应携带序列号，并按应用定义的顺序应用消息。

<a id="execution-model"></a>

## 执行模型

每个激活实例运行一个消息泵，在激活完成时启动。消息泵先处理就绪的收件箱消息，再投递就绪的发件箱消息，只要仍有工作便重复执行。消息泵的所有工作（包括处理器代码）均运行于激活实例的单线程调度器上。正在运行的激活实例会在到期工作持久化后立即处理它：日志写入完成和投递结束会直接向消息泵发出信号。

每一轮中，收件箱最多逐条处理 <xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.InboxBatchSize> 条已持久化且到期的消息，然后为该批次写入一次日志。发件箱为该 Grain 保持最多 <xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.OutboxBatchSize> 次正在进行的投递尝试。每次尝试独立完成并释放一个槽位，供下一条到期消息使用，因此缓慢或无响应的接收方只占一个槽位，不会阻塞发给其他接收方的消息。发件箱从不保存发给该 Grain 自身的消息；处理器发送此类消息时，会在本批次的日志写入中暂存其接受记录，并在后续轮次处理。

通过 <xref:Orleans.Durable.Messaging.IMessagingExtension.DeliverAsync*> 投递到 Grain 的消息会与 Grain 的其他请求交错执行。投递操作先验证信封、识别已记录的结果并验证新存储的发送时间，然后同步暂存消息的接受记录，再等待日志写入完成；并发投递共享日志写入。收件箱消息接收端在投递过程中同步运行，因此与处理器一样，它可能在某个请求因 `await` 而挂起时运行。

每个 Grain 有一个名为 `orleans.messaging.pump` 的持久化作业，为运行中的消息泵提供崩溃恢复保障。当任一队列或已注册的唤醒回调持有该激活实例可以处理的工作时，它会在最早的重试时间、`DeliverAt` 时间或回调指定的延迟之后唤醒 Grain。恢复时，已到期的工作约在一秒内继续执行。只有在该时间余量内不存在到期的已跟踪作业时，日志写入才安排作业，因此繁忙的 Grain 不会在每次写入时都执行 Durable Jobs 操作。激活失败时，Durable Jobs 会根据自身时钟以有上限的退避间隔无限重试消息泵作业；`AddMessaging` 仅为消息泵作业配置此重试策略。每次激活都根据恢复状态中的实际到期时间重新设置唤醒，确保已提交工作始终具有唤醒安排。

Grain 状态是权威数据源。每一轮都根据日志记录的消息、重试时间和 `DeliverAt` 时间决定工作，因此提前、延迟或重复唤醒都不会造成问题。

消息泵在处理队列消息的一轮执行期间以及发件箱投递尚未结束时，保持激活实例存活。以后才到期（`DeliverAt` 时间或重试时间）的消息不会阻止激活实例被回收；持久化作业会在到期时重新激活 Grain。

<a id="interleaving"></a>

### 交错执行

处理器作为 Grain 请求之外的激活实例工作运行，因此无论 Grain 是否可重入，处理器代码在每次 `await` 时都可以与 Grain 调用交错执行。投递及收件箱消息接收端也以相同方式交错执行。处理器、接收端和 Grain 方法的设计必须确保在这种交错执行下仍然正确。无状态工作者 Grain 在激活时会被拒绝。

若要在不可重入的 Grain 上独占执行处理器的提交阶段，可通过 <xref:Orleans.GrainBaseExtensions.RunRequestAsync*> 将其作为 Grain 请求运行。该请求等待当前 Grain 调用完成，并在执行期间排除其他调用：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_exclusive_commit" language="csharp":::

<a id="handler-outcomes"></a>

## 处理器结果

处理器上下文 <xref:Orleans.Durable.Messaging.IInboxHandlerContext> 提供收到的 <xref:Orleans.Durable.Messaging.IInboxHandlerContext.Message> 以及两种终结操作：

- <xref:Orleans.Durable.Messaging.IInboxHandlerContext.Complete*> 暂存消息的移除及其完成记录。
- <xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*> 将消息移入收件箱死信集合，附带非空白的原因，并将其 ID 记录为已处理。

每个成功执行的处理器恰好调用其中一种操作。重复调用 `Complete()`，或使用相同原因重复调用 `Fail`，会合并为同一个结果。上下文仅属于一次处理尝试；处理器返回后再使用该上下文会被拒绝。

终结操作会立即暂存变更，激活实例的任何一次日志写入都可能收集这些变更。应将每个处理器划分为两个阶段：

1. **准备。** 使用局部值执行异步 I/O、验证、解码以及可能失败的计算。在进入最后阶段之前检查取消请求。
2. **提交。** 在单个同步代码块中，第一次变更之后不再执行 `await`；发送强类型出站消息、应用业务更新、调用 `Complete()` 或 `Fail(reason)`，然后返回。

这种顺序确保业务变更、出站消息与完成记录在同一次日志写入中持久化。收件箱在处理器返回后持久化该批次。

根据失败是否有可能在以后成功，采取相应处理方式：

- **业务拒绝。** 发送强类型拒绝回复并调用 `Complete()`。
- **永久无效的输入**，例如永远不可能变为有效的命令。调用 `Fail(reason)`；如果协议规定了回复，也可发送强类型拒绝回复并调用 `Complete()`。消息立即离开收件箱，释放容量槽位。
- **暂时性故障**，例如依赖项不可用。抛出异常。Grain 停用，恢复后消息按退避策略重试，直至 `InboxRetryTimeout`；期间继续占用容量槽位。

其余情形对应以下结果：

| 处理器结果 | 处理结果 |
| --- | --- |
| 抛出异常，或未执行终结操作便返回 | 结果未知。Grain 停用；从日志恢复时丢弃本次尝试的暂存变更，并计入此次失败尝试。消息按退避策略重试至 `InboxRetryTimeout`，随后移入死信集合。 |
| 终结操作后抛出异常 | 记录异常，已暂存的结果仍会持久化。 |
| 相互冲突的终结操作 | 违反契约：消息传递停止，Grain 停用。 |

一次尝试的暂存变更与 Grain 共用待写入的日志，因此任何结果未知的尝试都使激活失败，而不是在同一次写入中将重试记录与这些变更一起提交。

<a id="backpressure-retries-and-dead-letters"></a>

## 背压、重试与死信

对于投递，无论 `SentAt` 取何值，即使接收方时钟回拨，已跟踪消息均获得其记录的响应。发送时间不晚于接纳时间下限的未跟踪消息获得 `Expired`。对于下限以上的未跟踪消息，收件箱将 `SentAt` 的上限设为其 Durable Jobs 时钟加 `DeduplicationWindow`；等于上限时允许接纳，超过时抛出 <xref:System.ArgumentOutOfRangeException>，既不暂存变更，也不交给接收端。发件箱将该异常视为一次失败尝试，保留原始 `SentAt` 并重试，直至 `SentAt + OutboxRetryTimeout`；耗尽重试机会后，以 `DeadLettered` 终结此次发送，之前是否成功接受仍未知。直接在发送 Grain 自身收件箱暂存的消息使用本地暂存时间，或时钟回拨后第一个高于接纳时间下限的时间，并走本地发送路径。

收件箱持有 <xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.MaxCapacity> 条待处理消息时，以 `Backpressured` 响应新消息，发送方稍后重试。每条待处理消息（包括等待 `DeliverAt` 时间的计划消息）从接受到完成均计入此限制。由于 `Send` 是同步的，Grain 发给自身的消息不受容量限制而直接接纳，但在完成之前仍计入容量；同样，即使尚未注册处理器，Grain 的收件箱也会接受这些消息，待处理器注册后再处理。

恢复过程将处理器的失败尝试计入后，收件箱按 <xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.RetryInitialInterval> 乘以最大为 64 的指数退避因子所得的时间间隔安排重试。从首次失败起，重试持续到 <xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.InboxRetryTimeout>（默认 6 小时），因此可以跨越服务中断、部署修复等暂时性故障。超时时执行最后一次尝试；若仍失败，消息进入收件箱死信集合，其 ID 被记录为已处理。调用 `Fail(reason)` 可立即将消息移入死信集合。

接收方报告 `Accepted`、`Duplicate` 或 `DeadLettered` 时，发件箱移除该消息。其他任何结果（包括发送方无法识别的状态，例如来自较新版本接收方的状态）均表示结果未知，发件箱按相同退避策略安排持久化重试，其他消息继续处理：

- `Backpressured` 在原始绝对投递时间预算内重试。
- `HandlerNotFound` 会记录错误，重试为部署注册处理器留出时间。
- 其他失败（例如基础设施中断期间调用失败且没有响应）采用与 `HandlerNotFound` 相同的重试方式。

<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.OutboxRetryTimeout> 从 <xref:Orleans.Durable.Messaging.InboxMessage.SentAt> 记录的暂存时间开始计算。每次尝试及恢复都保留该时间；重试到期时间为 `SentAt + OutboxRetryTimeout`。超时后不再启动尝试；发件箱随后将消息移入死信集合，并记录重试超时的原因。每次尝试携带原始 `SentAt`。接收方首先检查待处理与已完成标识。对于未跟踪标识，如果发送时间不晚于其单调不减的接纳时间下限，会在任何接收端或处理器运行前返回 `DeliveryResult.Expired`。发件箱将该消息保留为死信并以 `DeadLettered` 终结此次发送：此前可能已成功接受，只是确认响应丢失。再次授权发送之前，应通过消息源回调暂存持久化的应用对账工作。Durable Jobs 时钟决定重试调度与候选下限的推进；记录在日志中的发送时间和下限确保时钟变化时仍保留接纳判定。`OutboxRetryTimeout` 必须小于 `DeduplicationWindow`。

通过 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics> 检查死信，并使用 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveInboxDeadLetter*> 或 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveOutboxDeadLetter*> 移除已处理记录。移除操作在下一次日志写入时持久化。保留配置为两个集合限定容量；参见[容量、重试和保留策略](messaging-operations.md#capacity-retries-and-retention)。

快照将接纳时间下限推进到当前下限与观测到的 Durable Jobs 时间减去 `DeduplicationWindow` 两者中的较晚值，并对已接受的 `SentAt` 不晚于该下限的完成记录进行压实。下限和压实后的历史记录以原子方式持久化；在收到确认之前，内存中的记录仍会保留。

<a id="state-recovery-and-deletion"></a>

## 状态、恢复和删除

收件箱和发件箱各自是一个基于事件溯源的日志状态机，分别注册为 `orleans.messaging.inbox` 和 `orleans.messaging.outbox`。它们使用 Grain 配置的日志格式，支持 JSON 和 Orleans 二进制格式。

新的激活实例重放日志，根据已记录的尝试次数与重试时间恢复待处理工作。消息传递失败时（例如暂存或持久化失败），会拒绝该激活实例随后所有的日志写入，Grain 停用；下一次激活从日志恢复已提交的结果。

Grain 通过 `DeleteStateAsync` 删除自己的日志后，收件箱和发件箱从空状态开始，并继续接受和发送消息。

<a id="deployment-requirements"></a>

## 部署要求

配置 Durable Jobs 存储和 Journaling 存储，然后在 Silo 构建器或服务集合上通过 <xref:Orleans.Hosting.MessagingExtensions.AddMessaging*> 启用消息传递。可选的回调用于配置 <xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions>，这些选项会在启动时验证。注册过程会添加 Durable Jobs；如果应用已注册自己的 <xref:System.TimeProvider>，则使用该服务。

开发时，配置内存存储并启用消息传递：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_registration" language="csharp":::

Grain 通常通过构造函数注入，在构造或激活初始化期间解析 <xref:Orleans.Durable.Messaging.IInbox> 或 <xref:Orleans.Durable.Messaging.IOutbox> 来使用消息传递。消息通过 Grain 的消息传递扩展抵达其收件箱；日志恢复会将消息传递状态与 Grain 的其他日志化状态一起恢复。

以下 Grain 在激活期间注册强类型处理器。没有文本的通知永远无法变为有效输入，因此处理器通过 `Fail(reason)` 将其移入死信集合。对于有效通知，处理器计算可选回复并发送，更新计数，且在返回之前完成消息：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_grain" language="csharp":::

普通 Grain 方法使用稳定的命令 ID 暂存强类型发送，并通过一次日志写入将其持久化。客户端使用相同命令 ID 重试会暂存同一条消息：消息待处理期间，发件箱只保留一个条目；投递后，接收方将其识别为重复消息：

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_send" language="csharp":::

多 Silo 部署使用共享的生产级存储。内存 Durable Jobs 存储和日志存储适用于开发与测试。

监控收件箱和发件箱深度、重试与死信结果以及待处理消息的存续时间。根据工作负载的可用性要求和历史记录保留成本，确定接纳时间下限滞后量及重试预算。指标名称与诊断方式参见[消息传递运维](messaging-operations.md)。
