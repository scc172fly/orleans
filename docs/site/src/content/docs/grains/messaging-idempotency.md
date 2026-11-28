---
title: 消息传递的幂等性
description: 设计应用命令标识、层次键、保留的完成记录以及外部操作的幂等性。
ms.date: 10/09/2026
ms.topic: conceptual
---

<a id="messaging-idempotency"></a>

# 消息传递的幂等性

应用为每个逻辑命令分配一个稳定的层次 ID。持久化
消息传递在准入、重试、完成、诊断和恢复的全过程中使用该 ID。
接收方收件箱保留的完成记录能够识别来自任意 Grain 的
同一命令的重复提交。

有关提交协议，参见[消息传递](messaging.md)。
有关完整的 Grain 示例，参见[实践示例](messaging-recipes.md)。

<a id="choose-an-identity-at-each-boundary"></a>

## 在每个边界选择标识

| 标识 | 所有者与作用域 | 用途 |
| --- | --- | --- |
| 信封的 `MessageId` | 接收方 Grain 收件箱中的精确键 | 合并待处理命令的重复提交，并在保留期内识别已完成命令。 |
| 信封的 `MessageId` | 发送方 Grain 发件箱中的精确键 | 将一条待发送消息与其目标、主题及正文绑定。 |
| 工作流根键 | 应用协议 | 将相关命令 ID 和确定性回复 ID 归组。 |
| 外部幂等键 | 外部系统的账户或 API 命名空间 | 使用规范化命令 ID 识别重试的外部副作用。 |

在发送前，根据稳定的租户、实体和操作标识派生命令 ID。
每次重试和重新构造的提交均使用原始 ID。为独立的生产者
明确划分命名空间。转发消息的 Grain 保留原始
命令 ID。新的工作流命令
使用不同的子级或同级 ID；扇出操作为每个目标派生一个稳定的
接收方子级 ID。

一个键标识一个不可变命令：目标、主题和正文保持其
原有含义。真正的新预留操作、已授权的补偿操作或
修订使用新键。即使生产者与消费者在不同时间部署，协议升级也应保留
现有命令的标识。

<a id="transport-deduplication"></a>

## 传输去重

对于投递，收件箱根据已跟踪标识的待处理或已完成记录作出响应，
无论投递携带的 `SentAt` 为何，即使接收方时钟发生回拨也如此。
如果未跟踪标识的发送时间不晚于准入下限，则返回
`Expired`。高于下限时，收件箱对未跟踪标识的 `SentAt` 施加上限，即
接收方的 Durable Jobs 时间加上
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow>；
等于上限的时间允许准入，晚于该上限则抛出
<xref:System.ArgumentOutOfRangeException>，既不暂存变更，也不将消息提供给接收器。
发送方在原有预算内重试，因此应诊断时钟偏差或直接调用方设置了
错误时间戳的问题。

Grain 发送给自身的消息使用本地暂存时间，或在时钟回拨后使用高于
准入下限的第一个时间。对于历史压实而言，后一时间仍恰好位于
下限之后，即使它超出投递时间上限。

每个接收方检查精确的 `MessageId`；无论发送方或主题为何，它均标识该接收方的
一条命令。父键与其子键代表相互独立的
命令。待处理重复消息指向已接纳的工作，具有相同的
主题、正文字节和 `DeliverAt` 时间；若待处理重复消息的
主题、正文或 `DeliverAt` 不同，则失败。
已完成的重复消息在完成记录保留期间返回 `Duplicate`。
待处理及已完成记录保留 ID，以及
收件箱所接纳的 <xref:Orleans.Durable.Messaging.InboxMessage.SentAt>。重复提交
保持这一已接纳时间，并且在接收方返回
`Duplicate` 或重复其已记录的拒绝结果之前，记录已持久化。
应用遵守不可变命令契约；完成记录
确认原始执行已完成。

处理器暂存其业务变更与出站消息，随后调用
<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Complete*>，该方法暂存收件箱
条目的移除及完成记录。一次日志写入将它们一起持久化，重放时
将业务效果与完成记录一起恢复。因此，业务效果
对每次逻辑发送只应用一次。对于相同的 ID，处理器可能执行
多次，例如在尝试失败或激活实例丢失之后，因此准备阶段必须能够容忍重复
执行。

发送时的日志写入根据 Durable Jobs 时钟记录 `SentAt`。每次
尝试及恢复均使用同一时间，包括受到背压之后；
重试预算在 `SentAt + OutboxRetryTimeout` 耗尽。应将 `OutboxRetryTimeout` 配置为
小于
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow>。
每个收件箱将一个单调不减的准入下限记入日志。保留期
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow>
控制该下限落后于观测到的 Durable Jobs 时间的距离。
当已完成记录所接纳的 `SentAt` 不晚于
持久化下限时，即可移除。快照确认和恢复过程会回收这些记录。
此后，具有该原始发送时间的延迟投递会在任何接收器或处理器运行前收到 `Expired`。
时钟向前跳变可能使发送提前过期；时钟回拨
可能使历史记录保留更久。根据处理速率和保留期规划历史记录容量。
应用结果查询的保留策略独立满足业务查询与审计需求。

`Expired` 表示此前的接纳结果未知：确认可能在接收方通过压实移除
其完成记录之前丢失。在授权另一条命令前，使用原始 ID 对账
并核实业务状态。发件箱自动重试及恢复均保留
原始的 `SentAt`；直接投递的调用方
也应复用它。消息退出发件箱后，新提交的发件箱消息会获得新的暂存
时间。在某个标识的保留期结束后，重新提交的消息如果其新发送时间
被允许准入，则可能被再次处理；
授权该提交之前，应核实原始处理结果。

发件箱合并共享待处理消息 ID 的等价消息，无论它们是暂存的还是
已持久化的。等价消息具有相同的目标、主题、
有效载荷字节及 `DeliverAt` 时间；保留原始消息及其标头。将待处理 ID
复用于不同内容会抛出异常，并保持原始消息不变。

Grain 发送给自身的消息仅在该 Grain 的收件箱中按键标识。`Send` 同步应用
接收方规则：具有相同主题、正文字节
和 `DeliverAt` 时间的待处理重复消息会被合并；仍保留的已完成 ID 被识别为重复消息，而具有不同内容的待处理
重复消息会抛出异常。本地发送获得收件箱允许准入的暂存时间，
即使时钟已发生回拨也如此。已跟踪标识的本地重复提交
保持记录中已接纳的发送时间。

<a id="hierarchical-business-operation-keys"></a>

## 层次化业务操作键

<xref:Orleans.Durable.Messaging.MessageId> 通过封装一个
<xref:Orleans.Durable.Messaging.HierarchicalKey> 来标识消息。使用 `new MessageId(key)` 或
<xref:Orleans.Durable.Messaging.MessageId.Create*> 构造它，并通过
<xref:Orleans.Durable.Messaging.MessageId.Key> 读取键。消息 ID 的比较、哈希计算、解析和格式化
均通过该键进行。

<xref:Orleans.Durable.Messaging.HierarchicalKey> 是封装其规范转义路径的不可变值类型。
相等性判断、`==`、`!=` 和哈希计算均依据该路径，
使用区分大小写的序数比较。该键提供基于路径段的导航能力，包括
<xref:Orleans.Durable.Messaging.HierarchicalKey.GetParent*>、
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsParentOf*>、
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsChildOf*>、
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsAncestorOf*> 及路径段枚举器。
`default(HierarchicalKey)` 是未设置的值，通过 `IsDefault` 公开；信封
验证要求 ID 已设置。

一种实用的层次结构如下：

```text
tenants/{tenant}/orders/{order}
  inventory/{sku}/reserve
  inventory/{sku}/release
  payment/charge
  payment/refund/{refund}
  shipment/create
```

根键归组一个工作流；每个叶子键标识一个效果。重试请求保留
相同的叶子键。在授权新效果时，一次性分配稳定的尝试或修订路径段。
使用区域性无关的格式设置数值组件，并在构造之前规范化业务标识。
键比较按序数进行，并保留其原始拼写。

<xref:Orleans.Durable.Messaging.HierarchicalKey.Create*> 接受字面量路径段，
而 <xref:Orleans.Durable.Messaging.HierarchicalKey.CreateChildKey*> 追加一个字面量
路径段。两者均恰好转义一次斜杠和反斜杠字符。构造
`payment/charge` 时，使用彼此独立的 `"payment"` 和 `"charge"` 参数。
<xref:Orleans.Durable.Messaging.HierarchicalKey.Parse*> 读取已转义路径，
格式化则输出该规范表示形式。

以下辅助方法使用字面量租户与 SKU 标识构造命令 ID：

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_operation_keys" language="csharp":::

SKU `widget/blue` 占用一个字面量路径段：

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_hierarchy" language="csharp":::

在生产者、收件箱、日志、结果查询
及外部提供程序键之间使用相同的规范标识。
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsAncestorOf*> 在路径段
边界进行匹配：`orders/42` 将 `orders/42/payment` 归组，而 `orders/420/payment`
属于另一个订单。一个键也是其自身的祖先；`IsChildOf` 检查是否恰好多出一个
路径段。去重始终比较完整的键。

受信任的入口派生已授权的租户或实体根键，并在接纳工作前依据它核查声明的
命令键、主题及目标。键表达的是标识；
授权来自受信任的应用边界。
发送与投递将消息 ID 的规范路径限制为 1,024 个 UTF-8 字节，将
主题限制为 256 个 UTF-8 字节。

<a id="persist-a-result-not-just-a-flag"></a>

<a id="persist-a-result-not-just-a-flag"></a>

<a id="persist-queryable-results"></a>

## 持久化可查询的结果

处理器可以暂存持久化回复，其 ID 为命令稳定的 `result` 子键。
[库存示例](messaging-recipes.md#reserve-inventory-once-per-order-line)
在本地计算预留决策和下一库存值。强类型 `SendReply`
编码并暂存回复，随后方法同步更新库存并完成处理。库存不足是一个已完成的拒绝结果，库存保持不变。
最初提交的发件箱发送意图在其配置的策略范围内负责
结果投递。重复提交确认仍保留的完成记录，
并保留原始执行结果。

收件箱返回 `Duplicate` 时确认待处理或已完成的命令标识。
从持久化回复中取得业务结果，或使用相同的命令 ID 查询
业务状态。原始回复的投递策略结束后，可以使用独立的查询命令请求
结果。

在查找、释放或到期操作需要时存储预留记录，存储付款结果以支持状态查询与对账，
并存储工作流阶段以支持进度查询。
这些都属于具有明确查询及审计保留策略的业务状态。将它们与
本地效果、出站意图和收件箱完成操作一起提交。
[付款示例](messaging-recipes.md#charge-an-external-provider-with-a-stable-key)
提供按原始命令 ID 查询结果的功能。

<a id="external-side-effects"></a>

## 外部副作用

外部付款、电子邮件或 HTTP 操作有其自身的提交边界。激活实例可能在该操作成功之后、
其结果记入日志之前发生故障。
付款示例在本地准备期间使用
`context.Message.Envelope.MessageId.ToString()` 调用提供程序。提供程序保证请求的一致性，
并在使用该键进行重试时返回原始结果。

将提供程序账户或租户命名空间、键格式、保留期、冲突行为
和结果查询 API 纳入集成设计。对于可能到达提供程序的每次重试或重放，
都要保留其幂等记录。若提供程序限制键长度，
适配器可以对规范 ID 进行确定性哈希，并在本地
保留原始命令 ID 以供关联。应核实结果不明确的响应，或使用原始键
重试。对于结果不确定的 API，必须先执行其对账流程，
再授权另一项效果。

一旦结果已知，检查取消状态、发送强类型回复，并在不执行 await 的情况下完成
日志结果和 `Complete()`。每次异步
提供程序调用均应在首次共享状态变更之前进行。

<a id="fan-out-and-multi-grain-workflows"></a>

## 扇出与跨 Grain 工作流

在工作流根键下为每个参与者或步骤分配自己的命令 ID。一个当前有效的
发件箱键映射到一个目标，因此通知采用稳定的接收方专属
子键。协调器在自身日志中记录已提交的步骤并汇总关联结果。
参与者原子地提交其效果与确定性回复；
协调器将每个接收结果与下一步意图一起提交。

使用持久化步骤状态，例如 `Requested`、`Succeeded`、`Rejected` 或
`Compensated`，并记录参与者结果。拒绝结果可以针对已完成的步骤发出具有独立键的
释放或退款命令。每个参与者在自身精确的键下完成该补偿操作。
这为跨多个独立提交的 Grain 日志的工作流提供明确的前进
和恢复路径。

<a id="ordering-and-replay"></a>

## 排序与重放

| 操作 | 应用策略 |
| --- | --- |
| 预留库存或收取款项 | 在重试期间保留命令 ID，并在支持的时限内保留收件箱完成记录。 |
| 使用完整快照替换投影 | 仅应用较新的领域版本；将较旧的快照按已被取代的结果完成。 |
| 应用每个增量事件 | 持久化预期序列，在应用连续事件之前缓冲序列缺口。 |
| 有意使用修正后的逻辑重新处理 | 授权新的修订键，并记录它与原始结果的关系。 |
| 重放收件箱死信 | 核实以原始 ID 为键的效果，再授权新的修订键，例如原始键的子键；仍保留的完成记录返回 `Duplicate`，未跟踪的旧发送时间返回 `Expired`。 |

消息标识识别已完成的命令；领域版本或序列
建立顺序。
[投影示例](messaging-recipes.md#converge-an-out-of-order-stock-projection)
演示了完整快照策略。
