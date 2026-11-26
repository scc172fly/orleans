---
title: "运行发送信息"
description: "配置能力和保留、诊断交付、重放死字母和测量输送量。"
ms.date: 10/10/2026
ms.topic: how-to
---

<a id="operate-messaging"></a>

# 运行发送信息

使用此指南配置和操作
[通信通信](messaging.md).关于企业重播政策,见
[能力与等级键](messaging-idempotency.md).

<a id="configure-storage-and-execution"></a>

## 配置存储和执行

配置日记存储和持久工作存储,然后发送信息。
商业状态、结果查询状态和信息传递状态都通过同样的方式持续存在
grain 日记。 收件箱和发件箱登记该日记
`orleans.messaging.inbox` 和 `orleans.messaging.outbox`;提供申请说明
其它名称。 信件使用 grain配置的日记格式、 JSON 或 Orleans
二进制。

每个启动程序都在其调度器上运行一个信息泵,处理收件箱信息
准备期间等待的处理器延迟, 下一个 grain
独立地在其它 grains 信息的同时独立进行进度。
诸如租户/账户、租户/SKU或订单等所有权。 grain 设定约束
相继输送量; 如果有独立所有人, 添加 silos 增加能力
以分配。

存储配置,见 [日记日报配置](journaling/configuration.md),
[Azure 储存供应商](journaling/azure-storage.md), 和
[持久就业移徙](journaling/durable-jobs-migration.md).

<a id="capacity-retries-and-retention"></a>

## 能力、重试和保留

<xref:Orleans.Durable.Messaging.Configuration.DurableMessagingOptions> configures both inbox and outbox
通过 `AddMessaging`行为,并在启动时验证。

| 设置设置设置设置 | 默认默认 | 实际效果 |
| --- | --- | --- |
| `MaxCapacity` | 1,000 | 正在打开待发的收件箱信息。 `DeliverAt` 时间, 从接受时计数到完成。 新信件收 `Backpressured` 键;重复现有信件接收 `Duplicate`信息a grain 不论期限长短,凡被派往自己的单位,都可获准入境。 |
| `DeduplicationWindow` | 7天7天 | 将完整的身份保留到其被接受的 `SentAt` 在或低于持久录取底层时。观察到的“持久工作”的预付地面工资减去这一时期;确认的快照和收回合格记录。楼层上未跟踪的交货时间由接收器时间加上这个窗口相隔;自发发送使用当地时钟,或者回滚后第一次在地板上方。 |
| `RetryInitialInterval` | 1 秒 | 第一次处理信箱的重新尝试或发件箱交付之前的延迟。 每次重试都会将延误加倍, 最多为本间隔的64倍 。 |
| `InboxRetryTimeout` | 6小时6时 | 当处理器在第一次失败后如此长的一段时间一直失败, 信件会移动到收件箱中已死的信件。 最后一次尝试是在超时过后, 而重试信件将保留它的位置 `MaxCapacity`. `TimeSpan.Zero` 第二个失败时的遗书, 因为恢复无法从崩溃中断前的失败中看出失败尝试。 错误停止后( 如崩溃或处理失败未遂)的恢复过程对每条正确信息都算一个尝试; 优雅的解除功能不算一个。 |
| `OutboxRetryTimeout` | 6小时6时 | 绕过尝试直到 `SentAt + OutboxRetryTimeout`,包括后压。发送的日记本 `SentAt`,以及每一次尝试和收回都保存着它。必须短于 `DeduplicationWindow`。 |
| `DeadLetterRetentionPeriod` | 30天 | 删除超过此年龄的死信,使用 grain时钟进行时间戳和保留。 |
| `MaxRetainedDeadLetters` | 1,000 | 隔开信箱和寄件箱的死信 将最老的信封先移走 |
| `InboxBatchSize` | 32 | 检查每个泵通行证处理的收件箱信息。 |
| `OutboxBatchSize` | 32 | 绕过一个 grain的飞行中发送尝试。每次尝试都自行完成并释放空位,因此慢速接收器有一个空位,不延迟向其他接收器发来信息。 |

选择用于可容忍的断线和后压、 接纳的重试预算
保留保存历史费用留存期的留存期。
原始日记 <xref:Orleans.Durable.Messaging.InboxMessage.SentAt>。接收器
首先是追踪身份:待处理或已完成的记录答复
每次重复并保持发送时间 收件箱接受。
下层或下层的未跟踪发送时间返回 `Expired`。
地层只能进步,恢复才能存活。
或保留更多历史,而寄出时间和可持久底层则保留
接收原始发送决定。

未来确认后,在接收楼层以上未跟踪的交货将受约束。
不论 `SentAt`如何,其追踪身份都得到记录答复,包括
时钟倒回后。 在入场层或下方的未跟踪时间可接收
`Expired`,即使一个大的回滚 让它超过接收时间加上窗口。
超过这一界限的任何其他未跟踪的时间
<xref:System.ArgumentOutOfRangeException> without a journal change or sink offer;
承认在约束方面的平等。

A grain给自己使用本地中转时间或首次以上时间的电文
第一次还是
超过现有压缩下限的
交货时间- 约束。

保留 " 死字保留 " 适用于启动激活和新死亡时
简快的一幕持续着其入场底和紧凑的历史
模拟完成记录一直保留到承认底楼。
按处理率和配置的留存期分列的预算历史,包括
由钟声和后来重新提交时保留的额外历史。

收件箱能力供应
输出箱深度增长时应用。设定每个租户有效载荷和提交限制:
字节和序列化成本事项以及信息计数。
拒绝信件编号超过 1 024 UTF-8 字节,主题大于 256 UTF-8
任何耐久变化之前的字节。

一次性活泵的故障保险是每 grain 一个耐用工作,即:
工作一旦持久,即立即恢复。
恢复后的工作,以及后来应完成的工作,在重新试验时唤醒 grain 或
`DeliverAt` 时间。 写作时, 只有当没有跟踪工作时, 只有在该工作到期时才安排工作
第二, 因此忙碌的 grain 无法执行每个写作的持久工作操作。 抽动任务
激活失败时, 以后端上限值进行无限期重试, 每次激活都失败
重新武装从收复状态中唤醒的提醒。 恢复时间将重试合并
计划、持久就业投票和启动延迟。

排定的邮件保留在接收器日记和快照中,直到到期为止
占领和占领 `MaxCapacity` 将他们的容量、储存和回收预算
收件箱深度计数器包括了这些内容。

<a id="interpret-delivery-and-completion"></a>

## 口译的交付和完成

| 交付状态 | 意义和反应 |
| --- | --- |
| `Accepted` | 接收器将消息记下来。 请用相同的命令编号等待持久回复或询问商务状态 。 |
| `Duplicate` | 收件人在保留范围内有该信件的代号, 正在保存中等待或完成。 发件箱删除了此信件; 业务结果来自答复或查询。 重复一个信箱汇拒绝的信息, 则得到原始 `DeadLettered` 结果 。 |
| `Backpressured` | 接收器在容量上, 发箱重试。 检查接收器深度、 最老的电文使用年龄和处理器的延时时间 。 |
| `HandlerNotFound` | 接收器没有注册的操作员。 发件箱记录错误和重试。 在激活和部署期间检查处理器登记时被扭曲 。 |
| `DeadLettered` | 收件人以一个理由和可选应用程序定义的代码,长期拒绝该电文。 发件箱删除了电文,源提供的信息则以该原因和代码作为 `Rejected` 退位。 |
| `Expired` | 发送时间在接收器的持久接收层或以下,例如交付延迟过 `SentAt + DeduplicationWindow` 或接收器时钟跳出后。 早期接受未知。 发件箱保留了一纸空文, 将源信息退为 `DeadLettered`。 在批准再次发送前通过应用程序对调 。 |
| 任何其他地位 | 发送者无法解释状态, 例如从新接收器中读取一个, 所以结果未知。 发件人重试直到 `OutboxRetryTimeout`。 |

`Accepted`、 `Duplicate`、 `DeadLettered`和 `Expired` 是终点状态。

发件人的日记写确定了其发出信件的耐久性。
接受后确定收件箱条目的耐久性。处理器的日记写
使用相关回复
端对端业务确认。

<a id="diagnose-failures-by-boundary"></a>

## 按边界划分的诊断失败

| 症状 | 边界和补救 |
| --- | --- |
| 重复处理器错误 | 验证标的登记、 机构兼容性、 命令标识、 响应目的地和准备依赖性。 手持者例外情形解除 grain ,在恢复后重新尝试直到 `InboxRetryTimeout`,同时持有一个容量档位, 以便保留它们用于短暂故障。 使用 `Fail(reason)`的字母为永久无效输入, 或者发送打印拒绝回复并拨打 `Complete()`。 |
| 信件中以“ 没有注册处理者” 挂载的字母 | 处理器已注册注册, 但没有人接受主题。 信件在被重新搜索到 `InboxRetryTimeout`。为每个主题注册一个处理者 grain 接收。 |
| 正在对待指令重新使用发生冲突 | 发送者或接收者丢弃 `InvalidOperationException`。保留原命令编号、主题、目的地和有效载荷;使用单独的ID来实施新的命令。 |
|  处理器故障后停止使用 Grain | 处理器丢弃或返回时未拨打 `Complete()` 或 `Fail(reason)`。从日记回收后,将放弃其分阶段的更改并计算尝试,所以信件会回溯到 `InboxRetryTimeout`。保留短暂错误的例外,并在第一次修改之前移动可撤销的工作 。 |
| 带 `The activation stopped without recording the outcome of an attempt.` 的死信  | 恢复计数不干净的停止在信件重新试窗口启动后中断, 窗口也过期了。 在 grain日志中寻找一个失败的处理者或多次撞车。 一次坠机就计算了一个尝试, 并且从未用死字来写信息 。 |
|  处理器返回后 Grain 停止使用 | 处理者调用 `Complete()` 和 `Fail(reason)` ,但结果有冲突。确保每个分支阶段都完全有一个结果。 |
| `Complete()` 后记录的处理器错误  | 分阶段完成和发送信件的状态持续。 使用指定状态诊断错误 。 |
| Grain 储存或中继故障后停止使用 | 使用否决权的日记记录会将激活和 grain 失效进行逐个发布。 下一次启动会收回承诺的日记并恢复未完成的工作。 |
| 发件箱的死信,其理由说明重新尝试超时已过 | 原始发送 `OutboxRetryTimeout`中未记录终端回复。 请检查接收器可用性和处理器注册, 然后在授权重播前核对可能接受的命令 。 |
| 发送者用 `Expired` 和未知接收机结果的死信 | 发货被延迟过 `SentAt + DeduplicationWindow`, 或者前方接收器钟跳过了其持久的入场底。 通过应用程序来调和商业状态,检查发送/收件器钟以及大小 `DeduplicationWindow` , 以备容忍的发货延误和时钟变异。 `OutboxRetryTimeout` 保持比窗口更短的时间 。 |
| 收件人警告 `SentAt` 远超前; 发送者重试 `ArgumentOutOfRangeException` | 接收站楼上方的未跟踪身份在接收时间之前,超过 `DeduplicationWindow`。检查时钟和直接调用的时间戳创建情况。 记录的身份在回滚后仍然得到记录的答复。 保留原始逻辑发送时间到重试; 一旦其最初的预算重试结束, 在批准新提交文件前核对通过申请书发送的已写字母。 |
| 取消调用器 | 被接受的书写或交付继续通过实际结果进行。 询问持久的商业地位或用同一指令身份证重新尝试。 |

处理器采用两阶段模式:所有非同步和不可靠的制作用途
本地值, 以及最后阶段发送信息, 应用业务变化,
在一个同步块中调用 `Complete()` 。任何日记都写在激活上
捕捉 grain的分阶段更改,在
调查意外部分影响。

<a id="inspect-and-replay-dead-letters"></a>

## 检查和重放已死字母

说明经应用授权的诊断方法
<xref:Orleans.Durable.Messaging.IMessagingDiagnostics.InboxDeadLetters>
和 <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.OutboxDeadLetters>。
每一记录都包括信息、尝试数、失败原因和死字时间。
收件箱信息汇的拒绝是一种处理过的记录,而不是一纸空文;
发件人发信的发件人的发件人发信箱收到其理由和代码。
<xref:Orleans.Durable.Messaging.IOutboxMessageSource> failed to provide it carries only the
电文代示 ID,带有空有效载荷和默认接收器;其原因记录
源代码失败 。

<xref:Orleans.Durable.Messaging.IInboxHandlerContext.Fail*> 创建一个收件箱已绝信
将信息识别码记录为处理过的 所以
处理后邮件对计报告 `dead_lettered` 和重复返回 `Duplicate`
正常业务拒绝被键入应用程序
承诺作出的答复中承诺作出 `Complete()`; ; 和
[清单清单配制](messaging-recipes.md#reserve-inventory-once-per-order-line)
明确接受和拒绝保留结果的模式。

远程检查时,应披露一种 grain 方法,该方法复制记录的身份,
应用 DTO 时标、主题、理由、时间戳和相关有效载荷场。
死信封持有原始有效载荷和信头阵列;当
应用需要可变缓冲。

留存的故障记录提供有约束性的业务证据:
调节外部效应,修理生产商或
处理器, 并授权更正提交文件。 在保留前先采集证据
将它除去,并授权重放和诊断访问,与普通的分开
提交材料。

<a id="guarantee-application-follow-up"></a>

### 保证申请后续保障

终端交付时使用 <xref:Orleans.Durable.Messaging.IOutboxMessageSource>
要求应用行动的结果。
<xref:Orleans.Durable.Messaging.IOutboxMessageSource.OnRetired*>,同步地
编辑应用程序中自处理的后续信息
输出箱清除、结果和后续
重新输入待处理条目和
回调; 承诺后恢复恢复通过收件箱的耐用性继续跟进
后续处理者可以询问参与者,调和未知影响,
并承诺执行申请的下一个决定。

`DeadLettered` 退休包括重新尝试用尽、 `Expired` 答案和来源
早期接受失败。 接受日期早于是否为未知; `Rejected` 确定一个耐久
接受拒绝。保持通过恢复登记源所有权。
例外是启动失败, 并保存最后承诺的待入。
应用结果和工作保留遵循应用的商业政策;
诊断性死单收集 遵循其配置的能力和年龄

接收器记录了每个收件箱 死亡信件的讯息 身份和处理,所以
原始发送接收 `Duplicate` ,完成部分保留和 `Expired`
发信时间过后,会场上不设发信时间,记录压缩。
表示先前接受时丢失了答案。重放后将进行对账。

重放一个发箱的死信:

1. 维修接收机可用性、操作器登记或有效载荷兼容性
   问题,并查询参与企业状态 原始指令编号。
2. 调和原始结果,然后仅在以下情况下授权提交新划界案:
   商业协议允许使用。 <xref:Orleans.Durable.Messaging.IOutbox.Send*> 在
   原始条目已退出。
   结果; 被接纳的新发送时间可以创建另一个
   身份保留结束后生效。在授权时使用修改编号
   a 纠正或重复产生,并记录其与原件的关系。
3. 记录重放配置,删除记录
   <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveOutboxDeadLetter*>. The
   下一期日记的写作中, 保留着处置、删除和重播信件。

重放收件箱的死信:

1. 修理备用性依赖、操作员或有效载荷兼容性问题。
2. 以《京都议定书》为关键
   并调和失败尝试应用的任何效果。
3. 重新根据新的修订身份识别码(例如该命令的子密钥)重新上传命令
   例如, `{original}/replay/1`原案文,通过经授权的 grain 方法。
   记录修订与原始ID之间的关系。
4. 记录重放配置,删除记录
   <xref:Orleans.Durable.Messaging.IMessagingDiagnostics.RemoveInboxDeadLetter*>. The
   下一期期刊的著作中,仍然保留着处置、删除和外向修订。

外部提供者的留存和查询协议管辖原始方是否
能够安全地重新研究效果;在批准前将其与原始密钥调和
a 订正。

<a id="observe-progress-and-size-the-system"></a>

## 观察系统的进展和规模

下 `Microsoft.Orleans` 公尺数暴露了这些信息工具。
带有 `grain_type` 标签;有结果的对数也带有 `status` 标记。

| 文书文书文书文书 | 类型类型 | 口译口译 口译口译口译口译口译口译口译口译口译口译口译口译口译口译口译口译 |
| --- | --- | --- |
| `orleans-messaging-inbox-messages-received` | 柜台 | 接收方的交付结果: `accepted`、 `duplicate`、 `backpressured`、 `handler_not_found`或 `expired` (发送时间太旧)。发出后, grain 会自动记录 `accepted` 或 `duplicate` 。信箱槽会记录 `accepted`,或者当水槽拒绝时,则 `dead_lettered` 。 |
| `orleans-messaging-inbox-messages-processed` | 柜台 | 处理结果: `success`、 `retry`或 `dead_lettered`。汇的接收记录 `success`;汇的拒绝记录 `dead_lettered`。 |
| `orleans-messaging-outbox-messages-sent` | 柜台 | 以 `Send`制备的信件,包括向自己发送的 grain 信件,该信件绕过发件箱。 |
| `orleans-messaging-outbox-messages-delivered` | 柜台 | 发送者交付尝试结果: `accepted`, `duplicate`, `deadlettered` (持久的接受拒绝), `expired` (那日,是太旧了) `backpressured`, `handler_not_found`,或 `error` 没有任何记录地退职的讯息 `withdrawn` (来源撤回电文) `expired` (以重新试验已逝者的名义) `source_error` (自有来源未提供电文),或 `unprovided` 发送者无法解释记录其小写名称的状态 。 |
| `orleans-messaging-inbox-processing-duration` | 直方图, ms | 处理时间。 |
| `orleans-messaging-outbox-delivery-duration` | 直方图, ms | 执行期。 |
| `orleans-messaging-inbox-depth` | UpDownCounter | 正在运行 grains的待发收件箱信息 。 |
| `orleans-messaging-outbox-depth` | UpDownCounter | 正在运行 grains的发件箱信件。 |

深度仪器报告在信息添加和删除后,深度仪器报告变化,发布时间
当一个启动的讯息停止时, 激活会贡献。 他们的总和是当前
正在运行 grains中待完成的工作。

`Orleans.Messaging` 诊断倾听器、 `MessagingEvents`报告过渡情况
同步地激活它们,同时使用其 `IGrainContext`:

| 活动活动 | 升升 |
| --- | --- |
| `InboxMessageReceiving` | 新的发送时间在接收层以上, 在信箱阶段信件效果之前。 观察者的例外情形失败了, 就像一个汇的例外, 所以发送者会重新检修它 。 |
| `OutboxRetryScheduled` | 一次失败的运送尝试被重新安排,并计算出其企图、错误和下一次尝试时间。 |
| `OutboxMessageRetired` | 以已发、已发或已发或已发或已发的文字形式已退休的电文。 |

测试是否订阅 `MessagingEvents.AllEvents`,或使用 `DiagnosticEventCollector`
等待过渡而不是投票,并从一个
`InboxMessageReceiving` 观察员。

发件箱记录其送货单、发送信息,并在 `Debug`记录后压;
无法在 `Warning`解释的过期未提供的邮件和状态;以及
`HandlerNotFound` 答复、交付错误和源源失效 `Error`.

与该应用软件遥测管道有关的出口指标。
发育,最晚的等待年龄 从应用诊断, 反复失败的结果,
结合这些与《日刊》和《持久工作》的健康信号。
逻辑直指指令身份和事件调查对象,并保留
高心胸房客,在日志或痕迹中订购钥匙。

能力包括待定信息、有序列的机构、完成记录,
业务查询结果,并保留了不写的信件。
测量激活回放时间和存储量
与稳定状态处理同时进行。

<a id="budget-allocations-and-owned-memory"></a>

<a id="budget-allocations-and-managed-memory"></a>

### 预算拨款和管理下的记忆

每个信件都持有有效载荷阵列和可选的指定信头阵列, 所以信息大小
预算待编编码
字元、阵列间接费用和GC活动,以及信息传递记录和应用
。根据
日记能力和潜伏预算。

发箱状态、箱状态、处理人背景和交付业务参考
使用有效载荷数矩阵时,它们会使用。删除信件使其数组符合条件
其它引用端后收藏的参考文件。 见
[有效载荷和信头阵列](messaging.md#use-gc-owned-payload-arrays).

| 预算预算预算预算预算预算预算预算预算预算预算预算预算预算预算预算预算预算预算 | 计量度量 |
| --- | --- |
| 处理处理分配款 | 热调后, 管理整个进程分配的字节, 由已确认的信件除以 。 |
| 待 待 工 | 收件箱和发件箱深度、主动投送量计数以及序列有效载荷字节。 |
| 留存数据 | 已完成的信息标识和查询/审计结果,包括已配置的留存以及日记历史和快照。 |
| 支持内存 | 序列-元数据主要内容、序列-缓冲能力以及飞行中作业参考。 |
| 持久性和恢复工作 | 日记附录和快照以及持久工作提醒时间安排。 |

测量长期和许多目的地工作量的连续链
分配式堆放追踪将成本归因于生产成本
路径。比较同一有效载荷、链长、供应商和
使用未跟踪运行的运行进行潜伏比较。

<a id="evolve-application-payload-records"></a>

## 演变应用有效有效有效有效载荷记录

保持应用程序主题、命令标识和序列识别符稳定。
使用 `GenerateSerializer` 和稳定 `Id` 成员的有效有效载荷记录;保持阅读器
版本业务术语和主题
与有效载荷或操作政策变化不兼容。

生产商发布新主题前, 安装新主题的处理器。 退出后
处理的有待发信息、保留已写完的信件和授权重播。
坐标卡尼基编码更改, 以便现有命令保持身份 。

受信任的入侵的衍生房客所有权和授权目的地。
要求命令编号、对象和身体响应目的地
准备外部影响或改变商业状态之前先拥有所有权。
序列有效载荷大小,并保密 身体、钥匙和诊断日志。

<a id="measure-sequential-throughput"></a>

## 连续测量量

存储库 `Messaging.Sequential` 基准衡量单链至2,
4, 8 交互 grains 在一个 silo连续执行 1 024个连续耐久性
每一发件人每次发送的电话,等待每个处理者的日记确认
每个 grain 的制表器中都有一个固定代表,在结构器中登记一个打字处理器
以接受该 grain 作为其论点。处理者用打字的方式分期进行下一跳
`Send`,更新其柜台并完成信息。
`runs/{invocation}/hops/{hop}` 命令ID,带有变量数格式化。
检查确认和清理检查的准确总数
/ 人- 人grain 商业效果计数。

从仓库根运行 :

```powershell
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --job Dry --buildTimeout 600 --noOverwrite
dotnet run --project test\Benchmarks\Benchmarks.csproj --configuration Release --framework net11.0 --no-build -- Messaging.Sequential --filter "*SequentialMessagingBenchmark*" --buildTimeout 600 --noOverwrite
```

建设超时,可容纳孤立地建立基准依赖性
图形。“干运行”验证执行。测量的报表将 `Mean` 对一个报告进行正对
已经确认的消息:通过量为 `1 / Mean` , `Mean` 以秒表示。
基准重新创造,温暖每个测量迭代值之外组群的热度
其变化不定的日记和记忆中的作业分离出本地
使用部署的持久性提供者,交叉silo 安置,
和独立的生产能力计量连锁和并行链。
硬件、运行时间、承诺、提供者和基准预设,每次比较都设定。

使用 `--memory` 来报告每条已确认的全天电文所分配的经管理字节
。比较相同的 grain 计数、链长和存储阈值。
移动存储默认为100个附件或1 MiB , 以相片间隔; 见
[发展储存](journaling/configuration.md#development-storage) 两者的
以及它们的记忆和重现权衡
