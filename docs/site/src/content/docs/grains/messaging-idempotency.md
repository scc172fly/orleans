---
title: "通信通信应急能力"
description: "设计应用指令身份、等级钥匙、保持完成事实和外部应急能力。"
ms.date: 10/09/2026
ms.topic: conceptual
---

<a id="messaging-idempotency"></a>

# 通信通信应急能力

应用程序为每个逻辑命令指定一个稳定的等级标识符。
在整个入境、回程、完成、诊断和
接收收件箱保留的完成记录确认重复
任何 grain提交该命令。

承诺协议见 [通信通信](messaging.md)。
完整 grain 示例见 [实用实用食谱](messaging-recipes.md)。

<a id="choose-an-identity-at-each-boundary"></a>

## 在每一边界选择一个身份

| 身份特征 | 所有人和范围 | 目的目的宗旨 |
| --- | --- | --- |
| 信封 `MessageId` | 接收器 grain收件框中精确密钥 | 等待重复的果实,确认保留期间已完成的命令。 |
| 信封 `MessageId` | 发送 grain发件箱中精确密钥 | 将一个信息放入其目的地、主题和身体,等待发送。 |
| 工作流程根 | 申请程序协议 | 与组相关的命令编号和确定响应编号。 |
| 外部应急能力关键 | 外部系统的账户或 API 命名空间 | 使用“卡尼科命令编号”确认重新研究的外部副作用。 |

生成一个来自稳定租户、实体和行动识别码的指令身份
发送。每次重新输入和重新编辑的提交文件都使用原始 ID。Namespace
转发 grain 保留原
新的工作流程命令
使用独生子女或兄弟姐妹身份证;扇形输出产生一个稳定的接收儿童
每个目的地。

关键之一确定了不可改变的命令:目的、主题和身体保留
真正的新保留,授权赔偿或
修改后版本使用新的密钥。
即使生产者和消费者在不同时间被部署, 也仍然可以命令 。

<a id="transport-deduplication"></a>

## 运输的重复

对于交付货物,收件箱回答待发或已完成的跟踪身份
所派遣的, `SentAt`,包括后一个接收器时钟倒回。
其发送时间在或低于入场层的未跟踪身份可得到
`Expired`. 在楼层上方,收件箱将以下文件的未跟踪身份 `SentAt` 绑定为:
接收者的持久工作时间加
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow>;
承认平等,而后弃
<xref:System.ArgumentOutOfRangeException> with no staged change or sink offer. The
发送发送者在原始预算范围内重新列印, 所以诊断时钟扭曲或错误
直接打电话的人被钉上印章

A grain给自己使用本地中转时间或首次以上时间的电文
后时钟跳过后,入会层在倒时空跳过之后,后一层仍然仅超出
压实地板,即使超过交货时间约束。

每个接收器检查精确的 `MessageId`, 确定一个命令
父母及其子女是独立的,不论发件人或受送人或受体。
命令。待处理的重复文件是指已经接受的工作,并带有
同一主题、正文字节和 `DeliverAt` 时间;不同
主题、身体或 `DeliverAt` 失败。
填全的重复返回 `Duplicate` ,同时保留其完成记录。
尚待处理和完成的记录保留身份证和
<xref:Orleans.Durable.Messaging.InboxMessage.SentAt> which the inbox accepted. A repeat
记录在接收者回答前是耐久的。
`Duplicate` 或重复其记录中的拒绝。
申请支持不可改变指挥合同;完成事实
承认原执行。

处理器程序将它的业务变化和发送信息, 然后调用
<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Complete*>,该分级框
清除和完成记录。 一份日记将它们一起存在, 然后重放
将恢复业务效果和完成记录一起并列。
逻辑发送一次应用。处理者可以执行
同一身份识别器不止一次,例如,在尝试失败或激活失败后
损失,所以它的准备必须容忍重复。

发送日记从持久工作钟中写入记录 `SentAt` 。
和回收利用,包括后压,以及
将预算结尾重试 `SentAt + OutboxRetryTimeout`。配置 `OutboxRetryTimeout`
短于
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow>.
每个收件箱日记本都有非降排的入场楼层。
<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow>
所观察到的“持久工作”在最低进步时间上有多远,控制了多少距离。
完成的记录一旦被接受,即有资格删除 `SentAt` 时
光照确认并恢复它们。
交货延迟,且原发件时间为原来发送时间,然后收到 `Expired`
任何下水道或处理器运行。 前时钟跳跃可以过期, 提前发送; 向后发送
跳跃可以保留更长的历史。 通过处理率和留存率,预算历史可以保持更长的历史。
应用结果查询保留办法独立满足业务查询和审计需求。

`Expired` 表示先前的接受尚不清楚:确认可能已经
在接收器压缩其完成记录之前丢失了记录。
指定另一个命令前使用原始 ID。自动发件箱
重试和恢复保留原 `SentAt`;直接发货呼叫
退休后新的发箱申请得到一个新的中转站
之后的新的划界案
保留身份的端点,在新的发送时间是
承认;在批准之前核对原始结果。

发件箱将共享待定信件 ID 的等同信息合并到
等同信息具有相同的目的地、主题、目的和目的。
有效载荷字元, 和 `DeliverAt` 时间; 原始信件及其信头被保留。 重用待处理的 ID
将内容投放并保留原始信息。

grain 发送到它自己的邮件只用 grain的收件箱中键入。 `Send`
接球者同步规则:同一主题的待决重复, 体字节,
和 `DeliverAt` 时间合并、保留完整的ID为重复,待处理
本地发送接收一个中继时间
包括时钟倒回后。
身份保留了记录被接受的发送时间

<a id="hierarchical-business-operation-keys"></a>

## 等级级商业业务操作钥匙

<xref:Orleans.Durable.Messaging.MessageId> identifies a message by wrapping a
<xref:Orleans.Durable.Messaging.HierarchicalKey>. Construct it with `new MessageId(key)` or
<xref:Orleans.Durable.Messaging.MessageId.Create*>,然后读取键
<xref:Orleans.Durable.Messaging.MessageId.Key>. Message IDs compare, hash, parse, and format
穿过那个钥匙

<xref:Orleans.Durable.Messaging.HierarchicalKey> is an immutable value type wrapping its
平、 `==`、 `!=`和散列比较这条路径
提供对段的导航
<xref:Orleans.Durable.Messaging.HierarchicalKey.GetParent*>,
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsParentOf*>,
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsChildOf*>,
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsAncestorOf*>和一个段数计算器。
`default(HierarchicalKey)` 是指被 `IsDefault`暴露的未设定值;信封
验证需要设定的ID。

一个有用的等级是:

```text
tenants/{tenant}/orders/{order}
  inventory/{sku}/reserve
  inventory/{sku}/release
  payment/charge
  payment/refund/{refund}
  shipment/create
```

根根组对工作流程进行分组;每一叶叶都确定了一个效果。
指定一个稳定的尝试/修改段,在批准时一次指定
新的效果。
构建前。 键比较保留了它的正则拼写 。

<xref:Orleans.Durable.Messaging.HierarchicalKey.Create*> 接受字面部分,
和 <xref:Orleans.Durable.Messaging.HierarchicalKey.CreateChildKey*> ,仅附加一字
。 构造
`payment/charge` ,使用单独的 `"payment"` 和 `"charge"` 参数。
<xref:Orleans.Durable.Messaging.HierarchicalKey.Parse*> 读经,
并格式化发布卡通式代表。

下列助手使用字面租户和SKU识别符构建命令代号:

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_operation_keys" language="csharp":::

SKU `widget/blue` 占用一个字句段:

:::code source="../snippets/compiled-durable/Grains/MessagingRecipes.cs" id="messaging_hierarchy" language="csharp":::

制作者、收件箱、日志、结果查询使用相同的卡尼卡身份,
和外部提供者键。
<xref:Orleans.Durable.Messaging.HierarchicalKey.IsAncestorOf*> 相片段匹配
边界: `orders/42` 组 `orders/42/payment`,而 `orders/420/payment`
属于其它顺序。密钥是它自己的祖先; `IsChildOf` 测试完全是一个
附加的段。调试总是比较整键。

获得授权的承租人/实体根根取出被信任的内地,并核实索赔
键,在接受工作之前,命令、主题和目的地将与之相对。
表示身份; 授权来自信任的应用边界。
发送和传送限制在1 024 UTF-8字节和a
受受控的256 UTF-8 字节。

<a id="persist-a-result-not-just-a-flag"></a>

<a id="persist-queryable-results"></a>

## 持久性可查询结果

处理器可以制作一个耐久的回答,其身份是命令稳定的 `result` 孩子。
下 [清单清单配制](messaging-recipes.md#reserve-inventory-once-per-order-line)
计算其保留决定和下一批本地库存。 `SendReply`
编码和分解回复的状态,然后方法更新库存并同步完成。 短缺是完全拒绝, 没有变化
最初承诺的发箱意向用品在
重新提交确认保留完成事实
保留了原来的执行。

收件箱 `Duplicate` 确认待处理或已完成的指挥身份。
或询问
同一命令的同一个命令 ID。不同的查询命令可以在原始
答复的交付政策结束。

需要时保存保留记录、备查、放行或过期失效,付款
现状和调节结果,以及进展查询工作流程阶段的结果。
这些是商业状态,明确保留询问/审计保留。
带有本地效果、 输出意图和收件箱完成。 The
[付款配方](messaging-recipes.md#charge-an-external-provider-with-a-stable-key)
显示原始命令ID的结果查询。

<a id="external-side-effects"></a>

## 外部副作用

外部付款、电子邮件或 HTTP 操作有自己的承诺边界。
操作成功后, 且结果被日记前, 激活可能失败 。
支付配方在当地准备时,
`context.Message.Envelope.MessageId.ToString()`. 提供方强制执行要求一致性
返回该键的重试时的原始结果。

指定提供方账户/租赁商命名空间、密钥格式、保留、冲突
和结果查询 API 作为整合的一部分。
每一重试或重放,只要能达到它,其所有重试或重播都具备充分的事实。
关键长限制,适配器可以确定地按速
校正模糊
使用原始键对响应或重试。如果 API 结果不确定,则需要
在批准另一影响之前,其调节工作流程。

一旦结果被公布,观察取消、发送打印的回复并完成
被日记的结果和 `Complete()` ,没有等待。
提供商在第一次共享突变之前拨打服务。

<a id="fan-out-and-multi-grain-workflows"></a>

## 扇出和多grain 工作流程

给每个参与者或一步给每一个参与者或一步给自己的命令编号, 在工作流程根下。 1个现场
将关键地图发送到一个目的地,因此通知使用稳定的、针对特定受援方的
儿童协调员提交的一份记录提交了步骤和综合相关结果
参与者承诺其效果和决定性答复
原子;协调员将每个收到的结果和下一步的意图一起承诺。

使用耐久步骤状态,如 `Requested`、 `Succeeded`、 `Rejected`,或
`Compensated` 与参与者结果。拒绝可单独发出有键
已完成步骤的释放或退款命令。
使工作流程有一个明确的前方
独立承诺的 grain 期刊,以及恢复路径。

<a id="ordering-and-replay"></a>

## 秩序和重放

| 业务 | 应用政策 |
| --- | --- |
| 储备库存或收取付款 | 通过重试保存命令代号,并保留其信箱完成事实,以用于支持的地平线。 |
| a 用完整的快照取代投影 | 只应用较新的域名版本; 完成被取代的旧快照 。 |
| 应用每个递增事件 | 在适用毗连事件之前,应始终保持预期的顺序和缓冲缺口。 |
| 以正确逻辑进行有意识的再处理 | 授权使用新的修订关键字,记录其与原始结果的关系。 |
| 重放收件箱已用过的信封 | 以原始身份识别符为键的调和效果,然后授权使用新的修改键,例如原名的子节;保留完成回答 `Duplicate`;未跟踪的旧发送时间答 `Expired`。 |

信件身份识别确认已完成命令; 域版或序列
建立顺序。
[预测投影配方](messaging-recipes.md#converge-an-out-of-order-stock-projection)
显示完整光谱政策。
