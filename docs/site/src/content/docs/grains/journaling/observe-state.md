---
title: "观察日记状态和流汇草稿"
description: "观察 Orleans 日志值,读取仅附于附件的耐久日志,并向读者流出波动性草稿。"
ms.date: 11/20/2026
ms.topic: how-to
---

<a id="observe-journaled-state-and-stream-drafts"></a>

# 观察日记状态和流汇草稿

监视流将承诺的更改传送到 grain日记状态。一个 grain 以 <xref:System.Collections.Generic.IAsyncEnumerable`1> 方法将它们暴露在呼叫者面前。 <xref:Orleans.Durable.Journaling.IDurableLog`1> 添加了一个仅附加日志,其读者从序列号中恢复使用该日志,并起草出在其耐久条目存在之前流动挥发性内容(如模型输出)的草稿。

配置 [使用耐久状态](durable-state.md)中所描述的配置日记 。

<a id="expose-a-watch-from-a-grain"></a>

## 向一个 grain展示一只表

本 grain 记录在耐用日志中的谈话,并通过草稿将每个答复流出:

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="conversation_grain_interface":::

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="conversation_grain":::

Orleans 通过互换电话列出了 grain的 `IAsyncEnumerable<T>` 结果, 所以在 `Ask` 流响应时要注意继续交付。 在自定义激活执行背景中点出一个表, 正如 grain 方法一样 。 `End` 封存日志, 以结束每只手表的运行 。

<a id="committed-delivery"></a>

## 交付承付款项

观察流只提供持久性状态。 每个变化都用激活本地写入版本标记: 以该日记的版本来记录它。 每个写入捕捉每个州, 保存它所捕获的东西, 然后将一个水印作为版本发布给每个观察者, 后者提供能够实现可持久写作的变化。 在写取其条目后又换个转弯, 等待以下文字, 所以读者永远不会提前遵守。 同样的规则制约着其他确认 : 一个持久任务完成源的任务在记录结果的写作之后完成 。

永久状态将获得对通过它的价值、项目和条目的所有权, 日记记录每次修改时都会发生变化。 要更改一个、 指定或附加一个新实例。 观察流会提供同样的事例, 所以观察到的改变与其存储的文字相符 。

<a id="watch-a-value"></a>

## 观察值

<xref:Orleans.Durable.Journaling.DurableStateWatchExtensions.WatchAsync*> 手表 <xref:Orleans.Durable.Journaling.IDurableValue`1>。流从当前值开始,一旦它持久,就会开始于当值值,所以在下一个写作后,在数值被阶梯时开始的手表会收到它,然后在每个修改它的文章之后提供最新值。每个 <xref:Orleans.Durable.Journaling.ValueChange`1> 都包含该值和使它耐久的写法。一个读器速度慢的读者会接收最新的值并跳过中间值,因此一个值表不会积压。

无人观看的价值不会公布任何信息,也不会分配任何观察资料。 经理在第一流开始时创建了观察员状态,一旦恢复了状态。

<a id="read-a-durable-log"></a>

## 读取经久日志

<xref:Orleans.Durable.Journaling.IDurableLog`1>,以 <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddLog*>创建,为每个附加条目指定耐久、单倍增长序列号的下一个值:

- <xref:Orleans.Durable.Journaling.IDurableLog`1.Append*> 步进一个条目并返回其序列号。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.FirstSequence>、 <xref:Orleans.Durable.Journaling.IDurableLog`1.NextSequence>和 <xref:Orleans.Durable.Journaling.IDurableLog`1.CommittedSequence> 描述保留、附加和耐久条目。在 `CommittedSequence`之前的条目上贴页历史。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.TrimBefore*> 抛弃旧条目,将其保留为约束性保留。保留条目仍然有效。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.Clear*> 丢弃每个条目以替换它们, 例如用压缩摘要。 一个清晰的序列数需要一个序列号, 所以序列数在历史中永远不被再利用 。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.Epoch> 标识历史。在日志创建后,或在删除 grain日记之后,第一次更改以新的随机时段开始历史,并随此时间进行日志的日志日志日志。删除日记将新历史中的序列号从零重现。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.Seal*> 永久结束日志。 `Append`, `Clear`,和 `BeginDraft`,放弃开放的草稿,还可以剪短。只删除日记,不封上它。

<xref:Orleans.Durable.Journaling.IDurableLog`1.ReadAsync*> 读取保留条目的位置,然后等待新的条目。读者在位置上重新阅读日志中的条目,而不是缓冲它们,因此慢速的读者不保留条目。一个位置是一个时段和一个序列编号。每个事件都带有 <xref:Orleans.Durable.Journaling.LogEvent`1.Epoch>的历史时代,因此一个时段和序列号形成完整的重放检查站。作为读取当前历史历史的时代,通过零进行重播。除了条目外,流还传递这些 <xref:Orleans.Durable.Journaling.LogEventKind> 事件:

| 活动活动 | 意义的含义含义 |
| --- | --- |
| `Trimmed` | `TrimBefore` 在 `Sequence`之前丢弃条目。 每一个活动读者都收到它, 包括阅读了每个条目的读者。 `Sequence` 之前的位置的读者继续离开它 。 |
| `Reset` | 读者所观察到的条目被替换: `Clear` 丢弃了它们, 日记被删除, 读器又以另一个历史时代恢复了, 或者读者的位置会跟随日志的结尾。 丢弃在它之前阅读的条目。 流从 `Sequence` 所查明的历史中 `Epoch`. |
| `CaughtUp` | 河流已经传送了所有当读开始时的输入, 它曾经发送过一次 。 |
| `Sealed` | 日志在 `Sequence`之前结束。 这是流的终极事件 。 |

第一个保留条目之前的流开始于 `Trimmed`。以另一个历史时代开始,或以一个清除序列编号开始或在此之前开始于 `Reset`,包括恢复 grain 之后;而从清除后开始的流则没有。在附加、剪辑、清查或封存等内容时,流将接收条目、 `Trimmed`、 `Reset`和 `Sealed` ,因此每个读者收到的条目都是有效的复选点。位于未确认的曲目已经丢弃的条目等待曲目写作的读者。要恢复,通过最近事件的历史和收到的最新条目之后的位置。

`Sealed` 区分了从停止的激活中结束的日志。 当激活停止时, 流将结束, 没有它 。

<a id="stream-drafts"></a>

## 串流草稿

<xref:Orleans.Durable.Journaling.IDurableLog`1.BeginDraft*> 打开一个 <xref:Orleans.Durable.Journaling.IDurableLogDraft`1>:

- `Write` 将一个挥发性项目立即送到包括草稿在内的流中:通过 `includeDrafts: true` 到 `ReadAsync`。草案只在打开时将其项目保存在记忆中。
- `Commit` 关闭草稿,并附加零或更多条目以替换其项目。条目组成一个日记项,因此写一个条目就能使所有条目都持久,而无法编码的条目可以在不附上任何条目的情况下将草稿打开。读者会收到 `DraftCommitted`,其 `Sequence` 和 `Count` 给出承诺条目的范围,然后是条目。没有条目的 `Commit` 将干净地关闭草稿。
- `Abandon` 将草稿关闭,不输入条目和报告 `DraftAbandoned` ,不使用所有者 `Message`。处理开放式草稿会放弃它,因此失败的操作,例如中度反应失败的样板流,就会释放其草稿。

A 流报告 `DraftCommitted` 一旦承诺条目被永久化, 包含草稿并开始于打开草稿时的流流从零点位置接收草案的项目。 当草稿关闭时, 其项目就会发布: 一个尚未读取每个项目的读者 `DraftCommitted` 或 `DraftAbandoned` 下一条内容, 并且从未收到剩余项目。 承诺的条目包含最终内容 。

`DraftAbandoned` 含有 <xref:Orleans.Durable.Journaling.LogDraftAbandonReason>:

| 原因理由原因 | 原因原因原因 |
| --- | --- |
| `Abandoned` | 业主打电话 `Abandon`。 |
| `Disposed` | 车主把开口的草稿处理掉了 |
| `JournalDeleted` | 日志的日记被删除了 |
| `Sealed` | 记录是在草案开放时密封的。 |
| `PreviousActivation` | 另一起启动开启了草稿,日志保留不承诺,因此内容丢失。 |
| `Unknown` | 启动程序既未保留草案承诺,也未记录为何要关闭。 |

当日志被删除或日记被密封时,日志将关闭草稿。 日志所有人随后持有一份封闭草稿, 其中 `Write` 和 `Commit` 投出一个 <xref:System.InvalidOperationException> ,其中说明为何关闭。 请先检查 `IsOpen` ,然后才能承诺完成日志可以关闭的草稿 。

每个 <xref:Orleans.Durable.Journaling.LogDraftId> 都包含对日志实例的识别符号,因此包括打开草稿的激活。重新连接的读者通过它的位置和它正在查看的草稿。如果该草稿仍然开放,则流体从零位置重复其项目。日志将一个带有承诺条目的识别符号及其记录,所以在进行草案时,流体报告 `DraftCommitted` ,以任何激活中取代它的范围取代它,而日志保留了第一个条目。如果启动时放弃了草稿,流体报告 `DraftAbandoned` ,并以此为由;每个日志记录16个最近放弃的草稿的原因,旧的草稿和报告 `Unknown` 。以前开启过并且从未承诺过的草稿作为 `PreviousActivation`加以报告;将其内容从最后承诺条目中重新生成。

客户提供历史和进展中草稿,并跟踪其恢复位置:

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="conversation_client":::

<a id="failure-and-shutdown"></a>

## 故障和停机

日记写失败, 无法将管理器围成围栏并要求停止 grain 。 观察流后会发生 <xref:System.InvalidOperationException> 内部例外的存储失败, 读者从未收到错误写入内容包含的修改。 阅读器重新连接到新激活上, 从而恢复耐久状态 。 当管理器正常停止时, 流就会完成。 取消流的符号结束该流 。

删除 grain日记时记时记时记时值的默认值,或者对观察条目并放弃打开草稿的日志阅读器来说,将默认值投递到值表,或者对记录阅读器来说,将 `Reset` 发送到“ 注意观察条目并放弃打开草稿”。在草案中承诺删除后,读者会收到 `DraftAbandoned` 和 `JournalDeleted`。

<a id="test-code-which-uses-a-log"></a>

## 使用日志的测试代码

测试测试 grains 使用耐久状态对真实的日记国家管理人员。 <xref:Orleans.TestingHost.InProcessTestCluster> 配置时带有变化不定的日记存储功能, 将日记保存在 silo 内存,和 <xref:Orleans.TestingHost.InProcessTestCluster.DeactivateAsync*> 在新激活中恢复练习 。 将日记格式配置为制作 silos 或,如本试验一样,使用反射法 JSON 元数据:

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="journaling_test_cluster":::

需要 `IDurableLog<T>` 试验中 grain 以同样的方式测试它。 `IDurableLog<T>` 替代不会复制耐久性、草稿或阅读流。
