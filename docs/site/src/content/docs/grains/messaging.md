---
title: "通信通信"
description: "了解收件箱和发件箱的保障、执行模式和操作限制。"
ms.date: 10/10/2026
ms.topic: conceptual
---

<a id="messaging"></a>

# 通信通信

`Microsoft.Orleans.Durable` 软件包中的信息通信给每个 grain 都有一个耐久的收件箱和
外框基于 Orleans 《日刊和持久工作》。 grain 收到承诺
其日记中的讯息、业务变化和发送信息,例如处理器
和等待的效果 发送生存活性丧失。

> `Microsoft.Orleans.Durable` 是一个针对 .NET 11的释放前阿尔法包件
> `net11.0`. 与
> 明确的升级、回退、备份和回收计划,因为其APP和
> 单日记格式在稳定之前可以更改。

此页面描述信息、 承诺和执行模式。 继续使用 :

- [能力与等级操作键](messaging-idempotency.md)
  应用指令身份、保留完成事实和外部效果。
- 保留清单的[实用实用食谱](messaging-recipes.md) ,
  付款提供者对账、订单外预测和通知溢出。
- 配置[业务和投入](messaging-operations.md)
  诊断、死字母重放、升级和连续发送信息基准。
- 以相继方式书写多步工作流程[持久任务](durable-tasks.md)
  代码,它运行过相同的信箱和发件箱。

<a id="message-and-routing-model"></a>

## 消息和路由模式

每个 <xref:Orleans.Durable.Messaging.Envelope> 都携带一个申请包
<xref:Orleans.Durable.Messaging.MessageId>, which wraps a
<xref:Orleans.Durable.Messaging.HierarchicalKey>, a required `byte[]` payload,
任择性常规科目,可选名称为字数页头和可选
`DeliverAt` 时间。空有效载荷是有效的 。

A 音 A <xref:Orleans.Durable.Messaging.OutboxMessage> 将信封与其目的地对齐
(<xref:Orleans.Runtime.GrainId>). 送文员
<xref:Orleans.Durable.Messaging.InboxMessage> carrying the same envelope to that grain's inbox.
信封的识别码识别了逻辑命令。回复目的地,域
应用有效载荷的版本和其他商业数据旅行。

发送和交付在任何持久变化之前验证每项信息:

- 信息代号已设置,最多为1 024 UTF-8字节,以其语气形式出现。
- 有效载荷是无核的
- 提供的标的为无空,最多256个UTF-8字节。
- 页头键是非空的、结构完善的 UTF-16 字符串,页头值是非核值。
- 发送的消息有目的地。

重新处理和重新提交的申请
和 `DeliverAt` 时间。

<a id="register-handlers"></a>

### 注册处理器

将 <xref:Orleans.Durable.Messaging.IInbox> 注入 grain ,并添加处理器
<xref:Orleans.Durable.Messaging.IInbox.Register*>。 A. 的
<xref:Orleans.Durable.Messaging.IInboxHandler> has two members:
<xref:Orleans.Durable.Messaging.IInboxHandler.CanHandle*> 接收
<xref:Orleans.Durable.Messaging.InboxMessage> and selects the messages the handler
接受, <xref:Orleans.Durable.Messaging.IInboxHandler.HandleAsync*> 处理一个消息。
收件箱将每条消息发送到第一个注册的处理器,该处理器 `CanHandle`
返回 `true`,按登记顺序。选定的处理器将信件的
`HandleAsync`的终端结果。

输入处理器的注册
<xref:Orleans.Durable.Messaging.InboxExtensions.Register*> 扩展方法。
需要一个 <xref:Orleans.Durable.Messaging.MessageType`1> 绑定和一个代表,与
将有效载荷解码 并通过解码体和
代表的上下文:

| 代表 代表 代表 | 使用 |
| --- | --- |
| `Action<T, IInboxHandlerContext>` | 同步处理 。 |
| `Func<T, IInboxHandlerContext, CancellationToken, ValueTask>` | 最终更新前的同步准备工作。 |
| `Action<T, TArg, IInboxHandlerContext>` | 以明确的理由同步处理 `static` 代表。 |
| `Func<T, TArg, IInboxHandlerContext, CancellationToken, ValueTask>` | 以明确的理由处理非同步问题 `static` 代表。 |

使用 `TArg` 参数的超载让一个 `static` 羊羔接收 grain 或
同步打字处理器观察
代表正在运行前的当前处理取消代 表符号。 `Register`
返回收件箱,因此登记链。

手持者可以随时登记。在建设 grain 期间登记,或
,当收件箱在 <xref:Orleans.Grain.OnActivateAsync*>中时可以使用
开始处理已恢复的信件。 已经放入收件箱的信件等待到
处理器已注册。

接收器,没有注册的操作员,
`DeliveryResult.HandlerNotFound`,发送者重试
。当处理者被注册且无人接受该信件时,会进行处理
对该信件的处理器失败报告, 此信件在重试和废字母后
政策。

<a id="encode-ordinary-application-values"></a>

### 编码普通应用值

信件正文为普通 Orleans- 可排序类型 :

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_payload" language="csharp":::

<xref:Orleans.Durable.Messaging.MessageType`1> 将一个受该[质 约束的主体约束于
其体型<xref:Orleans.Serialization.Serializer`1> 。
<xref:Orleans.Durable.Messaging.MessageType`1.Decode*> 核实信封的标
定义申请的装订,在一种类别中
使用 <xref:Orleans.Serialization.Serializer> 服务、注册和注册来构建
将一个品级地投入单吨位,然后投入 grains:

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_messages" language="csharp":::

<xref:Orleans.Hosting.MessageTypeExtensions.AddMessageType*> 登记单
键入单顿的 `FromKeyedServices` 注射。

对象使用精确的正交拼法,例如 `inventory.reserve.v1`。
对象可以绑定相同的 CLR 类型。当一个对象使用基类型时,定义
序列合同中支持的多形态。

<xref:Orleans.Durable.Messaging.OutboxExtensions.Send*> 需要一个绑定、稳定的命令
ID、目的地和身体。目标为 <xref:Orleans.Runtime.GrainId>
或 <xref:Orleans.Runtime.IAddressable> grain 引用。帮助者编码
连续序列化在中继前完成,所以
序列序列错误使发件箱保持不变。
在启动任何信息之前, 编码失败没有发生, 然后开始
顺序排列的邮件。

<xref:Orleans.Durable.Messaging.OutboxExtensions.SendReply*> 需要一个绑定, 操作器将
附加
接收命令ID的字面 `result` 段。应用程序提供
通常来自请求机构如
`ResponseDestination`.

构建<xref:Orleans.Durable.Messaging.MessageType`1.Create*> 从
命令、 ID、 目的和正文。使用它明确发送信件
以其 <xref:Orleans.Durable.Messaging.OutboxMessage.ToInboxMessage*> 视图。

<a id="own-and-borrow-payload-slices"></a>

<a id="use-gc-owned-payload-arrays"></a>

<a id="use-payload-and-header-slices"></a>

<a id="use-payload-and-header-arrays"></a>

### 使用有效载荷和信头阵列

信封是一个只读值, 包含有效载荷阵列、 主题和可选
标题词典。 Struct 任务分配共享这些对象; RPC 复制和
脱序后制成独立副本。
发送并保持其内容在此后保持不变:发件箱状态、交付
重试、处理器背景和诊断读取相同的数组。

对于原始协议,指定编码有效载荷和协议使用的页眉。
接收者通过正则密钥查找信头,然后用商定的编码解码。
这个例子编码了装运有效载荷和两个应用信头的普通编码
序列器 :

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_byte_payload" language="csharp":::

头头携带有效载荷通过耐久性、回溯性、回收和
重复提交待执行或分阶段指挥的
有效载荷中的位置命令定义参数
以及主题,并使用标题作为随附背景,例如跟踪识别符号。

<a id="carry-independently-encoded-items"></a>

<a id="carry-related-values-in-one-message"></a>

### 将相关值包含在一个信息中

使用可连序记录来显示复合电文。字段中可以包含其他
应用、记录、收集和二进制数据。
和a) `byte[]` 在一个体中显示:

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_shipment" language="csharp":::

键入 `Send` 助手将整个 `Shipment`和一个已打印的处理器序列成序
接收解码记录。用于在信息发送外进行序列化,请使用
<xref:Orleans.Serialization.Serializer`1.SerializeToArray*> 和
<xref:Orleans.Serialization.Serializer`1.Deserialize*>.

<a id="schedule-processing"></a>

### 处理时间表

设置 <xref:Orleans.Durable.Messaging.Envelope.DeliverAt> 以在接收者中持有信件
框中,直到工作持续时间在“持久工作”时段。
<xref:Orleans.Durable.Messaging.OutboxExtensions.Send*> 助者与助者,
<xref:Orleans.Durable.Messaging.MessageType`1.Create*> 接受任选 `deliverAt`
参数。从“持久工作钟”中计算 `deliverAt` , grain 输入时是
指定的密钥 <xref:System.TimeProvider> 服务
`DurableJobTimeProviderNames.DurableJobs`.发件箱发送信息
接收者立即接受、复制、日记和处理者
信件到期时立即运行。 A grain 自动为您指定一个消息
发送日记时的文字中接受它。
接收器日记和快照,直到处理完毕,并算入
`MaxCapacity` 从他们被接受到他们完成

<a id="keep-payloads-in-application-state"></a>

### 有效载荷保持在应用状态

默认情况下,发送的有效载荷由发件箱进行日记,收到的有效载荷由发件箱进行日记,收到的有效载荷由
当应用程序本身的日记状态已经拥有有效载荷时, 发件箱
或收件箱汇将这一状态作为唯一日记副本:

- 使用 <xref:Orleans.Durable.Messaging.IOutbox.Register*> 注册 <xref:Orleans.Durable.Messaging.IOutboxMessageSource>，为通过 <xref:Orleans.Durable.Messaging.IOutbox.Send*> 按 ID 发送的消息提供内容。发件箱记录消息 ID 和投递状态，并提供普通消息的全部投递保证。发件箱在验证发送以及每次尝试投递前调用 <xref:Orleans.Durable.Messaging.IOutboxMessageSource.TryGetMessage*> 获取消息。消息源在负责该消息期间应返回相同的消息。如果消息不归它负责，则返回 `false`。最先返回消息的消息源负责该消息。
  - **撤回。** <xref:Orleans.Durable.Messaging.IOutbox.Withdraw*> 可移除待投递的消息，例如应用已判定超时的消息。撤回与消息源释放有效负载应安排在同一次日志写入中。发件箱不再重试，也不会调用 `OnRetired`。已经开始的投递仍可能送达消息。
  - **结束投递。** 发件箱安排移除消息时调用 <xref:Orleans.Durable.Messaging.IOutboxMessageSource.OnRetired*>，使消息源释放负载、应用记录最终结果及安排后续工作在同一次日志写入中提交。<xref:Orleans.Durable.Messaging.OutboxMessageRetirement> 有三种结果：
    - `OutboxMessageRetirement.Delivered` 表示接收方接受了消息，或将其报告为重复消息。
    - `OutboxMessageRetirement.Rejected` 表示接收方的收件箱接收器持久地拒绝了消息，结果带有接收器的 `Reason` 和应用定义的 `Code`。
    - `OutboxMessageRetirement.DeadLettered` 表示发件箱因重试超时、消息源失败或收到 `Expired` 响应而将消息转为死信，结果带有 `Reason`。此前接收方是否接受过消息仍未知。

    `OutboxMessageRetirement` 和 `DeliveryResult` 是 C# 联合类型，各种结果由类表示。可以使用类型模式和属性模式匹配，例如 `retirement is OutboxMessageRetirement.Rejected { Code: var code }`。发件箱先为死信复制一份已取得的消息，因此消息源可以在 `OnRetired` 中释放负载。在此回调中可安排一条发给自身的后续消息，通过收件箱的持久唤醒和处理流程执行应用恢复。若提交前发生故障，恢复时会重新取回待投递条目并调用回调。若结束结果已提交，则与后续工作一同恢复。回调抛出异常会使激活实例失败，其未提交的更改会被丢弃。应在后续消息处理器的准备阶段执行外部 I/O。
  - **消息源失败。** 发送后，如果负责消息的消息源抛出异常、返回无效消息或对该消息返回 `false`，发件箱只会将这一条目转为死信，并在同一次日志写入中以 `DeadLettered` 结束。Grain 继续处理其他消息。该死信只携带消息 ID，负载为空、接收方为默认值，并记录失败原因。恢复的条目如果没有已注册的消息源能提供，也同样转为死信，但没有可通知的消息源。

  如果消息源在 `Send` 期间抛出异常或返回无效消息，错误会传给调用方，且不会暂存任何更改。再次发送待投递的同一 ID 会合并请求。如果该 ID 已作为普通消息等待投递，则抛出 <xref:System.InvalidOperationException>。由消息源提供的消息不能发给发送方 Grain 自身。
- 使用 <xref:Orleans.Durable.Messaging.IInbox.Register*> 注册 <xref:Orleans.Durable.Messaging.IInboxMessageSink>。收件箱接受消息之前，接收器会收到每条已去重的消息，包括定时消息和 Grain 发给自身的消息。如果接收器处理该消息，<xref:Orleans.Durable.Messaging.IInboxMessageSink.TryAccept*> 返回 `true` 并将 `out` 参数 <xref:Orleans.Durable.Messaging.DeliveryResult> 设为下列结果之一。若接收器不处理该消息，则返回 `false` 并将该参数设为 `default`。
  - `DeliveryResult.Accepted.Instance`：接收器已暂存消息产生的更改。收件箱在同一次写入中仅记录消息已处理，并在写入完成后返回结果。
  - `DeliveryResult.Duplicate.Instance`：接收器已持有该消息。
  - `DeliveryResult.Backpressured.Instance`：接收器已达到容量上限，发送方稍后重试。
  - `DeadLettered`：使用 `new DeliveryResult.DeadLettered(reason, code)` 创建，表示永久拒绝。收件箱将消息标记为已处理，记录原因和可选的应用代码（例如 `orders/duplicate-order`），并将两者返回发送方。发送方的消息源通过 `OutboxMessageRetirement.Rejected` 收到这些信息。重复消息会收到相同的拒绝结果。

  汇的例外或其他结果使结果不明:收件箱阶段
  发件人发信后再重新整理直到
  `OutboxRetryTimeout`信息a grain 自己去失败 `Send` 时
  投出或报告 `Backpressured`。一个汇自电文没有收件箱
  水槽的功能, 和汇荣誉 `DeliverAt` 信息,每个水槽
  下降时遵循普通的收件箱路径 。
- 被登记为
  <xref:Orleans.Durable.Messaging.IInbox.RegisterWakeup*>,报告长期工作
  与源的汇等成分一样,在 grain日记。回调返回如何
  很快被停用 grain 必须醒过来才能恢复工作,或者在没有工作的情况下 `null`
  信息提醒后重新启动 grain ,直到工作结束为止;
  启动部分恢复了激活工作。

在建造或启动安装期间,在日报前登记源、汇和催醒回复
回收。 被回收的源提供的信息在无源的情况下失效
已经注册。 信箱在将身份提供给水槽之前复制了跟踪身份,
并过期致在可持久的入场
任何汇影响之前。

<a id="commit-and-delivery-guarantees"></a>

## 承诺和交付担保

- <xref:Orleans.Durable.Messaging.IOutbox.Send*> 中将发出信件的 grain级
  日记状态。 下一期日记写与 grain的
  其它已进入阶段的更改。 发件箱仅在该写入后发送消息
  完整。
- 发送 grain 的电文从不进入发件箱。 `Send` 阶段
  在 grain的自 [自己的收件箱中接受,因此同一日记写仍然
  接受 grain的其它分阶段更改, 以及插件程序
  仅在此写完之后才发出消息。当该信件在它捕捉到前失败时
  分阶段的变更,例如当时间安排其觉醒失败时,接受即停止
  与他们一起编织,再审的书写维持不变。一个失败的存储书写丢弃
  与其他的分阶段变化一起, 下一次激活恢复
  承诺状态。
- 重复使用同一身份和相同信息发送的邮件(与目的地相同,
  主题、有效载荷和 `DeliverAt` 时间) 保留原始电文。
  带有不同内容的 ID 投弃 <xref:System.InvalidOperationException>。
- 收件箱杂志接收人收到的信息或经处理的记录
  信箱汇所在在返回前接受信息
  `DeliveryResult.Accepted`,并仅在此后处理
  接受为可持久。
- 处理器的变更、发送信息、收件箱完成程序已进入阶段
  并坚持著写一本日记。
- 运输是 **at-least-once**。如果发送者在发件人发出通知后失去激活,则该发件人将失去激活
  收件人接受一个消息, 在发件箱记录发送前, 发件箱
  使用其原始 `SentAt`再次发送信件。效果一旦适用
  每一逻辑发送方式是通过留存的完成记录和持久的入场底数;
  处理器可以对同一信息执行不止一次的相同信息, 因此它必须准备
  容忍重复。
- 接收器复制精确 `MessageId`。重复未处理信件
  当主题返回 `DeliveryResult.Duplicate` 时,当它的主题,
  有效载荷,以及 `DeliverAt` 时间匹配,当它们存在差异时失效。
  完整信件返回 `Duplicate` ,而完成记录则保留,
  或原 `DeadLettered` 理由和代码,当信箱汇拒绝时。
  两种记录都保留 `SentAt` 接受的收件箱,不管重复携带。
  记录一旦压缩后,即用永久
  接收 `Expired`楼层,其先前的接受结果不明。
- 发送了 <xref:Orleans.Durable.Messaging.Envelope.DeliverAt> 的邮件,
  被接受后立即立即被接受,并在被接受为可持久且
  持久工作时钟到达 `DeliverAt`。 grain 给自己发来的信息是
  所接受的文字中,一个 grain 可以为自己安排耐久的工作
  连同其他日记的文字变化。
- 交付是 **无顺序的**。需要排序的应用包含序列号
  并按应用程序定义的顺序应用信件。

<a id="execution-model"></a>

## 执行模式

每个启动程序都运行一个信息泵, 启动完成后即开始。
泵处理已准备好的箱信息,然后发送已准备好的发件箱信息,以及
工作剩余时重复。所有泵工程,包括处理器代码,都在
启动单行调度器。 一个即时激活程序将尽快完成
因为它是耐久的:日记写完成和完成交付时,即表示泵
直接。 直接。

收件箱进程到
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.InboxBatchSize> durable, due
每条消息, 一次一个, 然后为批次写一次日记。 The the name
输出框保持
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.OutboxBatchSize> delivery
飞行中试图 grain。每次尝试都自行完成并腾出空位
下一个应发信件的接收器,因此一个缓慢或无反应的接收器有一个时档
继续向其它收件人发送信件。发件箱从不持有寄往
排 grain 本身; 将接受分批接受的状态与批次发送一个阶段的处理器
写作,然后一个过后处理它。

向 grain交付的货物,通过
<xref:Orleans.Durable.Messaging.IMessagingExtension.DeliverAsync*>,与
grain的其他请求。交付货物验证信封,确认已记录
并验证任何新存储的发送时间, 然后在信件上播放前
同步接受, 然后等待日记写; 同时交付
共享日记写。 收件箱信息汇在发送中同步运行, 所以
处理器可以使用该程序,在请求在 `await`暂停时,可运行该程序。

每个一个长期工作 grain, 命名 `orleans.messaging.pump`,即崩溃保险
工作时, 工作在现场泵执行中。 它会唤醒 grain , 一边排队, 一边登记
启动后重新输入时, 将保留激活工作, 最早的重试时间,
`DeliverAt` 时间或回调延迟。
日记只写在未进行跟踪工作时才会安排工作
空闲的空闲状态内, 所以忙碌的 grain 无法执行每个写作中的持久工作操作。 当
激活失败, 持久工作将泵作业无限期地重新压缩, 并封盖后端
时钟上; `AddMessaging` 配置此重试策略,仅用于泵工作。
每次激活都重新武装 实时时间从恢复状态中唤醒 所以
工作必须始终保持清醒。

Grain 州是权威国家。 每份通行证的作品来自日记
信息、他们重试的时间和他们的 `DeliverAt` 时间,如此早、晚、以及
重复的唤醒是无害的。

泵让启动程序保持活动,而过关处理排队列的信件,
发箱交付在飞行中。信件到期后,在它们 `DeliverAt`时发出
或重新试用时间, 离开可激活的收集文件; 持久工作重新启动 grain
当他们被送到临终的目的地的时候。

<a id="interleaving"></a>

### 间隔

处理器运行作为启动工作,但 grain 要求之外的工作除外,所以处理器代码间断
与 grain 每次电话 `await`,在重新输入和非再输入 grains 两者皆是。
发送和收件箱信息汇以同样的方式相互隔开。设计操作员, 汇,
和 grain 无国籍工人 grains
激活时被拒绝。

要将处理器的操作程序承诺阶段完全应用在非再入状态 grain上, 将其作为 grain运行
请求与 <xref:Orleans.GrainBaseExtensions.RunRequestAsync*>一起。请求等待
当前 grain 调用以完成并排除运行时的其他调用:

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_exclusive_commit" language="csharp":::

<a id="handler-outcomes"></a>

## 处理器结果

处理器上下文 <xref:Orleans.Durable.Messaging.IInboxHandlerContext>,显示
<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Message> 和两个终端
业务:

- <xref:Orleans.Durable.Messaging.IInboxHandlerContext.Complete*> 阶梯电文
  及其完成记录。
- <xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*> 将信件移到收件箱
  记录身份证明

每个成功的处理者都称其中之一为成功处理者。 `Complete()` 电话,或
重复 `Fail` 调用相同原因,合并。上下文属于
处理器返回后处理尝试和拒绝使用。

终端作业立即分阶段进行更改,任何日记都写在
激活可以捕捉它们。 每个操作器结构分两个阶段:

1. **准备。** 执行不同步 I/O、验证、解码和可错
   使用本地值计算。 在最后阶段之前观察取消 。
2. **提交。** 在一个同步的区块中,在第一次修改后没有 `await`
   发送打印的离线信件,应用业务更新,调用 `Complete()` 或
   `Fail(reason)`,然后返回。

此订单保留业务变动、 发送信件和完成相同
日记写。 收件箱在处理者返回后维持了批次 。

如何解决每一个失败, 取决于它能否成功:

- **拒绝营业。** 发送打印的拒绝回复并呼叫 `Complete()`。
- **永久无效的输入**,例如一个永远无法生效的命令。
  调调 `Fail(reason)`键,或发送打印的拒绝回复和呼叫 `Complete()` 时
  协议有回复。 消息立即离开收件箱并释放其能力
  时段。
- **中转故障**,例如无法使用的依赖性。丢弃。 grain 停止使用,
  恢复后,信件再重新发送到 `InboxRetryTimeout`,
  同时保留其能力。

以下结果涉及其余案件:

| 处理器结果 | 结果成果结果成果成果 |
| --- | --- |
| 例外,或返回无需终端操作 | 结果未知。 grain 停止使用、 从日记中回收, 丢弃了尝试的改变并收取了尝试费用, 信件回溯到 `InboxRetryTimeout`, 然后转到已绝信 。 |
| 终端业务后例外 | 例外记录在案,分阶段结果持续。 |
| 冲突结束阶段业务 | 合同错误:短信停止和 grain 停用。 |

尝试的阶段更改 将共享 grain待写入的日记账, 所以每个未知
结果是启动失败,而不是重新尝试与之同时进行。

<a id="backpressure-retries-and-dead-letters"></a>

## 后压、回压和死亡信

对于交付,跟踪电文收到其录音答复,无论 `SentAt`
包括接收器时钟后回滚。
允许进入的楼层接收 `Expired`。在楼层上方,信箱框中有一个未跟踪的
信息信息 `SentAt` 工作时间加 `DeduplicationWindow`;平等是
稍晚, <xref:System.ArgumentOutOfRangeException>,
发件箱处理
将“尝试失败”作为例外,保留原始 `SentAt` 并重试直到
`SentAt + OutboxRetryTimeout`;将已用尽的发送重新尝试将发送者退休为 `DeadLettered`
先前结果未知。 直接在发送 grain中播放的消息
使用本地的中转时间, 或首次超过录取层
后时钟跳过, 并遵循本地发送路径。

当收件箱被搁置
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.MaxCapacity> pending
信件, 它用 `Backpressured`回答新信件, 而发送者重试
之后,所有待发信息,包括预定的等待其
`DeliverAt` 时间, 从接受到完成为止, 计数到这个限制 。
发送给自己的 grain 邮件无论受限大小都被接受,因为 `Send`
彼此是同步的,你们应当期待到他们完成。 grain收件箱
在注册处理器和一旦注册处理器之前接受它们。

一旦追回费用向失败的处理器提出追收失败的处理器尝试, 收件箱安排在
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.RetryInitialInterval> multiplied
以64时为上限的指数系数表示。
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.InboxRetryTimeout> (6 hours by
)在第一次失败后,它们横跨短暂的断层,例如断断和断
用于修复错误的部署。上次尝试在超时过后进行;如果失败,
信件移动到收件箱中已绝信的字母,其身份文件被记录为处理过。
`Fail(reason)` 立刻给一个信息发个信

当接收器报告 `Accepted`、 `Duplicate`或
`DeadLettered`。任何其他结果,包括发送者无法解释的状况等
成为新接收器中的一员,结果未知,并计划进行耐久的重试
继续使用相同的回落, 而其他信件则继续 :

- `Backpressured` 在最初的绝对交付预算范围内重新进行核查。
- `HandlerNotFound` 记录错误,重试给予一个部署时间来登记处理者。
- 其他失败,例如,在基础设施停机期间没有答复的电话失灵,
  重试如 `HandlerNotFound`。

<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.OutboxRetryTimeout> starts
记录在
<xref:Orleans.Durable.Messaging.InboxMessage.SentAt>. Each attempt and recovery preserves
时间; 重试到期是 `SentAt + OutboxRetryTimeout`。
超时退出后没有尝试开始; 发件箱然后将信件移动到它
注明重试超时已过的死字。
带有原始 `SentAt`。
接收器先检查尚未进行和已完成的身份,然后核对身份。
身份,在非降为录取底数的回送时或低于此值
在任何汇或处理器运行之前`DeliveryResult.Expired` 。
发件箱保留了这个信息,作为一纸空文,并用 `DeadLettered`将它退掉:
先前的接受可能因确认失败而成功。
在批准另一个应用程序之前通过源回调来进行应用对账
工作时钟对重新试制时间安排和候选人底数进行调节
推进; 日记发送时间和地板以保存整个用户的入境决定
时钟更改。 `OutboxRetryTimeout` 必须短于 `DeduplicationWindow`。

通过 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics> 检查和删除已写过的字母并删除
处理 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveInboxDeadLetter*>记录
或 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveOutboxDeadLetter*>。
保留设置约束两个
收藏; 见 [能力、重试和保留](messaging-operations.md#capacity-retries-and-retention)。

将入场底座的截图提前至其现值的后面
所观察到的持久工作时间减 `DeduplicationWindow`,并完成契约
被接受的记录 `SentAt` 位于或低于该楼层。
紧紧历史的原子历史继续存在,而记忆中记录一直保存到
收到。

<a id="state-recovery-and-deletion"></a>

## 国家、追偿和删除

收件箱和发件箱 每一个都是事件源头的日记国家机器
以 `orleans.messaging.inbox` 和 `orleans.messaging.outbox`登记。它们使用
grain的配置日记日记格式; JSON 和 Orleans 二进制格式均为
支持的。

重新激活新日记重播并恢复记录中的未完成的工作
输入和重试时间。当信息失败时,例如由于中转或
但它否决了每以后的日记 有关激活和
grain 停止使用;下一次启动将收回
日记。

grain 之后用 `DeleteStateAsync`、收件箱和发件箱删除其日记
开始为空, 并继续接受并发送信件 。

<a id="deployment-requirements"></a>

## 部署所需经费

配置“持久工作”存储和《日刊》存储,然后将信息发送到
<xref:Orleans.Hosting.MessagingExtensions.AddMessaging*> 排在《公约》 silo 建筑或
服务收藏。可选的回调配置
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions>, which are validated at startup.
登记增加了可持久工作,并使用应用程序提供
<xref:System.TimeProvider> when one is registered.

开发、配置存储和启用信息:

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_registration" language="csharp":::

A grain 通过解决 <xref:Orleans.Durable.Messaging.IInbox> 或
<xref:Orleans.Durable.Messaging.IOutbox> during construction or activation setup, typically
交付通过它的构造注射达到 grain的收件箱
和日记恢复恢复 grain's
其他日记状态。

以下 grain 在激活期间登记了输入的处理器。
以 `Fail(reason)`来字母不附带文字的通知书,因为输入可以
无效的通知,它计算一个非选择性答复,发送
并更新其计数,

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_grain" language="csharp":::

普通 grain 方法, 将输入并持续稳定命令ID的打印发送程序分阶段
以一个日记写一个日记。客户用相同的命令ID级重新尝试相同
消息:发件箱保留了一个条目,而该信件在等待中,接收者
交付后确认为重复:

:::code source="../snippets/compiled-durable/Grains/MessagingSnippets.cs" id="messaging_send" language="csharp":::

多silo 部署使用共用、生产级储存。
工作以及日记存储支持开发和测试。

监测盒和发箱深度、重试和死字母结果以及
工作量的入职时间差和重算预算
可用性和留存历史费用。
公吨名称和诊断见 [运行发送信息](messaging-operations.md)。
