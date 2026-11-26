# Orleans.Durable.Abstractions

> [!IMPORTANT]
> 这个释放前的阿尔法包包在活动下提供 Orleans 耐久任务抽取
> 其方案拟订模式可以作为主机实施,在阿尔法释放之间演变
> 提供反馈。

此组件定义 Orleans 长期、非同步业务
排 `Orleans.Durable.Tasks` 命名空间,用于 .NET 11 (`net11.0`). An Orleans 东道主用品时间安排、持久性
确定时间和持久取消。任务定义取决于这种抽象
组合 Grain 接口项目,这些项目宣布 `DurableTask` 方法,客户则呼吁
也请参见 `Microsoft.Orleans.Durable`。

<a id="execution-model"></a>

## 执行模式

- `async DurableTask` 呼叫创建了单用途推迟定义。 它的编译器生成
  状态机器从主机运行定义时开始,在终端后释放捕获状态
  完成。为每次执行创建一个新实例。定义由 `DurableTask.Run`创建
  可反复安排。
- 每个根执行有一个 `TaskId`: `WithId` 指定的名称,或用于
  任务开始于一个没有名字的持久执行外。儿童身份文件附加到一个呼叫器
  生成的段或生成的段段到父代号。当
  无名儿童按照同样的定型顺序创建。 1个 `WhenAll` 或 `WhenAny`
  援引在作业编号下,为其同时批次的作业标识下指定稳定索引槽位。
  `WithId` 名称。独立并
  包括同时组合数的援引、使用明确的 `WithId`
  以确定顺序开始的或以其他方式开始的。 `WithId` 重复一次的段
  以顺序、 `line`、 `line$2`等方式,按其后来的发生情况来点出
  循环将他们的身份保留在重放中。生成的片段包含 `$`,但明确的名称不包含这些部分。
- A `TaskId` 格式和缩略语是其逃出的等级路径,例如 `order-42/charge`,其中 `\`
  逃逸 `/` 和 `\` 在一个段内。 System.Text.Json 序列序列成该字符串, 以值或
  字典键,通过 `TaskIdJsonConverter`, 所以 JSON 有效载荷和源产生的环境
  未经登记的任务标识。
- 将同一定义安排在与所记录的答复重新连接的现有ID
  东道主保留了与身份证明相关的第一个定义。
- 等待取消的信号会放弃该排程、投票或等待操作。 它确实会
  请求取消持久任务。
- `CancelAsync` 请求持久 *任务取消* ,并在主机记录完成
  请求。请求是单调和无能的,主机保留任务状态。取消
  合作者:取消 `DurableTask.Current.CancellationToken` ,
  `IsTaskCancellationRequested` 成为事实,请求送达任务随附的子女,
  当取消时,例如从
  等待被取消的子午,任务被取消;否则任务会与身体结果或身体结果一起完成。
  身体的补偿,如 `DurableTask.RunInline` 效应、被保护儿童以及
  日记状态变化,承诺。
- `CancelAsync(reason)` 任务代码中写到:
  `DurableTask.Current.TaskCancellationReason`,例如决定其 `catch` 块
  第一项请求的结果。 第一次请求决定了原因, 东道方保留了它, 任务取消
  并传给任务随附的孩子,以及一个重放,
  原始运行点首先观察到它,读到同样的原因。
  `TaskCancellationReason` 是在请求取消之前和请求无要求时 `null`
  原因。主机通过 `DurableTaskContext.RequestTaskCancellation`提供理由。
- 主机取消 *尝试*,一个任务正文的模拟运行
  `DurableTaskContext.CancelAttempt` 当尝试的作品无法再被记录时:
  grain 激活停止,任务被中止或删除,或者运行代码无法重播放
  记录任务任务执行(`UnsupportedVersion` 或 `Diverged`)的记录。从那时起,每个耐用
  试投 `DurableTaskAttemptCanceledException`,源自
  `OperationCanceledException` 并携带 `DurableTaskAttemptCancellationReason`: `RunInline`,开始
  或等待儿童,包括被保护的和被分离的儿童, `Post`, `ScheduleAsync`信号
  尝试取消的结束会丢弃的等待
  。 主机也抛弃了尝试响应的响应;停止激活的任务重放
  以及一项任务,其尝试无法重放记录 一直到代码
  重放它运行。 尝试代码的更改会变成日记 grain 状态,例如清理,
  以 grain的下个日记写入方式执行 。
- `DurableTask.Current.CancellationToken` 被任务取消和尝试取消
  取消, 所以普通任务代码在两种情况下 迅速终止的通话。
  `DurableTask.Current.AttemptCancellationToken` 仅因尝试取消而取消。
  任务取消后在已筛选的 `catch` 中运行了耐久清除
  `IsTaskCancellationRequested`,通过 `AttemptCancellationToken` 发出呼叫,因为
  `CancellationToken` 已被取消, 而且
  [ORLEANS0217](https://aka.ms/orleans/diagnostics#orleans0217) 报告经过清理后通过
  `CancellationToken`. 处理过程中的清理,例如释放气肿或
  处置资源,属于 `finally` 区块和 `using` 对帐单中,这两个语句都运行。
- 任何一个牌子上的注册都是普通同步 .NET 观察者,当该牌子是
  取消并迅速返回。
- 由主机提供的延迟使用 `DurableTask.Current.UtcNow`, 因此重放观察
  同一逻辑时间。 `DurableTask.DelayUntil` 等待绝对的到期时间。
<a id="responses"></a>

## 答复答复答复

`DurableTaskResponse` 状态尚未确定、签署、成功、取消和失败。
尚未答复和被接受的答复不完整。 已成功、取消和失败的答复
正在结束,等待终端响应返回其价值或丢弃记录
取消或失败。主机的等待返回终端响应; `ScheduledTask.WaitAsync` 和组合器
返回或产生观察到的控点,如 `ScheduledTask.WhenAny`、 `ScheduledTask.WhenEach`和
`WhenAllCompleted`,当等待返回不完整的答复时丢弃 `InvalidOperationException` 。

<a id="combinators"></a>

## 组合器

`WhenAll` 和 `WhenAny` 接受一份清单或任何输入序列,当他们被召来时,它们会捕捉到。
组合器作为等待执行的一部分运行,记录没有结果。
儿童在组合器操作识别符号下输入索引中识别的,因此其身份稳定
当输入顺序稳定时, 交叉重播放。 组合组同时安排其输入时间, 所以一个主机
可以一起把批量装进去

每个组合器也是输入收藏的扩展方法, 因此列表、 数组或查询组成于
地点:

```csharp
var reserved = await lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units))
    .WhenAll()
    .WithId("reserve");
```

手法相同: `handles.WhenAll(cancellationToken)`、 `handles.WhenAny()`、 `handles.Race()`、
`handles.WhenAllCompleted()`和 `handles.WhenEach()`。像 `[a, b]` 这样的收藏表达式没有其本身的类型,所以将其传递到静态形式,
`DurableTask.WhenAll([a, b])`.

`WhenAll` 如下 `Task.WhenAll`:它只在每次输入为终端之后完成,包括
永久取消时间达到。等待它时,当输入失败时,先将第一个失败按输入顺序丢弃;
否则,当任何输入被取消时,它会将第一个输入 `OperationCanceledException` 以输入顺序投放;
以其他方式 `WhenAll<T>` 返回输入顺序中的结果的数组。输入顺序选择例外,所以a
重放在任何完成顺序中都观察相同的。 `WhenAny` 返回赢家输入索引,
并且 `WhenAny<T>` 还返回其结果; 失败或被取消的胜者会让组合失败。 获胜者是
第一次完成输入, 且主机在重播放时选择了相同的赢家: 它从耐用
完成单,或根据确定决定身份证继续选择。 `WhenAny` 将所有其他预定时间
任务正在运行中。 持久取消将到达输入, 输入是附加的子子子, 组合器等待
其结果。

`Race` 保持结构化的同价货币:它安排投入,选择优胜者如 `WhenAny`,然后请求
以原因 `Another task won the race.`理由永久取消其他输入,并完成每次输入
是一个终端,所以没有输入超过它。 它返回什么 `WhenAny` 返回, 失败或被取消的赢家失败了它,
其它投入的结果不影响它。 `WaitAsync(timeout)` 和 `WaitUntilAsync(dueTime)` 都对定义有不同,因为
输入投入 `0`,并有一个耐用 `Delay` 或 `DelayUntil`输入 `1`重放时会记录延迟的及时时间,所以
从第一个运行开始计算超时的计时。 当延迟时间流过后, 任务会收到取消请求并有原因
如 `The operation timed out after 00:02:00.`等,组合器等待结果:结束的任务取消
以 a 失败 `TimeoutException` 其内有特经,而任务本身除外。 `OperationCanceledException`,以及任何其他
其结果,例如任务在观察请求后返回的结果,是:

```csharp
// The authorization runs as authorize/0 and its timeout as authorize/1.
var payment = await Payments.AuthorizeAsync(order.Total).WaitAsync(TimeSpan.FromMinutes(2)).WithId("authorize");
```

组合器生成的操作识别符号,例如 `$when-all-2`,计数它之前运行的组合器
执行。 `WithId` 给操作命名,因此当部署增加或删除时,其输入保留了识别符号
早期组合器 :

```csharp
var reserved = await DurableTask.WhenAll(lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)))
    .WithId("reserve");
```

输入为 `{taskId}/reserve/0`、 `{taskId}/reserve/1`等,一个记录筛选记录的主机a
命名 `WhenAny`中 `{taskId}/{name}/$winner`. `WithId(name).ScheduleAsync()` 将相交组合的
儿童任务,并记录合并结果,确定
执行时生成的操作识别符下的投入。

`ScheduledTask.WhenAll` 等待排 `WhenAll`:完成每一个任务后,它就会被扔
输入顺序的第一个失败,或者输入顺序的第一个取消。 `ScheduledTask.WhenAny` 返回返回
完成第一个任务, 包括失败或取消的任务。 在持久执行中, 主机选择它喜欢
`WhenAny`的胜者,来自其持久完成令或它记录在a以下 `$winner` 下的选定
生成 `$when-any-N` 操作标识符, 所以每次重播放都返回相同的任务。 在耐用执行之外, 它
返回第一个任务,即其持久响应到达时的永久响应。当被调用时它会捕捉候选人,保存选中
当调用者随后重新使用或修改输入收集时, 以及返回的任务观察到
电话取消和主机等候失败被传播。
选择时, 它取消并耗尽了失去的等待操作, 而没有取消持久任务 。

`ScheduledTask.Race` 选择赢家喜欢 `ScheduledTask.WhenAny`,在生成的 `$race-N` 操作操作标识符
要求持久取消其他任务,然后将每一任务都退回获胜者一次
是一个终端,所以每个控点的结果是可见的。一个控点 `WaitAsync(timeout)` 和 `WaitUntilAsync(dueTime)`
返回 `Task` 和 `Task<T>`,并附有定义超载的结果。在耐久执行中,超时是一个
执行时间表中 `1` 操作标识符下 `{segment}$timeout-N`, which the
控点的控点段和它的等待时间数数决定, 所以其他任务上的操作不会改变它, 而主机
选择获胜者; 在一个外, 系统时钟的超时间隔会延后, 手柄会收到取消
当它正在取消时请求。 它们的取消标记将放弃等待而不取消任务。 每一种类型都未命名
组合数计数自己的操作,例如 `$when-any-1` 和 `$when-any-2`。

<a id="scheduled-task-handles"></a>

## 排定任务处理

A `ScheduledTask` 是热辣的耐久的, `Task`:它携带任务身份证,并观察其反应。
一旦等待、民意调查或组合器观察终端响应, 手柄会缓存它, 所以 `IsCompleted`, `Status`,
和 `Exception` 同步检查任务, `ScheduledTask<T>.GetResult()` 返回结果或扔
记录失败或取消,如等待的结果。 `TryGetResult(out value)` 返回 `true` 与任务结果
成功完成。 在处理器观察终端响应前, `Status` 是 `Pending`。客户调查
与 `IsCompletedAsync`、 `GetStatusAsync`和 `GetResponseAsync(PollingOptions)`一起;一个持久的任务机构等待着
或等待一个持久的超时,而以其投票结果为开始 `NotSupportedException`
([ORLEANS0225](https://aka.ms/orleans/diagnostics#orleans0225)).

控点 `Id` 的控点指定执行中开始或观察任务,进行组合和重放操作
其 `TargetTaskId` 指定了主机的任务,其他当事人都负责该主机的工作。这两个地址是等号,除非
将任务保存在另一个标识符下:一个独立的任务的处理报告其根 IM 。
调用者通过 `IScheduledTaskHandle.TargetTaskId`引用调用者所援引的任务。主机描述一项任务
的特性,这些特性 `ScheduledTask.GetFeature<TFeature>()` 返回 `IScheduledTaskHandle.GetFeature<TFeature>()`,
例如,其他各方完成任务的地址。

同步排出每个输入的`DurableTask.ScheduleAll(inputs)` 列表, 这样主机就可以将批量以一个文字进行, 并且
将他们的控点按输入顺序返回,不等待结果。 `DurableTask.WhenAllCompleted(inputs)` 还等到
每个输入都是终端, 返回控点, 每个都随其观察到的结果; 输入失败或取消
并取消这次企图 `ScheduledTask.WhenAllCompleted(tasks)` 等待现有控点
其输入被确定为 `WhenAll` 投入,在 `$schedule-all-N` 或 `$when-all-completed-N`之下,或
指定 `WithId` 的段下。控点属于等待执行的部位,因此这些组合只运行
将一个工作作为其本身的任务安排在 `ScheduleAsync()` 扔弃
`NotSupportedException`.

`WhenAllCompleted` 精确补偿成功的投入:

```csharp
var reservations = await DurableTask.WhenAllCompleted(
        lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)))
    .WithId("reserve");
var reserved = lines.Where((line, i) => reservations[i].TryGetResult(out var ok) && ok).ToList();
if (reserved.Count < lines.Count)
{
    await DurableTask.WhenAll(reserved.Select(line => Catalog(line.ProductId).ReleaseAsync(OrderId))).WithId("release");
}
```

<a id="completion-order"></a>

## 完成令

`ScheduledTask.WhenEach(tasks)` 一次产生每项任务,按完成顺序作为 `IAsyncEnumerable`,
`DurableTask.WhenEach(inputs)` 将投入列入生成的 `$when-each-N` 操作识别符号下,当
开始查点,然后以同样的方式产生。一个手柄已经观察了它的终端反应和失败或失败
取消不会漏掉点数。 点数无法达到的输入会继续运行。 点数
取消代号后放弃输入列表, 等待输入, 而不取消输入 。

在持久执行范围内,东道主选择下一个任务中的每一项:在持久执行中首先完成的剩余任务
完成的顺序。每次重放都产生相同的顺序,包括一个已经完成更多任务重放,所以
以每个结果在到达时对它进行作用的代码重放相同的步骤:

```csharp
var quotes = await DurableTask.ScheduleAll(suppliers.Select(supplier => Supplier(supplier).QuoteAsync(order)))
    .WithId("quotes");
await foreach (var quote in ScheduledTask.WhenEach(quotes))
{
    if (quote.TryGetResult(out var offer) && offer.Price <= budget)
    {
        return await Supplier(offer.SupplierId).AcceptAsync(order).WithId("accept");
    }
}
```

`ScheduleAll(...).WithId(name)` 输入名称,如此示例。在耐用执行之外,
`ScheduledTask.WhenEach` 工作按等待观察响应的顺序来计算。

<a id="starting-and-observing-tasks"></a>

## 开始和观察任务

定义的配置设置了任务是什么, 以及当调用者调用时开始的调用
继续:

| | `await definition` | `await definition.ScheduleAsync()` | `definition.Post()` |
| --- | --- | --- | --- |
| 附加( 默认) | 一旦结果持久,儿童将跑步并返回其结果。 | 一旦儿童主机接受并持久储存,该儿童的手柄将返回。 | 执行处决的儿童的下一个耐久写作阶段,并归还其手柄。 |
| `.Shielded()` | 随附的, 电话取消没有到达孩子。 | 随附的, 电话取消没有到达孩子。 | 随附的, 电话取消没有到达孩子。 |
| `.Detached()` | 启动独立的根任务并返回结果; 取消等待只结束呼叫者的订阅。 | 一旦主机接受并永久存储, 就会返回根任务中的控件 。 | 与调用者的下一个耐久写作并返回其控点的根任务相继阶段。 |
| `.Detached(rootId)` | 作为 `.Detached()`,根号ID `rootId`下。 | 作为 `.Detached()`,根号ID `rootId`下。 | 作为 `.Detached()`,根号ID `rootId`下。 |

- 在可持久处决的范围内,一名随身儿童属于死刑执行的树:处决拥有该树,
  永久取消的, 到达它, 删除处决的树删除。
- `.Shielded()` 不让儿童被处决,而死刑仍然执行
  拥有它,等待它; [补偿取消的工作](#compensating-canceled-work).
- `WithId(name)` 命名开始任务执行的操作,作为计算发生时的名称,例如
  `reserve$2`。启动分配标识符,所以配置调用顺序不会改变
  ,并且配置的每个开始都进行下一次。
- 启动独立的根任务`.Detached()` 。 开始执行可以在运行期间完成,
  执行取消、中止和删除, 并不会达到。 开始名称
  用户名, 等待任务或处理后, 将结果保存为用户, 而不另设
  订阅; 取消等待仅结束订阅的状态。 其根 ID 来自主机
  东道国通过 `DurableTaskContext.HostId`,例如其 grain, 和
  执行的 执行的 ID, 以及启动操作的 ID: `WithId` 或处决的名称
  下生成的 ID。 根 ID 是单段 `{hostId}/{executionId}/{segment}`, 其中每个
  部分从它的 `/` 和 `\` 年为 `TaskId` 如此不同的主机、处决和操作
  根的区别 , 而重放则产生相同的根 。 分离任务的定义是主机
  开始为根任务, 如耐用 grain 调用, 因为主机存储并自行运行;
  步骤, `async DurableTask` 方法,以及作为开始执行的一部分运行的组合器,
  开始一个独立投出 `InvalidOperationException`
  ([ORLEANS0228](https://aka.ms/orleans/diagnostics#orleans0228)).
- `.Detached(rootId)` 启动独立的根任务 `rootId`,其他当事方事先知道
  等地址,例如 `place-order-42`。任务主机上的 `rootId` 从任何执行或
  包括重播在内的呼叫者 达到相同的任务
- 返回任务主机执行任务时返回`ScheduleAsync()` ,所以其 ID 即控件 `TargetTaskId`,
  也可以交给其他当事方,由后者以 `GetDurableTask`等调查手段处理。
  ,因为ID已经确定了另一个定义,所以失败。在持久执行中,执行
  观察每个被附着的孩子,并等待手柄、将其交给组合器或归还。
- `Post()` 同步: 它会分阶段开始, 启动时承诺使用调用器的下一个耐久
  写入, 加上调用代码所做的状态更改。 然后主机将启动
  重新尝试, 直到任务主机接受它, 而任务主机会重复任务所在的交付
  ID, 所以任务只开始一次。 任务主机不接受的起始点, 例如因为它
  端口失效 。
  在耐久执行内,开始记录为 `DurableTask.RunInline` 效果,所以是重放
  返回相同的控点, 不重新开始任务, 并等待处理后观察
  代码没有耐久的发件箱,例如客户无法发送。
- 在持久执行之外,每个任务都是根任务,所以 `.Detached()` 没有改变。 `WithId`
  和 `.Detached(rootId)` 提供根 I,使启动安全可以重试:一个打电话者,他再次进行
  模棱两可的失败( 如超时) 完成相同的任务后, 出现相同的失败。 没有名称的开始
  一个新的随机根代号, 手柄的 `Id` 报告, 并且行为就像普通电话一样 。

工作流程开始后续工作,完成工作,不等待其设置一个独立的任务:

```csharp
order.PlaceOrderAsync(request).Detached($"place-order-{orderId}").Post();
```

<a id="signals"></a>

## 信号

A `DurableSignal<T>` 指一个将值带入运行中任务的事件,例如装运
发件人和服务员共用一个说明:

```csharp
static readonly DurableSignal<Approval> Approved = new("approved");

var approval = await Approved.WaitAsync();
```

- 信号针对一个被援引的任务:一个打电话者,如客户、另一个 grain,或
  持续执行,分离它,开始。
  执行任务时,该任务在任何地方的任何地方都观察任务信号,包括等待
  `async DurableTask` 等待的任务的一步,等待的一步 `WhenAny` 每项引用的任务
  自己的信号
- 每个名称拉上每个任务的一个值。 东道主记录第一个值, 并显示任务状态
  并返回到该名称的每一个等待位置,包括重放中的等待。
  等值使任务保持不变,拒绝使用不同价值的信号。
  `For(suffix)` 的每个发生日期均产生单独的名称,例如 `approved/round-2`
  重复事件。
- 信号在等待之前就到达了, 等待立即返回。
- 等待处决的`WaitAsync()` 直接作为等待处决的一部分,因此不产生任何儿童
  任务 `TryWaitAsync(deadline)` 将等待与 `DelayUntil(deadline)` 相竞为 `Race` 和返回
  `(true, value)` 当信号在最后日期中获胜, `(false, default)` 在最后的最后期限中获胜。比赛取消了
  一旦其他输入和完成, 一旦两者都是终端, 则同时完成其它输入。 主机在重放时选择相同的赢家, 所以重放
  返回相同结果,并且 `WithId` 将种族与其他组合数一样命名:

```csharp
var (received, approval) = await Approved.TryWaitAsync(deadline).WithId("approval");
```

- 最后期限赢后,信号将保持其连接,所以稍后的 `WaitAsync()` 返回一个值
  时间到后。作为 `WhenAny` 输入,等待会比其他输入快,在信号获胜之后
  等待 `WaitAsync()` 再次返回记录值。
- 持久取消等待执行取消,取消等待。

<a id="compensating-canceled-work"></a>

## 补偿取消的工作

取消的工作流程一直运行到其身体完成, 这样它就可以撤销已经做的工作 。
开始补偿 `.Shielded()`:一个被保护的儿童跑到完成,工作流程等待
其结果如往常一样:

```csharp
public async DurableTask<OrderResult> PlaceOrderAsync(Order order)
{
    await inventory.ReserveAsync(order.Id).WithId("reserve");
    try
    {
        return await payment.ChargeAsync(order.Id, order.Total).WithId("charge");
    }
    catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
    {
        await inventory.ReleaseAsync(order.Id).WithId("release").Shielded();
        return OrderResult.Canceled(DurableTask.Current.TaskCancellationReason);
    }
}
```

- 被保护儿童,例如当地一步、延迟或呼叫另一个 grain,即使
  工作流程的取消是请求的, 而工作流程的取消 以后没有达到它,
  包括重放后。工作流程自己的 `CancellationToken`、 `IsTaskCancellationRequested`和
  `TaskCancellationReason` 仍然报告取消。
  被保护的子儿童, 并且用工作流程的执行树删除它。 通过自己的控管取消它
  取消它及其子孙。
- 只有在儿童被解除保护才能接触到儿童自己的孩子,所以只能接触到被保护的儿童自己的孩子。
  `DurableTask.Run` 步数和它开始的工作运行到完成。一个屏蔽组合器作为部件运行
  输入的工作流程和屏蔽。
- 期待的每一项持久经营荣誉 `.Shielded()` 同样的方式,不论幼儿还是儿童,不管其是否在
  工作流程的一部分:屏蔽信号等待、 `TryWaitAsync`、延迟、 `GetDurableTask` 订阅,
  `Race`、 `WhenAny`或 `WaitAsync` 在工作流程取消后完成,只有
  尝试取消时的删除结束。等待付款的工作流程
  `await Paid.WaitAsync().Shielded()` 仍然收到
  注销后即付款,并决定付款的含义。
- 工作必须比工作流程活得长,例如清理工作在工作流程取得结果之后继续进行
  被观察到,作为独立任务运行,工作流程的取消也没有达到:
  `inventory.ReleaseAsync(order.Id).Detached().Post()`.
- 将取消的任务的假期终止为即时取消:延迟,儿童其身体
  还没有开始, 和附属的孩子 没有保护 并计划后的请求,
  已经完成的儿童保留记录的结果,还有一个正在跑的孩子
  将请求本身加以观察。
- 重放取消的任务时, 在初始运行点首先观察到的请求值时, 重放已取消的任务值
  和清除,
  之后重播记录。

<a id="aborting-tasks"></a>

## 中止任务中的任务

`ScheduledTask.AbortAsync(reason)` 是指操作员,例如当一个机构忽略取消或
其清理工作被卡住了。 东道主终止了任务, 并断绝了子孙的连接
`DurableTaskAbortedException`其信息是原因, 并取消他们的运行尝试与
原因 `TaskAborted`尝试的持久操作 和
`IsTaskCancellationRequested` 留在 `false`,所以赔偿不会运行。等待中止的任务
丢弃 `DurableTaskAbortedException`。中止 a
任务结束后,它没有改变,独立根的分离任务被中止
分开。

<a id="writing-replay-safe-workflows"></a>

## 写入重放安全工作流程

主机重放一个未完成的执行, 例如, 其 grain 已停止使用 :
方法的正文从开始就再次运行,每个记录操作都返回其记录结果。 A
每次重放相同操作时, 以相同的操作和
同一顺序的识别特征和参数。

重播并不重建 grain的日记状态,
包括后来操作时作出的修改。

- **记录的业务** 每次执行一次,重放返回其记录的结果:
  儿童,如耐用 grain 电话、 `async DurableTask` 方法、 `DurableTask.Run` 代表,
  以及信号等待、分离和张贴启动及 `DurableTask.RunInline` 效应。
- **重新执行的代码** 是在身体最后一次记录操作之前,在每个重播中再次运行。
  它从方法的参数中计算数值,记录结果, `DurableTask.Current.UtcNow`,
  取消状态和在处决等待执行期间保持不变的配置。
- **同步尾巴** 跟随尸体最后一次记录操作,没有其他 `await`
  返回正文返回。它只在完成身体的运行中运行。执行
  `DurableTaskContext.OnBodyCompleted` 以任务所在的相同写法承诺其状态变化
  结果,所以不再重复播放它。

遵循这些规则的重新执行代码:

- 将每个副效果置于记录操作中或同步尾尾尾的每个侧效果中。更改 grain 状态,读取
  grain 状态,其价值决定以后的操作,并产生随机值、识别符号和
  以 `DurableTask.RunInline`、儿童或尾尾部中的时钟和环境值重放
  观察第一个运行记录值。从 `DurableTask.Current.UtcNow`读取逻辑时间。A
  其结果没有再运行的尾尾巴, 加上它的效果, 所以得出一个值
  尾尾尾从任务的身份和特性向外部服务发送,例如一通通勤钥匙。
  运行, 如 `$"{grainId}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}"`。
  后期任务识别符的运行量与后期任务标识符的因内化不同, 因此运行操作会变得新
  键调用儿童外部服务,其外部影响需要其自身的能力。
- 以日记 grain 状态存储记录和不可变收藏等不可变价值,
  指定一个新的值以更改一个值。状态 API 记录每一项任务,并修改
  国家持有的物体绕过它。
- 给有 `WithId` 的稳定的识别符号, 并给儿童以有条件地定期, 带环状,
  生成的识别符号只有在未命名时才稳定
  儿童、未命名 `DurableTask.RunInline` 效果、未命名的分离任务和组合按同一顺序创建。
- 以一个识别符号作为
  先前记录的运行前记录运行与所记录的执行执行有出入。主机取消了此尝试
  `DurableTaskAttemptCancellationReason.Diverged` ,并保持任务待定直至编码
  与记录重播相符。
  当部署在等待执行处决时改变工作流程时,保留现有步骤
  和识别符号,并给新步骤提供新的识别符号或分行
  `DurableTask.GetVersion`; 见 [版本修改](#versioned-changes)变化 `RunInline` 代表
  不影响死刑 记录下来。
- 完成不等待等待的 `async DurableTask` 法是正常步骤,其结果
  父向 父文档记录。同步状态转换效果以 `DurableTask.RunInline` 效果运行,
  承诺下一份持久写作,无需自己写作。
- 重放时重复执行代码中的日志。 `logger.ReplaySafe()` 将每个事件写一次; 见
  [复放诊断和记录](#replay-diagnostics-and-logging).
- 等待尸体所观察的任务,或等待他们,如:
  `WaitAsync(timeout)` 或 `ScheduledTask.WhenAny` 与 `DurableTask.Delay`民意测验,例如
  `IsCompletedAsync`,在任务运行时观察任务状态,记录一无记录,因此,一个持久的任务机构
  传票投出 `NotSupportedException`。
- 等待 `ConfigureAwait(false)`。一个尸体在其 grain 激活的调度器上运行,该调度器命令
  a. 尾尾鱼的状态变化,结果产生;
  其继续运行,与 grain的其他转弯相赛,并在那里开始持久作业;
  如 `DurableTask.RunInline`、 启动, 和 `DurableTask.GetVersion`等, 抛出 `InvalidOperationException`
  ([ORLEANS0226](https://aka.ms/orleans/diagnostics#orleans0226)).

<a id="recorded-effects"></a>

## 录音效果

在之前的调用器当前工作项目中运行同步效果`DurableTask.RunInline(name, effect)`
并且记录其结果。
包括重放、返回记录的结果或抛出记录的例外,不在此列
运行效果 :

```csharp
var summary = DurableTask.RunInline("pay", () => SetStatus(OrderStatus.Paid));
var paymentId = DurableTask.RunInline("payment-id", Guid.NewGuid);
```

- 记录承诺执行该任务的下一个耐久性写作,以及国家变化
  效果, 如日记 grain 状态, 所以效果的变化及其结果一起被连接
  并且一次适用。它没有增加任何写作和等待,在写作之前的运行将同时消逝,
  下一运行再次运行效果 。
- 效果通过结果将数据传送到身体,读取后步骤的改变
  状态变化,并产生数值,如识别符号和随机数字。
- 效果内的一个 `DurableTask.RunInline` 呼叫是其一部分,其记录将保留在
  长期运作,如开始一步或儿童、张贴和 `DurableTask.GetVersion`,
  物体在身体中运行:效果在效果下,它们抛 `InvalidOperationException`,效果记录为
  重放返回记录,而不运行效果
  ([ORLEANS0227](https://aka.ms/orleans/diagnostics#orleans0227)).
- 失败也被记录下来, 因为日记状态没有倒退: 投出前的更改
  无法存储的结果, 例如因为
  并不连环记录, 被记录为失败。
- 连续 `DurableTask.RunInline` 呼叫在一个同步区块中,并按日记状态
  更改, 在同一日记写中承诺, 没有其它转 grain 之间运行。
  返回并不意味着它的记录已经记录下来了
- 名称指效果,表示 `WithId` 指一个步骤: `pay`,然后是 `pay$2` ,指以后的一个步骤
  在同一运行中发生。 名称不包含 `$`。 `DurableTask.RunInline("get-id", Guid.NewGuid)`
  和 `await DurableTask.Run(async () => await ids.NextAsync()).WithId("get-id")` 使用相同的标识符,
  `get-id`,但运行方式不同:等待时的步数,可能稍后转弯,作为
  以 `DurableTask.Current` 和注销范围,并承诺在
  自己日记的日记写。 重放发现一种被记录在身体运行
  而不是再次运行工作, 将一个任务转换成
  在 `DurableTask.GetVersion`上的其他分支。
- `DurableTask.RunInline(effect)` 省略梯度的名称,例如
  `var paymentId = DurableTask.RunInline(Guid.NewGuid);`. 效果取决于下一次执行产生的效果
  身份标识符 `$child-1`、 `$child-2`等,这些未命名的效果与未命名的儿童共有,
  ,所以当身体按同一顺序创建其未命名操作时,它可以重放。Name 效果
  其位置在运行或部署之间可能发生变化。
- 效果是同步的。 恒常和非同步效应不会编译, 结果类型
  以通用代码(如 `ValueTask<T>`)为单向交错,抛出 `NotSupportedException`
  效果运行前, 工作将非同步作为等待的一步进行, 例如
  `await DurableTask.Run(async () => ...)` 或 `async DurableTask` 方法。 `async void` 代表,
  象一个 `async` 羊羔被投到 `Action`,第一个返回 `await`,所以既不起作用,也不起作用
  记录它的工作。
- 同步工作作为一种效果而不是一个步骤:既不等待也不观察的步骤
  其 `CancellationToken` 加上了处决儿童和自己写成的日记。
- 记录只适用于一个持久任务。 在一之外, 效果每次运行, 所以帮助者会使用此功能
  通常的方法与持久任务机构共享,可以称之为。

<a id="versioned-changes"></a>

## 版本修改

`DurableTask.GetVersion(name, minSupported, maxSupported)` 返回修改版本的
执行后,一个机构拥有每个版本的代码 而开始处决
先前编码有待处理:

```csharp
var shipping = DurableTask.GetVersion("carrier-booking", minSupported: 0, maxSupported: 1);
var trackingId = shipping >= 1
    ? await carrier.BookAsync(orderId).WithId("book")
    : await warehouse.ShipAsync(orderId).WithId("ship");
```

- 执行记录中第一个名字的检查版本, 以及以后每次检查和重放
  返回它。到达检查首记录 `maxSupported`;较早时的
  代码中运行了过去的记录 `0`。在版本之后有一个步骤,并附有执行记录。
- `DurableVersion` 声明更改名称和范围一次, 如 `DurableSignal<T>`:
  `static readonly DurableVersion CarrierBooking = new("carrier-booking", 0, 1);`,读用
  `CarrierBooking.Get()` 或 `CarrierBooking.IsAtLeast(1)`。
- 提高 `minSupported` 时将保存一个版本。执行中记录了一个超出范围范围的版本
  搁置:主机取消对 `DurableTaskAttemptCancellationReason.UnsupportedVersion`的尝试,
  其 `DurableTaskAttemptCanceledException` 携带一个 `DurableTaskVersionException`,丢弃
  尝试结果, 并保持任务待定直到支持版本重播的代码。
- 在一项持久任务之外,检查返回 `maxSupported`。名称不包含 `$`,并且
  [ORLEANS0218](https://aka.ms/orleans/diagnostics#orleans0218) 报告的名称和范围
  并不是常数

<a id="replay-diagnostics-and-logging"></a>

## 复放诊断和记录

`DurableTask.Current.IsReplaying` 报告一个机构运行是否是重新运行的操作,而
先前运行记录。从运行开始,发现早期运行操作的就是 `true`
记录在任务树上,例如儿童、独立任务的记录和 `RunInline` 记录中。
直到赛跑到达每个赛跑点或开始一个没有记录过的操作为止。
保持 `false`。第一个运行报告 `false` 整个过程。给任务发送了呼叫者的信号值
不算作有记录的操作

仅用于记录和诊断。 在日记写前完成的运行完成了它的工作
重放,所以代码仅运行时 `IsReplaying` 是 `false` 跑两次或永远跑两次,然后代码
其上各分支的树枝将不同的运行时间安排在不同的运行中。曾经运行的工作属于
内 `DurableTask.RunInline`一步,或者身体的同步尾巴

`logger.ReplaySafe()` 返回一个在任务重放时跳过事件的日志, 返回该日志。 所以每个事件都是
记录一次 :

```csharp
private readonly ILogger _log = logger.ReplaySafe();

public async DurableTask<OrderResult> PlaceOrderAsync(SubmitOrder request)
{
    _log.LogInformation("Placing order {OrderId}", request.OrderId);
    ...
}
```

日志员在日志时读取运行中的任务, 所以有一个实例可以用于耐久任务机构,
普通 grain 方法。在 `DurableTask.RunInline` 效果内或完成的一步内记录,
没有它,不要重复 因为重播返回 他们的记录结果 不运行。

软件包中包含分析器, 分析器报告代码, 重复播放会运行不同, 或者
运行的不为文字, 即诊断 ORLEANS0200 到 ORLEANS0228。 每个诊断链接到页面
说明危险及其补救。

<a id="host-responsibilities"></a>

## 东道国责任

主机来源于 `DurableTaskContext`,执行 `ISchedulableTask` 定义
和 `IScheduledTaskHandle` 处理器,并维持从 `TaskId` 到定义的绘图,
响应, 并取消状态。 主机返回了当已有
重新排入 ID , 以记录的方式返回相同的 `SelectCompletionAsync` 选择, 用于每次调用时的决定编号
和用品
replay-consistent `UtcNow` 。它运行和重放一个与耐用
请求及其记录的理由应用,并终止取消的取消假期作为取消。
`IScheduledTaskHandle.CancelAsync(reason, cancellationToken)`,其中记录了第一项请求的理由和任务
取消; 无理由请求通过 `null`。主机启动
任务(任务) `DurableTaskContext.StartTaskAsync`收到 `DurableTaskStartOptions`: `Detached` 用于 a
将执行命名为订阅者的独立根任务,以及启动时 `WaitForAcceptance`
一旦任务主机接受任务, 即完成。 它们读作 `DurableTaskContext.SchedulesShieldedChildren`
当他们开始生孩子时,与孩子一起记录下来,并且不得因父母早于子女而取消孩子的抚养权。
请求或传播父请求后的请求。 能够同步启动的东道主
`DurableTaskContext.PostTask`,以及能够从耐用发件箱实施中产生根起点的定义
`ISchedulableTask.Post`. `ISchedulableTask.ScheduleAsync` 一旦东道主接受并长期接受,即完成
任务已保存。
支持信号反 `DurableTaskContext.WaitForSignalAsync`的主机,记录每个主机
任务任务中每个信号名称的第一个值,然后从发件人发送信号。支持中止的东道主
执行 `IScheduledTaskHandle.AbortAsync`。支持记录效果超重的东道主
`DurableTaskContext.RunEffect`:它们返回了效果记录的结果,或运行了效果并处于阶段
记录在同一同步工作的记录中,因此记录承诺与状态一起改变效果。主机
其结果阶段导致
`DurableTaskContext.OnBodyCompleted`,它与人体的最终代码同步运行。
重放曲目重播覆盖 `DurableTaskContext.IsReplaying`。

执行的代码运行在一个调度器上, 当主机创建时该程序是当前
`DurableTaskContext`,例如 grain 激活的调度器,上下文状态需要
同步: 主机请求取消任务, 与 `RequestTaskCancellation` 同步; 取消尝试
设置该排程器上的`CancelAttempt` ,调试程序就建立这样的设置。背景是尝试取消状态的所在。
取消尝试 `DurableTaskAttemptCancellationReason.Diverged` 当重播的曲目偏离
其记录,其记录以抽象观察的方式观察到,例如记录的结果,代码的类型不再
将选中或已录入的选定,但不再为候选人。
其 `IsAttemptCanceled` 是 `true` ,并按其 `AttemptCancellationReason` 描述处理任务。
执行时记录在一种识别符号下,用同一定义重新连接记录的工作;
根据所记录的ID的定义,有差异。
