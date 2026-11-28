---
title: 消息传递实践示例
description: 实现库存预留、幂等付款、有序投影、持久化通知扇出以及已调度的工作流步骤。
ms.date: 10/10/2026
ms.topic: how-to
---

<a id="messaging-practical-recipes"></a>

# 消息传递实践示例

这些已编译的示例采用普通 Grain，注入
<xref:Orleans.Durable.Messaging.IInbox> 或 <xref:Orleans.Durable.Messaging.IOutbox>，以及命名的
日志状态。配置
Journaling 和 Durable Jobs，并调用
<xref:Orleans.Hosting.MessagingExtensions.AddMessaging*>，如
[消息传递](messaging.md#deployment-requirements)中所示。
部署时使用持久化共享提供程序；内存配置适合
本地运行。

所有处理器示例均使用局部值完成验证和异步准备，
随后发送强类型消息、应用业务更新并调用 `Complete()`，
期间不执行 await。永远不可能变为有效的输入使用
`Fail(reason)` 转入死信，或者以强类型拒绝回复应答。收件箱持久化该
结果。普通 Grain 方法等待
自身的日志写入。每个 Grain 使用
<xref:Orleans.Durable.Messaging.InboxExtensions.Register*> 注册强类型处理器，并直接注入发件箱。
Grain 注入应用的 `Messages` 类，其中的
<xref:Orleans.Durable.Messaging.MessageType`1> 绑定将各主题与其正文类型配对。

<a id="run-the-stock-reservation-sample"></a>

## 运行库存预留示例

[消息传递示例](https://github.com/dotnet/orleans/tree/main/samples/Messaging)
在一个本地 Silo 中运行订单 Grain 与库存 Grain。三个独立的
层次命令 ID 分别演示接受、库存不足和数量无效。
订单 Grain 通过其发件箱发送每个命令，等待
库存 Grain 的强类型回复，回复到达订单 Grain 的收件箱。宿主验证
每条命令收到自身的结果、拒绝不会改变库存，
并且库存 Grain 对每条命令仅完成一次处理。

从仓库根目录运行：

```powershell
pwsh .\samples\Build-Samples.ps1 -SkipExternalAssets
dotnet run --project .\samples\Messaging\Messaging.csproj --configuration Release --no-build
```

在这些 API 尚未发布期间，示例使用本地打包的 Orleans 软件包。
其 README 也描述了独立复制运行的工作流。易失性日志
和内存作业支持本地运行；要在重启后恢复，请配置持久化共享
提供程序。

<a id="reserve-inventory-once-per-order-line"></a>

## 每个订单行仅预留一次库存

使用 `available-stock`，为每个租户/SKU 设置一个库存 Grain。发送方使用
以下方法派生信封的命令 ID：
[OrderOperationKeys](messaging-idempotency.md#hierarchical-business-operation-keys)，
并将数量与回复目标放入 `ReserveStock` 中。
[强类型发送辅助方法](messaging.md#encode-ordinary-application-values)将记录编码到
信封的 `inventory.reserve.v1` 主题下。

库存 Grain 使用
<xref:Orleans.Durable.Messaging.InboxExtensions.Register*> 注册两个强类型处理器：`inventory.reserve.v1` 为预留方法解码
`ReserveStock`，而 `inventory.restock.v1` 为库存增加方法解码
`Restock`。每个方法接收已解码的记录
和处理器上下文。Grain 在
<xref:Orleans.Grain.OnActivateAsync*> 中注册二者，此时日志恢复已恢复其状态。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_inventory" language="csharp":::

预留方法在本地计算下一库存值及结果。`SendReply`
编码并暂存确定性的 `result` 回复，随后方法同步应用库存变更
并完成处理。第一次命令将库存扣减、回复
和收件箱完成操作一起提交。相同命令 ID 的重复提交会识别
保留的完成记录，并保持原始处理器的效果不变。
序列化的抽象 `ReservationResult` 派生自 `OrderOutcome`；
密封的 `ReservationAccepted` 和 `ReservationRejected` 记录区分不同的有效
业务结果类型，并携带原始数量及剩余库存。
非正数数量发送 `ReservationRejected`，附带 `InvalidQuantity`；
库存不足发送带有 `InsufficientStock` 的该结果。两者完成时库存均保持不变。
无效的不可变预留命令以持久化业务拒绝结束。
原始发件箱意图在其配置的投递策略范围内
投递回复。

对于非正数的 `Restock` 增量，处理器调用
<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*> 并给出明确原因，
在变更之前返回，因为该命令永远不可能变为有效。正数
增量在变更之前通过 checked 运算计算新库存值，随后将该
更新与收件箱完成操作一起提交。checked 溢出会抛出异常，遵循处理
重试策略直至 `InboxRetryTimeout`。
为每次不同的
补货操作分配自身的稳定命令 ID。示例中的 `SetAvailableAsync`
是管理用途的绝对库存更新。在
订单工作流中，为已预留库存添加明确的确认、到期和释放策略；
当这些操作需要查找时，保留以预留命令 ID 为索引的记录。
为释放操作分配独立的叶子键，例如
`{order}/inventory/{sku}/release`，并将其结果与库存恢复一起提交。
在应用的受信任入口处授权库存管理，
并派生租户/SKU 的 Grain 标识。

<a id="charge-an-external-provider-with-a-stable-key"></a>

## 使用稳定键通过外部提供程序收款

在 <xref:Microsoft.Extensions.DependencyInjection.IServiceCollection> 中注册所示网关契约的
实现。其适配器将键映射到
提供程序的幂等标头或请求字段，并实现提供程序的结果
对账协议。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_payment" language="csharp":::

处理器首先验证不可变命令。永远不可能变为有效的金额、货币或回复
目标会使用
<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*> 将命令转入死信，此时尚未调用任何提供程序。
提供程序调用先于共享日志状态的变更。异步处理器
将其令牌传给网关，该方法在执行 await 后，且在
`SendReply` 及最终结果更新之前检查取消。提供程序故障会抛出异常，因此
消息采用退避策略重试。如果提供程序操作成功，但激活实例
丢失导致本地提交中断，则新的尝试使用相同的提供程序键。
规范化的信封 ID 同时也是提供程序幂等键。本地完成之后，
收件箱在保留期间识别重复提交。结果查询
状态、确定性回复和收件箱完成操作共用一次写入。

规范化货币，并将命令 ID 的作用域限定为提供程序账户及租户。
将付款被拒绝视为已完成的结果；对于结果不明确的提供程序响应，
根据提供程序的同键重试或查询契约进行处理。像此 Grain 一样公开状态
查询，使调用方能够通过相同命令 ID 核实不确定的响应。
在整个重试期限内保留提供程序幂等记录；提供程序有键长度限制时，
使用确定性的规范键哈希进行适配。
参见[外部副作用](messaging-idempotency.md#external-side-effects)
中关于保留及对账职责的说明。

<a id="converge-an-out-of-order-stock-projection"></a>

## 使乱序库存投影收敛

仪表板或搜索投影可以接收完整库存快照，而不依赖
传输顺序。权威库存生产者为同一租户/SKU 分配单调
递增的版本。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_projection" language="csharp":::

如果版本 12 先于版本 11 到达，投影保留版本 12，
并将版本 11 以已被取代的结果完成。重复使用的版本与其原始内容绑定：
使用不同内容复用版本的快照，或包含无效
版本或库存值的快照，会通过 `Fail(reason)` 转入死信。
版本与完整库存值构成一条持久化记录。稳定的消息 ID
识别已完成的快照命令；领域版本对不同的
快照命令排序。

本示例适用于**完整替换快照**。增量扣款、入账
或库存变化事件需要采用连续序列策略，将缺口缓冲区记入日志，
并为缺失事件定义领域恢复路径。

<a id="publish-a-durable-notification-campaign"></a>

## 发布持久化通知活动

普通方法可以在一次业务更新中为多个目标暂存消息。为
通知活动分配稳定 ID，使重复的客户端提交能够找到已记录的内容及
接收方集合。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_fanout" language="csharp":::

活动记录与每个出站意图均在同一次发送方
日志写入中捕获。强类型批量
<xref:Orleans.Durable.Messaging.OutboxExtensions.Send*> 辅助方法会先对每条接收方
命令编码，再暂存任何命令，因此编码失败时不暂存任何内容；编码完成后按顺序
暂存。所有发送成功后添加活动记录，重复
提交使用相同的接收方命令标识及内容。
每个目标通过
[通知处理器](messaging.md#deployment-requirements)独立提交。每个信封的
ID 是活动根键下稳定的接收方子键，由键工厂负责对字面量 Grain 标识
进行转义。接收方保留的收件箱完成
记录识别该通知命令。活动记录也支持
`GetCampaignAsync` 查询。成功的发布响应
表示活动及其意图已持久化；接收方完成处理是之后的事件。

根据日志容量及延迟预算，在入口限制接收方数量和
有效载荷大小。对于较大的活动，持久化活动游标并
分批发出数量有界的接收方消息，将游标与每批一起提交。
如果需要投递确认，使接收方回复带键的结果，并
由活动 Grain 汇总这些结果。在提交及查询时限内保留活动状态，
并在支持的重复提交时限内保留接收方的
完成记录。

<a id="combine-the-recipes-into-an-order-workflow"></a>

## 将示例组合为订单工作流

接收多个主题的协调器为每个主题注册一个强类型处理器。
此示例注册共用一个方法的库存及付款结果处理器，
并按其确定性回复 ID 记录结果，以供进度及
结果查询：

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_dispatcher" language="csharp":::

每次注册按序数规则匹配其主题，使用相应绑定解码正文。
每个处理器在同一同步操作块中更新状态并完成处理。
解码失败及没有匹配处理器的主题遵循处理
重试及死信策略。

每个订单的协调器持久化预期步骤的键及阶段，发送
库存和付款请求，随后完成发起命令。每条
响应在一次本地提交中记录结果并完成处理。所有必需步骤
成功后，一次状态转换发出 `shipment/create`。

| 阶段 | 持久化转换 |
| --- | --- |
| Submitted（已提交） | 保存预期叶子键并发出库存及付款请求。 |
| Waiting（等待中） | 按回复 ID 保存每个关联的参与者结果；收件箱识别重复的回复命令。 |
| Ready（就绪） | 记录就绪阶段并发出具有独立键的发货请求。 |
| Rejected（已拒绝） | 记录业务拒绝，并为已完成的参与者步骤发出释放或退款命令。 |
| Finished（已结束） | 记录发货或补偿结果，并保留工作流状态以供结果查询和审计。 |

每次转换使用相同的先准备、再执行同步最终操作块的模式。将
业务拒绝保留在工作流状态中，使用
`Fail(reason)` 将永久无效输入转入死信，并仅将异常用于暂时性的准备阶段故障。使用
已记录的步骤结果在服务中断期间应答状态查询。

<a id="schedule-a-workflow-step"></a>

## 调度工作流步骤

Grain 通过向自身发送带有 `deliverAt`
时间的消息来调度之后的步骤。此结算 Grain 在宽限期结束后确认订单，除非
买家先取消：

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_grace_period" language="csharp":::

结算 Grain 根据 Durable Jobs 时钟计算 `deliverAt`，该时钟作为
带键 <xref:System.TimeProvider> 注入，名称为 `DurableJobTimeProviderNames.DurableJobs`。
结算 Grain 的收件箱在记录提交的同一次日志写入中接纳 `GracePeriodElapsed` 消息，
因此已调度步骤与
`Submitted` 状态同时存在。已调度消息计入结算 Grain 的
`MaxCapacity`，直到处理完成。消息到期时，Messaging 唤醒 Grain，
并按照收件箱重试策略运行其处理器。处理器依据当前
状态执行操作，因此已取消的结算会在不改变状态的情况下完成消息。
消息 ID 是提交 ID 的子键，重新投递的提交
会找到已记录状态，不再调度新工作。

<a id="choose-an-application-migration-pattern"></a>

## 选择应用迁移模式

将现有应用职责移入处理器及其强类型有效载荷
记录，使用收件箱/发件箱的提交边界记录出站意图：

| 现有应用模式 | Messaging 模式 |
| --- | --- |
| 请求/回复 Grain 方法 | 将稳定命令 ID 和主题放入信封，将回复目标放入正文；处理器在完成操作时暂存确定性的强类型回复。 |
| 业务更新后进行远程调用 | 将出站信封与业务更新一起暂存；已确认的发件箱状态驱动投递和重试。 |
| 外部提供程序调用 | 使用规范命令 ID 准备提供程序结果，再将其本地查询状态、回复及收件箱完成操作一起提交。 |
| 通知循环 | 为每个稳定的接收方标识调用强类型 `Send`，成功暂存后记录活动，并等待日志写入。 |
| 相关记录与附件 | 将应用记录及 `byte[]` 附件放入一条可序列化消息，使用强类型发送和处理器辅助方法。 |

迁移应用工作流时保留命令标识及已记录结果。
应用负责主题契约、回复路由、授权及 ID 构造。
参见
[应用有效载荷演进](messaging-operations.md#evolve-application-payload-records)
中有关应用记录滚动变更的说明。

<a id="verify-your-handlers"></a>

## 验证处理器

测试来自多个 Grain 的同 ID 重复提交、待处理请求复用冲突、
不同的子命令、乱序到达及本地准备阶段的
取消。一起断言业务状态、查询结果、
出站信封及完成操作。使用部署实际采用的提供程序测试激活实例恢复及不确定的
存储或提供程序结果。

可执行文档示例测试层次键隔离、
强类型预留接受、零或负数量拒绝、库存不足、
多态结果往返、回复暂存失败且库存不变、
永久无效补货、无效付款及无效快照转入死信、
checked 补货溢出、提供程序成功后本地取消的重试、
乱序投影、强类型多主题分派、活动批量编码、
Durable Jobs 时钟上的已调度步骤及普通复合消息
往返。在测试端到端重复提交时，验证原始回复的实际日志确认，
以及完成记录保留期的精确边界。
有关运行时保证与运维控制，参见
[消息传递运维指南](messaging-operations.md)。
