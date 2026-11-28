<a id="messaging-stock-reservations"></a>

# 消息传递：库存预留

一个独立的 .NET 11 控制台宿主运行一个本地主机 Orleans Silo 和两个
应用 Grain。**订单** Grain 使用应用命令 ID 提交强类型预留请求，
**库存** Grain 返回确定性的强类型接受或拒绝结果。
宿主测试接受、库存不足及无效数量三种命令，
每条命令均处理一次。
易失性日志和内存作业使示例能够独立运行。

<a id="run"></a>

## 运行

**在这些 API 发布之前，使用本地打包的 Orleans 源码进行构建。**
声明的版本遵循仓库示例批准的包版本基准。
仓库构建会使用本地包源中的当前源码包进行替换。

在仓库根目录执行：

```powershell
pwsh .\samples\Build-Samples.ps1 -SkipExternalAssets
# Or run Validate-Samples.ps1 to perform policy checks and the complete build.
dotnet run --project .\samples\Messaging\Messaging.csproj --configuration Release --no-build
```

`Build-Samples.ps1` 使用唯一版本号打包当前 Orleans 源码，并将
`OrleansSamplePackageVersion` 和 `OrleansSamplePackageSource` 传递给示例
解决方案。示例的 NuGet 引用使用与
应用项目相同的包边界。

<a id="copying-the-sample-out"></a>

### 将示例复制到仓库外

`Messaging/` 是完整的可独立复制单元，包括其自身的
`Directory.Packages.props`。这些 API 发布后，将其 Orleans
包版本对齐到该发布版本，然后运行：

```powershell
dotnet run --project .\Messaging.csproj --configuration Release
```

在此之前，独立构建具有明确的未发布包例外规定。
如需现在在仓库外测试副本，先使用
`Build-Samples.ps1` 打包 Orleans，再复制示例文件夹，并且**只替换副本中的**
集中管理的 Orleans 包版本，使其与本地包源生成的版本一致。
使用本地包源和正常的公共依赖项包源还原及构建：

```powershell
dotnet restore .\Messaging.csproj --source <absolute-local-package-feed> --source https://api.nuget.org/v3/index.json
dotnet run --project .\Messaging.csproj --configuration Release --no-restore
```

如果包源提供不同的稳定版和 Alpha 版，请为 Server/Sdk 使用稳定版，
为 Durable 使用 Alpha 版。所有传递性的 Orleans
依赖项都必须来自同一源码构建。仓库的 `samples/Directory.Build.targets` 重写配置不属于
可独立复制单元，因此仅向副本传递 `OrleansSamplePackageVersion`
不会替换其声明的版本。

<a id="how-it-works"></a>

## 工作原理

1. 宿主初始化十件 `trail-shoes`，然后通过订单发件箱使用独立 ID 提交三条命令：
   `orders/order-1042/reserve-stock` 预留两件，
   `reserve-too-much` 预留九件，`reserve-invalid-quantity` 预留 0 件。
2. `StockMessages` 将 `ReserveStock`、`Restock` 及抽象类型
   `ReservationOutcome` 绑定到带版本的主题，两个 Grain 均通过依赖注入获取它。强类型发件箱
   的 `Send` 和 `SendReply` 序列化记录，强类型 `inbox.Register` 处理程序
   接收已解码的记录。
3. `OrderGrain.ReserveAsync` 暂存命令并等待 `WriteStateAsync`；
   此次写入持久化后，发件箱投递命令，并在原始
   绝对投递预算内重试。
4. 库存处理程序在本地计算结果，然后使用
   `SendReply` 发送回复、更新库存并调用 `context.Complete()`，这些操作在同一个同步
   代码块中执行，因此回复、库存及收件箱完成记录在一次日志
   写入中提交。无效数量和库存不足属于业务拒绝：处理程序回复拒绝结果，
   并保持库存不变。非正数的 `Restock` 不具有回复协议，因此其处理程序
   调用 `context.Fail(reason)`。
5. 回复 ID 是命令 ID 的 `result` 子标识。订单处理程序将
   结果记录到其日志化回执中并完成，`WaitForOutcomeAsync`
   在该回执持久化后返回。投递过程与等待中的
   请求交错执行，因此宿主等待结果时，订单可以收到回复。
6. 库存 Grain 的收件箱保留每条已完成命令的 ID 及接受时的
   `SentAt`。重新投递（例如回复丢失后进行的重试）在记录仍被保留时收到
   `Duplicate`，记录仍保留原接受时间。
   记录移除后，未被跟踪的旧发送会在
   处理程序运行前收到 `Expired`。

成功运行时会输出：

```text
ACKNOWLEDGED: orders/order-1042/reserve-stock/result, outcome=ReservationAccepted
ACKNOWLEDGED: orders/order-1042/reserve-too-much/result, outcome=ReservationRejected (InsufficientStock)
ACKNOWLEDGED: orders/order-1042/reserve-invalid-quantity/result, outcome=ReservationRejected (InvalidQuantity)
VERIFIED: remaining stock=8, reservations=1, processed requests=3.
```

宿主检查每个关联结果，验证库存 Grain 对每条命令仅处理一次，
且库存仅扣减一次。检查失败或超过两分钟期限，
进程将以非零退出码结束。使用默认 Silo 和网关端口的本地主机
示例一次只能运行一个。
<a id="storage-and-production-boundaries"></a>

## 存储与生产环境边界

`UseInMemoryDurableJobs` 和 `AddVolatileJournalStorage` 使示例能够
独立运行。它们的确认操作会实际驱动 Journaling、消息传递泵和
持久化作业。**停止进程将丢弃其状态。** 配置持久化
日志和作业提供程序，才能在进程重启后恢复。示例使用
`orleans-binary` 日志格式记录可由 Orleans 序列化的记录。

接收方的日志化准入下限只会向前推进，即使发生时钟变化
或恢复也是如此。自已接受的发送时间起，经过配置的
`DeduplicationWindow` 后，已完成记录即可被移除。该下限
会在压实后使旧投递过期。对于已完成的重复请求，其确认的是所保留的
完成事实；调用方通过原始持久化回复获取业务结果，
或查询真实业务状态。最初提交的
发件箱意图会按照已配置的重试策略投递该回复。
`Expired` 结果或发送方重试耗尽均意味着此前是否已接受仍然未知。
在授权新发送之前，应按原始命令 ID 对账业务状态。
需要在崩溃后恢复最终操作的应用，应使用
`IOutboxMessageSource`，并在 `OnRetired` 中暂存结果和发给自身的后续
消息，使这些操作与移除传出条目原子地完成。

在生产订单服务中，从可信入口确定租户所有权，
对命令 ID、主题及回复目的地进行授权，并定义预留
查询、过期、释放及补偿策略。当这些操作需要查询预留时，按原始命令 ID
存储可查询的预留记录。外部效果使用
相同的规范 ID，并遵循其提供程序的幂等及对账协议。
