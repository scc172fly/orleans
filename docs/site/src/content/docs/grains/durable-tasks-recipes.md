---
title: "持久任务配方"
description: "抛出、种族、暂停、补偿、解决所有输入、完成过程、等待信号、开始独立工作、取消、中止和安排持久任务。"
ms.date: 10/14/2026
ms.topic: how-to
---

<a id="durable-task-recipes"></a>

# 持久任务配方

这些汇编的例子使用 grains
<xref:Orleans.Durable.Journaling.DurableGrain> and the contracts from
[持久任务](durable-tasks.md#define-durable-task-methods). 配置日记,
长期的就业机会,以及如以下所示的持久任务运行时间
[配置 silo 和客户端](durable-tasks.md#configure-the-silo-and-clients).
每种配方的结果 [工作对数的测量](durable-tasks-operations.md#observe-a-task)
贯穿 [运行时过渡](durable-tasks-operations.md#observe-runtime-transitions)
和 `DurableTaskEvents` 报告。

<a id="fan-out-to-every-line-with-a-named-whenall"></a>

## 扇形切换到有 WhenAll名称的每条线

**任务:** 每个订单线的储备储备库存同时并存,并继续执行
线条结果按线条顺序排列。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_fan_out" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.WhenAll*?displayProperty=nameWithType> 附表
每行每批保留一行,单批保留一行。 订单 grain 以一份日记本承诺
以输入顺序写,然后返回一个 `bool[]` 。它等待每个保留,包括
在一项取消请求之后,在没有任何保留的情况下 `catch` 块运行
。 `WithId("reserve")` 将输入确定为 `reserve/0`、 `reserve/1`,等等
开。

空虚 :

- 以决定顺序构建输入, 如命令的线条顺序。 输入
  索引是每项输入任务编号的一部分。
- 名称组合器。 一个未命名的组合器生成的 ID 计算
  将输入的数重新编号。
- `WhenAll` 丢弃输入顺序的第一个失败, 然后放弃第一个取消。 要采取行动
  每种输入结果的利用
  [`WhenAllCompleted`](#settle-every-input-and-inspect-each-outcome).

**核查:** 顺序 grain 列出 `place-order-{orderId}/reserve/0`
`reserve/{n-1}`, 每个 `Completed` 其与本 `True` 或 `False` 和每一目录
grain 列出一个带有同一ID的任务。

<a id="take-the-first-result-and-cancel-the-rest-with-race"></a>

## 取得第一个结果, 然后与 Race 取消其余的

**任务:** 从两个承运人中取第一个航运报价,然后取消另一个
承运人的请求。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_race" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.Race*?displayProperty=nameWithType> 附表和
将请求作为 `first-quote/0` 和 `first-quote/1` ,并选择填写的
首个 grain 印章每个输入结果的印页都有持久的完成顺序,因此
即使其他请求自此完成后, 仍重放选择同一赢家 。 `Race`
请求取消其他请求,原因
`Another task won the race.` 并等待请求为终端,所以工作流程
只有在两个承运人的电话都解决后,才继续。 `Race<TResult>` 返回
赢家索引和结果。

空虚 :

- 失败或被取消的赢家失败 `Race`。返回一个数值,例如 `null`,
  其失败是预期结果的投入。
- 取消是合作的,无视请求的承运人保留种族
  等待它的呼声完成, 和种族抛弃 呼叫的结果。
- 将其他输入投入运行,使用
  <xref:Orleans.Durable.Tasks.DurableTask.WhenAny*?displayProperty=nameWithType>,其
  其它输入运行到完成或运行时删除任务执行树。

**校验 :** 较慢的要求解决了,而航运 grain 列表中作为
`Canceling` 与原因 `Another task won the race.`;一次 `QuoteFirstAsync`
返回 `first-quote/0` 和 `first-quote/1` 均为终端。

<a id="time-out-a-call-with-waitasync"></a>

## 超时呼叫 WaitAsync

**任务:** 要求地面承运人报价,两分钟后放弃。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_timeout" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitAsync*?displayProperty=nameWithType>
比赛的呼唤, `ground-quote/0`并等待着持久的拖延, `ground-quote/1`运行时间
记录延迟的到期时间, 所以在停止作用后重放时保留了原
截止时间:从第一运行开始计时超时。当延迟先过,请拨
收到取消请求的原因
`The operation timed out after 00:02:00.`,以及 `WaitAsync` 等待承运人的
结束的呼声取消投出 <xref:System.TimeoutException>,其内心
例外是调用 `OperationCanceledException`;承运人返回的引号
反之为结果。在第一个电话打完后, `WaitAsync` 将取消延迟,
返回呼叫结果或丢弃失败。
<xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitUntilAsync*> 需要一个绝对值
而不是最后期限。

空虚 :

- 计算绝对截止时间
  <xref:Orleans.Durable.Tasks.DurableTaskContext.UtcNow?displayProperty=nameWithType>, 如此
  a 重放计算相同的截止时间。
- 接球 `TimeoutException` 处理超时。 `OperationCanceledException`
  报告的另外一次取消, 如工作流程自己的取消 。
- 整个种族的`Race(...).WaitAsync(timeout).WithId(name)` 线以
  儿 幼 `{name}/0` 以其下层的文采。
- 一项已经排定的任务
  <xref:Orleans.Durable.Tasks.ScheduledTask.WaitAsync*?displayProperty=nameWithType> 加上 a
  超时适用相同的规则。 在一项耐久的任务之外,例如客户的耐用任务,它
  测量系统时钟的超时。

**核查:** 航运 grain 将 `ground-quote/1` 列为 `Delayed` ,直至其到期。
暂停后 `ground-quote/0` 原因报告
`The operation timed out after 00:02:00.`,以及工作流程返回 `null`
航空公司的要求终止了

<a id="compensate-inline-when-the-workflow-is-canceled"></a>

## 当工作流程取消时进行内线补偿

**任务:** 当买方在审定期间取消订单时,解除所有保留
记录为什么取消订单

`catch` 块 [扇出配方](#fan-out-to-every-line-with-a-named-whenall)
在 `IsTaskCancellationRequested`上过滤器,因此它处理工作流程本身的任务
取消和通行证
<xref:Orleans.Durable.Tasks.DurableTaskContext.TaskCancellationReason?displayProperty=nameWithType>
赔偿金额:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_compensation" language="csharp":::

儿童中,如果一个取消的任务在不运行的情况下以取消的方式终止了该任务,除非
任务开始于
<xref:Orleans.Durable.Tasks.DurableTaskExtensions.Shielded*>. 屏蔽 `release`
每种输入的组合套套,因此输入和 `cancel` 效果都运行到
完成时间和
返回记录原因的 `OrderResult` 。保留从未被
没有任何留叶可释放, 所以 `ReleaseAsync` 返回命令是否维持
保留,释放每一行是安全的:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_catalog_grain" language="csharp":::

空虚 :

- 每一个孩子都得用盾牌来补偿。一个没有被保护的孩子会因取消而终止
  不带运行。
- 使补偿成为能干的,并报告他们是否行动,这样工作流程就可以
  补偿却不知道是哪一步
- 中止不能补偿,中止是给那些赔偿被拖延的任务。
- 工作必须比工作流程活得长,例如工作流程结果之后的清理
  交付,属于 [分离任务](#start-follow-up-work-without-waiting)。

**校验 :** 之后 `CancelAsync` 宽限期期间的顺序 grain 列表列表
`grace-period` 年为 `Canceled`, `release/{n}` 和 `cancel` 年为 `Completed`根
任务作为任务 `Completed` 和 A 相 `OrderResult` 其描述 `Cancelled by the buyer.`.

<a id="settle-every-input-and-inspect-each-outcome"></a>

## 解决每项投入并检查每个结果

**任务:** 保留每一行,在任何保留失效、被拒绝或被否决时
取消, 释放保留成功时的线条 。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_when_all_completed" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.WhenAllCompleted*?displayProperty=nameWithType>
将输入的处理器返回到输入中
组合成功,包括
工作流程在自己提出取消请求后,将输入输入。
对其结果表示意见,因此
<xref:Orleans.Durable.Tasks.ScheduledTask`1.TryGetResult*> 返回 `true` ,结果
成功完成的保留的撤回
<xref:Orleans.Durable.Tasks.ScheduledTask.Status>,
<xref:Orleans.Durable.Tasks.ScheduledTask.Exception>, and
<xref:Orleans.Durable.Tasks.ScheduledTask`1.GetResult*> 检查其他的。
使用屏蔽组合器释放所持的线条,因此当
工作流程被取消, 然后重新扔出取消的选项, 这样任务就以取消结束 。

空虚 :

- 每一个把手结果的分支,而不是综合结果:组合器从来没有
  丢弃输入。
- 保护盾只要工作流程在补偿时被取消,就会发布。
- `WhenAllCompleted` 只有在等待执行耐久处决时才运行。
  先前返回的`ScheduleAll` ,
  <xref:Orleans.Durable.Tasks.ScheduledTask.WhenAllCompleted*?displayProperty=nameWithType>
  等待的方式是一样的。

**核查:** ,有一个行的库存外线,分配 grain 清单 `reserve/{i}`
`True` 和 `False` 结果,a `release/{n}` 每个输入 `True` 仅行, 和
单表 grains'现有库存返回其起始价值。

<a id="process-results-as-they-arrive-with-wheneach"></a>

## 与 WhenEach一起到达的流程结果

**任务:** 请求数个承运人报价,保留第一个 `count` 报价
收到并取消其余请求。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_when_each" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.ScheduleAll*?displayProperty=nameWithType>
请求以 `quotes/0`、 `quotes/1`等形式列表,并返回其控件。
<xref:Orleans.Durable.Tasks.ScheduledTask.WhenEach*?displayProperty=nameWithType> 产量
每一个处理一次,按完成顺序,包括失败和取消的请求。
运行时间根据结果的持久完成序列选择下一个处理器, 所以
每个重放的重放产生相同的顺序, 运行同样的步骤, 包括一个重放
完成更多请求。 在循环后,
<xref:Orleans.Durable.Tasks.ScheduledTask.CancelAsync*?displayProperty=nameWithType>
取消每项请求并附上理由; 取消已经完成更改的请求
什么都没有。

空虚 :

- 依据查点结果的手柄决定
  后导出可在重放时报告不同的 `IsCompleted` 值。
- `DurableTask.WhenEach` 接受不接受 `WithId`; 要命名输入, 请等待
  `ScheduleAll(...).WithId(name)` 和列举 `ScheduledTask.WhenEach(handles)`.
- 查点结果无法持续处理的请求
  取消, 否则任务执行树被删除 。

**核查:** 航运 grain 清单列出每个承运人的 `quotes/{i}` 。
还没有完成报告 `Enough quotes were collected.` 时 `Canceling`,
其承运人所应尽的 grain .报告。

<a id="schedule-now-await-later-with-scheduleall"></a>

## 计划,等待稍后 ScheduleAll

**任务:** 开始打印每个包标签,记录在选择顺序时所选的顺序
标签打印,然后收集标签。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_schedule_all" language="csharp":::

`ScheduleAll(...).WithId("labels")` 以一个日记写方式执行每份标签请求
并返回他们的控点,不等待。 `pick` 步数运行,而标签打印。
<xref:Orleans.Durable.Tasks.ScheduledTask.WhenAll*?displayProperty=nameWithType> 然后
等待每个标签,然后将第一个失败按输入顺序进行输入,否则第一个
取消, 所以每个控点的 `GetResult()` 返回标签 。

空虚 :

- 在工作流程返回之前,等待每个控点,或将其传递给组合器。
  工作流程不会等待的运行 继续运行,运行时间在它的时候取消
  删除任务执行树。
- `ScheduleAll` 只有在等待执行持久处决时才运行,因为其手柄
  属于执行者

**核查:** 履行 grain 清单 `labels/{i}` 和 `pick`,以及标签任务
与 `pick`有关的任何顺序。

<a id="wait-for-an-external-event-with-a-deadline"></a>

## 等待有截止时间的外部事件

**任务:** 等待核准人就费用索赔作出决定最多3天。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_signal" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableSignal`1.TryWaitAsync*> 与耐久性相比
延迟到截止时间和返回 `(true, decision)` 或 `(false, default)`。任务
记录第一个决定,而每次等待和重放都返回它。普通 grain
以 `IDurableTaskManager.TrySignalAsync`的方式作出决定,
grain的待状态更改并返回一个 `DurableSignalResult`。客户端发送
通过 grain 参考文献的相同信号:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_signal_client" language="csharp":::

空虚 :

- 计算截止时间
  <xref:Orleans.Durable.Tasks.DurableTaskContext.UtcNow?displayProperty=nameWithType>,
  重播计算相同的截止时间。
- 处理引用的任务, 这里选择客户客户计划的任务根任务。 本地
  儿童的身份识别不是被引用的任务, 所以一个信号返回 `TaskNotFound`。
- 每个名字记录一个值。使用
  <xref:Orleans.Durable.Tasks.DurableSignal`1.For*> 以在一
  重复事件,例如每轮批准一个。
- 在任务开始期间,记录了在最后期限之后作出的决定和
  后期 `Decided.WaitAsync()` 在同一任务中返回。

**核查:** `DecideAsync` 返回 `Accepted`,同一决定再次返回
`Duplicate`,不同的决定返回 `Conflict`,任务结束后的决定
完成一个返回时没有填充 `TaskCompleted`。

<a id="start-follow-up-work-without-waiting"></a>

## 开始后续工作,不等待等候

**任务:** 在开具发票时发出收据,而不等待发票
通知服务或发票取消时取消收据。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_detach" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTaskExtensions.Detached*> 将该电话作为独立电话
通知 grain的根任务,名称为 `receipt-{invoiceId}`
<xref:Orleans.Durable.Tasks.DurableTaskExtensions.WithId*>, 和
<xref:Orleans.Durable.Tasks.ConfiguredDurableTask.Post*> 在发票 grain中分阶段处理
要求和在发票的处决树上的记录
发票结果,所以一个重放 到达记录开始没有意义。
以姓名收悉:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_detach_observe" language="csharp":::

空虚 :

- 以稳定值后指定分离任务。在现有的
  根 I 像其它任务ID 重新使用一样失败 。
- 将收据的身份证交给另一方的工作流量,然后方作出自己的决定
  以 `ScheduleAsync`,返回通知 grain 拿着它
- 通知 grain 在通知 `ResultRetentionPeriod` 之后保留收到结果
  完成,投票

**核查:** 通知 grain 清单 `receipt-{invoiceId}`和发票 grain
列表 a `$detach:…` 通知中注明的发票工作之下记录 grain 和
收据的根身份,直到发票的树被删除。

<a id="cancel-and-abort-from-ordinary-grain-methods"></a>

## 取消普通 grain 方法并中止该方法

**任务:** 在确认库存之前让买方取消订单,然后让
操作员中止一个被卡住的工作流程 。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_order_controls" language="csharp":::

与激活本身相对的 grain 注射器 `IDurableTaskManager`
运行时, 没有呼叫 grain 。 一旦请求完成, `CancelAsync(rootId, reason)`
承诺和倡导;工作流程遵守并决定其结果,如
[补偿食谱](#compensate-inline-when-the-workflow-is-canceled).
`AbortAsync(rootId, reason)` 承诺将工作流程及其执行树与
<xref:Orleans.Durable.Tasks.DurableTaskAbortedException>, whose message is the reason, and
向其它未解决的调用 grain发送中止。 客户端使用任务控件 :

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_client_controls" language="csharp":::

空虚 :

- 取消前先检查商务状态。取消后即到期
  工作流程通过了其可撤销的步骤,这些步骤仍然在运行补偿
  够
- 重复的取消请求是完全可以的,第一个请求的原因是
  工作流程读取的。
- 中止没有补偿, 并留下独立任务运行; 单独中止它们 。

**校验 :** 取消的订单诊断报告其根为 `Completed` 和
时退出工作流程的 `Canceled` ,
中止的根报告 `Faulted`
`DurableTaskAbortedException: <reason>`.

<a id="schedule-a-workflow-from-another-grain-or-a-client"></a>

## 调度来自另一个 grain 或客户端的工作流程

**任务:** 当买方检查并观察其结果时启动订单工作流程。

耐久 grain 从普通方法开始工作流程
<xref:Orleans.Durable.Tasks.ConfiguredDurableTask.Post*>,该程序将分阶段援引
以它发出的箱,所以清理的篮子和祈祷 一起在日记上写
其内容如下:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_schedule_from_grain" language="csharp":::

工作流程是顺序 grain上的一项根本任务,保留了顺序及其结果
`ResultRetentionPeriod` 完成后,篮子投票结果
`GetDurableTask<TResult>` grain- 参考扩展名。

客户将工作流程与
<xref:Orleans.Durable.Tasks.ConfiguredDurableTask.ScheduleAsync*>,返回时返回
顺序顺序顺序 grain 持此任务,等待着返回的手柄,或稍晚地重新接续它。
根标识符 `GetDurableTask<TResult>`:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_schedule_from_client" language="csharp":::

A <xref:Orleans.Durable.Tasks.PollingOptions> 数值为零的值 `PollTimeout` 返回返回
当前状态同时同步; 更长的超时等待时间要等到完成。
等待呼唤的持久任务,从幼年起即开始,并且 `WithId(name).ScheduleAsync()`
儿童开始,然后返回其手柄等待。

空虚 :

- 将根身份从商业身份(如排序号)中提取出来,带有 `WithId`的[命令号],所以
  重新查取的检出结果达到相同的任务,而不是启动第二个工作流程。
- 完成后, `ResultRetentionPeriod` 将保留根任务。
  `DurableTaskNotFoundException`,并重新开始同一根 根ID
  再来一次

**核查:** 顺序 grain 列出 `place-order-{orderId}`,篮子清单中未列出任务
并且, `GetOrderResultAsync` 命令船舶返回后的结果。
