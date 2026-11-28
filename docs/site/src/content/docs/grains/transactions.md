---
title: "Orleans 中的事务"
description: "使用 Orleans 事务状态实现分布式 ACID 事务。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-transactions"></a>

# Orleans 事务

[`Microsoft.Orleans.Transactions`](https://www.nuget.org/packages/Microsoft.Orleans.Transactions) 包支持跨一个或多个 Grain 调用及事务状态记录的分布式 ACID 事务。事务状态不同于 <xref:Orleans.Runtime.IPersistentState`1>：需要参与 Orleans 事务的数据应使用 <xref:Orleans.Transactions.Abstractions.ITransactionalState`1>。

<a id="enable-transactions"></a>

## 启用事务

通过 <xref:Orleans.Hosting.SiloBuilderExtensions.UseTransactions*> 在每个参与事务的 Silo 中启用事务：

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="enable_silo_transactions":::
创建或传播事务的外部 Orleans 客户端也必须调用 <xref:Orleans.Hosting.ClientBuilderExtensions.UseTransactions*>：

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="enable_client_transactions":::
如果未配置事务服务，事务调用将因 <xref:Orleans.Transactions.OrleansTransactionsDisabledException> 而失败。

<a id="configure-transactional-storage"></a>

## 配置事务存储

事务存储实现 <xref:Orleans.Transactions.Abstractions.ITransactionalStateStorage`1>。受支持的提供程序包 `Microsoft.Orleans.Transactions.AzureStorage` 用于注册基于 Azure Table 的事务存储：

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="configure_transaction_storage":::
如果不存在对应的具名事务存储提供程序，Orleans 可以将事务状态桥接到已配置的 <xref:Orleans.Storage.IGrainStorage>。桥接机制的效率较低，主要用于开发和兼容性场景。生产环境工作负载应优先使用专用事务存储提供程序。

<a id="declare-transaction-behavior"></a>

## 声明事务行为

将 <xref:Orleans.TransactionAttribute> 应用于 Grain 接口方法：

| 选项 | 行为 |
|---|---|
| <xref:Orleans.TransactionOption.Create> | 始终启动一个新事务，并在本次调用中抑制任何现有环境事务（ambient transaction）。 |
| <xref:Orleans.TransactionOption.CreateOrJoin> | 加入环境事务；如果不存在则启动新事务。 |
| <xref:Orleans.TransactionOption.Join> | 要求存在环境事务。 |
| <xref:Orleans.TransactionOption.Suppress> | 不使用环境事务执行。 |
| <xref:Orleans.TransactionOption.Supported> | 如果存在环境事务，则接收该事务，但不要求必须存在。 |
| <xref:Orleans.TransactionOption.NotAllowed> | 在事务中调用时失败。 |

:::code source="snippets/transactions/Abstractions/IAccountGrain.cs" id="transactions_account_grain_interface":::

在事务之外调用使用 <xref:Orleans.TransactionOption.Join> 的方法，或在事务之内调用使用 <xref:Orleans.TransactionOption.NotAllowed> 的方法，都会抛出 <xref:System.NotSupportedException>。

<a id="read-only-transactions"></a>

### 只读事务

将 <xref:Orleans.Concurrency.ReadOnlyAttribute> 应用于不更新事务状态的事务方法：

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="transactional_read":::
Orleans 会启动只读事务，并且可以采用简化的提交路径。只读事务尝试写入时，将因 <xref:Orleans.Transactions.OrleansReadOnlyViolatedException> 而中止。<xref:Orleans.TransactionAttribute.ReadOnly> 属性已过时，请改用 <xref:Orleans.Concurrency.ReadOnlyAttribute>。

<a id="exclusive-locks"></a>

### 排他锁

事务状态通常允许多个事务并发读取，并在事务需要写入时升级为排他锁。在资源争用时，相互竞争的锁升级可能导致事务中止。应用 <xref:Orleans.UseExclusiveLockAttribute>，可使读取操作也获取排他锁：

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="exclusive_transactional_read":::
这样可以避免锁升级冲突，但会降低读取并发度。适用于读取后很可能执行写入的方法，或经过测量确定的争用热点，不应将其作为普遍的默认设置。

<a id="access-transactional-state"></a>

## 访问事务状态

使用 <xref:Orleans.Transactions.Abstractions.TransactionalStateAttribute> 注入具名状态组件（facet）：

:::code source="snippets/transactions/Grains/AccountGrain.cs" id="transactions_account_grain":::

通过 <xref:Orleans.Transactions.Abstractions.ITransactionalState`1.PerformRead*> 读取状态，通过 <xref:Orleans.Transactions.Abstractions.ITransactionalState`1.PerformUpdate*> 更新状态。这些委托都是同步的，因为 Orleans 控制状态快照的读取、提交和丢弃时机。不要保留传入的状态对象，也不要在这些委托之外修改它。

> [!NOTE]
> 事务 Grain 不需要标记 <xref:Orleans.Concurrency.ReentrantAttribute>。可重入性是独立的调度选择。只有当 Grain 实现可以安全地处理交错调用，并且明确其吞吐量收益时，才启用可重入性。

在 <xref:Orleans.Grain.OnActivateAsync*> 执行期间，事务状态不可用；事务初始化发生在处理事务请求的过程中。

<a id="start-a-transaction"></a>

## 启动事务

<a id="from-a-grain-call"></a>

### 从 Grain 调用启动

标记为 <xref:Orleans.TransactionOption.Create> 或 <xref:Orleans.TransactionOption.CreateOrJoin> 的方法在不存在环境事务时会启动新事务。从该方法发出的调用，根据各目标方法的 <xref:Orleans.TransactionOption> 传播事务上下文。

:::code source="snippets/transactions/Grains/AtmGrain.cs" id="transactions_atm_grain":::

<a id="from-an-external-client"></a>

### 从外部客户端启动

解析 <xref:Orleans.ITransactionClient> 并执行委托：

:::code source="snippets/transactions/Client/Program.cs" id="transactions_client_program" highlight="11-12,30-31,38-44":::

<xref:Orleans.ITransactionClient.RunTransaction*> 支持返回 <xref:System.Threading.Tasks.Task> 和 <xref:System.Threading.Tasks.Task`1> 的委托。对于泛型任务形式，仅当委托返回 `true` 时才提交事务；返回 `false` 时中止事务。另一个重载还接受 `useExclusiveLock`。

<a id="commit-and-abort-semantics"></a>

## 提交与中止语义

启动事务的请求会在返回前确定事务结果：

- 委托成功执行时提交事务，除非显式返回 `false`。
- 应用程序异常会记录失败并中止事务。
- 中止事务会丢弃该事务中的所有事务状态更新。
- 原始应用程序异常可能作为 <xref:Orleans.Transactions.OrleansTransactionException> 的内部异常出现。

<xref:Orleans.Transactions.OrleansTransactionAbortedException> 表示事务已确定中止；如果应用程序命令可以安全重试，则可以重试。<xref:Orleans.Transactions.OrleansTransactionInDoubtException> 表示协调器无法确定最终结果。收到结果不确定的响应后，不要立即重复非幂等命令；应使用操作标识符，并在响应超时时间窗口结束后查询应用程序状态。

重试必须针对整个事务，而不是单独重试某个参与者的更新操作。限制重试次数并使用退避策略，否则高争用、锁升级冲突、过载及存储服务故障都可能造成重试风暴。

<a id="contention-and-timeouts"></a>

## 资源争用与超时

事务状态使用读写锁及死锁预防规则。常见的事务中止原因包括：

- 未能在 <xref:Orleans.Configuration.TransactionalStateOptions.LockAcquireTimeout> 规定的时间内获取锁。
- 持有锁的时间超过 <xref:Orleans.Configuration.TransactionalStateOptions.LockTimeout>。
- 发生锁升级冲突。
- 未能在 <xref:Orleans.Configuration.TransactionalStateOptions.PrepareTimeout> 规定的时间内完成准备阶段。
- 事务参与者或事务服务变得不可用。

<xref:Orleans.Configuration.TransactionalStateOptions> 的默认值如下：

| 选项 | 默认值 |
|---|---|
| <xref:Orleans.Configuration.TransactionalStateOptions.LockTimeout> | 8 秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.LockAcquireTimeout> | 10 秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.PrepareTimeout> | 20 秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.RemoteTransactionPingFrequency> | 60 秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.ConfirmationRetryDelay> | 30 秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.MaxLockGroupSize> | 20 |

提交确认使用 <xref:Orleans.Configuration.TransactionalStateOptions.ConfirmationRetryLimit>，默认值为 3。未附加调试器时，新启动事务的事务超时时间为 10 秒。

在参与事务的各个 Silo 中使用一致的状态选项配置：

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="configure_transaction_timeouts":::
较短的超时时间能让失败更快显现，但在负载高峰期也可能中止本可正常完成的工作。较长的超时时间会使锁及资源被占用更久。修改默认值前，应测量事务持续时间及资源争用情况。

<a id="design-guidance"></a>

## 设计指南

- 尽量缩短事务持续时间，并避免在持有事务锁时执行无关的远程调用。
- 在可行的情况下，按稳定的应用层顺序获取资源。
- 对真正的只读操作使用 <xref:Orleans.Concurrency.ReadOnlyAttribute>。
- 当测量结果表明锁升级是事务中止的原因之一时，有选择地使用 <xref:Orleans.UseExclusiveLockAttribute>。
- 确保发起事务的命令具有幂等性，并包含操作标识符。
- 分别监控事务中止、结果不确定、锁超时、准备阶段超时及存储延迟。
