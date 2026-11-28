<a id="microsoft-orleans-messaging"></a>

# Microsoft Orleans 消息传递

`Microsoft.Orleans.Durable` 中的消息传递功能提供作用域为 Grain 的持久化收件箱和发件箱，
它们基于 Orleans Journaling 和 Durable Jobs 构建。Grain 通过将消息暂存到
发件箱来发送消息；消息随 Grain 的其他日志化状态一起持久化，并
投递到接收方的收件箱，由收件箱至少处理一次，并
按消息 ID 去重。Grain 发给自身的消息会跳过发件箱：其自身的收件箱
在同一次日志写入中接受该消息。

<a id="setup"></a>

## 设置

配置 Journaling 和 Durable Jobs 存储，然后在 Silo
构建器或服务集合上调用 `AddMessaging`。可选的 `DurableMessagingOptions` 回调用于配置
容量、批大小、重试、去重和死信保留；这些选项会在
启动时验证。通过 `AddMessageType<T>(subject)` 注册具有类型的主题。

Grain 通过解析 `IInbox` 接收消息，通过解析 `IOutbox` 发送消息，
解析发生在构造或激活初始化期间，通常通过构造函数注入完成，以便两者
随 Grain 的日志一起恢复。解析 `IOutbox` 也会启用收件箱，后者
接受 Grain 发给自身的消息。消息通过 Grain 的
消息传递扩展投递到收件箱。无状态工作者 Grain 不受支持；支持可重入和允许交错执行的
Grain，Grain 代码负责保证调用
交错执行时的正确性。

<a id="messages"></a>

## 消息

`Envelope` 携带以下内容：

- `MessageId`：应用程序提供的 `HierarchicalKey`，封装在 `MessageId` 中，最多
  1,024 个 UTF-8 字节。它标识接收方的一条命令。
- `Payload`：不透明字节数据；允许空有效载荷。
- `Subject`：可选的按序数规则比较的名称，最多 256 个 UTF-8 字节，用于选择有类型的
  处理程序。
- `Headers`：可选的具名字节数组，键必须非空，值不得为 null。
- `DeliverAt`：基于 Durable Jobs 时钟的可选时间。接收方立即接受
  消息，待该时钟到达 `DeliverAt` 时处理。带有
  `DeliverAt` 的消息从被接受到完成期间都计入 `MaxCapacity`。

`OutboxMessage` 将消息信封与接收方配对。`InboxMessage` 携带相同的
信封及接收方所见的逻辑发送的 `SentAt` 时间。请保持已发布的有效载荷
和标头数组不变。

`MessageId` 封装 `HierarchicalKey`。使用 `new MessageId(key)` 或
`MessageId.Create(segments)` 创建，通过 `Key` 读取键。`HierarchicalKey` 封装
规范化的转义路径，两种类型的相等性及哈希计算均按序数规则比较该
路径。使用 `HierarchicalKey.Create`、`CreateChildKey` 和 `Append` 构造键，
使用 `GetParent`、`IsParentOf`、`IsChildOf`、`IsAncestorOf` 及路径段枚举器
遍历键的层次结构。

<a id="handle-messages"></a>

## 处理消息

`IInbox.Register(IInboxHandler)` 添加处理程序。每条消息交给第一个
已注册且 `CanHandle(InboxMessage)` 返回 `true` 的处理程序。选择过程只读取
消息；选中的处理程序在 `HandleAsync` 中完成处理。处理程序可在
任何时候注册；在构造或激活期间注册，可使它们
处理已恢复的消息。

`InboxExtensions.Register` 将 `MessageType<T>` 主题绑定到有类型的方法：

```csharp
inbox.Register(reserve, HandleReserve);
inbox.Register(reserve, this, static (request, grain, context) => grain.HandleReserve(request, context));
```

各重载接受同步或异步委托，并可选择传入 `TArg`
参数，以支持静态且不产生分配的委托。

处理程序通过 `IInboxHandlerContext.Complete()` 或
`Fail(reason)` 完成每条消息：

- `Complete()` 暂存消息的移除操作和完成记录。
- `Fail(reason)` 将消息移入收件箱死信，并将其 ID 记录为
  已处理。
- 处理程序抛出异常，或未调用上述任一方法便返回，均导致结果未知。
  本次尝试暂存的变更与 Grain 尚未提交的日志写入共享同一边界，因此 Grain
  停用，从日志恢复时丢弃这些变更。
- 因上述故障或崩溃导致激活实例停止，且未记录尝试结果时，
  恢复过程会为每条到期消息计入一次尝试。消息
  采用指数退避重试，直至自首次失败起达到 `InboxRetryTimeout`（6 小时），
  随后移入死信。当存在待处理消息时，正常停用会将正常
  停止记录写入日志，因此不计入尝试。崩溃可能使尚未运行的消息被计入一次尝试，
  因此每次崩溃最多多计一次尝试；单纯计数
  绝不会使尚未开始重试的消息转入死信。
- 在 `Complete()` 之后调用 `Fail(reason)`，或在调用 `Fail` 后使用不同原因再次调用，会使
  Grain 停用。在 `Fail(reason)` 之后调用 `Complete()` 则保留死信。

收件箱在一次日志写入中持久化每批消息的处理结果、业务变更和传出消息。
先用局部变量执行异步和可能失败的操作，
然后在一个同步代码块中执行发送、应用状态变更及完成操作，在首次变更之后不得使用 await
等待其他操作。
<a id="send-messages"></a>

## 发送消息

`IOutbox.Send(OutboxMessage)` 暂存消息。`OutboxExtensions` 提供有类型的辅助方法：

- `Send(type, messageId, receiver, body, deliverAt)` 编码并暂存一条
  消息，可选指定调度时间；批量重载可暂存多条消息。
- `SendReply(type, context, destination, body)` 暂存回复，回复 ID 是
  收到的消息 ID 的 `result` 子键，发送到应用程序
  在请求中携带的目标。

`MessageType<T>.Create` 和 `OutboxMessage.ToInboxMessage(sentAt)` 构造用于直接
投递到收件箱的消息。以待处理 ID 发送等价内容（目标、主题、有效载荷和
`DeliverAt`）会与原消息合并；内容不同时
抛出 `InvalidOperationException`。

当接收方就是发送消息的 Grain 时，`Send` 会将消息的接受操作暂存到
Grain 自身的收件箱而非发件箱，并应用收件箱的标识规则：
如果某 ID 对应的待处理内容等价，或已有保留的完成记录，
则视为重复；待处理 ID 对应内容不同时抛出
`InvalidOperationException`。接受操作在调用方下一次日志
写入中与其他暂存状态原子持久化；收件箱在该写入
完成后处理消息。由于 `Send` 是同步的，Grain 的收件箱不受
`MaxCapacity` 和处理程序注册情况限制，直接接纳该消息；未指定调度时间的
发给自身的消息在完成前仍占用供其他发送方使用的容量，且
需要等待处理程序才能处理。

<a id="journal-payloads-once"></a>

## 有效载荷仅写入日志一次

当有效载荷已存在于 Grain 自身的日志化状态中时，发件箱消息源或
收件箱接收器可使该状态成为其唯一的日志副本。请在
构造或激活初始化期间、日志恢复前注册两者。

- `IOutbox.Register(IOutboxMessageSource)` 添加消息源，
  `IOutbox.Send(messageId)` 暂存带有消息 ID 和投递
  状态的引用条目。第一个通过 `TryGetMessage` 返回该消息的消息源拥有该消息；
  发件箱调用 `TryGetMessage` 验证发送，并在每次投递尝试前再次调用；
  消息源在提供该消息期间始终返回相同的 `OutboxMessage`。上述所有
  投递规则均适用。对于不属于该消息源的消息，`TryGetMessage` 返回 `false`。
  消息源通过
  `IOutbox.Withdraw(messageId)` 撤回待处理消息，该操作与释放其
  有效载荷在同一次日志写入中暂存：发件箱不再尝试投递，也不会调用 `OnRetired`，但
  已在进行中的尝试仍可能将其投递。当消息退役时，发件箱
  在暂存移除操作的同时，以 `OutboxMessageRetirement` 调用 `OnRetired`：`Delivered`（接收方
  返回 `Accepted` 或 `Duplicate`）、`Rejected`（接收方的接收器拒绝消息，携带
  接收器的 `Reason` 和 `Code`）或 `DeadLettered`（发件箱将消息转入死信，
  携带其 `Reason`），因此消息源的变更在
  同一次日志写入中提交。发件箱死信保存已物化
  消息的独立副本，因此消息源可在 `OnRetired` 中释放其有效载荷。发送之后，若
  消息源抛出异常、返回无效消息（例如 ID 不同的消息）或
  不再提供该消息，则仅将该条目转入死信，与其 `DeadLettered` 退役在同一次日志
  写入中完成；Grain 继续处理其余
  消息。该死信仅携带消息 ID、空有效载荷和
  默认接收方，其原因记录失败情况。已恢复的条目若没有任何
  消息源提供，也会以相同方式转入死信，但没有所有者可通知。再次发送
  待处理引用会合并；如果 ID 已作为普通消息待处理，或没有
  消息源提供该消息，则抛出 `InvalidOperationException`；如果消息源在
  `Send` 期间抛出异常或返回无效消息，则异常向外传播，不暂存任何内容。
  发往发送方 Grain 自身的消息源消息会抛出 `ArgumentException`。如果
  恢复引用时没有注册任何消息源，则激活失败。
  `OnRetired` 在当前激活实例中针对每个条目执行一次。如果激活实例在
  退役写入提交前失败，恢复过程会恢复待处理条目和
  消息源先前的日志化状态；退役已恢复的条目时会再次调用
  回调。请将需要持久化的回调效果与退役暂存在同一次日志写入中；
  对该写入之外的效果保证跨重复调用的幂等性。
- `IInbox.Register(IInboxMessageSink)` 添加消息接收器。验证及
  去重后，收件箱依次将每条消息（包括发给自身的消息和指定调度时间的
  消息）交给接收器尝试接受。第一个 `TryAccept` 返回 `true` 的接收器
  报告相应的 `DeliveryResult` 情况：
  - `Accepted`：接收器已将消息暂存到自己的状态。收件箱在同一次写入中
    仅将已处理记录写入日志，并在该记录完成
    持久化后返回 `Accepted`。
  - `Duplicate`：接收器已持有该消息。
  - `Backpressured`：接收器已达到容量上限，因此发送方重试。发给自身的
    消息则使其 `Send` 调用失败。
  - `DeadLettered`：接收器拒绝消息并提供原因及可选的
    应用程序定义的 `Code`，例如 `orders/duplicate-order`；发送方的
    消息源在 `Rejected` 退役时收到两者。已处理记录保留
    原因和代码，因此在保留期内的重复消息（例如首次响应
    丢失后的重试）会以相同方式遭到拒绝。这是唯一的永久拒绝情况。

  接收器抛出异常或返回其他任何情况时，结果未知：收件箱不暂存
  任何内容，投递失败，因此发送方发件箱采用退避策略重试，
  直至 `OutboxRetryTimeout`；发给自身的消息则使其 `Send` 失败。
  由接收器拥有的消息永不占用容量，也没有收件箱重试生命周期；
  接收器遵循其 `DeliverAt`。未被接收器接纳的消息在
  `DeliverAt` 到达后走处理程序路径。在接收器运行前，保留的完成记录会进行去重；
  持久化的准入下限会使未跟踪消息的旧发送时间过期。
- `IInbox.RegisterWakeup(getWakeupDelay)` 为在 Grain 日志中持有持久化工作的组件（例如
  接收器）添加回调。该回调返回已停用的
  Grain 必须在多久后唤醒才能恢复该工作；无待处理工作时返回 `null`。
  每次仍有待处理工作的日志写入都会在实际存储前调度消息传递唤醒，
  唤醒机制将重新激活 Grain，直到工作全部完成。
  组件在激活时恢复工作。

<a id="delivery-and-processing"></a>

## 投递和处理

暂存的传出消息仅在发送方的日志写入
完成后投递。收件箱已接受的消息仅在接收方的日志
写入完成后处理；对于带有 `DeliverAt` 的消息，还须等到 Durable Jobs 时钟
到达该时间。定时消息立即投递和接受，Grain 发送给自身的
定时消息在发送操作的写入中被接受；每条消息
均保留在接收方日志中直至到期。每个接收方返回一种
`DeliveryResult` 情况：

- `Accepted`：消息已在收件箱中持久化，或者对于由收件箱接收器
  接受的消息，消息已持久化在接收器状态中，其已处理记录也一并持久化。
- `Duplicate`：消息 ID 正处于待处理状态或具有保留的已处理记录。如果消息
  曾被接收器拒绝，则其重复消息收到相同的拒绝结果。
- `Backpressured`：收件箱持有 `MaxCapacity` 条待处理消息。收件箱接纳
  Grain 发送给自身的消息时不受 `MaxCapacity` 限制。
- `HandlerNotFound`：接收方未注册任何处理程序。
- `DeadLettered`：收件箱接收器已持久化拒绝该消息，并提供原因及
  可选的 `Code`。
- `Expired`：消息的发送时间早于接收方去重窗口，且
  接收方未保留该消息的处理结果。本次尝试不暂存任何消息效果；
  此前的尝试可能已被接受，因此投递结果未知。

`Accepted`、`Duplicate` 和 `DeadLettered` 会移除发件箱条目。`Expired`
保留发送方死信并使条目退役，其先前结果仍然未知。
其他任何结果均会调度采用指数退避的持久化重试，
发件箱最多同时保持 `OutboxBatchSize` 次正在进行的尝试，因此速度慢的接收方
只会延迟发送给它的消息。背压占用同一个发送操作的绝对重试时间预算。
发件箱会将 `HandlerNotFound` 记录为错误。其他故障，包括
服务中断期间未获得响应的失败调用，会在自消息发送起经过
`OutboxRetryTimeout`（6 小时）后将消息转入死信。重试截止时间及
退避均遵循 Durable Jobs 时钟。投递顺序未作保证。

每个激活实例在其调度器上运行一个消息泵，用于处理收件箱批次
并投递发件箱消息。当任一队列或已注册的唤醒回调持有工作时，每个 Grain 有一个持久化作业
唤醒消息泵。日志化状态决定哪些
消息已到期，因此每次唤醒恰好处理已就绪的工作。活动的激活实例
在到期工作完成持久化后立即处理；该作业负责处理激活失败的情况，因此
已到期工作将在恢复后约一秒内继续。激活失败期间，作业以退避策略
无限重试；每次激活都基于恢复的状态重新设置它，
因此已提交的工作始终拥有唤醒机制。

<a id="state"></a>

## 状态

收件箱和发件箱是采用事件溯源方式实现的日志状态，名称为
`orleans.messaging.inbox` 和 `orleans.messaging.outbox`，支持 JSON 和
Orleans 二进制日志格式。它们与 Grain 的其他日志化
状态一起持久化。删除 Grain 状态后，两者从空状态开始并继续工作。

已完成的消息 ID 在从接受时的 `SentAt` 起经过 `DeduplicationWindow`
之后符合移除条件。已确认的快照和恢复过程在
准入下限覆盖其发送时间后回收符合条件的记录。死信按每个队列的
`DeadLetterRetentionPeriod` 和 `MaxRetainedDeadLetters` 上限保留。
死信时间戳及保留策略遵循 Grain 的 `TimeProvider`。

<a id="diagnostics"></a>

## 诊断

`IMessagingDiagnostics` 公开 `InboxDeadLetters` 和 `OutboxDeadLetters`，每条
记录包含消息、原因、尝试次数及转入死信的时间。
`RemoveInboxDeadLetter` 和 `RemoveOutboxDeadLetter` 移除记录，并在
Grain 的下一次日志写入中持久化该移除操作。

`Microsoft.Orleans` 度量仪发布接收、处理、发送和
投递消息的计数器，以 Grain 类型和状态作为标记；还发布处理和
投递时长的直方图，以及 `orleans-messaging-inbox-depth` 和
`orleans-messaging-outbox-depth` 两个用于待处理工作的增减计数器。Grain
发给自身的消息计为已发送及收件箱已接收，但绝不会计入
发件箱投递或深度指标。引用发送计为已发送；如果引用条目的
消息源无法提供消息，则其投递计为 `source_error`。收件箱
接收器接受的消息计为已接收 `accepted`、已处理 `success`；
接收器拒绝的消息则计为已接收及已处理 `dead_lettered`。

`MessagingEvents` 是 `Orleans.Messaging` 诊断侦听器，在执行每次状态转换的激活实例上
同步报告该转换：`InboxMessageReceiving` 在收件箱
暂存已投递消息之前触发，`OutboxRetryScheduled` 在重新调度失败投递时触发，
`OutboxMessageRetired` 在消息退役时触发。每个事件均携带激活实例的
`IGrainContext`。测试将这些事件用作同步屏障，并通过在
`InboxMessageReceiving` 观察者中抛出异常注入投递故障，使尝试像接收器抛出异常时一样失败。

`DurableMessagingOptions` 配置容量、批大小、重试、去重
及死信保留。

<a id="deduplication-window"></a>

## 去重窗口

发件箱将每次发送的 `SentAt` 时间写入日志；每次投递尝试及恢复
均复用该时间。`OutboxRetryTimeout` 默认为六小时，限制了从该时间起
的投递尝试时间，包括接收方施加背压的时间。

收件箱将每条已处理记录从接受时的 `SentAt` 起保留 `DeduplicationWindow`，默认为七天，
之后使其符合移除条件。在移除记录之前，
收件箱将准入下限写入日志：其时钟减去 `DeduplicationWindow`，该下限
只会向前推进。首先根据待处理及已处理记录确定已知结果。对于
收件箱已不再跟踪且 `SentAt` 小于或等于该下限的消息，在任何
接收器或处理程序运行之前返回 `Expired`。恢复过程保留下限。
收件箱绝不接受晚于其时钟加上
`DeduplicationWindow` 的投递 `SentAt`。对于高于准入下限且未被跟踪的此类消息，
以 `ArgumentOutOfRangeException` 拒绝，不暂存任何内容，发送方
因此会在重试预算内重试。已跟踪消息仍会收到记录的
结果，即使接收方时钟向后跳变也是如此；而未跟踪且
时间小于或等于下限的消息仍收到 `Expired`。Grain 发给自身的消息
使用自身时钟，或在时钟向后跳变后使用刚超过下限的第一个时间值。
待处理消息在完成处理前一直保留；完成记录在接收方时钟的
两个窗口以内符合移除条件；如果在时钟向后跳变后向自身发送消息，
则在刚超过下限之后符合移除条件。

时钟向前跳变可能使尚未投递的工作提前过期；向后跳变可能延迟
清理并延长重试。日志化的时间下限使已移除的消息在
两种情况下均保持过期。直接调用方应在重试时复用命令的 `SentAt`。
`OutboxRetryTimeout` 必须短于 `DeduplicationWindow`，以使重试
预算在接收方保留窗口内结束。

<a id="recoverable-terminal-outcomes"></a>

## 可恢复的终态结果

需要以持久化方式响应已过期或遭拒绝的发送操作的应用程序，应注册
`IOutboxMessageSource`，并通过 `IOutbox.Send(messageId)` 暂存这些发送操作。
在 `OnRetired` 中，将应用程序的终态结果及所有待处理的后续
工作暂存到日志化状态，同时释放消息源的有效载荷。这些工作会与
发件箱退役操作原子提交，并在崩溃后随 Grain 一起恢复。
在该回调中暂存发往自身收件箱的消息，便可在恢复后通过
消息泵的持久化唤醒及处理程序路径继续后续工作。

`Delivered` 表示接收方已持久化接受消息，或将其识别为
重复消息。`Rejected` 携带接收方已持久化的拒绝结果。`DeadLettered`
表示发件箱已停止投递，而接收方先前的结果未知，
包括收到 `Expired` 的情况。在发起新的发送之前，应通过
应用程序的幂等协议核对不确定的结果。同一次逻辑发送的重试应复用
原始 `SentAt`。

死信保留限制控制诊断历史的范围。应用程序拥有的
结果及后续工作的生命周期由应用程序自行决定。
<a id="learn-more"></a>

## 了解更多

请参阅[消息传递指南](https://dotnet.github.io/orleans/docs/grains/messaging/)，
了解保证、实践方案、幂等性及运维。可编译的示例位于
[文档代码片段](../../docs/site/src/content/docs/snippets/compiled/Grains/)中。
