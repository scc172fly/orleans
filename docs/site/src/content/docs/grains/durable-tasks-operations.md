---
title: "执行持久任务"
description: "配置保留和存储、检查任务状态、诊断重现差异、版本工作流程、中止被搁置的任务、处理过期信件以及工作量的持久存在。"
ms.date: 10/16/2026
ms.topic: how-to
---

<a id="operate-durable-tasks"></a>

# 执行持久任务

使用此指南配置和操作
[持久任务](durable-tasks.md). 持久的任务超过通讯,所以
[通信业务指南](messaging-operations.md) 也适用于电文
其带有的发明、结果、取消、信号和延迟恢复。

<a id="configure-storage-and-retention"></a>

## 配置存储和保留

`AddDurableTasks` 排在《公约》 silo 建筑公司增加信息传输,这增加了可持久工作,以及
在每个 grain日记账中以日记状态的形式显示任务状态
`orleans.durable-tasks.runtime`.给应用程序提供其他名称。
grain日记格式、 JSON 或 Orleans 二进制; `AddDurableTasks` 登记了 JSON
任务状态元数据、请求、结果和信号值的存储
Orleans- 定序有效载荷。 grain日记是任务状态的真伪来源;
测试将它保存在变化不定的日记存储(例如 `AddVolatileJournalStorage`) 时, 以内存为条件 。
启动设置无法在没有国家管理者日记的情况下实现耐用 grain 。

`DurableTaskOptions` 配置保留死刑和通过
`AddDurableTasks` 回调, 启动时验证 。

| 设置设置设置设置 | 默认默认 | 实际效果 |
| --- | --- | --- |
| `ResultRetentionPeriod` | 1天1天 | 保留一项任务的最后结果,该任务没有呼叫 grain,例如客户计划或分离的任务,用于投票,在未签名的完成后这一段时间。任务编号重复了完成后的这段时间的时间表。决不能是负的。 |
| `StalledExecutionRetryInterval` | 1分钟1分 | 频率 grain 持有 [执行停滞](durable-tasks-versioning.md#stalled-executions) 提醒,一旦激活结束, 启动后将重播它运行的代码上的执行。 必须是正数 。 |

`DurableMessagingOptions` 中的信息通信设置管理运输;见
[能力、重试和保留](messaging-operations.md#capacity-retries-and-retention).

- `OutboxRetryTimeout` 约束发件箱多久重试一次 请求、完成、
  取消, 或者在发送失败后发出信号 之后不按字母顺序进行删除 。
- `DeduplicationWindow` 重复重复重发的持久任务信息,与其他
  信息。它也是个接受召号的窗口: grain 在
  自发货人首次提出,而后又写了一堆废纸以来,这一时期已经过去。
  持有 `durable-rpc/expired` 代码的后压或输油管。
  在援引前发出的取消信号或信号,留置两次
  时间段,包括发送者时钟与 grain时钟之间的斜线,所以被接纳
  请求终止为取消,或观察信号。
  时间没有到,所以删除 grain 记录警告。
- 直接接受任务
  状态, 这样他们不占用收件箱容量。 延时恢复是预定的邮件
  排 grain 自己去送他们,所以他们不指望 `MaxCapacity`,如其它预定
  信件; grain 为其预定的每次延迟时间保留一次恢复,直到该延迟时间
  到期时间。

配置共享、制作-职等日刊和持久就业供应商
多个silo 部署;见 [日记日报配置](journaling/configuration.md)。

<a id="retention-and-deletion"></a>

## 保留和删除

每个被援引的任务都有一棵处决树:任务、其当地步骤、延误、拖延、拖延、拖延、拖延、拖延。
组合记录、 `DurableTask.RunInline` 记录、版本决定、信号记录、订阅其他任务
单项任务、信号发送标记及其呼叫其他 grains的记录。 grain
在一个日记中删除树写:

| 由 | 删除 |
| --- | --- |
| 一项耐久任务或另一种耐久 grain 的普通方法  | 当发件箱在调用 grain 后完成工作时退出,且该结果已可持久地接受。 |
| 客户端、普通 grain 方法或将其分离的持久任务 |  完成后 `ResultRetentionPeriod` 。 |
| 任何打电话的人,在完成后,都用无字母 | `ResultRetentionPeriod` 死信之后 |

一项长期任务所赞同的任务 `GetDurableTask` 或以
另立任务, 保留在发件箱将完成的完成时间退
订阅者。
完成期重新开始保留期,就像一个呼叫者的。

a grain普通方法预定的调用记录是其根,
保留用于 `ResultRetentionPeriod` 其结果降临之后,

删除树的删除会取消未完成的任务:删除日记写后发送
取消每个待决呼叫的另外一个 grain, 执行处决时有信号
在那日之后,他们写完,而他们的结局被抛弃。完成和延迟,将恢复;
发送到已删除的任务时,将被忽略。删除后,将投出民意调查
`DurableTaskNotFoundException`,并且安排相同的任务ID再次运行请求。

下 grain 当激活时、 每一日记写后、 和在它之前, 保留应用
调查或客户的日程安排,因此,无论在什么情况下保留其职位,都不得执行任何任务。
。空闲 grain 在其日记中保留过期状态,直到其中之一发生为止。

覆盖客户民意调查结果最长间隔的 `ResultRetentionPeriod` 大小
重试时间表,并在每份 grain日记本中说明保留的任务状态:
保留客户排定任务的完成率乘以
期间。

<a id="observe-a-task"></a>

## 观察任务

Grain 引用引用暴露任务控制: `GetDurableTask<TResult>(rootId)` 扩展
返回 `DurableTaskReference<TResult>` ,该返回将进行民意调查、等待、取消或中止
现有任务,而且一项持久的任务等待于赞同其结果,
`SignalDurableTaskAsync` 和 `TrySignalDurableTaskAsync` 扩展发出信号。
`GetStatusAsync` 汇报任务 `DurableTaskStatus`, `GetResponseAsync` 返回
回答,它能维持结果或例外。对任务 grain 没有做的任务的民意调查
保留丢弃 `DurableTaskNotFoundException`:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_diagnostics" language="csharp":::

| 状况状况现状状况现况 | 意义的含义含义 |
| --- | --- |
| `Pending` | 任务没有记录结果,包括取消请求或延迟尚未落实。 |
| `CompletedSuccessfully` | 任务成功完成 。 |
| `Canceled` | 此任务被取消 。 |
| `Failed` | 任务失败或中止;中止的例外是 `DurableTaskAbortedException`,其信息是中止的原因。 |

一个 grain 观察、取消、中止和信号自己的任务,而无需通过 grain 的呼叫
`IDurableTaskManager`,它从启动服务中解决。

<a id="observe-runtime-transitions"></a>

## 运行期过渡

`Orleans.DurableTasks` 诊断倾听器 `DurableTaskEvents`报告每个
运行时启动时间的激活时同步过渡
,并使用激活 `IGrainContext` 和任务编号。
与 grain的下一期日记写入。

| 活动活动 | 升升 |
| --- | --- |
| `TaskCreated` | 运行时间 任务状态: 援引的任务,或执行的操作 |
| `TaskCompleted` | 运行时将任务的最终结果进行演练, 事件带有这种结果。 |
| `TaskDeleted` | 运行时间将任务从执行树上删除,或在其保留期结束时。 |
| `ExecutionStarted` | 任务身体的尝试开始于激活 |
| `ExecutionStopped` | 尝试结束:它的身体完成,或者中止或删除取消尝试。 |
| `ReplayEnded` | 重播尝试启动了一项没有记录过早期尝试的操作,因此 `IsReplaying` 报告 `false`。 |
| `SignalWaitStarted`, `SignalWaitEnded` | 执行开始或结束等待信号, 以信号记录为根据。 |
| `SelectionWaitStarted` | 诸如 `DurableTask.WhenAny`等选择在其决定下没有发现任何已结候选人和等待。 |
| `VersionRecorded` | 执行记录了随后发生的更改,该事件随该更改的名称而发生。 |
| `ExecutionStalled` | 尝试因运行中的代码无法重现其执行而停止;任务仍然未完成。 `Reason` 是 `UnsupportedVersion` 或 `Diverged`, `Exception` 描述着该摊位。 见 [处决总数](durable-tasks-versioning.md#stalled-executions)。 |

测试是否订阅 `DurableTaskEvents.AllEvents`,或使用 `DiagnosticEventCollector`
消息报到,重整,
至 `MessagingEvents`, whose `InboxMessageReceiving` 观察员注射
交付缺陷;见 [观察系统的进展和规模](messaging-operations.md#observe-progress-and-size-the-system)。

<a id="diagnose-replay-divergence"></a>

## 诊断性重显示差异

重放必须在同一操作下在每个记录的身份识别码下安排相同的操作,并必须解码
记录结果:当一项行动具有相同的类型和目标时,即相同:
在同一 grain上,无论提出何种论点,都引用相同的接口方法,因此呼叫
其论点改变或序列化不同,则返回其记录的结果。
重放计划不同的操作,或记录的结果不再编码
执行 执行拖车:运行时取消
企图 `DurableTaskAttemptCancellationReason.Diverged`丢弃其结果,
任务将保留在待定的任务中。运行时记录警告并提醒 `ExecutionStalled`, whose
`Exception` 表示差异,例如:

```text
Durable task 'place-order-…/authorize-payment' is already associated with a different request.
It records a call to method '…Invokable_IPaymentGrain_…_Authorize…' on grain 'payment/…'
(fingerprint 3F2A9C01B7E4), and this attempt requested a call to method
'…Invokable_IPaymentGrain_…_Capture…' on grain 'payment/…' (fingerprint 91C0D4E2A6B8). A replay
schedules the same operation under each identifier: a call or request invokes the same method on
the same target, whatever its arguments; new workflow steps use new identifiers.
```

所记录的操作的电文名称,例如呼叫 grain、延迟、 a
`WhenAny` 决定、 `DurableTask.RunInline` 效果、独立任务启动或信号,
被请求的。调用名称,以其生成的可引用类型和它的
指纹是其序列参数的散列,它显示诊断中的电话分开。
但不决定它们是否匹配。

- 以呼叫另一种方法或 grain重新排序的调用替代了职级调用
  未命名儿童,或在现有的儿童或组合器之前添加一个未命名的儿童或组合器。
  `DurableTask.GetVersion`的修改,因此在处决之前保留其记录
  业务。
- 环状或状况表计划有身份证明的儿童,取决于是否确定
  值。
- 部署改变了取消任务的机构, 使其重放在
  早期运行时观察到取消的取消点。
  重放到达的点和记录到的点。

儿童之间读取重放时不同值的代码,例如
`DateTime.UtcNow`,随机数或可变 grain 状态,传给儿童
并不不同:重放读取了第一个运行的通话记录结果
与第一个值相制成的第一个值。 `DurableTask.RunInline` 记录这样的值,所以每个运行
将同一条通过到以下代码。

尸体观察分歧是尝试的 `DurableTaskAttemptCanceledException`,
其内在例外是分歧和尝试的后来持久行动
等待任务的人,将等待任务来临。 grain 每天一觉醒来
`StalledExecutionRetryInterval`,因此一旦启动结束, 稍后的激活重播
任务:部署与记录相符的代码,例如将更改依
`DurableTask.GetVersion`,然后执行恢复。要结束任务,中止任务;
见 [中止已挂停的任务](#abort-stuck-tasks)。观察 `ExecutionStalled` 以查找被停止的状态
处决。

<a id="version-workflows-safely"></a>

### 安全版本工作流程

当部署在等待处决期间改变工作流程时,这些处决
在新代码下重播 :

- 保持现有步骤、 `RunInline` 效果和分离启动,使用其身份识别方法,
  和目标。调用或分离的起始时间,其参数如果部署改变返回,则在
  记录了死刑的原论据的结果。
  更改了 `RunInline` 代表,不影响死刑,
  属性和步骤,用同一名称的效应和步
  记录到效果或反向变化的分行
  `DurableTask.GetVersion`.
- 给新步骤新的身份识别码, 并设置取决于此之后的新行为的步骤
  现有措施。
- 给每个孩子和组合命名, 稍后的更改可能会移动, 所以生成 ID
  切换。 脱落的任务和信号发送从分离的
  任务或信号, 以便它们不会移动。 一个未命名的分离任务产生其根代号
  执行执行的未命名操作, 当稍后更改时用 `WithId` 命名
  可能会移动它。
- 保留 Orleans 序列制序号,并添加
  含有新 `[Id(n)]` 值的成员。无法解码已记录结果的重放
  差异。
- 延迟期的改变适用于新的延误。
  到期时,运行时记录一个警告。

改变在等待处决之前已经通过的行动,例如取代一步
或更改其参数, 关于 `DurableTask.GetVersion`的分支:每项执行如下
其记录版本,处决从先前的代码开始
`0`版本。见 [版本耐久任务](durable-tasks-versioning.md)。

要退休旧工作流程形状, 添加新形状作为新方法和安排新工作
并保留旧的方法,直到完成待决任务。

<a id="abort-stuck-tasks"></a>

## 中止已挂停的任务

中止一项任务,如果其机构忽视取消或补偿无法完成:

- 从自控 grain、 `IDurableTaskManager.AbortAsync(rootId, reason)`。
- 来自客户端或工具, `grain.GetDurableTask<TResult>(rootId).AbortAsync(reason)`。

中止任务失败时, 中止执行任务和执行树失败
`DurableTaskAbortedException` 在一个日记写中,向每个待拨的电话发送中止
对另一个 grain被召唤者中止了自己的树 取消了运行中的尝试
取消的尝试的持久操作
`DurableTaskAttemptCanceledException` 与原因 `TaskAborted`,和
`IsTaskCancellationRequested` 停留 `false`,所以中止没有补偿;撤消任何
尝试解风时运行的代码释放出它的
`finally` 区块中的处理中资源及其日记状态变化承诺
下次写入 grain的日记。中止终端任务不会改变任何内容,
独立任务被单独中止。 诊断然后将任务报告为 `Faulted`
与 `DurableTaskAbortedException: <reason>`.

<a id="dead-letters"></a>

## 死亡信

耐久任务信件使用主题,从 `durable-rpc/`开始: `invoke`,
`complete`、 `cancel`、 `resume`和 `signal`。
适用于这些文字;检查并删除通过 `IMessagingDiagnostics`,作为
[检查和重放已死字母](messaging-operations.md#inspect-and-replay-dead-letters)中说明的。

- 接受者 grain 永久拒绝信息,其收件箱记录信息
  与拒绝的理由和代码一起处理的拒绝理由和代码:一个已经工作身份的援引
  指定另一个请求或呼叫者,拒绝的信号,例如:
  或已完成任务时的对立值。
  `durable-rpc/conflict` 代码的代码 `DurableTaskRejectionCodes`发送者发件人发件箱
  当它报告被拒退的召回时,它会予以退职。
- 接收信息 grain 以其他理由,例如a) 不接受
  瞬时故障或无法解码的有效载荷被重新检索。
  重新试镜可以达到一个能解码的激活
- 发件箱中发送的邮件如果发送失败,则无法发送 `OutboxRetryTimeout`。
- 发件箱的废字母 完成,其存储的结果不再解码
  示例,在部署后删除了结果类型,以解码错误为其
  完成并保留任务
  `ResultRetentionPeriod`;读取结果的行动,如民意测验,报告
  解码错误。 grain 正在处理其他工作 。
- 被存储的任务请求不再解码, 例如在部署移除后
  输入其参数所持的、不中断 grain激活而停止的: grain
  运行其他任务, 民意测验报告该任务为 `Pending`和 `ExecutionStalled` 报告
  与原因 `Diverged` 和编码错误。保存的请求被保留,所以a
  稍后激活其代码编码请求的代码后启动该任务;中止结束的任务
  而不是。
- 任务的结果或例外没有序列化, 比如值或例外
  其数据含有没有编码器、且与 `SerializerException`相矛盾的某类型值的数据
  指定类型。失败如同任何结果一样发生,且 grain 保持
  承诺开展其他工作。

完成一个死字字母的完成后, 请求处决等待。 决定是否
取消或中止执行, 并在纠正后重放一封死信
原因。 原因。

启动点的目标拒绝或未完成的开始 失败了起始任务
记录, so `ScheduleAsync`,并等待任务或处理它, 扔失败在
每次重播 :

| 结果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果 | 拒绝代码 | 失败失败 |
| --- | --- | --- |
| 任务编号已经指定了另一个请求或呼叫者。 目标立即拒绝它 。 | `durable-rpc/conflict` | `DurableTaskConflictException` |
| 请求到达时,请在目标进入窗口后, `DeduplicationWindow` 自启动器开始启动后。 | `durable-rpc/expired` | `DurableTaskRejectedException` |
| 目标拒绝使用其他代码或无代码。 | 目标代码 | `DurableTaskRejectedException` |
| 目标不接受在 `OutboxRetryTimeout`之前的援引, 因为短暂失败或请求无法解码, 或者发件箱无法达到 。 | | `DurableTaskDeliveryException`, 与死亡信的原因 |

诸如 HTTP 端点等呼叫者告诉冲突,通过选择
另一任务代号, 从未被接受的开始, 按例外类型报告。 `DurableTaskRejectedException.Code` 报告目标代码, 并且
`Target` 否认者, grain.

<a id="client-waits-across-outages"></a>

### 客户在断电时等待

客户端或普通 grain 方法,通过对 grain进行民意调查等待任务。
无法到达 grain,因为连接下降, grain的 silo 停止或
崩溃、 呼叫时间出来或 grain 停止使用, 无法完成民意调查
`DurableTaskAttemptCanceledException` 和原因 `ActivationStopping`,使任务继续运行,所以
等待用指数反向回击重试它,从50米到5秒, 下一次投票到达
排 grain下一个激活。等待随着任务的结果结束,
`DurableTaskNotFoundException` 任务 grain 不再保留,或当
呼叫者的信物 或者等待时间的超时结束

<a id="capacity-and-throughput"></a>

## 能力和投入

- 记录效果 `DurableTask.RunInline`,不添加任何日记笔迹:其记录和状态
  更改了它的承诺, 并使用任务下一个耐久的写作。 每个效果都保留一个记录 。
  将结果保留在任务树被删除之前。
- 本地一步成本一日记写:
  写,所以一个步骤, 改变状态和返回, 无需等待即可修改其状态
  变化和结果一起产生。
- 组合器和 `ScheduleAll` 同步安排其投入,同时运行时间
  将引用合并为一份日记。
- 每项请求和结果有效载荷都按 grain一次日记,元数据更新
  写不改,完成结果也由它写不完
  任务状态, 任务状态则保留它用于 `ResultRetentionPeriod`。 完成序列添加
  每项任务状态的整数为一整数。
- 执行的每个部分, 代码在两个耐久等待之间的间隔, 以请求方式运行
  启动时,并遵循 grain的再生规则。对于非再生者 grain,
  等待其他工作的部分,如呼叫另一个 grain,则持有 grain
  一种 grain 方法有效,一个 grain 的并行步骤一次运行一个段。
  热 grain 将工作流程的流量捆绑,所以通过业务分割工作流程
  拥有者, 如每顺序 grain 。
- 与 grain的其他工作互切切换,这样等待客户不会延迟
  完成任务的工作。
- 将执行树砍掉,并保留树木
  将 grain日记快照增长到删除为止。

监控信息传输的测量标准 用于收件箱和发件箱深度、重写和死亡字母
包含耐久任务信件; 见
[观察系统的进展和规模](messaging-operations.md#observe-progress-and-size-the-system).

<a id="known-constraints"></a>

## 已知制约因素

- 适用性涵盖参与《日刊和通信》的状态和信息
  外部副作用需要其自身的应急能力,例如请求键
  源自任务代号。
- 一项持久的任务直接将自己的 grain的耐久方法称为地方步骤。
  以提及其 grain 为单位的持久呼叫被否决。
- 一项持久的任务向其他 grains 与 `SignalDurableTaskAsync`, 其中
  发送重播放的记录; `TrySignalDurableTaskAsync` 是客户端和普通的
  grain 方法。
- 信号针对引用的任务:客户、另一个 grain或持久任务
  开始分离它们
- `ScheduleAll`、 `WhenAllCompleted`和 `WhenEach` 只有在等待
  执行耐久执行;将一项任务作为自己扔出的任务列表
  `NotSupportedException`.
- 启动停止新执行,弃鱼停放尝试,让我们运行
  将投入的工作留待重放
  下次启动
- 适应器和抽象物等待着它们 NuGet 并且永续地出版,
  在评价整合时,可以修改格式。
