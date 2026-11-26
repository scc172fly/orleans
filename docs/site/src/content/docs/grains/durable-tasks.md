---
title: "持久任务"
description: "了解持久任务方案编制模式,重新播放并承诺提供保障、取消、信号和任务生命周期。"
ms.date: 10/14/2026
ms.topic: conceptual
---

<a id="durable-tasks"></a>

# 持久任务

Orleans 耐久任务运行返回的 grain 方法
<xref:Orleans.Durable.Tasks.DurableTask> or <xref:Orleans.Durable.Tasks.DurableTask`1>
工作流程是通常的顺序C##:它等待着其他
grains,当地各州的过渡、拖延和外部信号。
记录在 grain日记的日记中,因此工作流程能度过激活损失并恢复
长期任务在 [通信通信](messaging.md)上空执行,随
使用、结果、取消和信号之间的 grains 通过 grain's
耐久的箱和发箱,以及超越持久工作,这唤醒了 grains 延误应交
和在这些国家被 grain 停止使用

> [!IMPORTANT]
> `Microsoft.Orleans.Durable` 和 `Microsoft.Orleans.Durable.Abstractions` 是
> 目标 .NET 11(`net11.0`)并等待其第一个 NuGet的甲型A型货包
> 以出版物出版、发表和
> 明确的升级、回滚、备份和回收计划,因为其APP和持续
> 稳定前可以修改格式格式。 Orleans 仓库参考文件中的项目
> 它们是源项目。

本页介绍方案拟定模式及其保障。

- [任务身份和参考](durable-tasks-identity.md) 任务编号是如何的
  构建, 并改变它们稳定, 以及如何从别处处理任务 。
- [结构通货币](durable-tasks-structured-concurrency.md) 以便他们依定律而得享受许多树木的果实,
  注销如何通过这些方式流动。
- 精确作用一次的效果[能力](durable-tasks-idempotency.md)
  以及需要应急钥匙的外部影响。
- [重演安全规则](durable-tasks-replay-rules.md) 临的可
  任务分析器强制执行。
- [版本](durable-tasks-versioning.md) 用于在处决期间修改工作流程代码
  尚未解决,即将过期的旧版本。
- 给付款提供人[与其他系统结合](durable-tasks-integrations.md) ,
  数据库、 HTTP 服务、 Webhooks 和重试地点 。
- [实用实用食谱](durable-tasks-recipes.md) 以享受恩惠、赛跑和报酬,
  其它 grains.
- 配置[操作和排除故障](durable-tasks-operations.md)
  保留、诊断、重现差异、工作流程的版本和能力。

<a id="choose-durable-tasks-or-message-handlers"></a>

## 选择持久任务或信件处理器

这两种模式都承诺企业状态、收到的信息和在
grain和每份的邮箱和发件箱 grain.

| 使用耐久任务 | 使用 [信件处理器](messaging.md) |
| --- | --- |
| 以相继代码书写,在步骤之间产生结果的多步工作流程。 | 响应单个命令和事件, 每条信息有一个处理器呼叫 |
| 阵阵、赛跑和完成 处理耐用电话的订单 | 应用定义的信息身份、主体、信头和原始有效载荷。 |
| 长期的时针、最后期限和等待工作流程中的外部事件。 | 应用模型明确显示的排定信息及国家机器。 |
| 合作取消,内线补偿和记录理由。 | 业务答复和拒绝作为打字信息交换。 |

<a id="define-durable-task-methods"></a>

## 界定持久任务方法

一个 grain 接口通过返回 `DurableTask` 或
`DurableTask<TResult>`对比参数和结果 grains 且坚持工作,
需要,以及 Orleans 序列器 :

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_contracts" language="csharp":::

执行 grain 来源 <xref:Orleans.Durable.Journaling.DurableGrain>. The grain's
国家变化的持久状态
任务结果在一日记中进行, 呼叫者收到结果
写完后:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_catalog_grain" language="csharp":::

`async DurableTask` 方法体是一个推迟定义:调用该方法创建
定义,而持久任务运行时间在任务身份下运行。
完成而不等待是正常的一步。

在一个长期的任务中,等待一个持久的呼唤,作为儿童的一项任务,它作为儿童的跑步任务而运作。
使用客户端或普通 grain 方法执行,等待它或
<xref:Orleans.Durable.Tasks.DurableTaskExtensions.ScheduleAsync*> 开始作为根任务。
[开始和观察任务](#starting-and-observing-tasks) 描述每个启动方式
一项任务。

<a id="example-an-order-workflow"></a>

## 示例:订单工作流程

以下 grain 将命令作为一项持久任务运行。它记录了每个状态变化
记录效果,等待一个宽限期,同时保留每一行,授权
普通 grain 方法取消工作流程
有理由、中止、发送和发运货物。当工作流程被取消时,一条线
或付款减少, 释放保留库存内线:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_order_grain" language="csharp":::

篮子 grain 在根 ID `place-order-{orderId}`下启动工作流程。
a 停止作用,下一次激活从开始重播 `PlaceOrderAsync` : `submit`,
`grace-period`、 `validate`和 `reserve` 投入返回其记录的结果,
工作流程继续第一个还没有完成的操作。
`playground/DurableOrderProcessing` 项目中的项目 Orleans 存储库运行一个完整的
使用浏览器界面版本的此工作流程版本, 用户界面通过耐用 HTTP 设置订单
运行,独立预测更新和诊断端点。

<a id="task-identity"></a>

## 任务身份

其 grain 和 <xref:Orleans.Durable.Tasks.TaskId>确定了一项持久的任务。
任务代号为等级级:根代号,由初始来电者选择,然后是
每个嵌套水平的一等量。 [任务身份和参考](durable-tasks-identity.md)
列出各种身份及其稳定性。上面的顺序工作流程生成了这棵树:

```text
place-order-{orderId}
place-order-{orderId}/submit
place-order-{orderId}/grace-period
place-order-{orderId}/validate
place-order-{orderId}/reserve/0
place-order-{orderId}/reserve/1
place-order-{orderId}/authorize-payment
place-order-{orderId}/pay
place-order-{orderId}/$signal:shipped
place-order-{orderId}/ship
```

- <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WithId*> 给有马子的孩子起名
  。一个名称,如果运行重复,例如在一个循环中,将确定其后继
  依顺序排列的发生情况: `line`, `line$2`, `line$3`等待其姓名的儿童
  接收生成的段,该段以 `$` 开始,并遵循以下顺序:
  执行中创建了未命名的儿童。名称不包含 `$`。
- 名为组合的用户在自己的输入ID下提供其输入ID:
  `DurableTask.WhenAll(...).WithId("reserve")` 将其投入确定为 `reserve/0`,
  `reserve/1`等,按输入顺序排列。
- 在调用者的范围下,另一个 grain 的调用在那个 grain 上,
  `$caller/{callerGrainId}/{incarnation}/{taskId}`,其中 `{incarnation}` 指明了调用者的运行时间:
  调用 `place-order-{orderId}/reserve/0` 的顺序记录是目录 grain的任务
  `$caller/order/{orderId}/{incarnation}/place-order-{orderId}/reserve/0`. 每个来电者都有自己的
  使用一个
  根 ID 有其自身的化身, 所以在先前被删除的 后运行, 调用新任务 。 A
  根任务, 如独立启动, 将根ID 保留在 grain和根ID
  `$caller` 保留。
- 以同样的方法再次安排任务编号,从同一个调用者重新连接到
  现有任务及其记录的结果,无论论点如何,都可重试
  参数序列化的参数以不同的顺序排序, 以不同的方式达到任务。
  接口或方法失败, 所以任务编号重复时间表而 grain 保留
  任务。

一项持久任务直接要求自己的 grain的耐久方法,作为地方步骤,例如:
`await RefreshPricesAsync().WithId("refresh")`. 提及《京都议定书》的持久呼吁
拒绝自定义任务 grain 。

<a id="recorded-effects-local-steps-and-calls-to-other-grains"></a>

## 记录的效果、当地步骤和调用其他 grains

*记录效果*,
<xref:Orleans.Durable.Tasks.DurableTask.RunInline*?displayProperty=nameWithType>,运行
返回前, 在调用器当前工作项中同步代码返回时的当前工作项目, 没有
而不被处死,且记录其结果或所投的(意见),
记录在与该日记相同的日志中进行
重放返回所录结果
不再运行效果。 顺序工作流程会以这种方式更改状态 :

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_run_inline" language="csharp":::

- 效果改变 grain 状态一出就准确,读作后操作变化的状态
  并生成识别符号、随机值和每重播放一次的墙时数
  观察。
- 效果将数据通过结果传送到工作流程:被捕获的任务
  变量不会在以后运行中发生。
- 失败也被记录下来, 因为日记状态没有回退: 更改已发生
  抛出前有记录的例外。结果不连续
  以该失败记录。在日记写前结束的运行
  记录还丢失效果的状态变化,因此下一运行再次运行效果。
- 在一个同步的街区中连续效应,
  更改,在同一日记写中承诺,且 grain 在
  其记录并不表示它犯下了罪行:它犯下了
  任务任务下一个日记写入后, 任务也会被删除。 效果也随着任务取消而发生, 所以
  赔偿记录了其影响。
- 名称指一个效果,表示 `WithId` 指一个步骤: `pay`,然后指一个 `pay$2`
  后来在同一运行中发生。
- `DurableTask.RunInline(effect)` 省略名称,例如
  `var reference = DurableTask.RunInline(Guid.NewGuid);`。执行时的效果
  下一个生成的子代号, 如 `$child-3`, 未命名效果与未命名
  儿童,因此它发现它的纪录 当每次运行都创建了
  。名称效果,其位置在运行或部署之间可能发生变化。
- 效果同步:非同步和耐久效应不会编译,
  错误显示替换 `await DurableTask.Run(async () => ...)`。效果运行
  激活调度器上的 `RunInline` 之后 `ConfigureAwait(false)` 扔弃
  `InvalidOperationException` 没有运行它。
- 记录只适用于长期任务。
  a 普通 grain 方法共享,效果每次运行。

A *当地步数* 是 `async DurableTask` 方法的相同 grain等待着作为孩子,
记录其成果, grain任务状态和记录
承诺第一个耐久书写:其结果或儿童、拖延或信息
其结果在工作项目中进行,该工作项目将运行最终代码,因此国家
代码与结果一起承诺时的修改。在重放中,一个步骤与记录
返回结果,不再次运行。

A *呼叫到另一个 grain* 这是一种永恒的祈祷。 grain 记录调用
在其任务状态和阶段中,在发箱中进行援引;两者都承诺
被调用对象的收件箱直接接受
任务状态,运行方法,然后通过调用器的收件箱返回结果
信息使用来自
发送、接收、任务和主题, 所以重新复制和重播放的发送
已经销毁了

<xref:Orleans.Durable.Tasks.DurableTask.Run*?displayProperty=nameWithType> 创建
无法启动的步进, 用于不叫 grain 的工作, 例如呼叫外部
服务。该职档在等待、安排或张贴时,在可能情况下在
a 后转,作为处决儿童,自 `DurableTask.Current` 和注销
创建它不起任何作用,线索池上也没有运行。重放返回
代表记录的结果, 并经营一个代表 其结果没有再次记录,
因此,代表的外部影响需要其自身的能力。

<a id="effects-and-steps"></a>

### 效果和步骤

`DurableTask.RunInline("hold-id", Guid.NewGuid)` 和
`await DurableTask.Run(async () => ...).WithId("hold-id")` 使用相同的标识符,
`{taskId}/hold-id`,并且都记录一次结果。当代码不同时它们就不一样
运行,其他的差异随之而来。“内嵌”的意思是其它地方的含义
.NET中,与 `TaskScheduler.TryExecuteTaskInline`一样:代码同步运行于
当前的工作项目而不是排期。

- 在当前工作项目中,效果在返回 `RunInline` 之前立即运行。因此
  效果显示,连续效应与它们变化时的状态结合作用
  调用者的 `DurableTask.Current`,在任务取消后运行。
- 等待、计划或张贴时, 可能稍后转机,作为
  儿童被处决。所以它有自己的 `DurableTask.Current`,这个步骤没有被遮蔽
  当父子关系取消请求在启动前被请求取消时, 其完整为已取消的完整 。
  其记录在一本日记中作成。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_effect_and_step" language="csharp":::

重放发现一种记录, 身体现在运行其他的差异
而不是再次运行工作:它的执行摊位,以及 `ExecutionStalled` 名称
两种不同的种类。
将效果改变为步骤或步骤,
[`DurableTask.GetVersion`](durable-tasks-versioning.md) 处决时
旧的唱片,保留它。

<a id="publish-progress-and-stream-drafts"></a>

## 出版进展和流集草稿

工作流程通过一份期刊向观察员公布进展情况
<xref:Orleans.Durable.Journaling.IDurableLog`1> of its grain观察员阅读日志时
<xref:Orleans.Durable.Journaling.IDurableLog`1.ReadAsync*>,通常通过
`IAsyncEnumerable<T>` grain 方法。 [观察日记状态](journaling/observe-state.md)
描述读取、恢复和剪切日志。

出版一个条目,并附上其记录效果
`DurableTask.RunInline(() => log.Append(item))`。效果附加于
任务, 和条目承诺与该效果记录一起在下一个任务中
耐久写。重放返回已录入的序列编号,不附加附件,所以
承诺模式的读者按任务顺序接受每个条目一次
计算任务参数的每个条目、记录的结果和逻辑
时间, 所以每次重放时, 都在同一位置上做相同的附件。 条目适合进度
用户界面监视,例如命令的每个状态或每个
导出已完成分割 :

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_progress" language="csharp":::

`playground/DurableOrderProcessing` 项目保持每份订单的状态,作为
其工作流程依附的快照, 并以此方式将其传送到浏览器中
服务器已存在事件 。

流流内容,例如模型的反应, 运行在
本地步点。 该步骤打开了一张草稿
<xref:Orleans.Durable.Journaling.IDurableLog`1.BeginDraft*>,写每个项目都写下来,并承诺
其最后同步代码中的最后内容, 在其最后的 `await`之后:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_stream_draft" language="csharp":::

- 草稿的项目变化不定。
  `includeDrafts: true` 至 `ReadAsync` 接收
  每一项作为每条的阶段写成,当 `Commit` 附加内容时内容就变得耐久
  替换草稿的条目。
- 步骤的结果在工作项目中进行, 运行它的最后同步代码, 所以
  附加 `Commit` 条目和步骤记录的结果以相同方式被
  日记写入。 在承诺模式中的读者一旦写入, 即接收条目
  耐久。
- 因此,内容完全承诺一次:每个承诺的期刊都同时载有
  和该步骤的记录,或两者中两者的记录。写完后重放返回
  记录结果,不运行该步。在写丢失之前结束的运行
  两者都存在,所以下一赛跑是前进的一步, 流和承诺内容。
- 包括取消后在内的一个步骤,将放弃的草案予以处置,
  重播再重新播放它,不流出
- 在一个阶级内日日记更改属于其最终同步代码。
  在一个 `await`,例如 `Append`等能够以日记写方式进行承诺之前的单步
  在步骤完成之前要求其他工作 grain ,以及运行
  再一次使变化再次发生。

<a id="replay-and-determinism"></a>

## 重弹和确定论

停止或失败后,下一次激活将重现每个待执行的任务:
方法的正文从开始就再次运行,每个记录操作都返回其记录
。结果,在 grain `OnActivateAsync` 完成和
取消在 grain `OnDeactivateAsync`之前取消运行任务尝试的取消
运行,所以他们后来的持久运行就抛下;你看
[任务取消和任务取消](durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation).
每次重放时,每次重放相同的操作和
同一身份和论点,按相同顺序排列。

重放并不重建 grain日记状态, 维持最新承诺的状态
长期机构因此
三种代码:

- **记录操作** 每次执行一次运行,重放返回其记录
  成果:向其他 grains、地方步骤、 `DurableTask.Run` 代表、
  延迟、 信号等待和发送、 分离和发布启动, 以及 `DurableTask.RunInline`
  效果。
- **重新执行的代码** ,在尸体最后一次记录操作之前,再运行到
  计算方法参数的数值,记录的结果,
  和保持
  处决时,也处于死刑候审状态。
- **同步尾尾尾尾尾尾** 跟随尸体最后一次记录操作,没有其他
  返回身体之前的`await` 。它只在一个完成身体的运行过程中运行,并且
  以任务结果的同一日记写时的状态变化承诺
  操作请求写入, 所以不要再重复播放它了 。

下列 grain 发放实际的票号,授权付款,并
记录同步尾巴的结果:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_replay_regions" language="csharp":::

遵循这些规则的重新执行代码:

- 将每个副作用置于记录操作或同步尾巴中。
  记录操作从方法的参数、先前结果和
  其它数值在每次重放时相同。
- 读取和修改所记录的效果、步骤或同步尾巴的 grain 状态。 Grain
  显示每个承诺的修改, 所以重放代码读到状态
  写下操作 。
- 执行时的逻辑时间 `DurableTask.Current.UtcNow`的读取时间,
  <xref:Orleans.Durable.Tasks.DurableTask.Current?displayProperty=nameWithType> 曝光
  执行后,处决的执行效果尚未实施,并被推进到
  记录每个孩子的完成时间,记录执行观察结果,所以重放
  当完成之前的工程时,要对相同时间和延迟进行观察。
- 产生随机值和识别特征,记录效果或儿童,以及呼叫
  职级外部服务或 `DurableTask.Run` 代表,其外部影响需要
  他们的自身能力。
- 以 `WithId` 命名儿童,当他们被有条件地安排在循环中时,或
  组合和名称组合。生成的区段是稳定的
  只有在按照同一顺序创建了无名儿童时才使用。
  发送由来自目标的ID和根ID或信号,所以添加
  一个人不能移动别人
- 通过一个返回 `logger.ReplaySafe()` 的日志记录器登录, 所以每个事件都一次登录 。

[重演安全规则](durable-tasks-replay-rules.md) 列出每一规则,
以及报告它的分析师诊断

<xref:Orleans.Durable.Tasks.DurableTaskContext.IsReplaying?displayProperty=nameWithType>
运行报告是否是先前运行的重运行操作:它是 `true`
运行开始后在任务树上除
符号,直到运行到达每个运行点或启动一个不
先前运行记录, 然后停留 `false`。 仅用于记录和诊断。 A
运行在日记写前结束, 工作被重放, 所以代码运行
只有 `IsReplaying` 才有 `false` ,可以运行两次或永远不能运行,并编码在其中的分支
以不同的运行模式列出不同的操作。

重放, 将不同的操作安排在记录ID下, 或者无法解码
记录的结果有差异,不同的是:运行时间拖延执行,直到
代码,该代码与记录重放相匹配,并生成 `ExecutionStalled`,该代码命名
和所请求的操作。
[诊断性重显示差异](durable-tasks-operations.md#diagnose-replay-divergence).
在执行处决待审期间改变工作流程
`DurableTask.GetVersion`,它使每个执行方都能够对修改版本进行修改
和版本 `0` 执行前代码经过更改的处决;见
[版本耐久任务](durable-tasks-versioning.md).

<a id="combinators-and-handles"></a>

## 组合器和控点

混合体同时安排其输入时间, 所以运行时将进行
等待,一个组合器作为等待处决的一部分运行
输入指数所标明的每个输入都是儿童
组合器操作 ID 的下端的组合器操作 ID。 用 `WithId`命名操作, 所以输入
当以后部署时增加或删除早期组合时,他们的身份证将保留。

每个组合器也是任务序列的扩展方法, 所以列表、 列表、 a
设置的查询
`lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)).WhenAll().WithId("reserve")`.
控点列表以同样方式组成,例如 `handles.WhenAny(cancellationToken)`。
收藏表达式没有它自己的类型, 所以 `[a, b]` 转到静态形式 :
`DurableTask.WhenAll([a, b])`.

- <xref:Orleans.Durable.Tasks.DurableTask.WhenAll*?displayProperty=nameWithType> 如下
  `Task.WhenAll`. 。它将在每次输入为终端后完成,包括
  永久取消达到。 然后按输入顺序抛出第一个失败;
  其它的,输入顺序中第一次取消;否则 `WhenAll<TResult>` 返回
  输入顺序中的结果。输入顺序选择例外,所以重放观察
  同一个
- <xref:Orleans.Durable.Tasks.DurableTask.WhenAny*?displayProperty=nameWithType> 返回
  第一次完成输入的索引, `WhenAny<TResult>` 也返回其
  组合失败。其他输入
  继续跑
- <xref:Orleans.Durable.Tasks.DurableTask.Race*?displayProperty=nameWithType> 选择它的
  等赢 `WhenAny` 并返回相同值,然后请求取消
  其它输入,并随原因而输入 `Another task won the race.` ,等待各输入
  终端终端,所以没有输入会超过比赛。失败或被取消的赢家失败
  ;其他投入的结果并不。
- <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitAsync*> 和
  <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitUntilAsync*> 种族任务,输入
  `0`,有耐久延迟的输入 `1`。任务完成前,组合器
  取消延迟并返回任务结果。当延迟先到时,
  任务的任务中接收一个取消请求,该请求的理由名称为超时,并且
  任务结果的等待:结束的任务取消后会失败
  <xref:System.TimeoutException>, and any other outcome, such as a result which the task
  无论如何,返回是结果。
- <xref:Orleans.Durable.Tasks.DurableTask.ScheduleAll*?displayProperty=nameWithType>
  将输入和返回其 <xref:Orleans.Durable.Tasks.ScheduledTask>
  输入顺序的控件,不等待结果。
- <xref:Orleans.Durable.Tasks.DurableTask.WhenAllCompleted*?displayProperty=nameWithType>
  等待直到每个输入为终端并返回控点。输入失败或
  取消后组合取消成功, 这样工作流程可以对每个
  其结果,例如通过对成功的投入进行精确补偿。
- <xref:Orleans.Durable.Tasks.DurableTask.WhenEach*?displayProperty=nameWithType> 附表
  其投入和产生每个人的处理器 `IAsyncEnumerable`,按完成顺序排列。
  输入失败或取消效果就像成功一样。

下 grain 每个儿童最终结果的邮票,每个儿童的终端结果都有一个持久的完成顺序,
记录结果的日记。 `WhenAny`、 `Race`和 `WhenEach` 选择
以最小序列的剩余输入, 所以每重播放一次都选择相同的赢家
输入相同的顺序,包括重播,其中更多的输入已经完成。
`ScheduleAll`, `WhenAllCompleted`,和 `WhenEach` 仅在耐久的等待中运行
因为他们的手柄属于执行过程

A <xref:Orleans.Durable.Tasks.ScheduledTask> 处理一个任务。等待一经等待,a
或组合器观察其终端响应、 `IsCompleted`、 `Status`和
`Exception` 报告同步。
<xref:Orleans.Durable.Tasks.ScheduledTask`1.GetResult*> 返回结果或丢弃
记录失账或注销,以及
<xref:Orleans.Durable.Tasks.ScheduledTask`1.TryGetResult*> 返回 `true` ,结果
任务完成后,处理器在观察终端响应前
`Status` 是 `Pending`。在一项持久的任务中,一个把手的 `Id` 命名了任务的运作,
组合和重放地址,
<xref:Orleans.Durable.Tasks.ScheduledTask.TargetTaskId> names the task on the grain which
其它当事方处理的:
`GetDurableTask` 或 电话 的 任务 、 或 其 目标 上 的 任务 。 对于 现有 把手 、 可以 观察
<xref:Orleans.Durable.Tasks.ScheduledTask.WhenAll*?displayProperty=nameWithType>,
<xref:Orleans.Durable.Tasks.ScheduledTask.WhenAllCompleted*?displayProperty=nameWithType>,
和 <xref:Orleans.Durable.Tasks.ScheduledTask.WhenEach*?displayProperty=nameWithType>
提供相同的语义,以及
<xref:Orleans.Durable.Tasks.ScheduledTask.Race*?displayProperty=nameWithType> 返回返回
每一个手都看到结果后,就赢了。 `WaitAsync` 和
`WaitUntilAsync` 加上超时,可遵循组合组合的超时规则;在耐久之外
任务,例如客户端的任务,它们测量系统时钟的超时。 `WhenEach`
接受 `WithId`,因此命名的表格将输入时间与
`ScheduleAll(...).WithId(name)` 和列举 `ScheduledTask.WhenEach(handles)`.

<xref:Orleans.Durable.Tasks.DurableTask.Delay*?displayProperty=nameWithType> 和
<xref:Orleans.Durable.Tasks.DurableTask.DelayUntil*?displayProperty=nameWithType> 是
以逻辑时间测量的耐久计时器。运行时记录延迟到期时间和
发送预定的回信给 grain ,其内容保留到持久工作
逻辑时间读取“持久工作”的时钟,所以时间到到到应得时间
框中的内容框共共享一个时间域。记录到的到期时间是承诺:要求重放
另定一个到期时间, 例如配置的时长更改后, 保留记录
一次运行并记录警告。 超时使用这些延迟, 所以从运行中计算超时
第一次等待它。

<a id="cancellation"></a>

## 注销

取消是合作、持久和无能的。 grain 请求理事会提出
与 `IDurableTaskManager.CancelAsync(rootId, reason)`任务、客户和其他
打电话者通过
<xref:Orleans.Durable.Tasks.ScheduledTask.CancelAsync*?displayProperty=nameWithType>.
请求执行一项任务,但 grain 不保留丢弃 `DurableTaskNotFoundException`
请求在被请求之前,通过任务执行树传播:

- 拖延,儿童 尸体还没有开始的 和新的孩子
  任务计划取消后终止, 不运行 。
- 办理手续的儿童通过
  <xref:Orleans.Durable.Tasks.DurableTaskContext.CancellationToken?displayProperty=nameWithType>,
  等待其他 grains 电话的,收到被调用者申请的取消通知
  完成自己的任务。
- 任务机构决定结果。等待一个取消的孩子扔
  `OperationCanceledException`。当该例外情形脱离
  身体; 捕获体,其结果或失败会完全完成。
- 完成的孩子们记录他们的成绩,重放取消的任务
  与重放的运行点相同,在请求中对请求进行观察,然后身体开始
  这一点是该身体持久运行之一的开始,例如
  儿童开始、 `DurableTask.RunInline` 效应或等待任务或信号:a
  正在运行的物体在目前工作时会观察请求,而当前工作需要等待
  长期的等待是等待,运行时间记录了任务点。重放时保持
  持久牌号未排出,直到它到达记录点。
  其点没有承诺遵守请求,到达最后一次行动
  记录前行的,然后记录此点。在它之前结束的重放
  记录点 [闲置](durable-tasks-versioning.md#stalled-executions) 有差异。

第一项请求的理由是可持久的:
<xref:Orleans.Durable.Tasks.DurableTaskContext.TaskCancellationReason?displayProperty=nameWithType>
以取消的处决、其子孙、每一次重播和
将它转到其他 grains的待发电话。

身体通过开始赔偿,从
<xref:Orleans.Durable.Tasks.DurableTaskExtensions.Shielded*>. 被保护儿童,例如
本地步、 延迟或呼叫另一个 grain, 工作将持续完成, 尽管任务
请求取消,后来的一项要求跳过,而该机构仍然拥有
等待结果,每输入一个屏蔽的组合盾,每输入一次,每输入一次。
等待行动以同样的方式将盾牌封上 所以一个屏蔽信号等待,延迟
`GetDurableTask` 订阅, `Race`, `WhenAny`,或 `WaitAsync` 完成后
取消任务的任务。如果尝试,通过禁用、中止或
删除任务执行树, 仍然结束屏蔽操作 。

行政中止,通过 `IDurableTaskManager.AbortAsync(rootId, reason)` 或
<xref:Orleans.Durable.Tasks.ScheduledTask.AbortAsync*?displayProperty=nameWithType>,
执行任务时失败的树及其执行树
<xref:Orleans.Durable.Tasks.DurableTaskAbortedException> in one journal write, stops
取消运行中的尝试, 并向每个待决电话发送中止通知到另一个 grain。
取消尝试的持久操作
<xref:Orleans.Durable.Tasks.DurableTaskAttemptCanceledException>, and
`IsTaskCancellationRequested` 保留 `false`,所以中止结束一项没有补偿的任务。当一个机构忽略取消或其
清理工作被卡住了。

任务取消和尝试取消的记录不同;见
[任务取消和任务取消](durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation).

<a id="starting-and-observing-tasks"></a>

## 开始和观察任务

永久调用上的配置设置了任务是什么, 以及开始的调用
当召唤者继续喊叫的时候,

| | `await call` | `await call.ScheduleAsync()` | `call.Post()` |
| --- | --- | --- | --- |
| 附加( 默认) | 一旦结果持久,儿童将跑步并返回其结果。 | 一旦儿童 grain 接受并存储,返回其手柄。 | 执行后的儿童将使用下一期日记进行阶段,然后返回其手柄。 |
| `.Shielded()` | 随附的, 电话取消没有到达孩子。 | 随附的, 电话取消没有到达孩子。 | 随附的, 电话取消没有到达孩子。 |
| `.Detached()` | 启动独立的根任务并返回结果。 | 一旦根任务 grain 接受并存储后,返回其处理。 | 将根任务与调用者的下一期日记写入并返回其控点的状态相接。 |

- 一名随身生子属于行刑者所生的树:行刑者拥有它,其父或母系
  取消时,将孩子删除。
- 启动独立根<xref:Orleans.Durable.Tasks.DurableTaskExtensions.Detached*>
  工作目标、后续工作、清理工作(比起步者早于启动者的时期)和
  启动器不需要其结果。启动器可以运行时完成,
  任务持续运行, 贯穿启动器的取消、 中止和删除。 其根 ID
  是 <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WithId*> 姓名、逐字记录、
  以该名称或 `{grainId}/{taskId}:{segment}`为地址的其他各方,
  `{grainId}` 是指开始 grain , `{segment}` 是执行的生成代号
  下个未命名的操作, 所以在不同的 grains 上启动任务编号相同的工作流
  执行
  用户,如等待任务或处理任务时,收到结果;取消
  等待只结束订阅。
- <xref:Orleans.Durable.Tasks.ConfiguredDurableTask.ScheduleAsync*> 返回时返回
  目标 grain 已经接受并长期保存任务, 所以任务编号, 手柄
  <xref:Orleans.Durable.Tasks.ScheduledTask.TargetTaskId>, can be handed to other parties,
  处理该主题的 `GetDurableTask`目标承认
  目标的日记写书店
  任务。一个目标拒绝的起始点,因为任务编号已经确定了
  其它方法,丢弃 `DurableTaskConflictException`。其他不接受该方法
  开始, 例如目标无法解码的请求, 重新尝试, 以及开始
  接收前未接受 `OutboxRetryTimeout` 扔弃
  `DurableTaskDeliveryException`. 等待任务时, 同样的失败会丢弃。 见 [死亡信](durable-tasks-operations.md#dead-letters)。
- <xref:Orleans.Durable.Tasks.ConfiguredDurableTask.Post*> 是同步的。它会分阶段
  引用调用器的发件箱, 该发件箱承诺使用 grain下一期日记写,
  加上调用代码所设定的状态变化, 并一次性交付。
  目标在完成任务时保持任务, 所以任务如果是其它身份
  政党向上查找从 `ScheduleAsync`开始。在一个耐久的任务中,开始记录为 `DurableTask.RunInline` 效果,所以
  重放返回同一把手,等待手柄稍后观察结果。
  客户端没有发信箱, 所以 `Post` 扔弃 `InvalidOperationException` 在那里,和
  [ORLEANS0216](../diagnostics/orleans0216.md) 已经报告。
- 在一项持久的任务中,有一份独立任务的记录 `$detach:{target}:{rootId}`,
  开始时要承诺, 因此重播到达记录后就没有什么意义。
  启动者持有请求书的传召,直至其目标接受请求
  开始时间在删除起始树的树之后, 才能维持。 在同一个根代号下启动相同方法后, 任务将再次保持不变 。
- 一项长期任务之外的一项持久任务,每一项任务都是根本任务,所以 `.Detached()` 留空电话
  。 `WithId` 提供根 根 ID,并让开始安全重试:一个调用器
  模棱两可的失败(如超时)达到相同任务后,重试开始。 A
  开始时不启动 `WithId` 接受一个新的随机根 ID, 控点的 `Id` 汇报,
  并且行为如一般的召唤。

检出后清理篮子,

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_checkout_post" language="csharp":::

<a id="signals"></a>

## 信号

<xref:Orleans.Durable.Tasks.DurableSignal`1> 带有外部事件,例如
发送通知或批准, 并进行中任务。 发件人和服务员共享一个
标注符,其中命名信号:

- <xref:Orleans.Durable.Tasks.DurableSignal`1.WaitAsync*> 等待信号值。
  <xref:Orleans.Durable.Tasks.DurableSignal`1.TryWaitAsync*> 与一个
  和返回的最后期限 `(true, value)` 或 `(false, default)`,以及
  每一次重播
- 信号地址为 *所援引的任务*:客户端、另一个 grain或一个
  将它分离到 grain上。在任务执行树的任意位置等待
  观察它的信号。
- 任务记录每个信号名的第一个值, 然后返回到每次等待
  ,包括重放中的等待。在等待之前发出的信号是
  记录,等待同时返回。一个重复信号,其等值相等
  任务保持不变,拒绝不同的值。
  <xref:Orleans.Durable.Tasks.DurableSignal`1.For*> 产生一个单独的名称,例如
  `approved/round-2`,针对重复事件的每一次发生。
- A grain 用 `IDurableTaskManager.SignalAsync` 或
  `TrySignalAsync`,与 grain的待变状态承诺。
  其他 grains 使用 `SignalDurableTaskAsync` 和
  `TrySignalDurableTaskAsync` grain- 参考扩展,以及长期任务
  grains 使用 `SignalDurableTaskAsync`,将发送重放记录在
  `$send:{target}:{taskId}:{name}`。在一次执行中重复发送一个信号,
  `TaskNotFound`之后的重试,按发生时、 `$send:...$2` 等编号。
  于是,他们各再次发出, `Try` 窗窗窗窗窗窗返回 a `DurableSignalResult`: `Accepted`,
  `Duplicate`、 `Conflict`、 `TaskNotFound`或 `TaskCompleted`。
- grain 可在一份日记中对另一个 grain 进行援引和发出信号
  。然后,信号信息可以先到达;接收方 grain 记录其价值,
  礼拜降临的时候,他们要遵守它。

<a id="await-tasks-which-another-caller-started"></a>

## 等待另一个呼叫器启动的任务

`GetDurableTask<TResult>(rootId)` grain- 参考扩展名,或其 `TaskId` 超载,
将 <xref:Orleans.Durable.Tasks.DurableTaskReference`1> 返回为现有任务,例如
一项持久的任务等待提及
订阅任务结果, 其他代码等待结果 。
参考文献可通过一项扩展方法等待,因此 ASP.NET 核心最小 API
处理器将它返回为值,并且 <xref:Orleans.Durable.Tasks.IDurableTaskReference>
读取 `Target` 和 `Id` ,没有结果类型。例如,一个网钩中继等
操作后,再交付结果:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_reference_relay" language="csharp":::

- 这是一项持久的任务,等待参考、安排或转交 `WhenAny`,
  `Race`、 `WaitAsync`或 `WaitUntilAsync` 认认执行时的孩子,记录
  儿童身份,该操作指定给孩子的身份,如呼叫另一个儿童 grain。
  并承诺在一份日记中订阅,同时向儿童发送回放
  不再订阅了
- 订阅者通过发件箱前往任务 grain 的发件箱,作为一次不需
  问题 grain 将儿童添加到任务之外。
  用户除了任务提示器之外,
  向告发者施以刑,并保留任务直至完成后退休。
  任务的结果:结果、失败或取消。
- 终端任务订阅迅速完成,一个任务订阅
  保留 grain 不作为援引的任务,因为保留结束或从未存在
  引用时, 快速失败, 与 `DurableTaskNotFoundException`发生 迅速失败; 重放也遵守同样的
  。一个任务编号,如果达到用户的用户,是指其 grain 持有的任务,
  原因 `ScheduleAsync` 返回时返回 grain 已经接受了任务。
  `WaitAsync` 或 `WaitUntilAsync`。
- 取消儿童,例如当 `WaitAsync` 最后期限过后或输入 `Race` 时
  失败、 以取消结束并终止订阅。 此任务继续 。
- 订阅存储的日记本上完成的同一 grain 任务
  任务的结果。
- 客户端和普通 grain 方法等待相同的参考,或调查、取消或中止
  完成使命, 号召该任务 grain.

<a id="store-and-send-task-addresses"></a>

### 存储并发送任务地址

A <xref:Orleans.Durable.Tasks.DurableTaskAddress> 将任务 grain 与身份识别码对齐, 所以代码
以日记状态存储任务, 以 grain 调用发送任务, 或者在 HTTP 中返回任务
有效载荷,然后到达任务后。 Orleans 将其序列化, System.Text.Json 写道:
以 `{"Target":"order/42","Id":"place-order"}` 内 JSON 和HTTP有效载荷,无
登记。A <xref:Orleans.Durable.Tasks.TaskId> 的序号是其逃出路径,例如
`place-order/reserve`.

- `GrainFactory.GetDurableTask<TResult>(address)`或对 a
  `IClusterClient`返回任务参考, 它等待、 订阅、 投票,
  取消或中止,如上所示。
- `DurableTaskReference<TResult>.Address` 读取参考地址。
- `handle.TryGetAddress(out var address)` 读作 a
  <xref:Orleans.Durable.Tasks.ScheduledTask> handle's task: a root task which a client or an
  普通 grain 方法开始,或在耐久任务内,一个分离的任务或一项任务
  `GetDurableTask` 随附的孩子属于其打电话者的树上,所以
  控点没有地址 。

A grain 记录它所执行的、等待的、调查的或取消的事务,
在任何几次恢复活动之后:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_store" language="csharp":::

[任务身份和参考](durable-tasks-identity.md#address-a-task-from-elsewhere)
说明哪些任务已经解决,以及它们要解决多久。

<a id="commit-model-and-guarantees"></a>

## 提交模式和担保

- `(grain, TaskId)` 指定一个执行执行。任务编号的再使用与
  它从同一个打电话者中引用相同的界面方法; 参数没有比较,
  和再利用与不同界面或方法冲突。
- 请求身份、任务状态、电话订阅和信箱完成前承诺
  开始执行。
- 任务状态和发送信件共享 grain日记帐簿的
  grain自己的日记状态。 失败的日记写将 grain和
  下一轮启动从承诺状态恢复。
- 执行的每个部分, 代码在两个耐久等待之间的间隔, 以请求方式运行
  排在《公约》 grain,因此它遵循 grain`作为 grain 方法确实如此。
- 当执行的段运行或等待运行时, grain 将保持一个永久工作
  于30秒内重新启动撞车的 grain 并恢复
  执行。停在电话结果、信号或延迟恢复
  当它等待那迹象的时候,当它重新生动地, grain.
- 发出耐久 grain呼吁,包括从普通方法中发出呼吁,分阶段
  发件箱中的引用, 所以它以原子方式执行 grain的状态变化 。
  在一个长期的任务中,等待并安排一个呼叫阶段,同样地。
- 每个请求和结果有效载荷都按 grain,按任务状态一次日记。
  以文字写成的完成信也保留了结果,
- 重新执行前要恢复。儿童身份、完成顺序和发出的信息
  复放稳定。
- 终端结果和通知完成通知 通知呼叫者一起承诺。
  第一次持久的终端结果赢得了完成和取消之间的竞争。
- 人体的产物结果 由工作项目进行 操作身体最终代码
  激活, 所以它承诺在同一个日记里 写到代码所在的状态
  修改, 任何操作请求写入的, 都更改。 A `DurableTask.RunInline` 记录
  与影响状态变化处于同一工作项目阶段,并承诺与之保持一致。

可适用性涵盖参加 Orleans 日刊和
外部副作用,例如向付款提供方打电话等,需要
其自身的能力,例如任务编号中产生的请求键;见
[持久任务中具有的](durable-tasks-idempotency.md).

<a id="lifecycle-and-retention"></a>

## 生命周期和保留

A 音 A *引用任务*客户或他人 grain 计划, 拥有执行树 :
任务及其后代, 他们没有援引的任务本身, 例如当地
单列、延迟、 `DurableTask.RunInline` 记录、独立任务记录、信号发送
标记、订阅和调用其他 grains的通话记录。
终端,其 grain 删除树:

- 当一个 grain 调用任务时,在日记中写到完成后将退出的任务
  呼唤 grain 已持久地接受了结果。
- 当任务没有呼叫 grain时,例如客户预定的任务或
  独立任务, 完成后 `DurableTaskOptions.ResultRetentionPeriod` 。
  在此期间,将保留的结果归还原结果,随后投票
  `DurableTaskNotFoundException`.
- 在这两种情况下,在每一个被确认的持久任务完成之前,都不得退休。

删除树的树删除会取消未完成的任务。 因此任务 ID 将更改时间表
直至任务被删除,在删除后安排时间再执行请求。
完成和延迟恢复到达删除任务时的补全和延迟被忽略。 见
[保留和删除](durable-tasks-operations.md#retention-and-deletion) 用于
业务细节。

<a id="configure-the-silo-and-clients"></a>

## 配置 silo 和客户端

需要为长期性任务发布储存和工作持久储存的《日刊》。
`AddDurableTasks` 排在《公约》 silo 建筑商添加了通信和持久任务运行时间,
任务状态 grain登记和登记 JSON 元数据中
JSON 日记日记格式对任务状态的需要。 它的可选回调配置
`DurableTaskOptions`. 开发时,配置模拟存储:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_registration" language="csharp":::

安排、观察、取消或信号通知持久任务
客户端适配器 :

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_client_registration" language="csharp":::

Grains 源自 <xref:Orleans.Durable.Journaling.DurableGrain> 包含可持久任务,并且
`AddDurableTasks` 给每个这样的 grain 一个收件箱和一个发件箱。在激活设置期间,
在日记恢复前,运行时间与 grain的日记、收件箱和发件箱连接,
和 grain 可注射 `IDurableTaskManager` 观察、取消、中止并发出信号
任务自任务。 任务状态可以使用任何日记格式;请求和结果存储于
排 Orleans 使用共享、制作/职等期刊和持久工作
多silo 部署的提供者;见
[日记日报配置](journaling/configuration.md).
