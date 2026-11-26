---
title: "Orleans中交易"
description: "使用已分配的ACID交易,交易状态 Orleans 。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-transactions"></a>

# Orleans 交易

[`Microsoft.Orleans.Transactions`](https://www.nuget.org/packages/Microsoft.Orleans.Transactions) 软件包提供分布式ACID交易,涉及一个或多个 grain 电话和交易国家记录。交易状态不同于 <xref:Orleans.Runtime.IPersistentState`1>:使用 <xref:Orleans.Transactions.Abstractions.ITransactionalState`1> 用于参与 Orleans 交易的数据。

<a id="enable-transactions"></a>

## 启用交易

启用每个参与 silo 的交易 <xref:Orleans.Hosting.SiloBuilderExtensions.UseTransactions*>:

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="enable_silo_transactions":::
创建或传播交易的外部客户端 Orleans 也必须调用 <xref:Orleans.Hosting.ClientBuilderExtensions.UseTransactions*>:

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="enable_client_transactions":::
没有交易服务的交易电话与 <xref:Orleans.Transactions.OrleansTransactionsDisabledException>发生故障。

<a id="configure-transactional-storage"></a>

## 配置交易存储

交易存储工具 <xref:Orleans.Transactions.Abstractions.ITransactionalStateStorage`1>支持的提供方一揽子计划 `Microsoft.Orleans.Transactions.AzureStorage` 登记册登记 Azure 表格交易储存:

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="configure_transaction_storage":::
如果没有指定交易提供者, Orleans 可以连接交易状态到配置的 <xref:Orleans.Storage.IGrainStorage>。桥梁效率较低,用于发展和兼容性设想方案。

<a id="declare-transaction-behavior"></a>

## 申报交易行为

应用 <xref:Orleans.TransactionAttribute> 到 grain 接口方法 :

| 选项 | 行为行为 |
|---|---|
| <xref:Orleans.TransactionOption.Create> | 总是启动新的交易, 并禁止此电话的任何环境交易 。 |
| <xref:Orleans.TransactionOption.CreateOrJoin> | 加入环境交易,或在不存在时启动。 |
| <xref:Orleans.TransactionOption.Join> | 需要环境交易。 |
| <xref:Orleans.TransactionOption.Suppress> | 执行不包含环境交易。 |
| <xref:Orleans.TransactionOption.Supported> | 环境交易存在但不需要时,接收该交易。 |
| <xref:Orleans.TransactionOption.NotAllowed> | 当被召入交易时失败 。 |

:::code source="snippets/transactions/Abstractions/IAccountGrain.cs" id="transactions_account_grain_interface":::

<xref:Orleans.TransactionOption.Join> outside a transaction and <xref:Orleans.TransactionOption.NotAllowed> inside a transaction throw <xref:System.NotSupportedException>.

<a id="read-only-transactions"></a>

### 只读交易

对不执行交易状态更新的交易方法应用 <xref:Orleans.Concurrency.ReadOnlyAttribute> :

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="transactional_read":::
Orleans 开始只读交易, 并使用一个缩放的提交路径。 以只读的交易中止为尝试的写入会与 <xref:Orleans.Transactions.OrleansReadOnlyViolatedException>发生中断。 <xref:Orleans.TransactionAttribute.ReadOnly> 属性已经过时; 使用 <xref:Orleans.Concurrency.ReadOnlyAttribute>。

<a id="exclusive-locks"></a>

### 独家锁

交易状态通常允许同时读取并升级为交易写作时的独家锁。 竞争升级可以在争议下中止。 请使用 <xref:Orleans.UseExclusiveLockAttribute> 获取独家锁, 其内容如下:

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="exclusive_transactional_read":::
这样可以避免以较低读取通货价格为代价的锁定冲突。 使用它来进行阅读后可能写作的方法或测量争议热点,而不是作为一揽子违约。

<a id="access-transactional-state"></a>

## 进入交易状态

使用 <xref:Orleans.Transactions.Abstractions.TransactionalStateAttribute>注射命名状态表 :

:::code source="snippets/transactions/Grains/AccountGrain.cs" id="transactions_account_grain":::

通过 <xref:Orleans.Transactions.Abstractions.ITransactionalState`1.PerformRead*> 阅读并通过 <xref:Orleans.Transactions.Abstractions.ITransactionalState`1.PerformUpdate*>更新。代表是同步的,因为 Orleans 控制了状态快照的读、承诺或丢弃。不要保留所提供的状态对象或在这些代表之外进行变换。

> [!NOTE]
> 交易 grain 不需要 <xref:Orleans.Concurrency.ReentrantAttribute>。再入是一个独立的时间安排选择。只有当 grain 的实施对断开电话安全且能理解输送效益时,才启用它。

<xref:Orleans.Grain.OnActivateAsync*>期间没有交易状态;交易设置是作为交易请求的一部分发生的。

<a id="start-a-transaction"></a>

## 开始交易

<a id="from-a-grain-call"></a>

### 从 a grain 调

标记 <xref:Orleans.TransactionOption.Create> 或 <xref:Orleans.TransactionOption.CreateOrJoin> 的方法在没有环境交易时开始交易。该方法发出的呼吁根据每个目标方法 <xref:Orleans.TransactionOption>传播上下文。

:::code source="snippets/transactions/Grains/AtmGrain.cs" id="transactions_atm_grain":::

<a id="from-an-external-client"></a>

### 来自外部客户端

解决 <xref:Orleans.ITransactionClient> 并管理一名代表:

:::code source="snippets/transactions/Client/Program.cs" id="transactions_client_program" highlight="11-12,30-31,38-44":::

<xref:Orleans.ITransactionClient.RunTransaction*> 要求代表返回 <xref:System.Threading.Tasks.Task> 和 <xref:System.Threading.Tasks.Task`1>。通用任务表格仅在代表返回 `true`时才承诺;返回 `false` 中止时承诺。超载也接受 `useExclusiveLock`。

<a id="commit-and-abort-semantics"></a>

## 提交和中止语义

启动交易的请求在返回前解决:

- 成功的代表承诺,除非明确返回 `false`。
- 应用程序例外记录失败并中止交易。
- 中止会丢弃交易中所有交易状态更新。
- 原始申请例外可以作为 <xref:Orleans.Transactions.OrleansTransactionException>的内侧例外出现。

<xref:Orleans.Transactions.OrleansTransactionAbortedException> 报告了已知的中止, 如果应用程序命令可以安全重试, 可以重新尝试。 <xref:Orleans.Transactions.OrleansTransactionInDoubtException> 表示协调员无法确定最终结果。 在进行检测后, 不要立即重复非权宜人的命令; 在响应时间窗口结束后使用操作标识符和查询应用程序状态 。

重整交易必须重新尝试整个交易,而不是个人参与者更新。 试图并使用回扣。 高争议、锁式冲突、超负荷和存储中断都可能造成复选风暴。

<a id="contention-and-timeouts"></a>

## 内容和超时

交易国使用读者/作家锁和僵局预防规则。

- 在 <xref:Orleans.Configuration.TransactionalStateOptions.LockAcquireTimeout>之前未获取锁定 。
- 锁定时间超过 <xref:Orleans.Configuration.TransactionalStateOptions.LockTimeout>。
- 一场关禁状态的冲突
- 未在 <xref:Orleans.Configuration.TransactionalStateOptions.PrepareTimeout>之前完成准备。
- 无法提供参与人或交易服务。

默认 <xref:Orleans.Configuration.TransactionalStateOptions> 值为:

| 选项 | 默认默认 |
|---|---|
| <xref:Orleans.Configuration.TransactionalStateOptions.LockTimeout> | 8秒8秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.LockAcquireTimeout> | 10秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.PrepareTimeout> | 20秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.RemoteTransactionPingFrequency> | 60秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.ConfirmationRetryDelay> | 30秒 |
| <xref:Orleans.Configuration.TransactionalStateOptions.MaxLockGroupSize> | 20 |

提交确认使用 <xref:Orleans.Configuration.TransactionalStateOptions.ConfirmationRetryLimit>,其默认为3。 新启动的交易在不附加调试器时使用10秒交易超时。

参与 silos时连贯一致的配置状态选项 :

:::code language="csharp" source="../snippets/compiled/Grains/TransactionSnippets.cs" id="configure_transaction_timeouts":::
更短的超时时间越快失效,但可以在负载高峰期间中止健康的工作。 更长的超时时间保留了锁和资源的时间越长。 在改变违约之前先测量交易期限和争议。

<a id="design-guidance"></a>

## 设计指导设计指南

- 保持交易的短时间, 并避免不相关的远程电话, 同时保持交易锁定 。
- 获得资源,在可行的情况下按稳定的应用级别顺序进行。
- 使用 <xref:Orleans.Concurrency.ReadOnlyAttribute> 来进行真正只读操作。
- 当锁定升级是测量的中止源时,有选择地使用 <xref:Orleans.UseExclusiveLockAttribute> 。
- 使启动命令的天能者成为启动命令的天能者,并包含操作识别符。
- 监测中止、 内疑结果、 锁定超时、 准备超时和存储延迟 单独 。
