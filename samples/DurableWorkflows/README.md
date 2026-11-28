<a id="durable-orleans-workflows"></a>

# Orleans 持久化工作流

本示例将工作流作为 Orleans 持久化任务运行，即返回 `DurableTask<T>` 的 Grain 方法；
`Microsoft.Orleans.Durable` 运行时将这些任务记录在各 Grain 的日志中，并在停用或 Silo 故障后恢复执行。
示例演示：

- 使用稳定的根任务 ID 和子任务 ID 执行持久化 Grain 调用；
- 通过具名 `DurableTask.WhenAll` 执行扇出与扇入，其分支为 `branches/0`、`branches/1`，依此类推；
- 使用 `DurableSignal<T>` 及 `TryWaitAsync` 提供的截止时间实现人工审批；
- 具有持久化原因的协作式取消，以及在受取消屏蔽的子任务（`.Shielded()`）中执行内联补偿；
- 使用 `WhenAllCompleted` 确定并发步骤结果，并仅补偿成功步骤的订单 Saga；
- 以操作 ID 为键的幂等效果；
- [支付及邮件效果协议](ExternalEffects.md)，包括提供程序键、持久化重试策略、对账和经授权的后续操作；
- 在另一个服务副本上恢复进行中的审批工作流；
- 通过根 ID 观察保留的结果。

Aspire AppHost 启动两个服务副本、用于 Orleans 成员资格的 Redis，以及用于
Journaling 和 Durable Jobs 的持久化 Azurite Blob Storage。服务公开健康检查和 HTTP 终结点，而不使用固定端口。

<a id="run"></a>

## 运行

```shell
aspire run --project DurableWorkflows.AppHost
```

打开 Aspire 仪表板显示的服务终结点。以下为请求示例，其中 `$service` 为该终结点：

```shell
curl -X POST "$service/workflows/basic/basic-1?input=hello"
curl -X POST "$service/workflows/fan-out/fan-1" -H "Content-Type: application/json" -d '["one","two","three"]'
curl -i -X POST "$service/workflows/approval/approval-1?subject=production"
curl "$service/workflows/approval/approval-1/status"
curl -X PUT "$service/workflows/approval/approval-1" -H "Content-Type: application/json" -d '{"subject":"production","approved":true,"reason":"reviewed"}'
curl "$service/workflows/approval/approval-1/status"
curl -i -X POST "$service/workflows/cancellation/hold-1"
curl "$service/workflows/cancellation/hold-1/status"
curl -X DELETE "$service/workflows/cancellation/hold-1?reason=operator-request"
curl "$service/workflows/cancellation/hold-1/status"
curl -X POST "$service/workflows/orders/order-1?failShipping=true"
```

<a id="workflows"></a>

## 工作流

`WorkflowGrain` 托管所有工作流。服务在由工作流 ID 派生的根 ID 下调度各工作流，
例如 `approval-approval-1`；工作流使用 `WithId` 等待各子任务，因此子任务具有如下 ID：
`approval-approval-1/decision`。每次运行中，一个工作流 ID 标识一个工作流：使用该 ID 重复请求会访问
现有工作流并返回其状态或结果，不受请求参数（例如不同的输入、
项目列表、主题或配送选项）影响。运行时通过根 ID 和所运行的方法标识已调度任务，
而非通过其参数。

- **基础及扇出。** 工作流每个步骤调用 `ActivityGrain` 一次。扇出工作流按项目顺序返回各分支的
  结果，包括记录该结果的任务 ID，例如 `fanout-fan-1/branches/2`。
- **审批。** 工作流等待 `approval-decided` 信号，截止时间是按其
  持久化时钟计算的启动后三天。`PUT` 在工作流未运行时启动它，并通过
  `TrySignalDurableTaskAsync` 将决定作为信号发送给根任务。任务记录首个决定：重复该决定会成功，不同的决定或
  截止时间后的决定返回 `409 Conflict`。无论 `PUT` 指定何种主题，都会访问正在运行的工作流，因此
  结果报告工作流启动时的主题。
- **取消。** 工作流预留库存并保留一天。`DELETE` 在工作流尚未
  运行时启动它，并请求带有原因的持久化取消。运行时将首次请求及原因与
  任务一起记录，因此重放可以观察到它们。工作流使用 `WhenAllCompleted` 确定预留结果，
  捕获取消，并在受取消屏蔽的子任务 `.Shielded()` 中释放已成功的预留。它以
  `canceled: true` 和取消原因完成。
- **订单 Saga。** 工作流通过 `WhenAllCompleted` 并发预留库存及扣款，随后发货。
  发货失败时，通过具名 `WhenAll` 对成功步骤执行退款和释放操作。库存、支付和
  活动 Grain 按操作 ID 记录每个效果，重复操作时返回已记录效果，因此重放
  或重复执行 Saga 时，每个效果仅应用一次。

审批和取消命令返回 `202 Accepted`，并带有指向 GET 状态资源的 `Location` 标头。
状态响应包含根任务 ID，以及 `pending`、`succeeded`、`canceled` 或 `failed` 之一。成功
响应包含工作流结果；已取消及失败响应包含安全的摘要。客户端在发送命令后轮询状态
资源，而不是重复发送命令。

```json
{
  "taskId": "approval-approval-1",
  "status": "pending",
  "result": null,
  "error": null
}
```

GET 在不调度任务的情况下轮询保留的任务；对于从未启动或
保留结果已过期的工作流，返回 `404 Not Found`。工作流失败时，仍返回 `200 OK` 状态响应，包含 `status: "failed"`。

若要手动触发故障转移，先启动审批工作流，在 Aspire 仪表板中停止其活动的 `service` 副本，然后
提交决定。测试套件在工作流等待期间终止拥有它的 Silo，并通过原任务句柄观察它在
另一 Silo 上的完成情况。

<a id="guarantees-and-operational-boundaries"></a>

## 保证及运行边界

- Journaling 将各持久化任务的状态、其 Orleans Messaging 收件箱及发件箱的变更，以及 Grain 的持久化
  集合一同提交。各副本使用相同的 Journaling 和 Durable Jobs 存储。
- 运行时从头重放未完成的工作流，已完成的子任务返回其已记录结果。
  子任务之间的代码在每次重放中计算出相同的值；它从
  `DurableTask.Current.UtcNow` 读取时间。
- 客户端调度的任务（本示例的每个工作流均属此类）完成后，将保留
  `DurableTaskOptions.ResultRetentionPeriod` 指定的时间（此处为 24 小时）。在此期间，调度其根
  ID 会返回已记录结果；此后，轮询返回 `404`，调度相同的根 ID 会再次运行
  工作流。由另一个 Grain 调用的任务，在该 Grain 接受
  结果后，会与其执行树一起删除。
- 本示例的应用效果通过操作 ID 保证幂等性，独立于任务保留期。Orleans Journaling 和 Messaging
  之外的效果需要自身的幂等机制。
- 监控持久化任务诊断及 Messaging 死信，将死信和重试耗尽视为
  运维人员可见的故障。

<a id="package-availability"></a>

## 包可用性

`Microsoft.Orleans.Durable`、`Microsoft.Orleans.Durable.Abstractions` 和 `Microsoft.Orleans.Durable.AzureStorage`
为 Alpha 预发行包。`samples/Build-Samples.ps1` 将当前源码打包到本地包源，并使用
该确切版本构建本示例。在这些包发布前，通过该脚本构建示例。

<a id="test"></a>

## 测试

```shell
dotnet run --project DurableWorkflows.Tests/DurableWorkflows.Tests.csproj
```

测试项目是 Microsoft Testing Platform 应用程序，因此 `dotnet run` 会运行其测试。测试在进程内
Orleans 集群中使用易失性日志存储，无需外部服务。覆盖内容包括
每个工作流、信号锁存及截止时间、取消恢复与补偿、Saga 补偿及
幂等性、跨 Silo 恢复、HTTP 状态资源，以及保留结果过期。
