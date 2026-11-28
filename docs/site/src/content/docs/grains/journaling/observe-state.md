---
title: 观察日志化状态并流式传输草稿
description: 观察 Orleans Journaling 中的值，读取仅追加的持久化日志，并向读取方流式传输尚未持久化的草稿。
ms.date: 11/20/2026
ms.topic: how-to
---

<a id="observe-journaled-state-and-stream-drafts"></a>

# 观察日志化状态并流式传输草稿

观察流传递 Grain 日志化状态中已提交的更改。Grain 通过返回 <xref:System.Collections.Generic.IAsyncEnumerable`1> 的方法向调用方公开观察流。<xref:Orleans.Durable.Journaling.IDurableLog`1> 还提供仅追加的日志，读取方可以从指定序号恢复读取；它也提供草稿，使模型输出等尚未持久化的内容能在相应持久化条目生成之前以流的形式传输。

按照[使用持久化状态](durable-state.md)中的说明配置 Journaling。

<a id="expose-a-watch-from-a-grain"></a>

## 从 Grain 公开观察流

此 Grain 将会话记录在持久化日志中，并通过草稿流式传输每次响应：

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="conversation_grain_interface":::

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="conversation_grain":::

Orleans 通过交错执行的调用枚举 Grain 的 `IAsyncEnumerable<T>` 结果，因此在 `Ask` 流式传输响应的同时，观察流仍可持续传递数据。应当像 Grain 方法一样，在所属激活实例的执行上下文中枚举观察流。`End` 会封存日志，从而终止该日志的所有观察流。

<a id="committed-delivery"></a>

## 传递已提交的更改

观察流只传递已持久化的状态。每项更改都带有激活实例内部的写入版本标记，即捕获该更改的日志写入版本。每次写入捕获所有状态，将捕获的内容持久化，然后向每个观察者发布该版本的一个水位标记；观察者据此传递本次写入所持久化的更改。如果另一个执行轮次在本次写入捕获条目之后暂存了更改，该更改必须等待下一次写入，因此读取方不会提前观察到它。其他确认也遵循相同规则：持久化任务完成源的任务在捕获其结果的写入完成之后才完成。

持久化状态取得传入的值、项和条目的所有权，日志在每次更改发生时记录它。要修改其中一项，应赋予或追加一个新实例。观察流传递的是这些相同的实例，因此观察到的更改与对应写入所存储的内容一致。

<a id="watch-a-value"></a>

## 观察一个值

<xref:Orleans.Durable.Journaling.DurableStateWatchExtensions.WatchAsync*> 用于观察 <xref:Orleans.Durable.Journaling.IDurableValue`1>。流从当前值开始，但前提是该值已持久化；因此，在值已暂存但尚未持久化时开始的观察流会在下一次写入后收到它，之后每次写入更改了该值时，流都会传递最新值。每个 <xref:Orleans.Durable.Journaling.ValueChange`1> 都包含该值以及使它持久化的写入版本。读取较慢时，读取方只接收最新值并跳过中间值，因此值观察流不会积压数据。

如果一个值无人观察，就不会为观察而发布任何数据或分配任何资源。管理器在恢复状态后，于第一个观察流启动时创建其观察者状态。

<a id="read-a-durable-log"></a>

## 读取持久化日志

通过 <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddLog*> 创建的 <xref:Orleans.Durable.Journaling.IDurableLog`1> 为每个追加的条目分配下一个持久化且单调递增的序号：

- <xref:Orleans.Durable.Journaling.IDurableLog`1.Append*> 暂存一个条目并返回其序号。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.FirstSequence>、<xref:Orleans.Durable.Journaling.IDurableLog`1.NextSequence> 和 <xref:Orleans.Durable.Journaling.IDurableLog`1.CommittedSequence> 分别描述保留的、已追加的和已持久化的条目范围。提供分页历史记录时，应读取 `CommittedSequence` 之前的条目。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.TrimBefore*> 丢弃旧条目以限制保留范围。保留的条目仍然有效。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.Clear*> 丢弃全部条目，以便用其他内容替换，例如压实后的摘要。清空操作消耗一个序号，因此同一段历史中的序号绝不会重复使用。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.Epoch> 标识一段历史。创建日志或删除 Grain 的日志之后的首次更改会开启一段具有新随机纪元的历史，该纪元随更改一起记录到日志中。删除日志会使新历史的序号从零重新开始。
- <xref:Orleans.Durable.Journaling.IDurableLog`1.Seal*> 持久化地终止日志。已封存的日志拒绝 `Append`、`Clear` 和 `BeginDraft`，放弃尚未关闭的草稿，但仍允许截除旧条目。只有删除日志才能解除封存。

<xref:Orleans.Durable.Journaling.IDurableLog`1.ReadAsync*> 从指定位置读取保留的条目，然后等待新条目。读取方会从日志中的当前位置重新读取条目，而不是将条目缓存在读取方，因此读取较慢也不会在读取方保留条目。一个位置由纪元和序号组成。每个事件都在 <xref:Orleans.Durable.Journaling.LogEvent`1.Epoch> 中携带所属历史的纪元，因此纪元与序号共同构成完整的重放检查点。传入零作为纪元可读取当前历史。除了条目，流还会传递以下 <xref:Orleans.Durable.Journaling.LogEventKind> 事件：

| 事件 | 含义 |
| --- | --- |
| `Trimmed` | `TrimBefore` 已丢弃 `Sequence` 之前的条目。所有活动读取方都会收到此事件，包括已读取全部条目的读取方。位于 `Sequence` 之前的读取方从该序号继续读取。 |
| `Reset` | 读取方已经观察到的条目被替换：`Clear` 丢弃了它们，日志被删除，读取方使用另一段历史的纪元恢复读取，或者读取方的位置超出了日志末尾。丢弃收到此事件之前读取的条目。流从 `Epoch` 标识的历史中的 `Sequence` 继续。 |
| `CaughtUp` | 流已经传递读取开始时所有已提交的条目。该事件只传递一次。 |
| `Sealed` | 日志在 `Sequence` 之前已结束。这是流的最后一个事件。 |

从第一个保留条目之前开始的流以 `Trimmed` 事件起始。使用其他历史的纪元，或从已清空的序号及其之前的位置开始的流，以 `Reset` 事件起始，即使 Grain 已重新激活也是如此；而从清空位置之后开始的流不会收到该初始事件。追加、截除、清空或封存操作持久化后，流才收到条目、`Trimmed`、`Reset` 和 `Sealed`，所以读取方收到的每个条目都是有效的恢复位置。如果读取方处于尚未提交的截除操作已丢弃的条目范围内，它将等待该截除操作的写入。恢复读取时，应传入最近一次事件的纪元，以及最近收到的条目之后的位置。

`Sealed` 用于区分日志已结束与激活实例已停止。激活实例停止时，流不会收到此事件便结束。

<a id="stream-drafts"></a>

## 流式传输草稿

<xref:Orleans.Durable.Journaling.IDurableLog`1.BeginDraft*> 打开一个 <xref:Orleans.Durable.Journaling.IDurableLogDraft`1>：

- `Write` 将尚未持久化的项立即传递给包含草稿的流：向 `ReadAsync` 传入 `includeDrafts: true`。草稿仅在打开期间将其项保存在内存中。
- `Commit` 关闭草稿，并追加零个或多个条目以替换草稿中的项。这些条目组成一个日志记录，因此一次写入即可将它们全部持久化；如果有条目无法编码，则保持草稿打开且不追加任何条目。读取方会收到 `DraftCommitted`，其 `Sequence` 和 `Count` 指示已提交条目的范围，随后收到这些条目。不带任何条目的 `Commit` 会正常关闭草稿。
- `Abandon` 关闭草稿而不追加条目，并报告带有所有者 `Message` 的 `DraftAbandoned` 事件。释放尚未关闭的草稿会放弃它，因此失败的操作（例如在响应传输中途失败的模型输出流）会释放其草稿。

流在已提交条目完成持久化后报告 `DraftCommitted`。如果包含草稿的流在草稿尚未关闭时启动，它会从位置零开始接收草稿中的项。草稿关闭后，其项会被释放；尚未读完全部项的读取方接下来会收到 `DraftCommitted` 或 `DraftAbandoned`，而不会再收到剩余项。已提交的条目包含最终内容。

`DraftAbandoned` 携带一个 <xref:Orleans.Durable.Journaling.LogDraftAbandonReason>：

| 原因 | 说明 |
| --- | --- |
| `Abandoned` | 所有者调用了 `Abandon`。 |
| `Disposed` | 所有者释放了尚未关闭的草稿。 |
| `JournalDeleted` | 该日志的底层持久化日志已删除。 |
| `Sealed` | 草稿尚未关闭时，日志已被封存。 |
| `PreviousActivation` | 另一个激活实例打开了该草稿，日志没有保留它的提交记录，因此其内容已丢失。 |
| `Unknown` | 当前激活实例既未保留该草稿的提交记录，也未记录其关闭原因。 |

删除底层日志或封存日志时，日志会关闭草稿。此时所有者持有的是已关闭的草稿，对其调用 `Write` 和 `Commit` 会引发 <xref:System.InvalidOperationException>，并说明关闭原因。提交可能被日志关闭的草稿之前，应检查 `IsOpen`。

每个 <xref:Orleans.Durable.Journaling.LogDraftId> 都包含打开该草稿的日志实例标识符，因而也标识了对应的激活实例。重新连接的读取方传入其位置以及之前正在查看的草稿。如果该草稿仍然打开，流会从位置零重新传递其中的项。日志将草稿标识符与其已提交条目一起记录，因此如果草稿已经提交，只要日志仍保留这些条目中的第一项，无论在哪个激活实例中，流都会报告 `DraftCommitted`，并附带替换该草稿的条目范围。如果当前激活实例放弃了草稿，流会报告 `DraftAbandoned` 及其原因；每个日志记录最近放弃的 16 个草稿的原因，对于更早的草稿则报告 `Unknown`。由先前的激活实例打开且从未提交的草稿会被报告为 `PreviousActivation`；应从最后一个已提交条目重新生成其内容。

此客户端呈现已提交的历史和正在进行的草稿，并跟踪恢复读取的位置：

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="conversation_client":::

<a id="failure-and-shutdown"></a>

## 故障与关闭

日志写入失败会隔离管理器并请求停用 Grain。随后观察流会因 <xref:System.InvalidOperationException> 而失败，其内部异常为存储故障，读取方不会收到此次失败写入所包含的更改。读取方重新连接到新的激活实例，由该实例恢复持久化状态。管理器正常停止时，流正常完成。取消流的令牌会终止该流。

删除 Grain 的日志会向值观察流传递默认值，或向已观察过条目的日志读取方传递 `Reset`，同时放弃尚未关闭的草稿。如果某个读取方正在跟踪草稿，而删除操作丢弃了该草稿的已提交条目，则该读取方会收到带有 `JournalDeleted` 原因的 `DraftAbandoned`。

<a id="test-code-which-uses-a-log"></a>

## 测试使用日志的代码

应使用真实的日志化状态管理器测试使用持久化状态的 Grain。配置了易失性日志存储的 <xref:Orleans.TestingHost.InProcessTestCluster> 将日志保留在 Silo 内存中，<xref:Orleans.TestingHost.InProcessTestCluster.DeactivateAsync*> 可验证在新的激活实例中的恢复过程。可按照生产 Silo 的方式配置日志格式，也可像此测试一样使用基于反射的 JSON 元数据：

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingObservationSnippets.cs" id="journaling_test_cluster":::

将接受 `IDurableLog<T>` 的代码托管在测试 Grain 中，以同样的方式进行测试。手写的 `IDurableLog<T>` 替代实现无法复现持久性、草稿或读取流的行为。
