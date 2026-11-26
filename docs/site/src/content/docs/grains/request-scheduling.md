---
title: "Grain 请求申请时间安排"
description: "理解 Orleans 转手执行,中继和再生"
ms.date: 10/15/2026
ms.topic: concept-article
---

<a id="grain-request-scheduling"></a>

# Grain 请求申请时间安排

每个启动 grain 一次执行一个转折。 Orleans 将请求运行到完成或达到不完整的 `await`,然后将继续时间安排在另一个转弯上。两个转弯从不同时执行同一激活。

默认情况下, 当当前请求不完整时激活不会启动不同的请求。 这种非输入模式使得变异 grain 状态更容易解释:

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="serialized_counter_grain":::
虽然方法在 `await`时的产量,但在该请求完成之前,另一项请求没有观察到或改变 `_value` 。

<a id="avoid-blocking"></a>

## 避免屏蔽

永远不要同步地阻断从 grain 代码中输入的不完整任务。 `.Result`、 `.Wait()`、 `WaitAll`和 `GetAwaiter().GetResult()` 可以使激活陷入僵局,并消耗线串线索。使用 `await`。

普通普通 `await` 恢复 grain 激活调度器上的代码。 不要使用 `ConfigureAwait(false)` 内 grain 方法,因为继续可以避免 grain 普通目的图书馆可在内部使用; grain 返回到 grain 当它通常等待图书馆时, 排程器 。

<a id="interleaving-and-reentrancy"></a>

## 间断和再留空

插入 Orleans 允许在先前的请求不完整时开始或恢复另一项请求。激活仍为单行,但从不同的请求中转转可以换行。每 `await`后重新检查关于变异状态的假设。

| 机制机制机制 | 范围范围范围 |
|---|---|
| <xref:Orleans.Concurrency.ReentrantAttribute> | 所有申请 grain 级的申请均可中断。 |
| <xref:Orleans.Concurrency.AlwaysInterleaveAttribute> | 标记的接口方法可以与任何请求发生间断。 |
| <xref:Orleans.Concurrency.ReadOnlyAttribute> | 标记的只读方法可以与其他只读的方法相互交叉。 |
| <xref:Orleans.Concurrency.MayInterleaveAttribute> | 每项请求都有一个上游检查。 |
| <xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy> | 范围明确的呼叫链可以重新激活 已经进入了该链条 |

使用最狭窄的机制解决问题。 耐力可以改善I/ O- Heavy grains 的吞吐量,防止周期性呼叫僵局,但也允许国家改变两轮之间的交替。

<a id="reentrant-grains"></a>

### 重新ENTRant grains

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="reentrant_catalog_grain":::
不同的请求代码并非同时运行, 但多个不完整的电话可以通过交替转动来取得进展。

<a id="method-level-interleaving"></a>

### 方法层次间距

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="interleaving_method_attributes":::
`ReadOnly` 是一个列表承诺。主动的只读请求可以相互串连,在每次只读的请求完成之前,集体排除可写请求,除非另一项政策允许重叠。保持 grain 与只读方法的状态不变。

<a id="predicate-based-interleaving"></a>

### 基于预发性连接

`MayInterleave` 指接受 <xref:Orleans.Serialization.Invocation.IInvokable>的上游。使用其访问器方法;不存在 `Arguments` 属性:

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="may_interleave_grain":::
保持上游的确定性,快速和副作用自由。

<a id="call-chain-reentrancy"></a>

### 呼叫链回转

当已知的呼叫路径必须回拨启动激活时使用呼叫链中继器 :

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="call_chain_reentrancy":::
范围允许在处置之前与该呼叫链相关的回调,但比标记整个 grain 重新输入线窄。

<a id="timers-and-external-work"></a>

## 计时器和外部工作

Grain 计时器回调会参与日程安排。回调不会默认中断; 当故意时配置 `GrainTimerCreationOptions.Interleave` 。

要在激活的日程安排下从另一线索运行工作,请见 [将背景工作作为 grain 请求运行](#run-background-work-as-a-grain-request)。

仅使用 <xref:System.Threading.Tasks.Task.Run*> 来从 Orleans 排程器中分离出不可避免的同步阻塞或 CPU 工作。 不要从线状资源库代表处访问 grain 状态。在激活调度器上恢复执行后,等待并更新 grain 状态。 见 [外部任务和 grains](external-tasks-and-grains.md)。

<a id="run-background-work-as-a-grain-request"></a>

## 将背景工作作为 grain 请求运行

<xref:Orleans.GrainBaseExtensions.RunRequestAsync*> 激活时作为请求再回调。从任何线索,例如流、信息或图书馆回调中调用它来应用结果 grain 与表上所列日期相同 grain 电话:

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="run_request_from_callback":::
默认 <xref:Orleans.Runtime.LocalRequestOptions>,请求:

- 跟随 grain重新恢复规则及当前对未恢复请求的后排 grain.
- 通过 grain 呼叫过滤器,这些过滤器将它视为没有参数的 `IGrainRequestInvoker.RunRequestAsync` 。
- 启动一个新的呼叫链。调用线上的 <xref:Orleans.Runtime.RequestContext> 值不会流入回拨中。
- 恢复激活的闲置收集期
- 绕过 <xref:Orleans.Configuration.SiloMessagingOptions.MaxEnqueuedRequestsHardLimit>。

回调可以同步,如此示例中那样,或者返回一个 `Task` 或 `Task<TResult>`。返回的任务随着回调的结果或例外而完成。回调收到传递到 `RunRequestAsync`的取消牌,并在请求被排队时取消。如果在请求运行之前启动停止操作,任务与 <xref:Orleans.Runtime.OrleansMessageRejectionException>有错。

过 grain 其他数值的数值,以及 `state` 参数,并使用 a `static` 羊羔,所以电话不能分配结束时间 grain,等待着在相同的启动僵局上提出的另一项请求的返回任务,因为排队的请求等待当前完成。

<a id="configure-the-request"></a>

### 配置请求

传送 <xref:Orleans.Runtime.LocalRequestOptions> 以更改请求的日程安排。 将常用背景工作设为 <xref:Orleans.Runtime.LocalRequestOptions.KeepAlive> `false` , 以免在内存中保持一个否则闲置的激活。 设置 <xref:Orleans.Runtime.LocalRequestOptions.Interleave> 到 `true` , 以便与其他 `[AlwaysInterleave]` 方法一样, 在其它请求的交替之间运行请求 :

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="run_request_options":::
<a id="deliver-a-custom-request"></a>

### 提交海关请求

<xref:Orleans.Runtime.IGrainContext.ReceiveLocalRequest*> 向激活发送了 <xref:Orleans.Runtime.ILocalRequest> ,与 `RunRequestAsync`相同的时间表。调用过滤器、日志和跟踪观察请求方自己的界面名称、方法名称和参数,因此自定义的请求可以描述工作内容。启动请求传递请求方对 <xref:Orleans.Runtime.ILocalRequest.Complete*>的答复,或者如果启动拒绝请求则作为例外。请求书向其观察员报告结果,包括任何失败:

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="receive_local_request":::
