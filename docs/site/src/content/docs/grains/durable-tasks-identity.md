---
title: "长期任务身份和参考"
description: "如何构建持久任务身份证明的参考文献,哪些识别文件是打电话者、处理者和诊断者所暴露的,它们是如何在重复播放和部署中保持稳定的,以及如何储存和传递任务引用。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="durable-task-identity-and-references"></a>

# 长期任务身份和参考

每项持久任务由主办任务的 grain 和等级确定。
<xref:Orleans.Durable.Tasks.TaskId>. Identity is how a replay finds each recorded
操作,重新审判的开始是如何达到相同任务,被调用对象如何区分
以及其它缔约方如何观察、取消和信号任务。
运行时间构造, 每个模型都可见, 变化
并如何从别处处理任务。

<a id="the-shape-of-a-task-id"></a>

## 任务编号的形状

任务代号是被 `/`分离的段的路径路径:根段,然后是一个段
键入的每个水平。 `DurableTask.Current.Id` 报告执行中的 ID,
和 `TaskId.Child(segment)`、 `Parent()`和 `IsAncestorOf(other)` ,在这条道路上行驶。

- `TaskId.ToString()` 返回逃出路径,该 `TaskId.Parse` 反正读数。 A `/` 或
  `\` 以 `\`,因此一个部分包含 grain 身份证,此种
  `report/q3`,以 `report\/q3`表示。
- `TaskId.CreateRoot(segment)` 和 `string rootId` 超载,例如
  `GetDurableTask<TResult>(rootId)` 和 `IDurableTaskManager.CancelAsync(rootId, reason)`,
  取一个未逃脱的根段。
- 运行时生成的线段, 以开始或包含为单位, `$`。 使用
  `WithId` 和 `DurableTask.RunInline` 在一项持久任务中接受的任务不包含 `$`,因此
  名称永远不等于生成的段; 见 [ORLEANS0207](../diagnostics/orleans0207.md)。

以下工作流程创建了大多数类型的ID。 它的注释中给出了每个
操作记录:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_tree" language="csharp":::

<a id="kinds-of-ids"></a>

## 身份识别类型

<a id="root-ids"></a>

### 根标识

根任务在 grain上没有父本。 开始该任务的当事人选择其根代号:

| 开始于 | 根代号 | 示例 |
| --- | --- | --- |
| 客户端或普通 grain 方法,其中 `WithId(name)` | `name`,逐字记录 | `publish-q3` |
| 客户端或普通 grain 方法,不使用 `WithId` | 以 `"N"` 格式格式的新 GUID  | `3f2a9c01b7e44d6b9a0c5e8f1d2b3a47` |
| 一项持久的任务,与 `.Detached(name)` | `name`,逐字记录 | `archive-q3` |
| 一项耐久任务,有 `.Detached()` ,无名称 | 一个段, `{grainId}/{taskId}/{segment}`:开始 grain、开始执行执行的ID和产生的一部分执行下一个未命名操作的下个未命名操作,每个部分都有 `/` 和 `\` 逃脱 | `report\/q3/publish-q3/$child-2` |

根 ID `$caller` 是为下列调用范围保留:
其它 grains 在它下创建任务, 客户端或 grain的启动在它下进行
`ArgumentException`外
耐久任务, `.Detached()` 将无任何改变,因为客户端启动的每一项任务都是
已经有一个根。从导出独立的根 ID 包括起始 grain,所以工作流程
不同 grains 上的任务编号相同,任务编号相同。 启动不同的任务; 启动必须
将它与 `WithId`共享一个任务名称。

<a id="operations-of-an-execution"></a>

### 执行的操作

尸体运行的每项操作记录在行刑证下面

| 业务 | 段次 | 示例 |
| --- | --- | --- |
| 以 `WithId(name)`命名或有记录效果的儿童、儿童延迟、儿童组合、儿童组合、儿童订阅 `DurableTask.RunInline(name, effect)` | `name`;同一名称在一次运行、 `name$2`、 `name$3`等过程中后来发生,等等 | `publish-q3/check$2` |
| 未命名儿童、延迟、 `DurableTask.Run` 步、订阅或 `DurableTask.RunInline(effect)` | `$child-{n}`,按运行创建它们的顺序编号 | `publish-q3/$child-1` |
| 未命名的组合器或超时 | `${kind}-{n}`,其中 `{kind}` 是 `when-all`, `when-any`, `race`, `schedule-all`, `when-all-completed`, `when-each`,或 `timeout` | `publish-q3/$race-1` |
| 组合集输入 | `{combinator}/{index}`,按输入顺序 | `publish-q3/render/0` |
| 工作超时和延迟, `WaitAsync` 和 `WaitUntilAsync` | `{timeout}/0` 和 `{timeout}/1` | `quote/0`, `quote/1` |
| 无法决定的 | `{combinator}/$winner` 用于 `WhenAny`, `Race`和超时,或 `{combinator}/$next-{n}` 用于 `WhenEach` | `first-quote/$winner` |
| 一份决定 `DurableTask.GetVersion(name, ...)` 的版本  | `$version:name`,每次执行一次 | `publish-q3/$version:carrier-booking` |
| 独立开始 | `$detach:{targetGrainId}:{rootId}` | `publish-q3/$detach:archive\/archive:archive-q3` |
| 长期任务发出的信号 | `$send:{targetGrainId}:{taskId}:{name}`;重复发送, `$2`, `$3` | `place-order-42/$send:order\/42:place-order-42:shipped` |
| 信号记录值,在所援引的任务之下 | `$signal:{name}`; `DurableSignal<T>.For(suffix)` 名称 `{name}/{suffix}` | `publish-q3/$signal:approved`, `publish-q3/$signal:approved\/round-2` |

- 儿童命名、儿童命名、儿童名称组合和命名效果每个共享一个识别符号
  出现名称: `DurableTask.RunInline("x", f)` 和 `await DurableTask.Run(g).WithId("x")`
  `x`下两个记录。
  使用“执行间隔”和“
  差异名称为两种类型。
- 无名儿童、无名效果、未命名订阅和
  共享 `$child-{n}` 计数。 未命名的组合器和超时
  共享 `${kind}-{n}` 数。
- `$detach:` 和 `$send:` 部分、 `:`、 `\`和 `$` 的部件内部分已逃脱
  与 `\`.
- 只有当完成令无法决定时,例如当
  输入完成后没有完成程序, 输入的输入在
  `SelectionWaitStarted` 当选择等待时, 将出现诊断事件 。
- 每个地方步骤都有自己的执行,所以其作业在身份证下面嵌巢,例如:
  `publish-q3/check/$child-1`.

<a id="calls-to-other-grains"></a>

### 调用其它 grains

呼叫另一 grain 的电话,在呼声段下调用树上记录。
年为 `publish-q3/render/0`中显示的信息 grains 以其名称命名的呼号 *线编号*:
来电者的运行, 一个图形界面的化 grain 储存时的薄荷
根任务使用的调用, 格式以 `"N"`格式化, 然后是调用区段。 the
被调用者将任务存放在呼叫者的视野下,

```text
{incarnation}/{callerTaskId}
$caller/{callerGrainId}/{incarnation}/{callerTaskId}
$caller/report\/q3/9b1c0e4f2d7a4e8c8f3b5a6d1e2c3b4a/publish-q3/render/0
```

范围使呼叫者分开:两个 grains ,其工作流程具有相同的任务编号一
不同的身份识别码下呼叫,所以不能重新连接对方的任务。
连续运行:一次 grain 删除根任务树,排程相同的根 ID
新的化身再次运行, 所以它的呼唤达到新任务, 它的信息不是
与前一次竞选的反弹,对前一次竞选的答复,如迟到
完成或延迟恢复, 改变什么也改变不了。 在一次运行中, 绘图是一个纯
儿童身份,重放和重新播放
调用达到相同的任务。在被调用的受访者中, `DurableTask.Current.Id` 报告范围
ID, 并且 `DurableTask.Current.Incarnation` 报告每棵树的运行情况。
发送根 I , 则由被调用者存储 。 订阅和分离
以用户身份使用
输出的完成地址。

<a id="signals-and-subscriptions"></a>

### 信号和订阅

信号地址为所引用的 *任务*:根任务,或另一个 grain 调用的任务。
其价值记录在这项任务 `$signal:{name}`之下,在那里等待
任务树的树找到它。 一个未命名的等待运行作为等待执行的一部分,记录为否
操作;等待 `WithId` 名称以该名称下的孩子身份运行。
其身份没有改变。

等待一个耐久任务中的 `DurableTaskReference<TResult>` ,安排它或
将文件传给组合器记录一个订阅儿童,该作业的身份证下有一个订阅儿童。
指定名称,如 `$child-3` 或 `WithId` 的名称。该子子
任务的结果。

<a id="where-each-id-is-visible"></a>

### 每一ID显示的位置

| 来源来源 | 《报告》报告报告 |
| --- | --- |
| `DurableTask.Current.Id` | 运行中的执行: 一个根、 一个步骤或被调用对象的范围任务 。 |
| `ScheduledTask.Id` | 执行中开始或观察任务操作的操作:儿童身份、 `$detach:` 记录或订阅儿童。除持久任务外,根身份。 |
| `ScheduledTask.TargetTaskId` | 任务的任务 grain 由其他当事方处理的: 独立任务根号,被调用人 `$caller/...` ID, 或者订阅时要观察的任务。 在持久任务之外, 根 ID 。 |
| `IDurableTaskReference.Id` | 引用的任务,加上 `Target`,及其 grain。 |
| `DurableTaskAddress.Id` | 由其他缔约方以 `Target`处理其 grain的任务。 |
| `DurableTaskEvents` | 过渡所涉每项任务、作业、信号等待和选择的识别。 |

客户通过手柄观察自己启动的身份证:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_client" language="csharp":::

<a id="internal-names"></a>

### 内名

运行时间也使用不是身份识别, 也不是编程模式的一部分的名字:
日记状态 `orleans.durable-tasks.runtime` 以及其中的表格,例如
`$tasks`, `$task-requests`,和 `$task-results`,并请求指纹
`$`,例如 `$effect`、 `$version`、 `$subscribe:`、 `$signal-send:`和 `$completion-decision:`。
诊断显示其中一些;它们的格式在释放之间会发生变化。

<a id="stability-across-replays-and-deployments"></a>

## 各种重播和部署之间的稳定

重放在新的运行计算, 因此一个ID 的 ID 下查找每个记录操作
当每次运行都从相同的输入计算时, 均是稳定的 :

| ID 身份 | 稳定时 |
| --- | --- |
| 根标识符 `WithId` 名称 | 总是:打电话的人产生它。 |
| 指定行动, `name` 或 `name$n` | 名称不变,同一名字以前的情况也不改变。 |
| `$child-{n}` 和 `${kind}-{n}` | 每一次运行都按照相同的顺序 创建了之前的未知操作 |
| 组合器输入 `{combinator}/{index}` | 组合的 ID 及其输入顺序没有改变 。 |
| `$detach:`, `$send:`,和 `$signal:` 记录记录 | 总是:它们来自目标、任务和名称。 |
| `$version:` 记录 | 总是:它们来自更改的名称, 执行时每次检查名称时都有一个记录。 |
| 衍生的分离根根ID | 开始执行的 ID 和在开始之前的 `$child-{n}` 计数没有变化 。 |
| 调用员范围ID | 打电话者的孩子身份没有改变 |

身份识别找到记录;记录操作决定重播是否匹配。 A
当它要求使用同一目标的相同操作时, 重播放匹配的游戏: 呼叫
同一 grain上的相同界面方法,同一方法的分离起始点
相同的根代号、相同任务的订阅、延迟、一步或效果。
运行时间根据生成的可引用类型来识别方法, 且不比较
参数,例如通过添加 a/
选项字段或升级更新一个序列式播放器、重放相同操作和
复选 a
其ID 下根任务也匹配:同一调用者使用相同方法到达
保存任务, 不管其参数如何, 以及另一种方法或调用冲突。 每个记录
也存有其请求中出现分歧和争议的系列论证的指纹,
冲突信息报告。

部署之间的变动对待处决的处决产生影响如下:

- **重命名一个 `WithId` 名称** ,给操作提供了新的ID。重放时找不到记录
  旧名下的记录就留在
  树直到树被删除
- **在现有的未命名操作改变其生成之前添加一个未命名的操作**
  ID。重放后再在变换的ID下找到另一个操作的记录:
  另一种方法或 grain 使用相同方法时,
  在同一 grain 返回其它调用结果,而本地级的返回
  另一步的结果。
- **更改参数的序列表**或调用传票中的论点,
  每一个操作的身份都保持不变
- **添加一个有名称的操作** ,并使用新名称,其他标识将保持不变。
- **在现有名称重新编号之前添加或删除重复名**
  后一情况;见 [ORLEANS0208](../diagnostics/orleans0208.md)。

列出以后更改可能移动的每个操作名称,给新的操作提供新名称,以及
续 [安全版本工作流程](durable-tasks-operations.md#version-workflows-safely).
改变在等待处决之前已经通过的行动,
`DurableTask.GetVersion`;见 [版本耐久任务](durable-tasks-versioning.md)。

<a id="address-a-task-from-elsewhere"></a>

## 处理来自别处的任务

任务由 grain 及其任务编号来处理。
观察、取消、中止或信号任务,而 grain 保留它。

<a id="durable-references-belong-to-root-tasks"></a>

### 持久引用属于根任务

根任务, 客户端、 普通 grain 方法和独立开始创建的原始任务
寿命: grain 保留这些款项, `ResultRetentionPeriod` 完成后,和
直到每个用户完成全部任务, 并提交长效的参考文件。

随附子女,包括被召人的子女 `$caller/...` 属于其所有者的树上:
被调用者一旦接听调用 grain 接受其结果,且所有人 grain接受其结果,便将其删除。
一旦根根是终端, 将删除所有者树的树。 在执行中使用它的控件
并开始其他政党作为根追踪的工作。

<a id="derive-references-from-business-identity"></a>

### 商业身份的衍生参考

当任务 grain 和根 ID 源自商业价值时, 各方计算
之前的归档文件由
仅报告身份识别码:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_lookup" language="csharp":::

衍生引用不需要存储,而从重新审取的参考文献以同名开头
选择能识别业务经营的名称,例如
年为 `archive-{reportId}`并保持他们稳定的部署。

<a id="store-and-send-addresses"></a>

### 存储和发送地址

<xref:Orleans.Durable.Tasks.DurableTaskAddress> 是指在其它地方指定任务的价值:
其 其 其 年 其 其 其 其 其 其 `Target` grain 任务和任务以及委员会的任务 `Id`. Orleans 连号 grain 电话和日记
状态, System.Text.Json 将其写成 `{"Target":"report/q3","Id":"publish-q3"}` 。
JSON 和HTTP有效载荷。 `TaskId` 自己连环地将它当作逃生之路

| 将 | 使用 |
| --- | --- |
| 读取一个控点的地址 | `handle.TryGetAddress(out var address)`返回 `true` 根任务、 单项任务或 `GetDurableTask` 并可以观察其 `Id` 是控手的 `TargetTaskId`。被附着的孩子的手柄没有地址。 |
| 读取参考地址 | `DurableTaskReference<TResult>.Address`、或 `Target` 和 `Id` 通过 `IDurableTaskReference`。 |
| 解决地址 | `GrainFactory.GetDurableTask<TResult>(address)` a 中 grain,或 `client.GetDurableTask<TResult>(address)` 客户。 |
| 信号任务 | `GrainFactory.GetGrain(address.Target)`,然后 `SignalDurableTaskAsync` 或 `TrySignalDurableTaskAsync` 与 `address.Id`。 |

处理时决心以任何其他方式行事的参照:一项持久的任务等待完成
其它代码等待,投票,取消或中止
A. 任务,通过它。 grain 专用来管理自己的住址和所达到的,
任务在任何一次恢复后再次发生:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_store" language="csharp":::

工作流程将地址存储在同步尾尾尾, 因此地址与
任务结果。 `AwaitArchiveAsync` 读取存储地址时有记录效果,因此
重放相同任务的订阅者。

客户返回 HTTP 呼叫器的地址, 稍后请求将它发回
信号、 等待或取消任务 :

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_send" language="csharp":::

地址可以控制任务,不管是谁持有:谁能称为 grain
能够取消、中止或信号任务。授权之前含有地址的请求
依《公约》采取行动的任何决定或 grain 外部请求触发触发

<a id="retention-and-not-found-results"></a>

### 留存和未发现结果

参考解决,而 grain 保留任务:

- 保留一项任务,而不呼叫 grain,例如客户端启动或分离根(如用户端启动或分离根)
  用于 `ResultRetentionPeriod` 完成后,直至每个
  完成确认为持久的任务。
- 一项任务, grain 一旦拨号被删除, 即被删除 grain 富含可耐性
  接受其结果。

删除后,投票和订阅会发出 `DurableTaskNotFoundException`信号
返回 `TaskNotFound`,同一ID下的一个起始点将请求作为新运行再次运行
连在通讯的内 `DeduplicationWindow`:呼吁,
新的运行的取消、订阅、信号和延迟恢复都有自己的
早期运行的迟到回复没有到达它。 A
`DurableTaskNotFoundException` 也报告了一项从未开始或开始的任务
尚未达到 grain ; `ScheduleAsync` 一旦返回 grain 持有任务, 则返回。
返回的代号,在留存结束之前找到。
[保留和删除](durable-tasks-operations.md#retention-and-deletion).
