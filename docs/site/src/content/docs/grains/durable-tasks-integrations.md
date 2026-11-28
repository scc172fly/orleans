---
title: 将持久化任务与其他系统集成
description: 使用幂等键从持久化任务调用支付提供程序、数据库和 HTTP 服务，将持久化任务作为 HTTP 操作提供服务，将 Webhook 作为信号接收，并选择暂时性故障的重试位置。
ms.date: 10/17/2026
ms.topic: how-to
---

<a id="integrate-durable-tasks-with-other-systems"></a>

# 将持久化任务与其他系统集成

本文的示例将持久化任务与 Orleans 外部的系统连接起来，包括支付提供程序、
数据库、提供长时间运行操作的 HTTP 服务、启动持久化任务的 HTTP 客户端，
以及用于返回处理结果的 Webhook。
每次外部调用都在一个步骤中执行，记录其结果，并发送从任务派生的幂等键，
因为结果未提交的步骤会再次运行。
外部客户端使用接口表示，因此这些示例无需提供程序的 SDK 即可编译。
[持久化任务中的幂等性](durable-tasks-idempotency.md) 解释了
这些示例所依赖的保证。

<a id="choose-where-to-retry"></a>

## 选择重试位置

故障可分为两类：*暂时性*故障，例如超时、速率限制或服务中断，
可以在后续尝试中恢复；以及*永久性*故障，例如银行卡支付被拒绝、验证错误
或冲突，每次尝试都会产生相同的结果。应根据暂时性故障的持续时间选择重试层级，
并将永久性故障作为业务结果处理：

| 故障 | 重试位置 | 方式 |
| --- | --- | --- |
| Grain 调用或持久化任务启动请求无法投递或处理 | 消息传递层 | 自动重试，直至 `OutboxRetryTimeout` 到期（默认 6 小时），随后抛出 `DurableTaskDeliveryException`。工作流不重试。 |
| 短暂的外部故障，持续数秒 | 步骤内部 | 在调用外围使用循环或弹性管道，例如 Polly 或 `IDurableHttpClient` 的弹性处理程序，每次尝试使用同一幂等键。 |
| 外部服务中断，持续数分钟至数小时 | 跨步骤 | 使用步骤循环和 `DurableTask.Delay` 实现退避，跨停用保持等待进度，并由逻辑时间中的截止时限限定。 |
| 拒绝，例如支付被拒绝、冲突或验证错误 | 不重试 | 将其作为步骤的返回值，或捕获相应异常，再执行补偿或报告错误。 |

消息传递层仅将明确的拒绝视为永久性故障：如果启动请求的任务 ID 已标识另一个方法，
则以 `DurableTaskConflictException` 失败；若目标使用自己的错误码拒绝请求，
则以 `DurableTaskRejectedException` 失败。

<a id="call-a-payment-api-with-an-idempotency-key"></a>

## 使用幂等键调用支付 API

**任务：** 通过按以下幂等键对请求去重的提供程序向客户收费：
`Idempotency-Key`，例如 Stripe 支付服务。

提供程序客户端通过异常报告支付被拒绝和暂时性不可用：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_payment_provider" language="csharp":::

步骤将工作流需要处理的结果转换为一个值，因此步骤记录的结果能够告诉重放过程
实际发生了哪种结果：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_charge_outcome" language="csharp":::

键由 Grain 标识、任务 ID 和操作名称组成，这些值在每次重放和尝试中
都保持不变：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_idempotency_key" language="csharp":::

工作流在 `DurableTask.Run` 步骤中收费；发生短暂不可用时，
在该步骤内部重试两次：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_charge_in_step" language="csharp":::

- 步骤接收 `DurableTask.Current.CancellationToken`，工作流的任务取消
  和该步骤的执行尝试取消都会使此令牌被取消。将其传给
  客户端以及两次尝试之间的延迟操作。
- 每次尝试发送同一键，因此提供程序只收费一次。如果 Silo 在提供程序完成收费后、
  步骤结果提交前发生故障，则下一次运行再次发送
  同一键，提供程序返回原来的收费结果。
- 银行卡支付被拒绝时返回一个值。步骤完成，工作流在每次重放时都根据
  该拒绝结果继续运行。
- 弹性管道（例如 Polly `ResiliencePipeline`）可以同样的方式替换委托内部的循环：
  在步骤运行期间进行重试，使用同一幂等键以及
  步骤的取消令牌。

注意事项：

- 在步骤的重试循环之外计算键，使用在重放期间保持稳定的值。
  在任务体中使用 `Guid.NewGuid()` 生成键，会使键在每次重放时发生变化；参见
  [ORLEANS0201](../diagnostics/orleans0201.md)。
- 保持步骤内部的重试时间较短。步骤在重试期间保持执行实例运行，而
  停用会取消该步骤的执行尝试，下一次运行会从第一次
  重试开始。

**验证：** Grain 将 `charge` 列为 `Completed`，并记录收费 ID；
提供程序仪表板显示该键只有一笔收费，所有重复请求均标记为
幂等重放。

<a id="retry-across-steps-with-durable-backoff"></a>

## 使用持久化退避跨步骤重试

**任务：** 当支付提供程序中断数小时时持续尝试收费，
无需让 Grain 始终保持激活状态。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_charge_with_backoff" language="csharp":::

每次尝试都是一个步骤：循环将它们命名为 `charge`、`charge$2` 等，并将延迟命名为
`charge-backoff`、`charge-backoff$2`。报告服务不可用的步骤会以该结果完成，
工作流根据提供程序的 `Retry-After` 进行持久化
延迟，Grain 可以在等待期间停用。重放会返回各次已记录的
尝试结果，并恢复尚未完成的延迟。截止时间根据逻辑
时间计算，因此每次重放计算出的截止时间相同。

注意事项：

- 使用截止时间或尝试次数限制重试，并确定达到限制时工作流的
  处理方式，例如执行补偿或报告失败。
- 每次尝试和延迟都在任务树中添加一条记录，直至删除该任务树。
  如果工作流以较短间隔重试数天，应使用更长的间隔或一个分离式
  后继任务。

**验证：** 提供程序不可用期间，Grain 将各次尝试列为 `Completed`，
其结果为不可用，并将最近一次 `charge-backoff` 列为 `Delayed`，直至其到期
时间。

<a id="write-to-a-database"></a>

## 写入数据库

**任务：** 在关系数据库中只记录一次发货，并根据数据库当前内容
作出决策。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_database" language="csharp":::

- **读取在步骤中执行。** 工作流根据 `find` 步骤记录的行进行决策，
  因此即使数据表之后发生变化，重放仍选择相同的分支。
- **写入以操作 ID 为键。** 使用操作 ID 的唯一约束执行 upsert 操作，
例如 `INSERT ... ON CONFLICT (operation_id) DO NOTHING`，可以使重复写入
  只生效一次。不支持 upsert 的存储系统应将操作 ID 和变更记录在同一个事务中，
  并在应用变更前检查操作 ID。
- **每个事务使用一个步骤。** 提交多项变更的事务在同一个步骤中运行，
  使变更及其操作 ID 一起提交；步骤的记录
  告诉工作流这些变更已经提交。

<a id="deliver-database-changes-to-grains"></a>

### 将数据库变更投递至 Grain

如果服务提交数据库变更后必须为该变更启动工作流，则在同一个事务中
向发件箱表写入一行记录。中继读取该表，并为每行启动一个
持久化任务，以该行标识命名：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_outbox_relay" language="csharp":::

该行的 ID 使启动具有幂等性：如果中继在将记录标为已发送前崩溃，
则再次启动该行对应的任务时会访问已存在的任务。接收 Grain 的
任务状态即收件箱。应将 `ResultRetentionPeriod` 设置得长于
同一行首次和最后一次中继之间的最长间隔。

**验证：** 数据库中每个操作 ID 对应一行，重复中继同一发件箱记录
两次后，Grain 中仍只有一个名为 `outbox-{id}` 的任务。

<a id="call-http-apis-from-durable-tasks"></a>

## 从持久化任务调用 HTTP API

**任务：** 向将标签生成作为长时间运行操作处理的服务提交请求，等待
标签，并在工作流被取消时取消该作业。

`Microsoft.Orleans.Durable.Http` 包中的 `IDurableHttpClient` 将 HTTP
请求和操作作为调用任务的步骤运行。在 Silo 中注册命名客户端，
通过 `HttpClient` 的构建器配置 HTTP 客户端，该构建器由 `AddDurableHttpClient`
返回；再将该客户端作为键控服务注入：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_http" language="csharp":::

- **幂等键来自步骤标识。** 每个步骤中使用非安全方法的请求都携带
  `Idempotency-Key`，由服务、Grain、步骤的任务 ID 和
  任务的运行世代标识 `DurableTaskContext.Incarnation` 派生，因此步骤在
  故障或停用之后再次运行时，会访问第一次尝试启动的作业，而同一任务 ID 的后续
  运行会发送新的键。幂等键处理程序位于构建器添加的处理程序之前，
  例如位于弹性处理程序之前，因此重试也携带同一键。
- **响应会被记录。** 重放读取每个步骤已记录的响应，
  而不重新发送请求。
- **操作遵循长时间运行操作约定。** `202 Accepted` 响应
  通过 `Operation-Location`、`Azure-AsyncOperation` 或
  `Location` 指定操作的位置。`DurableHttpWaitMode.Events` 在一个步骤中读取状态监视器的服务器发送事件，
  而 `DurableHttpWaitMode.Poll` 根据
  `Retry-After` 声明的间隔进行轮询（受任务的轮询间隔限制），轮询之间使用持久化延迟，
  因此长时间等待不会使 Grain 始终保持激活。`WaitAsync` 使用逻辑时间的
  截止时限限定等待。操作失败或被取消时抛出
  `DurableHttpOperationException`。
- **将取消传递给远程操作。** `CancelOperationAsync` 发送
  `POST {status monitor}:cancel`，携带任务的取消原因或截止时间的取消原因，
  并使用取消屏蔽使其在工作流被取消后仍可运行。`SignalOperationAsync` 以同样的方式传递
  信号。

使用 `DurableTask.Run` 自行发送请求的步骤也可以从
`HttpClient` 获得相同的键，该客户端由 `AddDurableIdempotencyKey` 配置。有关所有方法和选项，
参见[包 README](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable.Http/README.md)。

**验证：** 无论步骤的请求重试多少次，标签服务针对每个步骤标识
只接收一个作业；Grain 列出启动及等待步骤，每个步骤均为
`Completed`，并包含已记录的响应。

<a id="serve-durable-tasks-as-http-operations"></a>

## 将持久化任务作为 HTTP 操作提供服务

**任务：** 让 HTTP 客户端将退货作为长时间运行操作启动，安全地重试
请求、跟踪其进度、取消操作并传递检验结果。

`WithDurableOperations` 使路由组内每个处理程序返回持久化 Grain 调用的端点
启动执行该调用的操作：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_http_operations" language="csharp":::

- **`Idempotency-Key` 为操作命名。** 每次请求重试均到达
  首次尝试所启动的操作；不含键的启动请求收到
  `400 Bad Request`。操作键由组的作用域、路由参数值、
  调用方的授权作用域和标识符组成，因此调用方只能访问自身的
  操作，并且调用作为使用该键命名的根任务运行。如果某个键
  已被另一请求用于启动不同的工作（调用其他方法或另一个
  Grain），则返回 `422 Unprocessable Content`；即使重试参数不同，仍访问同一
  操作。
- **状态监视器报告进度。** 操作完成时，启动请求返回 `200 OK` 和结果，
  包括在 `Prefer: wait=N` 请求的等待时限内完成的情况；
  否则返回 `202 Accepted` 及 `Operation-Location` 响应头，即
  `GET {group}/operations/{id}` 的 URL。该状态监视器报告 `Running`、`Succeeded`、
  `Failed` 或 `Canceled`，以及结果或错误，并遵循 `Prefer: wait=N`。
- **通过事件流传输状态。** `GET {group}/operations/{id}/events` 以服务器发送事件的形式传输状态
  监视器，直到操作进入终态，因此浏览器的
  `EventSource` 无需轮询即可跟踪状态。
- **客户端取消任务并向任务发送信号。** `POST {group}/operations/{id}:cancel` 请求
  持久化取消并携带原因；`POST {group}/operations/{id}/signals/{name}`
  传递由 `MapDurableSignal` 映射的信号。操作宿主在其日志中记录
执行操作工作的任务，因此取消请求和信号均可到达该任务，
而不受提供服务的激活实例影响。
- **授权由应用负责。** 路由组上的策略和筛选器覆盖其
  启动端点以及提供操作服务的路由。

操作宿主保留已完成的操作，因此在以下保留期内会根据操作键去重：
`ResultRetentionPeriod`。[包 README](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable.Http/README.md) 描述了所有响应、
Nexus 协议，以及向 Webhook URL 交付操作结果的回调。

**验证：** 重复发送 `POST` 请求并使用同一 `Idempotency-Key`，会返回相同的
`Operation-Location`，状态监视器报告操作的结果，并且
事件流以终态结束。

<a id="receive-webhooks-and-callbacks-as-signals"></a>

## 将 Webhook 和回调作为信号接收

**任务：** 等待支付提供程序发送确认收费的 Webhook。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_webhook" language="csharp":::

Web 端点验证 Webhook 签名，并调用普通 Grain 方法，
由该方法通过 `TrySignalAsync` 将确认结果作为信号传递。对于作为 HTTP 操作运行的任务，
`MapDurableSignal` 提供相同的信号投递，路由为
`POST {group}/operations/{id}/signals/{name}`。任务记录信号的
第一个值，因此：

- 在工作流开始等待之前到达的 Webhook 会被记录，等待操作会立即返回
  该值。
- 提供程序再次发送同一事件时收到 `Duplicate`，端点
  以成功状态码确认，因此提供程序停止重试。
- 同一信号对应不同值时返回 `Conflict`，端点报告并
  记录该结果。
- `TryWaitAsync` 使用持久化截止时限限制等待。

如果 Webhook 针对同一个任务报告多个事件，则为每个事件使用一个信号名称，通过
`DurableSignal<T>.For` 派生，例如每个收费 ID 使用一个信号。

**验证：** 首次投递返回 `Accepted`，重复投递返回
`Duplicate`，工作流以确认结果完成。

<a id="handle-rejections-without-retrying"></a>

## 处理拒绝而不重试

**任务：** 使用业务 ID 启动后续调查，并处理无法成功的
启动请求。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_rejections" language="csharp":::

- `DurableTaskConflictException` 报告该 ID 已标识另一个方法。使用相同 ID 的每次
  重试都会遇到相同的冲突，因此工作流应解决冲突，
  例如读取现有任务，或者选择另一个 ID。
- `DurableTaskRejectedException` 报告目标使用自己的错误码拒绝了请求，该错误码位于
  `Code` 中。
- `DurableTaskDeliveryException` 报告消息传递层未能在
  `OutboxRetryTimeout` 内投递的启动请求。工作流将其视为超过
  截止时限的服务中断。
- 应用层结果（例如银行卡支付被拒绝或订单商品缺货）是由
  步骤及 Grain 方法返回的值，因此工作流应根据结果选择分支，而非重试。

**验证：** 使用同一 ID、同一方法但不同参数再次启动，会到达第一个任务；
使用同一 ID 启动另一个方法时会抛出
`DurableTaskConflictException`，目标的死信会记录
`durable-rpc/conflict` 错误码；参见[死信](durable-tasks-operations.md#dead-letters)。
