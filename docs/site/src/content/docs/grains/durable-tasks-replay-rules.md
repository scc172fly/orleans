---
title: "持久任务重用安全规则"
description: "提及那些使持久任务机构在重现中正确纠正的规则,每项规则防止的失败,以及执行这些规则的分析器诊断 ORLEANS0200 至 ORLEANS0223 。"
ms.date: 10/16/2026
ms.topic: reference
---

<a id="replay-safety-rules-for-durable-tasks"></a>

# 持久任务重用安全规则

永久任务在停止或因重新运行其身体而失败后恢复执行
开始, 而每个记录操作都返回其记录结果。
页面保持该重放,与记录操作的运行量相等。
`Microsoft.Orleans.Durable.Abstractions` 软件包携带分析器,由分析仪检查
创建时间并报告诊断 ORLEANS0200 至 ORLEANS0223;每份诊断页面
显示触发它的代码和共同的修补。

规则背后的模型,见
[重弹和确定论](durable-tasks.md#replay-and-determinism) 和
[持久任务中具有的](durable-tasks-idempotency.md).

<a id="replayed-code-recorded-operations-and-the-tail"></a>

## 重放代码、记录操作和尾尾

分析员将每个持久任务机构划分为区域:

- **记录操作** 每次执行一次,重播返回结果:
  等待持久电话、当地步骤、 `DurableTask.Run` 代表、拖延、信号
  等待、组合和控件; `DurableTask.RunInline` 效果;和 `Post`。等待
  `Task`- 在同一项目中进行持久业务的返回助手计数
  一体行动
- **重放代码** 是仍然可以达到记录操作的代码。
  重放播放它, 在后来的操作 承诺了结果和状态后
  变化。
- **同步尾尾尾** 是无法从中获取任何记录操作的代码。它运行
  只有在完成身体的运行中, 且其状态变化与任务相关的运行时, 且该任务将执行
  结果。
- **录音机构**、 `DurableTask.RunInline` 和 `DurableTask.Run`的代表
  获得地方级机构,每记录结果运行一次。
  其本身是一个具有自己区域的长期任务机构。

在当前工作项目之前,一个 `DurableTask.RunInline` 效果立即运行
返回; 返回; 可能时,在等待、计划或张贴时, `DurableTask.Run` 步数运行
后转转,作为儿童处决。在一个同步区块中,连续效果
因此,在同一个日记上承诺与它们改变的状态一起写,但无
下个 grain 彼此之间运行,每一步都有自己的权利
`DurableTask.Current`、注销范围、日记写。
同一名称的同名同带有一个识别符号,因此将不同的
记录了处决的;在 `DurableTask.GetVersion`中将变化部分分。

重播与记录有出入,安排另一行动在
或找到一个不再解码的 记录结果,不会失败
物体:运行时间将执行拖慢 `DurableTaskAttemptCancellationReason.Diverged`
并保持任务未完成, 直到代码与记录重放匹配。 见
[诊断性重显示差异](durable-tasks-operations.md#diagnose-replay-divergence) 和
[处决总数](durable-tasks-versioning.md#stalled-executions).

以下 grain 列出了每个区域:

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_replay_regions" language="csharp":::

持久任务机构是一种 `async` 方法、局部函数或返回的羊羔
`DurableTask` 或 `DurableTask<TResult>`,或 `DurableTask.Run` 接收的代表。
普通普通 grain 方法不是持久的任务机构,规则不适用于它们。
分析是程序内部的分析:它不检查帮助者,如果身体没有打电话
或别的集会的助手。

<a id="rules"></a>

## 细则细则规则规则规则规则规则规则规则规则规则规则规则规则规则细则细则细则细则细则

| 第条规则规则规则规则规则规则规则规则规则规则规则规则规则规则规则规则规则规则规则 | 预防不行为 | 诊断 |
| --- | --- | --- |
| 读取和修改记录效果、步骤或尾部的 grain 状态。 | 重放再次应用更改, 或者从后来操作所写的状态决定 。 | [ORLEANS0200](../diagnostics/orleans0200.md) |
| 生成记录效果、 步骤或尾部中的时钟、 随机和环境值, 以及从 `DurableTask.Current.UtcNow`读取时间。 产生尾巴发送到外部服务的价值, 如不起作用键, 以记录效果或一步, 因为其结果不会再次运行的尾巴 。 | 重放计算其他参数、 ID、 键或截止时间, 并产生或重复外部效果 。 | [ORLEANS0201](../diagnostics/orleans0201.md) |
| 等候外部行动 以阶梯或尾巴。 | 重播重复操作,观察另一个结果,或在服务无法使用时失败。 | [ORLEANS0202](../diagnostics/orleans0202.md) |
| 更改状态, 在最后外部等待一步或尾部后 。 | 更改承诺没有结果, 而下一转则再次应用它 。 | [ORLEANS0203](../diagnostics/orleans0203.md) |
| 返回 `DurableTask.RunInline` 效果的输出。 | 重放跳过效果, 而身体的变量保留了最初值 。 | [ORLEANS0204](../diagnostics/orleans0204.md) |
| 等待、安排或张贴所有持久任务定义。 | 任务从不运行。 | [ORLEANS0205](../diagnostics/orleans0205.md) |
| 运行每个 `async DurableTask` 定义一次。 | 第二步投出 `InvalidOperationException`。 | [ORLEANS0206](../diagnostics/orleans0206.md) |
| 名称操作没有 `$`。 | 运行时以 `ArgumentException`拒绝名称 。 | [ORLEANS0207](../diagnostics/orleans0207.md) |
| 给每个呼叫站点自己取名。 | 取消或重新排序呼叫站点, 更改其他人的已记录身份。 | [ORLEANS0208](../diagnostics/orleans0208.md) |
| 尸体留在启动的调度器上 | 与 grain和 `RunInline` 和 `Post` 进行比赛,然后从调度器上丢弃。 | [ORLEANS0209](../diagnostics/orleans0209.md) |
| 通过耐久组合运行并行的持久工作。 | 儿童按线条顺序获得身份证明,重播可以改变这种身份证明,而失去投入的情况却在继续。 | [ORLEANS0210](../diagnostics/orleans0210.md) |
| 等待 `DurableTask.Delay` 和 `DelayUntil`。 | 停止功能会失去等待,重播会再次等待整个持续时间。 | [ORLEANS0211](../diagnostics/orleans0211.md) |
| 由 `DurableTask.Current.IsTaskCancellationRequested`过滤器取消处理器 。 | 取消一项企图,例如解除职务,对一项无人取消的任务进行补偿。 | [ORLEANS0212](../diagnostics/orleans0212.md) |
| 从机构内非同步代码返回任务 。 | 尸体完成后继续工作,其失灵则逃脱。 | [ORLEANS0213](../diagnostics/orleans0213.md) |
| 给每次重复等待的信号名。 | 每等一次返回信号接收的第一个值 。 | [ORLEANS0214](../diagnostics/orleans0214.md) |
| 日志重放代码, 贯穿 `logger.ReplaySafe()`。 | 每次重播完成操作的事件记录。 | [ORLEANS0215](../diagnostics/orleans0215.md) |
| 员额调自 grains 以及长期任务机构和客户的日程安排。 | `Post` 扔球,因为客户没有耐用发包 | [ORLEANS0216](../diagnostics/orleans0216.md) |
| 通行证通过 `AttemptCancellationToken` 以任务取消清理所发出的呼叫。 | 清理的电话马上结束, 因为 `CancellationToken` 已经取消 。 | [ORLEANS0217](../diagnostics/orleans0217.md) |
| 将常数和范围通过到版本检查中。 | 一项改动记录了若干决定,或部署记录版本,没有分支机构处理。 | [ORLEANS0218](../diagnostics/orleans0218.md) |
| 等待、合并或归还每个计划孩子的手柄。 | 婴儿的失败没有被观察,删除了处决的树 取消。 | [ORLEANS0219](../diagnostics/orleans0219.md) |
| 在循环和条件中以及在其他未命名的行动之后命名操作。 | 循环、条件或部署转移生成识别资料,重放则不同。 | [ORLEANS0220](../diagnostics/orleans0220.md) |
| 运行同步工作 `DurableTask.RunInline`。 | 一步加进一个处决儿童和日记笔迹, | [ORLEANS0221](../diagnostics/orleans0221.md) |
| 代表可传回 `DurableTask.Run` 和 `DurableTask.RunInline`。 | 任务记录 a `async void` 第一次代表完成 `await`工作开始前 | [ORLEANS0222](../diagnostics/orleans0222.md) |
| 将新的不可变值指定为 grain 状态。 | 换个地方绕过日记, 所以恢复的状态与记忆中的状态不同。 | [ORLEANS0223](../diagnostics/orleans0223.md) |

<a id="state-values-and-external-work"></a>

### 国家、价值观和外部工作

前四条规则将每个副作用 完全放在一码内 Grain 状态保持
重新播放代码,重现新状态,并再次应用其写作。
`DurableTask.RunInline` 效果,一步的终代码 尾尾部各进行更改
外部通话按步骤进行,其结果
重放返回后, 他们发送了来自任务的一个不起作用的密钥, 因为一个步骤
其结果没有再运行,你看
[将耐久任务与其他系统整合](durable-tasks-integrations.md).

<a id="definitions-and-names"></a>

### 定义和名称

持久任务的定义是冷淡的,对于 `async DurableTask` 方法来说,是单用。
所记录的操作用其名称或它位于未命名的
操作, 所以名称在每一个呼叫站点保持独一, 没有 `$`, 并且在整个
[任务身份和参考](durable-tasks-identity.md) 列出每个
运行时生成的和会影响它的更改的 身份识别符。

<a id="scheduling-time-and-concurrency"></a>

### 时间安排、时间和货币

启动的调度器命令一个身体操作 尸体留在它上面运行
并行的、同时并 `DurableTask.WhenAll`, `WhenAny`, `Race`, `ScheduleAll`,
和 `WhenEach`长期拖延和超时记录了它们
逻辑时间到期。 [结构通货币](durable-tasks-structured-concurrency.md)
描述组合集如何结算输入 。

<a id="cancellation-signals-logging-and-starts"></a>

### 取消、信号、伐木和开始

永久取消被记录下来, 和宿主站不是, 所以处理者把他们分开。
信号抓取每个名称一个值的匹配。 除非日志跳过, 否则在重放代码中重复使用日志
重播。 `Post` 阶段在调用 grain发件箱中开始一个启动, 而客户没有这个发件箱 。

<a id="rules-which-the-analyzers-dont-check"></a>

## 分析者不检查的规则

这些规则取决于数据或部署历史,分析者无法观察到:

- **定购投入的附表。** 构建组合输入和循环
  从数组、列表或排序序列中产生耐久工作。 A `HashSet<T>` 或字典
  在另一个进程中,在清除量之后可以按另一顺序列出,再分配
  输入索引和发生次数。
- **从重放稳定配置中决定 。** 配置, 重播放的配置读取
  作为一种选项,设定环圈的界限,在处决尚未执行时保持不变。
  记录延迟在其配置的时段变化时保持其记录的适当时间。
- **不要在 `IsReplaying`上分支。** `DurableTask.Current.IsReplaying` 为伐木和交易服务
  诊断。在工作之前完成的运行过程被重放,所以代码
  运行时只有 `IsReplaying` 是 `false` , 才能两次运行或永远无法运行 。
- **版本,由添加或按分支组或按分支组提供。** 保持现有的操作、名称和参数
  重复播放的呼声按方法和目标与记录相符,
  添加新的
  在现有名称之后使用新名称的行动,
  改变执行处决之前通过的业务
  `DurableTask.GetVersion`,并添加新的工作流程形状作为新方法。见
  [版本耐久任务](durable-tasks-versioning.md).
- **打破历史** 每一个记录的行动 都留在任务树上 直到
  树树被删除。一个循环周期无限的工作流程将结束每一代人,并开始
  其继任者是一项独立的任务,例如 `subscription-{id}-{generation}`。

<a id="configure-the-analyzers"></a>

## 配置分析器

规则适用于每个引用抽象包件的工程。设置
规则的严 `.editorconfig`例如,在重播日志上失败:

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0215.severity = error
```

每一诊断页都说明在适当抑制时如何进行。
