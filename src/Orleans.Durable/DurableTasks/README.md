<a id="microsoft-orleans-durable-tasks"></a>

# Microsoft Orleans 持久任务

> [!IMPORTANT]
> 释放前Alpha `Microsoft.Orleans.Durable` 包装中耐久任务船上的船舶,
> 等待第一颗 NuGet 其年度出版物的APP和坚持的格式在
> 稳定。

`Microsoft.Orleans.Durable` 中的耐久任务运行时间执行返回的 grain 接口方法
`Orleans.Durable.Tasks.DurableTask` 以上 Orleans 的通信信息。它拥有请求
程序、运行时间调适器、存储整合、代码生成绘图,
诊断、取消、传播、完成交付、信号和持久拖延,
中,通过预定的收件箱信息恢复。

<a id="configuration"></a>

## 配置

本存储库中的项目在声明或
调用一个耐久 grain 接口:

```csharp
clientBuilder.AddDurableTasks();

siloBuilder.AddJournaling();
siloBuilder.AddDurableTasks();
```

silo 登记构成通信,利用可持久工作唤醒 grains
配置含有耐久日记存储的日刊
提供和生产《持久就业》的供应商和提供者。
以任何日记格式,包括
默认 JSON 格式和 Orleans 二进格式。请求和答复
Orleans- 序列有效载荷,因此其类型需要 Orleans 序列器
否 JSON 元数据; `AddDurableTasks` 登记册登记 JSON 元数据元数据
任务状态(Q0Q)日记里有任务状态,即 grain
处决 grain的真相来源;测试应记住变化不定
日记存储,例如 `AddVolatileJournalStorage`。

耐久 grains 来源于 `DurableGrain`, `AddDurableTasks` 提供每一种
grain 一个收件箱和一个发件箱。在启动装置期间,在日记恢复之前,
应用器将运行时间与 grain日记和收件箱绑定; 没有日记状态管理器的激活失败
设置期间 。

`GetDurableTask<T>(rootId)`或其 `TaskId` 超载返回 `DurableTaskReference<T>`
客户和普通 grain 方法等待执行,民意调查
取消或中止:

```csharp
var task = grain.GetDurableTask<MyResult>("stable-root-id");
var response = await task.GetResponseAsync(cancellationToken);
```

参考可通过 `GetAwaiter` 扩展方法等待参考,因此,用于检查
返回类型 返回的实例类成员,例如 ASP.NET 核心最小 API 核心最小 API,将它作为值处理:一个处理器
直接返回 `grain.GetDurableTask<T>(id)` 。 `IDurableTaskReference` 读取引用的 `Target`
和无结果型号的 `Id` 。

`DurableTaskAddress(GrainId Target, TaskId Id)` 是一个任务参考值,它是一个数值,编码存储
以 grain 调用和 HTTP 有效载荷中记账状态或发送。 Orleans 将其序列化,并且
System.Text.Json 写成 `{"Target":"{grain type}/{grain key}","Id":"{task ID}"}`,并附有元数据
减去 `AddDurableTasks` 登记册登记 JSON 日记。 `TaskId` 连成一条条条路
抽象体在其中声明的转换器。 `IGrainFactory.GetDurableTask<T>(address)`, 其中
grains 和 `IClusterClient` 共享,将地址转换为参考,
`DurableTaskReference<T>.Address` 读作如下:

```csharp
_pending.Value = order.GetDurableTask<OrderResult>(orderTaskId).Address;
await WriteStateAsync();

// Later, after any number of reactivations, in a durable task or elsewhere:
var result = await GrainFactory.GetDurableTask<OrderResult>(_pending.Value);
```

`ScheduledTask.TryGetAddress(out address)` 读取一个把手的任务地址,通过手
`IDurableTaskReference` 特性。处理根任务,客户端和普通 grain 方法
开始或返回的 `IDurableTaskManager.GetTask` ,并在耐久执行中,使用分离控件
任务和任务 `GetDurableTask` 订阅者有地址。 随身儿童属于其打电话者的地址。
树没有果实,

客户的等待和民意调查以及普通 grain 方法重试离开任务的失败
未受影响的:失去连接、中断或坠毁 silo、呼叫超时或停止活动
激活。它们以指数反向,从50毫秒到5秒,直到任务 grain 的答案或
调用通知器的牌子或等待时间超时结束等待。 没有 root 或根, 结果保留
立即过期丢弃 `DurableTaskNotFoundException` 。返回的失败响应仍然是执行
失败, 包括存储用户例外情形类型相同时 。

一项持久的任务等待同文提及,以认同援引任务的结果。
诸如客户计划的行动, 网络钩中继报告:

```csharp
var result = await operation.GetDurableTask<OperationResult>("operation-42")
    .WaitAsync(TimeSpan.FromDays(1));
```

- 等待参考、安排时间或将其转交 `WhenAny`、 `Race`等组合体,
  或 `WaitAsync` 自认一名被处决儿童,记录在儿童身份证下,
  儿童的指纹、 `$subscribe:{target}{taskId}`、指定任务和
  以一日记写下, 重放时会到达孩子
  不再订阅,并观察记录的结果。
- 订阅是一种 `durable-rpc/invoke` 信息,该信息携带儿童的身份证而不是
  要求:目标既不列出时间表也不申报任务,不记录打电话者,而且核证没有。
  除了请求者外,它还给任务完成目的地添加了 `(subscriber grain, child ID)`
  以 `(caller, task ID)`订阅,任务结果到达每个目的地
  方式:通过发箱,任务保留到完成退休。
  与结果:结果、失败或取消。
- 订阅终端任务即时发送完成的任务,并发送给
  目标不保留,因为援引的任务使儿童在 `DurableTaskNotFoundException` 完成
  。到达用户的任务编号是指其目标已经存在的一项任务:
  目标接受任务后返回`ScheduleAsync` ,并开始一个耐久执行
  独立的任务在起始点本身中订阅。
- 取消该子文件夹的终止为取消, 并发送一个不订阅的 `durable-rpc/cancel`
  给儿童命名信息,在任务运行时删除目标;任务
  继续中止或删除儿童树的树木,以同样的方式结束,完成
  到达时,孩子的改变结束。
- 订阅同一 grain 任务时,在任务状态中添加目标,并写入
  保存任务结果的,也使孩子完整。
- 旁听者在等待投票的投票和控制成员,
  目标; 持久执行不会记录这些电话。

耐久 grain 通过 `IDurableTaskManager`观察、取消和中止其任务,
经理人与激活者竞争
运行时, 没有调 grain 给 grain 本身。 它的超载将根 ID 字符串移入
地址根任务 `TaskId.CreateRoot(rootId)`:

```csharp
public sealed class OrderGrain(IDurableTaskManager tasks) : DurableGrain, IOrderGrain
{
    public async Task<bool> CancelAsync()
    {
        await tasks.CancelAsync(PlaceOrderTaskId(this.GetPrimaryKey()), "Cancelled by the buyer");
        return true;
    }
}
```

取消请求可能有一个理由,而取消的工作流程则源自
`DurableTask.Current.TaskCancellationReason`,例如记录它停止的原因。
请求确定理由; `ScheduledTask.CancelAsync(reason)`,
`DurableTaskReference<T>.CancelAsync(reason)`,和 `IDurableTaskManager.CancelAsync(taskId, reason)`
随身携带来自客户、工具以及任务 grain的物品。

耐用 grain 调用时设置配置,设置任务是什么,以及调用时间何时启动
呼唤者接着说:

```csharp
// In a workflow: start the order as an independent root task, in the same write as the basket's change.
_ = order.PlaceOrderAsync(request).Detached(PlaceOrderTaskId(orderId)).Post();

// Anywhere: return once the target holds the task, so its ID can be handed to other parties.
var placement = await order.PlaceOrderAsync(request).WithId(PlaceOrderTaskId(orderId)).ScheduleAsync();
```

- 长期工作,等待电话、安排时间或张贴记录随附子女
  指定任务 识别并分阶段发送 `durable-rpc/invoke` 信息,其目标要求为打电话者承担任务。
  根码是每个 grain的标定号,所以目标将儿童储存在呼叫者的标定范围内,
  `$caller/{callerGrainId}/{incarnation}/{taskId}`:两个打电话者,其工作流程共用根 ID和
  以 儿童 的 儿童 命名 。 *线编号*,
  `{incarnation}/{taskId}`, `{incarnation}` 是调用者运行的化身,即图形用户界面
  . 电民的, grain 当它存储根任务 的调用时, 以 `"N"`. A
  根 ID 在 grain 删除了前一运行后再次运行的 root ID 后, 原 ROD 具有新的化身, 所以它的调用
  与前一跑的对手,他们的信息不会被拆卸。
  儿童电话线识别号是目标地图的范围,
  目标的完成名称, 将识别号线作为用户; 呼叫者忽略了完成
  早期运行。订阅和独立开始用他们的窃听器识别码给孩子命名。
  根 ID , 由独立启动后发送的根 ID , 处理目标的根任务本身, 以及根 ID
  `$caller` 被保留: 只有来自其他 grains 的电话才能创建它下的任务 。
  目标确认在日记写时, 一旦该日记存储任务, 且启动器的发信箱就承认此请求
  以书面形式将接受的招工记录在儿童的笔录中,如已交付,则予以退职。
  `ScheduleAsync` 一旦接受或任务结果为耐久, 则返回该接受或任务结果。 `Post` 阶段
  儿童及其在调用代码栏中被引用,所以他们承诺使用 grain下一期日记
  写; 到达孩子的回放不会再发送。 目标有一次张贴任务
  使用时的调用, 所以将任务身份识别码 交给其他人的代码 开始的任务与
  `ScheduleAsync`.
- 独立呼叫是目标上独立的根任务。 此主机禁用耐久调用 grain :
  目标从电话的序列请求中回收根任务, 从而将任何其他任务分离出来
  `ISchedulableTask` 操作失败 `NotSupportedException` 在记录任何事项之前或
  下发送的执行记录
  `{taskId}/$detach:{target}:{rootId}`,记录根标识和请求的方法,因此
  重播到达记录 不重新开始任务, 无论请求的论据,
  使用该请求并命名记录
  目标在储存时会将记录添加到任务完成目的地
  任务。
  记录显示任务如订阅 `GetDurableTask` 一样:任务结果完成
  取消、中止或删除树的树,或者启动人的取消仅终止
  。其根 ID 是 `.Detached(rootId)` 名称、 逐字记录或单段
  `{grainId}/{taskId}/{segment}`,其中每个部分都逃脱 `/` 和 `\` 任务ID做, `{grainId}`
  起始 grain 和 `{segment}` 是业务的段,因此工作流量与任务编号相同
  不同的 grains 上产生不同的根代号。
- 控点 `TargetTaskId` 的控点指定了它记录开始或观察到的任务,该 grain
  存储该选项:独立任务根代号、订阅订阅的 `GetDurableTask` 订阅任务;
  和 `$caller/{grainId}/{incarnation}/{taskId}` 拨打电话 grain其它缔约方则处理由它承担的任务,例如
  客户对独立任务进行民意调查 `GetDurableTask<T>(handle.TargetTaskId)`把手柄 `Id`
  指定记录,其中将组合、取消和重播地址。
- 发件箱通过引用保存对另一个 grain 的任务的每一项援引,启动器提供
  由一个日记的待查信号列表 以信息识别键到发件箱退款
  表格与执行树分开,因此从开始到删除树的开始
  并且引用的有效载荷被日记一次。一个回放,它到达了一个开始, 其援引
  仍在等待与该软件连接。
- 目标仅在其任务识别牌已经确定另一请求或请求时才立即拒绝一次援引。
  其最终的电话,即:它用死字字母来表示援引从 `durable-rpc/conflict` 代码发出的电码
  `DurableTaskRejectionCodes` 及其原因,启动人的发信箱将请求退
  目标因其他原因不接受援引,例如:
  短暂失败或请求无法解码,丢弃,所以启动器的发件箱重新发送
  发送到消息服务 `OutboxRetryTimeout`;在滚动部署期间,可重试达到
  启动程序将失败存储在初始记录中
  `ScheduleAsync`,等待任务或手柄, 把它放在每场重播中:

  | 结果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果成果 | 守则法典 | 失败失败 |
  | --- | --- | --- |
  | 任务编号已经确定了另一个请求或呼叫者。 | `durable-rpc/conflict` | `DurableTaskConflictException` |
  | 目标进入窗口后 即通讯 `DeduplicationWindow` 目标可能已经运行并删除了任务, 所以结果未知 。 | `durable-rpc/expired` | `DurableTaskRejectedException` |
  | 目标拒绝使用其他代码或无代码。 | 目标代码 | `DurableTaskRejectedException` |
  | 发件箱无法确认在 `OutboxRetryTimeout`之前的援引,比如从未解码的引用或在其入境截止后的目标围栏。 较早的尝试可能已经达到目标,结果不明。 | | `DurableTaskDeliveryException`, 与死亡信的原因 |

  未知结果意味着任务可能已经运行:确定是否完成了,例如通过投票
  或检查其效果,然后在另一任务编号下重新开始工作。

  `DurableTaskRejectedException.Code` 目标代码报告, `Target` 否认者, grain.
- 在一个耐久 grain的普通方法中, `ScheduleAsync` 直接将目标称为对象,就像客户一样
  目标存储任务后返回;呼叫 grain 本身时,会经过 grain
  自动运行时间。 `Post` 将调用 grain发件箱的顺序, 并以此方式承诺下一个 grain
  日记写, 加上该方法的状态变化, 并一次性发送。 The the name
  任务已启动, 是一个根任务, grain 不存储, 目标将结果发送到无
  grain.
- 目标存储任务后客户端的 `ScheduleAsync` 返回。 客户端没有发件箱,
  苏 `Post` 扔弃 `InvalidOperationException`.
- 向 grain 本身发出的呼叫在日记写作后以本地根开始。
  目标保留根任务,如客户端计划根,该根调查 `GetDurableTask<T>(rootId)` 。

`DurableSignal<T>` 带有外部事件,如发运通知或人
任务执行等待 `WaitAsync()`和发件人
将一个数值交付到 `(grain, task ID, signal name)`:

```csharp
static readonly DurableSignal<Shipment> Shipped = new("shipped");

// In the workflow, which a client or another grain invoked under its task ID.
var shipment = await Shipped.WaitAsync();

// From a client, or from a durable task on another grain.
await order.SignalDurableTaskAsync(workflowId, Shipped, shipment);

// From the grain's own methods.
await tasks.SignalAsync(workflowId, Shipped, shipment);
```

- 引用的任务记录了每个名字的第一个值,作为该任务中
  树, `{taskId}/$signal:{name}`,加上交付的修改。
  树上每等一等这个名字,包括重放时的等待,返回记录
  值,记录与树一起删除。一个在等待之前发出的信号
  立即完成等待。
- 重复发出信号,其序列价值相等,成功而任务不变。
  具有不同值的信号,即终端任务不值的信号
  和任务的一个信号,该 grain 被否决:
  呼叫者接收 `InvalidOperationException` 或 `DurableTaskNotFoundException`,以及收件箱
  拒绝是拒绝的信号 其原因
- 发送者可以进行一个呼吁,并用一个字母发出信号,发件人发送发信箱
  自动传送,所以信号信息可以先到达。收件箱记录了信号信息
  任务 grain 还没有被维持, 并且引用找到了价值 当它
  到达。 grain 承认在通信到 `DeduplicationWindow` 已经过去
  发件人先是先开始制作,到达后再将价值维持在窗口的两倍
  覆盖发件人的时钟和 grain时钟之间的斜线,所以这是一个公认的祈祷
  当没有在指定时间内到达任何援引时, grain 将删除数值并记录
  警告。
- 在一个持久的任务中, 送行者树上的标志和标记 在一份日记里写着
  开始像一个已张贴的启动, 所以一重放到达标记时不会再次发送它, 任何值
  标记是
  `{taskId}/$send:{target}:{task}:{name}`,并重复发送一个信号 执行,
  如在 `TaskNotFound`之后重新处理,编号为 `$send:...$2`,等等,所以每个发送
  目标再次报告其 `Duplicate`等值。
  发件箱中的 grain 是 `durable-rpc/signal` 信息,其身份包括
  发送运行的函数属性; 目标的收件箱记录任务中直接值
  状态。给 grain 本身的信号以同一文字记录。
- 使用客户端或普通 grain 法, `SignalDurableTaskAsync` 调用目标,
  一旦任务完成, 任务将永久持有该值。 A grain 通过
  `IDurableTaskManager.SignalAsync`,其中承诺 grain的待变状态。
- `IDurableTaskManager.TrySignalAsync` ,对于客户和普通 grain 方法,
  `TrySignalDurableTaskAsync` 返回一个 `DurableSignalResult` ,而不是抛掷: `Accepted` 或
  `Duplicate` 一旦任务长期存在,或 `Conflict`, `TaskNotFound`,或
  `TaskCompleted` 这是因为一个无效的迹象,它不能改变任何事物。
  管理器记录结果时会使用发送方式, 因此重放返回相同的结果 :

```csharp
var result = await tasks.TrySignalAsync(PlaceOrderTaskId(orderId), Shipped, shipment);
if (result is DurableSignalResult.Conflict or DurableSignalResult.TaskCompleted)
{
    return false; // Already shipped, or the order finished.
}
```
- 等待将执行时间提前到信号被记录的时间。
  `TryWaitAsync(deadline)`, a `Race` 等待和等待之间 `DurableTask.DelayUntil`, whose
  索引的槽位属于等待执行执行的树。 比赛取消了输入
  一旦两者都是终点,就会损失和完成。

<a id="diagnostics"></a>

## 诊断

`DurableTaskReference<T>.GetStatusAsync` 和 `GetResponseAsync` 报告任务状况和结果。
`DurableTaskEvents`, `Orleans.DurableTasks` 诊断听器,报告运行时间的过渡
同步启动, 使它们成为启动, 当运行阶段时它们: `TaskCreated`,
`TaskCompleted`, `TaskDeleted`, `ExecutionStarted`, `ExecutionStopped`, `ReplayEnded`,
`SignalWaitStarted`、 `SignalWaitEnded`和 `SelectionWaitStarted`。
`IGrainContext` 测试把事件作为障碍,而不是投票

<a id="runtime-guarantees"></a>

## 运行时间保障

- `(target GrainId, TaskId)` 确定一个执行。请求的身份是其
  使用该方法所指定的调用和界面法
  生成的可引用类型; 它的参数没有比较。 排程任务编号
  同一呼叫者用同样的方法, 达到现有任务
  当争论有分歧或序列化不同时,例如部署之后
  添加可选字段。一个带有另一种方法或调用器扔出的日程
  `DurableTaskConflictException`, an `InvalidOperationException`离开
  每项任务还保存其请求的指纹
  重复的争论,诊断报告与任何对比都没有。
- 请求身份、任务状态、电话订阅、收件箱完成
  执行开始前承诺收回所有权。
- 任务状态和发送信件共享 grain日记承诺边界
  包括给自己发送的 grain 信息,由它自己的收件箱接受。
  操作在中转和阶段前验证操作,但只有安全
  承诺; 失败的日记写将 grain停止使用, 下次激活
  从承诺状态中收回。
- 收件箱信息同步同步播放, 因此收件箱将每个信息都发送到
  和它引起的变化。 使用、完成、取消和信号
  交付后即被接受;排定的延迟恢复处理
  执行开始、当地完成和取消后进行推广
  承诺。
- 启动后恢复任务状态
  恢复后,在 grain开始执行已追回的处决
  `OnActivateAsync` 已完成。
- 执行的尝试分部分进行: 其耐久等待之间的代码。
  每个段系关于激活的本地请求, 其次于 grain
  重新恢复规则,对于非再返回 grain 规则,一个段排除 grain
  请求的其他请求,作为 grain 方法中。 等待的持久等待,如
  等待儿童的结果、信号、延迟或数个孩子的第一个
  完成,将尝试停放并结束其部分; 一旦等待完成,
  新的段段恢复等待代码。 其他的等候, 如日记写
  并呼叫其他 grains,留在段内。
- 等待自身任务 grain的请求,例如 grain 方法
  等待 `this.AsReference<T>().M()` 或 `IDurableTaskManager.GetTask(id)`的
  响应, 必须让任务的各个部分与它同时运行: 标记它的界面
  方法 `[AlwaysInterleave]` 或 grain 类 `[Reentrant]`,或等待任务
  耐久的等待结束其段段。
  等待请求的请求 相反,将部分置于其背后直到呼唤
  用于 grain独有部分的代码,例如
  交易,将段次保持后方直到关闭段段,即使从
  一种 `[AlwaysInterleave]` 方法,因此它只等待 grain的任务
  区域外。
- 运行时登记了它与收件箱一起的提醒延迟: 而 grain 持有可调试的
  执行,即其段运行或即将运行的
  杂志在grain的持久工作提醒,在它成为
  。在可执行处决时发生撞车的 grain ,将重新启动
  30秒内,恢复后再执行。当
  启动,它的恢复等待处决 重新武装了警醒,
  等待处决的死刑维持着警醒,所以
  执行在可调试状态下恢复的拆卸丢弃时,
  启动 A
  当它等待消息重新启动 grain时,停放的处决恢复。
- 启动关闭从解除功能开始,到 grain
  `OnDeactivateAsync` 运行。 它停止新的执行并取消所有运行中
  与原原为 `ActivationStopping`:从那时起,尝试
  持久作业抛出 `DurableTaskAttemptCanceledException`及其 `AttemptCancellationToken` 和
  `CancellationToken` 已被取消。 停停的尝试被丢弃, 没有运行
  进一步的代码;运行的片段末,结果被丢弃。
  下次激活后再次播放已执行的工作。 其他 grain 工作, 如
  `OnDeactivateAsync` 和箱,在部分完成的时候,
  和被取消的段清理后进行的时间状态变化
  与它一起。民意调查或等待
  激活失败, 以 `DurableTaskAttemptCanceledException`作为调用
  运行时停止时到达,任务在下一个时间继续
  启动
- 每个请求和结果有效载荷都按 grain一次日记。被调用者的信箱
  接受的引用,而调用者的收件箱接受完成,直接
  任务状态,仅将信件的处理记录与它同时进行。
  被调用后发件箱的日记只显示每个完成的识别和交付状态,
  运行时间 将存储结果中的信息 变为每次发送时的电文
  任务状态持续
  请求和结果有效载荷与任务元数据分开,从而更新
  元数据不会重写它们。
  其已死亡的信件有一份检查和重写结果的副本,而
  任务状态, 即为 `ResultRetentionPeriod`保留自己的任务, 所以是一个投票者或
  重新援引仍然观察着结果。
- 终端响应和完成通知一起承诺。
  保留结果直到完成时从发箱中退休。
- 任务完成目的地是其用户:其援引者,每个可持久
  与 `GetDurableTask`连接的婴儿,以及每次处决的记录
  任务作为独立的任务开始。每个任务都收到结果
  完成完成后,将取得结果,任务保留到每个
  任满退休:在最后一个退休人员退休时,删除有 grain 调用员的任务;
  . 一件没有任何人的任务,能维持它的效果; `ResultRetentionPeriod` 完成后,
  至少在用户完成后退休之前。
  重新恢复该周期。 grain 将完成数据直接接受为
  儿童; 结束或用树树被删除的儿童的完成日期
  已接受, 未作修改。
- 客户客户或其他 grain 安排为请求的被援引任务
  一种树,是被禁的;
  当地儿童、拖延、混合决定、订阅、
  独立任务记录和呼叫 grains。调用者选择任务识别符号,因此,援引的任务嵌入
  另一任务的身份识别特征保持其自己的生命周期。一旦援引的任务
  后端, grain 删除其执行树木:在日记写
  当调用 grain 可持久接受时,将填充的填充时间退去;或
  `DurableTaskOptions.ResultRetentionPeriod` 完成后无
  grain 呼叫器,例如客户端预定的任务;完成时间
  以无文字字母命名, 用于在死信之后的那段时间。 任务识别符
  因此,在完成之后,将 `ResultRetentionPeriod` 的时间安排加以重复,
  并在删除后安排它,再次执行请求,作为与
  独立于Messages的 `DeduplicationWindow`。
  客户民意调查保留了结果,删除后进行了民意调查
  `DurableTaskNotFoundException`. grain 在启动时适用留存,
  在每个日记写完之后, 在它回答民意调查之前, 客户的日程表,或者
  用户订阅,所以没有人会看到保留结束的任务。 grain其它
  请求和留言 所以等待客户不会延误工作
  完成任务。
- 删除执行树删除后取消未完成的任务: 删除日记
  向每个待发的呼叫发送一个取消的文字签名 grain, 然后每发一个
  写后即发出执行运行的信号,结果被丢弃。 A
  删除执行的下一步日程安排投递 `OperationCanceledException`。
  完成和延迟恢复,从而导致删除任务或较早运行
  根的ID,该根的ID再次运行,被忽略。
  另一儿童在另一子女父母 grain 儿童在被录入其请求之前,即已到达的,
  到达时取消为已取消的, 记录用树来删除。 A
  从客户端直接取消 grain 方法,或 `IDurableTaskManager.CancelAsync`直接取消 grain的任务
  丢弃的投 `DurableTaskNotFoundException` 并且什么都不存,
  grain 在Messages的 `DeduplicationWindow` 自其发出以来已经过期之前接受传讯。
  发件人首先先将发送文件的 `InboxMessage.SentAt`,然后用 `durable-rpc/expired` 代码写成一个后字母。
  窗口到达后,早期记录保存在窗口上,其记录为该窗口的两倍,因为进入时读到发件人的钟和
  记录的寿命 grain; 传递线条, 一个窗口的发送时间比 grain时钟要早到多少。
- 完成后,其储存的完成结果不再解码,例如事后
  设置删除结果类型,无法实现。发件箱
  完成,记录解码错误作为原因
  运行时间将它像任何死字母完成一样退休,而不解码结果。
  激活不会解码存储的结果, 所以 grain 继续处理其它
  工作; 读取结果(例如投票)的操作,如民意测验,代表解码
  错误到他们的呼叫器。
- 不再解码的存储请求,例如部署后删除类型
  其论点认为,检疫只对其任务进行隔离。
  记录类型,以及是否在不解码请求的情况下存储请求,所以 grain
  激活并运行其他任务。 开始任务解码请求; 如果失败, 则启动此任务
  任务等待等待,等待其存储的请求和摊位不变:运行时间日志a
  警告和电警告, `ExecutionStalled` 与 `DurableTaskAttemptCancellationReason.Diverged`
  和每次激活一次的解码错误,并保存 grain
  `StalledExecutionRetryInterval` 警醒后,一个后来的激活,其代码编码
  请求会运行任务。 选票将任务报告为待定, 中止任务结束 。
- 一种没有序列化的结果或失败, 例如某类的值或例外
  无法保存没有编码的编码。任务错误与 `SerializerException`
  哪些名称类型和序列错误,以及过失与任何结果一样发生,
  任务结束和 grain 继续从事其他工作。
- 取消是合作、单调和一无所有。
  传播: 它取消任务树中每个执行的耐久标记, 阶段
  取消对另一个 grain的每次待拨电话,该被调用者对自己的电话适用同样的规则
  任务,结束延误和任务, 并且结束那些尸体还没有被取消的任务。
  决定任务结果: 当取消时取消任务, 否则取消任务取消 。
  随身子女没有被保护,也没有被保护。
  请求在不运行而取消后开始,然后从此开始,从此开始,即从此
  甚至在请求书写前,即通过 `IsTaskCancellationRequested`将请求书写成
  完整。 机构在取消点观察取消: 开始于一个
  儿童开始、 `RunInline` 效应或等待任务或
  信号,一个运行机构在目前点观察它,对于停在耐用
  等待是等待,执行记录在 `{execution}/$cancellation-observed`下的点数。
  重放一个被取消的机构, 保留着它持久的标记
  直至达到记录点,并观察注销情况,因此完成
  儿童返回记录的结果,身体将补偿最初的病痛。
  发现未记录点,但没有发现记录点,则在注销时,如果它达到最后一项操作,即
  记录前一次尝试,记录下这一点。重放在记录点之前结束
  不同的, 并拖到到达点的代码重放它。 第一个持久的终端
  比赛赢得结果
- 身体开始补偿 `.Shielded()`。被保护儿童,例如当地一步、延迟或
  呼叫到另一个 grain记录它祖先的取消没有达到 它被创造
  当其父权取消已请求时, 后一请求跳过它及其
  并用它自己的取消状态重现它。
  开始输入的每个输入。 一个作为身体一部分运行的屏蔽信号等待, 与
  尝试 `AttemptCancellationToken`, 所以机体的取消不会结束它; 屏蔽
  包括独立任务的记录在内的订阅,不是它没有订的。
  取消其树木的中止,或
  将树的树除去,仍可达到每一个屏蔽的作业。
  完成任务本身。
- 请求取消的理由与就每项任务提出的请求有关,随
  取消每个待取消的呼叫到另一个 grain, 并且是被取消机的重放内容
  调自 `DurableTask.Current.TaskCancellationReason`.
- 行政堕胎,通过 `IDurableTaskManager.AbortAsync`、 `DurableTaskReference<T>.AbortAsync`
  或 `ScheduledTask.AbortAsync`,将任务及其执行树失败后的任务和行树
  `DurableTaskAbortedException` 在一个日记写中,它也对每个待发邮件进行中止
  另 grain;被调用者将中止任务树的树,然后逐个中止。在树上运行的每次尝试都被取消
  与原因 `TaskAborted` 其果实被抛弃,故它后来的持久运行被抛下;
  树的延滞随着树而结束,所以他们
  恢复程序已老化。 存储的例外信息是中止原因, 任务完成时间
  当它被中止时, 记录被中止; 将任务报告为 `Failed`
  包含原因的`DurableTaskAbortedException` 。中止终端任务更改
  无效,在援引前的中止 记录为取消。
- 一项独立的任务是其目标的根,没有呼叫者。
  开始树的执行树只删除
  观察到了。在同一根ID下开始使用同一方法,任务没有改变。
  另一种方法,即目标字母 与引用一样
  和启动人的记录与拒绝失败。一个分离的任务,普通 grain 方法设置
  目标的死信 持有拒绝的援引
- 拖延会持续其合乎逻辑的及时时间和代代相传。
  `durable-rpc/resume` 致其 grain的电文,其中 `Envelope.DeliverAt` 设于
  到期时间。 grain自己的收件箱接受延迟日记中的信件
  写作,没有发箱的往返旅行, 所以拖延和它的恢复 一起承诺
  在一个字写, 和收件箱进程 恢复 一旦持久工作时钟
  到达应到的时间。逻辑时间、到期时间和任务时间戳读取该时钟,
  排 `TimeProvider` 键密钥 `DurableJobTimeProviderNames.DurableJobs`,所以它们共享
  恢复时间由延迟的一代和运行过程的化身来围上栅栏, 恢复时间随时间顺序安排而起: 恢复时间取消的延缓时间
  将它作为取消的终端化,重复和腐烂的恢复器是无害的。
  和所有预定的讯息一样 简历并不代表Messagage的
  `MaxCapacity`:其发送者将其约束, grain 为每个发送者各保留一次续会
  计划推迟,直到该延迟到时候
  延迟的时机就是承诺:重复,要求下次再有时间
  例如,在配置的期间改变后,保留记录的时间和
  日志警告。 当它写入时在飞行终端中, 延迟取消
  当该写入完成时取消 。
- 电文使用发件人、接收人、任务和
  重放的主体, 所以重新播放发送 commeesce 和再复制的输出。 A
  发送者发送信息后路径完成并检测到与
  任务录音的来电人或被叫者。
  类似 grain 方法、耐久任务、信号和取消信号
  组群中每个客户和 grain 都可使用,即 Orleans信托
  边界。
- 当地一步的记录承诺以第一个耐用文字:其结果或
  儿童、延迟或信息列表。 因此,同步步骤需要花费一
  步骤的身份和投入取自父母的投入和
  早期的结果,所以重播 没有记录就会再次进入一步。
- 人体的产物结果 是在操作身体最终代码的工作项目中
  与日记的代码相同
  重放, 返回
  因此,记录的结果发现这些改动曾适用过一次。
- 启动时产生 `DurableTask.RunInline` 效果,结果记录下来
  以行刑树的终点条目 `{taskId}/{name}`,
  与日记相同的工作项目说明效果变化。
  下个日记写, 重放返回已记录的结果而不运行
  和结束的激活
  之前的写后会同时丢失两个, 所以下次尝试再次产生效果。 结果
  代码继续运行
  例如,在 `ConfigureAwait(false)`之后激活,无法记录效果。
  影响和子共享识别符号: `DurableTask.RunInline("x", f)`
  `x`,因为 `WithId("x")` 指一个儿童,但有一个未命名的效果,
  `DurableTask.RunInline(effect)`,将执行的下一个子
  标识符号,例如 `$child-3`。重弹中发现一个孩子,产生效果
  或儿童开始、不同和差异两者的切换效果
  类型; 名称的组合器输入在效果识别符号下,或
  效果,当一个命名的组合器记录了输入时。
- 与执行记录不符的重播,请求进行另一次行动
  或找到不再编码的记录结果,
  跳行:运行时间取消尝试
  `DurableTaskAttemptCancellationReason.Diverged`,与 `UnsupportedVersion`一样
  对于一个没有支持版本的版本, 抛弃其结果, 保持任务未完成, 并引起
  `ExecutionStalled` 行动的身份就是它
  其类型、识别符号和目标:呼声 grain 和方法,
  开始根 ID 和方法, 或者订阅任务。 参数没有比较, 所以a
  重播谁的呼唤通过了其他论点,或 重复不同系列的论点,
  每一个记录都存储着
  创建此操作的操作,例如呼叫、一步、一步、延迟、效果或取消
  请求延迟,如果有步骤
  记录过、记录过延误的一步或记录过电话的延迟
  无需观察或更改另一操作的记录。 grain 将每个操作的音响都响起
  `DurableTaskOptions.StalledExecutionRetryInterval`,所以后一激活重放
  使用它运行的代码执行。 与其它方法发生冲突的呼叫者
  在同一根标识符下仍然收到 `DurableTaskConflictException`。
- `DurableTask.GetVersion(name, minSupported, maxSupported)` 记录
  执行后的变化是其树的终点入口,
  `{taskId}/$version:{name}`,在处决时第一次检查姓名时, 弹道像
  a 效果记录。当它到达检查时,执行记录 `maxSupported`
  和 `0` 当重播放到达检查时, 当先前运行的操作运行时
  记录的记录仍未达到,因为先前的代码经过检查。
  在同一援引任务中附上执行记录。
  重新播放到达时, 执行中取消检查的部署
  重放进度没有改变。 试图找到支持方以外的版本
  区域间距摊位:运行时间取消 `UnsupportedVersion`的尝试,因为
  取消停止激活的尝试, 所以检查和尝试稍后
  永久操作扔出 `DurableTaskAttemptCanceledException`,其内部例外
  `DurableTaskVersionException`。尝试的结果被丢弃,任务将停留
  等待,且 grain 保持一分钟的警醒,以便稍后激活重播。
- 一项持久的任务,即公布在 `IDurableLog<T>` of its grain。 它
  附加 `DurableTask.RunInline(() => log.Append(item))`条目,其条目
  流内容写成一个步骤
  由 `BeginDraft()`草稿,读者通过 `includeDrafts: true` 接收
  将草案写入最终同步代码,让条目
  以步骤结果为同一日记写。 反弹返回
  记录序列编号,不附加或流出。
- 任务正文报告 `IsReplaying` 的运行运行自开始,而前运行
  任务树上记录了操作:儿童,包括订阅其他
  任务、分解和信号发送标记,以及记录的效果,但不表示值或
  被援引的后裔。
  一旦跑跑到达他们每个人的手中,或者开始一个操作
  之前没有记录运行; 再次完成操作无法阻止它。 它不能停止操作 。
  仅提供记录和诊断服务。
- 儿童识别资料、甄选决定和
  外向的耐久信息仍然可以重放。
  在一个记录标识符失败的错误中,在所记录的标识符下操作
  每项执行的逻辑世界协调时
  时间在影响发生前一直存在,然后在重放时被施用和再利用;
  每名儿童因执行而完成的
  观察,因此延迟在完成之前的工作开始。
- 每个终端结果都带有完成序列,在 grain 印章上
  同一日记写着:当地儿童的结果、呼叫的结果
  当呼叫者收信箱接受时 其恢复处理后延迟的结果
  后期阶段的结果得到更高的顺序。
  序列向任务状态添加一个整数, 没有日记条目: 激活源
  回收任务的最后序列。 `WhenAny` 和 `WhenEach` 选择
  以最小序列完成的候选填全程序
  未经记录决定的候选人,包括若干结果承诺
  候选人完成的完整过程没有顺序,例如:
  取消的祖先,其启动被拒绝或结果在序列标记前被存储的结果被删除,由
  死刑执行的树上记录了决定,而一项有记录的决断会让胜者保住

可适用性涵盖参加 Orleans 日刊和
直接外部副作用要求它们自己有一定的能力
担保。
