---
title: "传递实用食谱"
description: "实施库存保留、无能力支付、定单预测、持久通知溢出和预定工作流程步骤。"
ms.date: 10/10/2026
ms.topic: how-to
---

<a id="messaging-practical-recipes"></a>

# 传递实用食谱

这些汇编的例子使用普通 grains 输入
<xref:Orleans.Durable.Messaging.IInbox> or <xref:Orleans.Durable.Messaging.IOutbox>, and named
配置
日报和持久工作及呼吁
<xref:Orleans.Hosting.MessagingExtensions.AddMessaging*> 所示
[通信通信](messaging.md#deployment-requirements).
使用持续共享的供应商进行部署;模拟配置是
适合当地执行。

所有处理示例都与本地操作完成验证和同步准备
值,然后发送打印的邮件,应用业务更新,并调用 `Complete()`
无法生效的投入用
`Fail(reason)` 或以打印拒绝回复回答。
等待结果的普通 grain 方法
每个 grain 登记了输入的处理器
<xref:Orleans.Durable.Messaging.InboxExtensions.Register*> 并直接注入发箱。
Grains 申请表 `Messages` 类别,其
装订物<xref:Orleans.Durable.Messaging.MessageType`1> 对每个对象都配有其体型。

<a id="run-the-stock-reservation-sample"></a>

## 运行储备样本

下 [通信路中抽样](https://github.com/dotnet/orleans/tree/main/samples/Messaging)
运行顺序 grain 和库存 grain 在一个本地主机中 silo三个独立的
测试是否接受, 库存不足, 无效
数量。命令 grain 通过其发件箱发送每个命令,等待
库存 grain键入回复,该回复达到顺序 grain收件箱。
每一个指挥官都得到自己的结果 拒绝命令的军令库存保持不变
并且库存 grain 处理每个命令一次。

从仓库根 :

```powershell
pwsh .\samples\Build-Samples.ps1 -SkipExternalAssets
dotnet run --project .\samples\Messaging\Messaging.csproj --configuration Release --no-build
```

样本使用当地包装的 Orleans 包,而这些API等待公布。
其《检索、评估和监测和评价方法》还描述了自成一体的复制工作流程。
以及模拟工作支持本地执行;配置持续共享的提供者
恢复恢复。

<a id="reserve-inventory-once-per-order-line"></a>

## 每个定单项目一次的储备库存

使用一个清单 grain 每租户/斯洛伐克克朗和 `available-stock`发送者来源
信封的命令编号
[OrderOperationKeys](messaging-idempotency.md#hierarchical-business-operation-keys),
将数量和答复目的地放在 `ReserveStock`。
[类型格式化的发送助手](messaging.md#encode-ordinary-application-values) 编码
记录在信封 `inventory.reserve.v1` 主题下。

清单登记册登记了两个打字处理器,
<xref:Orleans.Durable.Messaging.InboxExtensions.Register*>: `inventory.reserve.v1` 解码
`ReserveStock` 用于保留方法和 `inventory.restock.v1` 解码
`Restock` 用于库存加固方法。每种方法都有其解码记录
和处理器上下文。 grain 登记在
<xref:Orleans.Grain.OnActivateAsync*>,在日记账恢复后恢复状态。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_inventory" language="csharp":::

保留方法计算其下一个存量和本地结果。 `SendReply`
编码和分级确定性 `result` 回复,然后该方法应用库存
并同步完成。 第一命令 承诺库存减少,回答,
和以内框一起完成。重复使用同一命令识别
保留完成事实并保存原始处理器效果。
序列式抽象 `ReservationResult` 来自 `OrderOutcome`;
[密封的 `ReservationAccepted` 和 `ReservationRejected` 记录区分
有效商业结果类型,并携带原数量和剩余库存。
非正数发送 `ReservationRejected` ,并加上 `InvalidQuantity`;
缺货时,会发送到 `InsufficientStock`。两者均完整无变化的库存。
无效的不可更改的保留作为持久的商务拒绝,完全完整。
最初的发件箱意向提供了答复
在其配置的交付政策范围内。

对于非正向性加量 `Restock` ,处理人呼叫
有充分理由的<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*>
,在突变前返回,因为此命令永远不能生效。正
突变前计算所检查的新股票值,然后承诺
测试过溢出投出并跟随处理
重试策略到 `InboxRetryTimeout`。
使每个
正在重新锁定自己的稳定命令编号。 该示例是 `SetAvailableAsync`
行政绝对库存更新。
命令工作流程,增加明确的确认、到期和发布政策
保留保留时,保留保留和保留保留保留
需要搜索操作。 例如, 给释放自己的叶子
`{order}/inventory/{sku}/release`,并承诺其成果与恢复种群。
授权股票管理并产生租户/SKU grain 身份识别点的识别特征
应用程序信任的切入点 。

<a id="charge-an-external-provider-with-a-stable-key"></a>

## 用稳定的密钥向外部供应商收取费用

注册 <xref:Microsoft.Extensions.DependencyInjection.IServiceCollection>
其适配器绘制了执行该合同的关键
供应商的应急信息主管或请求字段,执行供应商的成果
和解议定书。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_payment" language="csharp":::

处理器首先验证不可改变的命令。 数量、 货币或响应
无法成为有效死字母的指定目标
<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*>,在任何提供者呼叫之前。
提供商在共享日记更改前先拨打服务。
将标记传到网关, 之后取消方法检查
等待 `SendReply` 和最终结果更新。一个供应商的错误抛出,所以
以回落重写信件。 如果服务提供商成功并激活
替换尝试使用同一供应商的钥匙,从而中断了当地承诺。
罐装信封识别码也是供应商的应急钥匙。
完成后, 框中确认保留期间重新提交。 结果查询
状态,确定性回复, 和收件箱完成共享共享 单写。

货币正常化,指令编号的范围扩大到供应商账户和租户。
将拒绝付款作为已完成成果处理;将模棱两可的供应商答复处理
使用供应商的同一密钥重试或查询合同。 为此插入状态查询
grain 确实如此,所以打电话者可以用同一个命令编号调节不确定的反应。
保留供应商在全重试时的应急能力事实;使关键长度与
当供应商需要时,确定性卡通钥匙大麻。
见 [外部副作用](messaging-idempotency.md#external-side-effects)
保留和调节责任。

<a id="converge-an-out-of-order-stock-projection"></a>

## 预测出超序库存

仪表板或搜索投影可独立接受完整的库存快照
权威的库存生产者指定单调式
同一房客/SKU的增量版本。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_projection" language="csharp":::

如果第12版在第11版本之前到达,则预测保留第12和
完成第11版被取代。重复版本与原内容相关:
以简单快照重新使用内容不同的版本或携带无效的
以 `Fail(reason)`.
版本和完整库存价值是一份持久记录。
承认完成快照命令;域版命令不同
快速命令 。

本配方适用于 **完整替换快照**。
或库存- delta 事件使用带有日记间距缓冲的连续序列政策
以及一个域定义的失踪事件的恢复路径。

<a id="publish-a-durable-notification-campaign"></a>

## 发布一项持久通知运动

一种普通的方法可以按一个商业更新后, 使几个目的地进入多个目的地。
a 运动稳定身份证明,因此,客户提交反复提交时,发现其记录的内容和
接收方设置。

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_fanout" language="csharp":::

竞选记录和每个意图的输出都在同一发件人中记录
写入日记日记写。 打字批次
<xref:Orleans.Durable.Messaging.OutboxExtensions.Send*> 帮助者编码每个收件人
在进行任何命令之前, 命令在任何开始前, 所以编码失败失败没有阶段, 然后在它们中进行
运动记录在发送成功和重复发送之后添加
提交文件使用相同的收件人指令身份和内容。
每一目的地通过下列方式独立承诺:
[通知处理器](messaging.md#deployment-requirements)每个信封
ID 是运动根下稳定的接收儿童, 具有字面 grain 身份
由钥匙厂处理的逃出 接收者保留了箱填好
运动记录也支持
`GetCampaignAsync` 查询。成功公布的答复
意味着运动和意图是持久的;接受者完成后将是一个事件。

根据日记,在您的入侵时, 检查接收人数和有效载荷大小
对于规模较大的运动,坚持一个运动光标和
释放受封的接收方块,将光标和每个块一起投入。
如果需要确认交货,则接受方答复应附有关键结果;
让运动 grain 保留竞选状态
支持的接收方完成数据
重新提交地平线。

<a id="combine-the-recipes-into-an-order-workflow"></a>

## 将食谱合并到顺序工作流程

接受几个主题的协调员登记每个输入处理器一名
本示例登记共享库存和付款结果处理人
其确定性答复记录结果
成果查询:

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_dispatcher" language="csharp":::

每次登记都与注册主体的顺序一致,并用其编号和编号编码
。每个处理器在状态更新时以相同的同步块完成。
处理后,没有匹配的处理器就解码故障和对象
重试和死字政策。

一名按级协调员坚持其预期的步骤关键和阶段,
并提交库存和付款要求,然后完成启动指挥。
将结果记录在对回应中,并在当地作出一项承诺。
步骤已经成功,一个过渡发放 `shipment/create`。

| 阶段 | 持久过渡 |
| --- | --- |
| 提交提交 | 保存预期的叶钥匙,发放库存和付款请求。 |
| 等待 | 保存每个相关参与者在回复编号下的结果; 信箱确认重复响应命令。 |
| 准备就绪 | 记录备案阶段并发出单独配有钥匙的装运请求。 |
| 被拒绝 | 记录业务拒绝,并发出已完成的参与人步骤释放/退款指令。 |
| 完成已完成 | 记录装运或补偿结果,保留工作流量状态,以便查询和审计结果。 |

每个过渡都使用相同的准备-时同步-最终街区模式。
工作流量状态中企业拒绝,
`Fail(reason)`,以及临时准备失败的例外保留。
记录了在停业期间回答状况查询的步骤结果。

<a id="schedule-a-workflow-step"></a>

## 安排工作流程步骤

grain 稍后一步安排,以发一个 `deliverAt`电文的形式发送信息
时间。本次退票确认的订单,除非宽限期已过,否则
买方首先取消:

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_grace_period" language="csharp":::

取出从“ 持久工作钟” 中计算 `deliverAt` ,它输入该时钟作为
键 <xref:System.TimeProvider> 的名称 `DurableJobTimeProviderNames.DurableJobs`。
检出收件箱接受同一日记本中的 `GracePeriodElapsed` 消息
写入文件提交文件,因此计划步骤在
`Submitted` 状态确实。 计划信件的选中时间为退签
`MaxCapacity` 直到它完成。信件在信件到期时唤醒 grain
操作者对当前运行
状态,所以取消的检出可以在不改变状态的情况下完成信件。
信息代号是提交方的身份证的孩子,是重新提交的
发现记录中的状况和时间表没有新情况。

<a id="choose-an-application-migration-pattern"></a>

## 选择应用程序迁移模式

将现有应用责任移至操作器及其输入的有效载荷
使用信箱/发件箱承诺输出意图的边界:

| 现有应用模式 | 通信模式 |
| --- | --- |
| 请求/答复 grain 方法 | 将稳定的指令编号和主体放入信封及其身体的响应目的地;处理者在完成后分级作出确定型号的答复。 |
| 更新业务,随后是远程电话 | 与业务更新一起的离岸封套阶段;确认发送州驱动器交付和重试。 |
| 外部供应商呼叫 | 使用语法指令代号来准备提供方的结果, 然后将其本地查询状态、回复和收件箱完成合并。 |
| 通知循环 | 每个稳定的接收者身份都打 `Send` ,在成功进行后记录竞选活动,等待日记的写作。 |
| 有关记录和附文 | 将应用程序记录和 `byte[]` 附件放在可连号信件中,使用打字发送和处理助手帮助器。 |

在移动应用程序工作流程时,保存命令身份和记录结果。
申请者拥有标的合同、答复路线、授权和身份证施工。
见见
[应用有效载荷演进](messaging-operations.md#evolve-application-payload-records)
用于滚动应用程序记录修改。

<a id="verify-your-handlers"></a>

## 验证您的处理器

几个缔约国 grains、待处理请求再利用的矛盾性,
当地
建立商业状态、查询结果和
测试启动恢复和模糊
对照部署中实际供应商,储存/供应结果。

可执行文件实例对按等级划分的关键项进行分级隔离,
零/负数量拒绝、短缺、
往返多态结果 答录阶段失败 没有存货突变
永久无效重新储存、支付无效和无用垃圾发光的死字母
检查后退溢出量,提供商成功/当地取消再试,
出局预测,输入多主题发送,运动批量编码
持久工作时钟和普通综合信息
核对原始答复的实际日记账确认和
测试端对端重新提交时的确切完成-保留边界。
运行期保证和操作控制,见
[通信业务](messaging-operations.md).
