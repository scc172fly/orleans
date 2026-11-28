<a id="microsoft-orleans-durable-jobs"></a>

# Microsoft Orleans 持久化作业（Durable Jobs）

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久化作业提供一个分布式、可伸缩的系统，用于调度在指定时间执行的一次性作业。与面向周期性任务的 Orleans 提醒器不同，持久化作业适用于预约通知、延迟处理、定时执行的工作流步骤和基于时间的触发器等未来的一次性事件。

**主要特性：**
- **至少执行一次**：作业被调度为至少执行一次
- **持久化**：作业可跨 Grain 停用和 Silo 重启存续
- **分布式**：作业在各 Silo 之间自动分配和再均衡
- **可靠性**：失败的作业可依据可配置的策略自动重试
- **丰富的元数据**：可为每个作业关联自定义元数据
- **可持久化取消**：取消请求阻止后续执行尝试；已经运行的尝试仍可能完成

<a id="getting-started"></a>

## 入门

<a id="installation"></a>

### 安装
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
```

对于需要持久化的生产场景，还需安装存储提供程序：

```shell
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
```

<a id="configuration"></a>

### 配置

<a id="using-in-memory-storage-developmenttesting"></a>

#### 使用内存存储（开发和测试）
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        // 使用内存存储配置持久化作业，作业数据不会持久保存
        .UseInMemoryDurableJobs();
});

await builder.Build().RunAsync();
```

<a id="using-azure-storage-production"></a>

#### 使用 Azure 存储（生产环境）
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        // 使用 Azure Storage 存储持久化作业
        .UseAzureBlobDurableJobs(options =>
        {
            options.BlobServiceClient = new Azure.Storage.Blobs.BlobServiceClient("YOUR_CONNECTION_STRING");
            options.ContainerName = "durable-jobs";
        });
});

await builder.Build().RunAsync();
```

<a id="advanced-configuration"></a>

#### 高级配置
```csharp
builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        .UseInMemoryDurableJobs()
        .ConfigureServices(services =>
        {
            services.Configure<DurableJobsOptions>(options =>
            {
                // 每个作业分片覆盖的时长（作业按时间分片）
                options.ShardDuration = TimeSpan.FromMinutes(5);

                // 加载前瞻时间窗口内符合条件的分片，并按指定间隔检查
                options.ShardLoadLookaheadPeriod = TimeSpan.FromMinutes(10);
                options.ShardCheckInterval = TimeSpan.FromMinutes(5);
                
                // 每个 Silo 可并发执行的最大作业数
                options.MaxConcurrentJobsPerSilo = 100;
                
                // 自定义重试策略
                options.ShouldRetry = (context, exception) =>
                {
                    // 出队次数小于 3 时，按指数退避安排重试
                    if (context.DequeueCount < 3)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, context.DequeueCount));
                        return DateTimeOffset.UtcNow.Add(delay);
                    }
                    return null; // 停止重试
                };
            });
        });
});
```

<a id="select-named-journal-storage"></a>

## 选择命名日志存储

`UseJournaledDurableJobs` 在 `ISiloBuilder` 或 `IServiceCollection` 上安装
日志化分片管理器及持久化作业 JSON 元数据支持，而不依赖具体的
存储后端。请注册支持目录功能的日志提供程序，然后使用
`DurableJobsOptions.ActiveProviderName` 选择它。默认名称为 `"Default"`。
`DrainingProviderNames` 最初为空。

```csharp
siloBuilder
    .AddAzureBlobJournalStorage("jobs-a", options =>
    {
        options.BlobServiceClient = originalClient;
        options.ContainerName = "jobs-original";
    })
    .AddAzureBlobJournalStorage("jobs-b", options =>
    {
        options.BlobServiceClient = currentClient;
        options.ContainerName = "jobs-current";
    })
    .UseJournaledDurableJobs(options =>
    {
        options.ActiveProviderName = "jobs-b";
        options.DrainingProviderNames.Add("jobs-a");
    });
```

命名的 Blob、Table、Redis、S3 和易失性存储注册将存储、目录
和状态管理器工厂绑定到同一配置的命名空间。显式命名使
Grain 日志记录使用的默认提供程序保持独立。每个物理
命名空间仅应以一个选定名称进行注册。`UseInMemoryDurableJobs`、
`UseAzureBlobDurableJobs` 和 `UseAzureTableDurableJobs` 仍是
为默认绑定提供的便捷组合方法。易失性存储仅在其所属进程的
生命周期内保留作业；如需重启恢复，请使用持久化存储。

`"Default"` 存储绑定保留现有的未命名后端选项
管道；`"jobs-a"` 等非默认绑定使用命名后端选项。

大多数部署仅选择一个提供程序。此时查找已知但未缓存的分片，
会直接读取该提供程序的元数据，不受为 Grain 状态注册的其他日志提供程序影响。
迁移期间，发现过程覆盖写入和
排空提供程序；未缓存的已知 ID 查找首先查询写入提供程序，
然后按需查询排空提供程序。提供程序不可用会导致查找
失败；只有在所有选定提供程序中成功确认不存在，才能确定分片不存在。

新调度使用在写入提供程序中新建的分片。现有分片在所有权管理、重放、执行、重试、重新调度、
取消、压实和删除时均继续使用其原有提供程序。
分片 ID 的时间戳/GUID 表示形式、
`jobs/shards/<id>` 路径以及与提供程序无关的序列化 `DurableJob`
句柄保持不变。分片在其整个生命周期内具有
唯一的权威存储位置。

<a id="cut-over-and-retire-a-provider"></a>

### 切换并退役提供程序

绑定在启动时确定。对于从 A 到 B 的迁移：

1. 在任何 Silo 在 B 中创建作业之前，部署两个绑定，并在**每个**
   负责调度的 Silo 上选择 A 用于写入、B 用于排空。
2. 滚动部署将 B 用于写入、A 用于排空。在此过程中仍配置为 A 的 Silo 依然可以创建
   A 分片。当所有负责调度的 Silo
   均使用 B 且先前的调度调用全部结束后，切换完成。如果需要严格的切换边界，
   请在切换期间暂停应用调度。
3. 保留 A 的变更权限。排空需要列举、读取、认领、
   持久化更新、重试、取消、压实和删除操作。
4. 所有写入方完成切换后，使用
   `IDurableJobsStorageInspector.InspectAsync("jobs-a", cancellationToken)` 检查 A 的完整命名空间。
   处理所有剩余分片，包括未来到期、已有所有者、有毒和
   未识别的条目。根据存储后端的实时列举行为，重复执行成功的
   完整清单检查。
5. 仅在确认完全排空后，才从 `DrainingProviderNames` 中移除 A。
   只有其他使用方也已结束使用后，才退役该绑定及存储。

检查器返回 `DurableJobsStorageStatus`，其中包含 `ProviderName`、
`IsWriteProvider`、`ShardCount`、`OwnedShardCount`、`PoisonedShardCount`、
`UnrecognizedShardCount` 以及可空的 `OldestShardStartTime` /
`NewestShardStartTime`。它通过调用方进程，使用只读目录和元数据操作读取
完整的目录快照。发生故障会使调用失败：
检查失败时显示**未知**。计数描述的是分片，每个分片
可包含多个作业；已有所有者的分片和有毒分片计数可能重叠。退役要求
成功获得完整且总数为零的清单，并且集群范围内的写入方均完成
切换。

如需回滚，请保持两个提供程序均被选用，将写入切回 A，并排空 B。
B 中的现有作业继续在 B 中处理。在整个迁移期间保留存储命名空间及
兼容的日志读取器。

参阅[基于重启的迁移示例](../../samples/DurableJobsMigration/README.md)，
了解 Azure 模拟器中的持久化存储、使用保留句柄取消作业及本地
清单检查；参阅[迁移指南](../../docs/site/src/content/docs/grains/journaling/durable-jobs-migration.md)，
了解部署及监控细节。

<a id="shard-discovery-and-lookahead"></a>

## 分片发现与前瞻范围

每个 Silo 发现开始时间处于 `DurableJobsOptions.ShardLoadLookaheadPeriod` 所指定范围内的分片，
该范围以当前持久化作业时间提供程序的时钟为基准。默认前瞻时间为 10 分钟。
已发现的分片在其开始时间进入 `ShardActivationBufferPeriod` 时开始处理。
`DurableJobsOptions.ShardCheckInterval` 控制定期发现及可写分片清理
检查，默认为 5 分钟。Silo 加入或离开活动成员集合时也会触发
检查；被宣告死亡时同样如此，因此在成员资格机制宣告该 Silo 死亡后，
会立即接管其分片。
前瞻时间允许非负时长；为零时选择开始时间不晚于
当前时间的分片。发现范围以 `DateTimeOffset.MaxValue` 为上限。
检查间隔允许 1 至 4294967294 毫秒的时长。

分片日志使用如下形式的名称：
`jobs/shards/20260909T1200000000000Z-<unique-id>`。固定宽度的 UTC 开始时间
位于唯一后缀之前，因此名称的序数排序与分片开始时间的排序一致。每轮扫描
列举原始 `jobs/shards/` 前缀，并使用包含端点的 `JournalCatalogListOptions.MaxId` 上限覆盖前瞻
范围。该范围涵盖所有更早的开始时间，包括因长时间
停机而逾期的作业。在读取候选分片元数据之前，目录会过滤未来分片的标识。

每次定期或成员资格检查都会启动新一轮本地范围扫描，遍历每个选定的
提供程序。候选分片共享最早优先顺序和总认领预算。
发现过程按配置顺序枚举选定的目录，请求元数据并
缓冲各提供程序的候选分片，直到其枚举成功。当所有选定
目录均完成或发生故障后，发现过程对成功获取的
结果进行排序和去重，然后使用每个已提供的
带 ETag 的所有权快照；如果快照不可用，则读取当前元数据。
若快照将本地 Silo 标记为所有者，发现过程会复用已缓存的分片，或在缓存未命中时读取
当前元数据。刷新后的描述符确定资格、所有权以及打开新实例前
所需的任何条件式认领。
认领按最早优先顺序进行，并要求使用快照的 ETag 进行条件式更新，因此
并发的所有权变更会拒绝陈旧的认领。持久化作业使用的提供程序必须提供
元数据 ETag 并强制执行条件式更新；缺少 ETag 时会报告发现错误。
分配的分片在打开时即被交付，使执行能够在后续候选分片评估期间继续进行。
认领预算仅限制新的认领；
即使预算耗尽，本地已拥有的分片仍具有处理资格。

恢复分片时会重放其中的作业，验证本地所有权，并在发布实例前
在单独的日志流中持久化追加一条大小有限的所有者记录。
该记录的大小取决于 Silo 地址，因此即使分片大于
提供程序单次追加上限，也可以恢复。此次内容提交建立
新写入方的存储并发边界：旧所有者的待处理写入会因日志已变更而失败，
新所有者从恢复后的
状态继续推进。提交后会再次验证所有权，以检测打开期间是否发生了
覆盖此次认领的更新认领。并发的本地打开操作共享同一次恢复，并使用
同一规范实例。新建日志从其首个写入方开始。
每个调用方的取消只结束自身的等待。如果发起共享打开操作的调用方取消了该操作，
仍在等待的参与方会在该次尝试完成清理后重试。
持久化日志提供程序强制执行条件式内容变更；普通元数据标注和
同一所有者执行的关闭操作仍保留仅针对元数据的重试行为。

目录提供程序利用各自的存储能力应用原始前缀和范围约束。
时间戳表示形式还支持更窄的日或小时前缀，以及包含两端的 `MinId`
和 `MaxId` 区间，供调用方选择特定时间窗口。恢复从
分片命名空间的起点开始，确保所有逾期作业仍具有处理资格。
Azure Table 默认映射使用带索引的键范围。Azure Blob 和具有排序保证的通用 S3 列举
可以定位下界并在上界处停止。S3 Express 和 Redis 在提供程序定义的遍历期间筛选时间边界。
发现过程自行对选定的名称进行排序，
以保证不同提供程序之间采用一致的最早优先处理顺序。存储列举的工作量和请求
延迟仍取决于提供程序。Blob、Table 和 Volatile 目录随标识提供元数据快照；
S3 和 Redis 则需要单独读取候选分片的元数据。

每轮扫描在完成之前拥有其枚举和选定标识集合。迁移期间，
目录故障会丢弃该提供程序的部分结果；分配故障会跳过该提供程序的
剩余候选分片。错误会连同提供程序名称一起报告，后续检查通过新一轮扫描重试。
恢复延迟包括每个选定目录的列举请求
和存储客户端的重试延迟。请配置存储客户端的请求超时和重试上限，
以满足恢复延迟要求。已经交付给本地管理器的分片
会在观测到取消之前受到跟踪，
并继续完成其执行生命周期。
取消请求贯穿列举、元数据和日志操作。

较短的前瞻时间可减少恢复分片的提前加载。较短的检查间隔
会提高扫描频率，缩短新插入或新获得处理资格的分片的等待时间。
公开的 `JobShardManager.AssignJobShardsAsync` 方法将同一个有序发现流
收集到其完整结果列表中。

<a id="shutdown-lifecycle"></a>

## 关闭生命周期

调度、取消请求和激活操作使用共享的
`Orleans.Internal.AdmissionGate` 工具（来自 `Orleans.Core`）实现无锁准入。
调用方在 `using` 局部变量中保留返回的只读令牌，并在开始工作前检查 `Entered`；
释放令牌会解除准入占用。
每个成功准入的令牌均有一个所有者，并由其恰好释放一次。原子递增操作
在同一次操作中预留计数并检查关闭标志；发现已关闭的尝试
会立即释放其计数。预先检查会拒绝在递增前已发现关闭的调用方，
因此后续到达的请求不会改变排空计数。
调度及取消请求一直持有准入资格直到操作完成，
包括所有权查找与远程取消路由。激活操作持有准入资格，
直到其执行任务发布并入队。
关闭通过 `CloseAsync` 原子设置关闭标志，向正在进行的请求
发出取消信号，并在获取运行中分片的快照和取消执行之前等待这些操作完成。
回调失败会被记录到日志中；关闭流程仍会继续排空请求、等待执行结束
并释放分片。

已成功执行的调度和取消写入保留其结果；成功创建的分片仍由创建方拥有，
即使取消与创建完成发生竞争也不例外。随后关闭操作
等待当前扫描以及所有已准入分片的执行和清理。它使用关闭令牌注销
仍处于非活动状态的缓存分片，然后释放这些分片。日志化提供程序
释放非空分片以供其他 Silo 认领，并删除空分片，
包括请求取消后才完成创建的分片。

<a id="usage-examples"></a>

## 使用示例

<a id="basic-job-scheduling"></a>

### 基本作业调度

<a id="1-implement-the-idurablejobhandler-interface"></a>

#### 1. 实现 IDurableJobHandler 接口
```csharp
using Orleans;
using Orleans.Durable.Jobs;

public interface INotificationGrain : IGrainWithStringKey
{
    Task ScheduleNotification(string message, DateTimeOffset sendTime);
    Task CancelScheduledNotification(CancellationToken requestCancellationToken);
}

public class NotificationGrain : Grain, INotificationGrain, IDurableJobHandler
{
    private readonly ILocalDurableJobManager _jobManager;
    private readonly ILogger<NotificationGrain> _logger;
    private DurableJob? _durableJob;

    public NotificationGrain(
        ILocalDurableJobManager jobManager,
        ILogger<NotificationGrain> logger)
    {
        _jobManager = jobManager;
        _logger = logger;
    }

    public async Task ScheduleNotification(string message, DateTimeOffset sendTime)
    {
        var userId = this.GetPrimaryKeyString();
        var metadata = new Dictionary<string, string>
        {
            ["Message"] = message
        };

        _durableJob = await _jobManager.ScheduleJobAsync(
            new ScheduleJobRequest
            {
                Target = this.GetGrainId(),
                JobName = "SendNotification",
                DueTime = sendTime,
                Metadata = metadata
            },
            CancellationToken.None);

        _logger.LogInformation(
            "Scheduled notification for user {UserId} at {SendTime} (JobId: {JobId})",
            userId, sendTime, _durableJob.Id);
    }

    public async Task CancelScheduledNotification(CancellationToken requestCancellationToken)
    {
        if (_durableJob is null)
        {
            _logger.LogWarning("No scheduled notification to cancel");
            return;
        }

        var cancellationRequested = await _jobManager.CancelAsync(_durableJob, requestCancellationToken);
        _logger.LogInformation(
            "Notification {JobId} cancellation request recorded: {CancellationRequested}",
            _durableJob.Id,
            cancellationRequested);

        if (cancellationRequested)
        {
            // 取消后不会开始新的执行尝试，已经开始的尝试仍可能完成。
            _durableJob = null;
        }
    }

    // 执行持久化作业时会调用此方法
    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var userId = this.GetPrimaryKeyString();
        var message = context.Job.Metadata?["Message"];

        _logger.LogInformation(
            "Sending notification to user {UserId}: {Message} (Job: {JobId}, Run: {RunId}, Attempt: {DequeueCount})",
            userId, message, context.Job.Id, context.RunId, context.DequeueCount);

        // 在此发送通知
        // 若发送时抛出异常，是否重试由作业的重试策略决定
        
        _durableJob = null;
        return Task.CompletedTask;
    }
}
```

<a id="2-order-workflow-with-multiple-jobs"></a>

#### 2. 包含多个作业的订单工作流
```csharp
public interface IOrderGrain : IGrainWithGuidKey
{
    Task PlaceOrder(OrderDetails details);
    Task CancelOrder();
}

public class OrderGrain : Grain, IOrderGrain, IDurableJobHandler
{
    private readonly ILocalDurableJobManager _jobManager;
    private readonly IOrderService _orderService;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrderGrain> _logger;

    public OrderGrain(
        ILocalDurableJobManager jobManager,
        IOrderService orderService,
        IGrainFactory grainFactory,
        ILogger<OrderGrain> logger)
    {
        _jobManager = jobManager;
        _orderService = orderService;
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task PlaceOrder(OrderDetails details)
    {
        var orderId = this.GetPrimaryKey();
        
        // 创建订单
        await _orderService.CreateOrderAsync(orderId, details);
        
        // 安排在交付前 24 小时发送提醒
        var reminderTime = details.DeliveryDate.AddHours(-24);
        await _jobManager.ScheduleJobAsync(
            new ScheduleJobRequest
            {
                Target = this.GetGrainId(),
                JobName = "DeliveryReminder",
                DueTime = reminderTime,
                Metadata = new Dictionary<string, string>
                {
                    ["Step"] = "DeliveryReminder",
                    ["CustomerId"] = details.CustomerId,
                    ["OrderNumber"] = details.OrderNumber
                }
            },
            CancellationToken.None);

        // 安排订单到期处理，以便在届时仍未收到付款时处理订单
        var expirationTime = DateTimeOffset.UtcNow.AddHours(24);
        await _jobManager.ScheduleJobAsync(
            new ScheduleJobRequest
            {
                Target = this.GetGrainId(),
                JobName = "OrderExpiration",
                DueTime = expirationTime,
                Metadata = new Dictionary<string, string>
                {
                    ["Step"] = "OrderExpiration"
                }
            },
            CancellationToken.None);
    }

    public async Task CancelOrder()
    {
        var orderId = this.GetPrimaryKey();
        await _orderService.CancelOrderAsync(orderId);
    }

    public async Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var step = context.Job.Metadata!["Step"];
        var orderId = this.GetPrimaryKey();

        switch (step)
        {
            case "DeliveryReminder":
                await HandleDeliveryReminder(context, attemptCancellationToken);
                break;

            case "OrderExpiration":
                await HandleOrderExpiration(attemptCancellationToken);
                break;
        }
    }

    private async Task HandleDeliveryReminder(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var customerId = context.Job.Metadata!["CustomerId"];
        var orderNumber = context.Job.Metadata["OrderNumber"];
        
        var notificationGrain = _grainFactory.GetGrain<INotificationGrain>(customerId);
        await notificationGrain.ScheduleNotification(
            $"Your order #{orderNumber} will be delivered tomorrow!",
            DateTimeOffset.UtcNow);
    }

    private async Task HandleOrderExpiration(CancellationToken attemptCancellationToken)
    {
        var orderId = this.GetPrimaryKey();
        var order = await _orderService.GetOrderAsync(orderId, attemptCancellationToken);
        
        if (order?.Status == OrderStatus.Pending)
        {
            await _orderService.CancelOrderAsync(orderId, attemptCancellationToken);
            _logger.LogInformation("Order {OrderId} expired and canceled", orderId);
        }
    }
}
```

<a id="advanced-scenarios"></a>

### 高级场景

<a id="job-with-retry-logic"></a>

#### 带重试逻辑的作业
```csharp
public class PaymentProcessorGrain : Grain, IDurableJobHandler
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentProcessorGrain> _logger;

    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var paymentId = context.Job.Metadata?["PaymentId"];
        
        _logger.LogInformation(
            "Processing payment {PaymentId} (Attempt {Attempt})",
            paymentId, context.DequeueCount);

        try
        {
            await _paymentService.ProcessPaymentAsync(paymentId, attemptCancellationToken);
            return Task.CompletedTask;
        }
        catch (TransientException ex)
        {
            _logger.LogWarning(ex, "Payment processing failed with transient error, will retry");
            throw; // 交由重试策略处理
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment processing failed with permanent error");
            throw; // 如果重试策略返回 null，此作业不会重试
        }
    }
}
```

<a id="tracking-job-completion"></a>

#### 跟踪作业完成情况
```csharp
public class WorkflowGrain : Grain, IDurableJobHandler
{
    private readonly Dictionary<string, TaskCompletionSource> _pendingJobs = new();

    public async Task<DurableJob> ScheduleWorkflowStep(string stepName, DateTimeOffset executeAt)
    {
        var job = await _jobManager.ScheduleJobAsync(
            new ScheduleJobRequest
            {
                Target = this.GetGrainId(),
                JobName = stepName,
                DueTime = executeAt,
                Metadata = null
            },
            CancellationToken.None);

        _pendingJobs[job.Id] = new TaskCompletionSource();
        return job;
    }

    public async Task WaitForJobCompletion(string jobId, TimeSpan timeout)
    {
        if (_pendingJobs.TryGetValue(jobId, out var tcs))
        {
            using var cts = new CancellationTokenSource(timeout);
            await tcs.Task.WaitAsync(cts.Token);
        }
    }

    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        // 执行工作流步骤
        
        // 将该步骤标记为已完成
        if (_pendingJobs.TryRemove(context.Job.Id, out var tcs))
        {
            tcs.SetResult();
        }

        return Task.CompletedTask;
    }
}
```

<a id="how-it-works"></a>

## 工作原理

<a id="architecture-overview"></a>

### 架构概述
1. **作业分片**：按时间窗口对作业进行分片（默认值：1 分钟窗口）
2. **分片所有权**：每个分片由单个 Silo 拥有并负责执行
3. **自动再均衡**：Silo 发生故障时，其分片自动重新分配给健康的 Silo
4. **有序执行**：在分片内部，按到期时间顺序处理作业
5. **并发控制**：`MaxConcurrentJobsPerSilo` 设置限制并发作业执行数量

<a id="job-lifecycle"></a>

### 作业生命周期
```
┌─────────────┐
│  Scheduled  │ ──▶ Job is created and added to appropriate shard
└─────────────┘
      │
      ▼
┌─────────────┐
│   Waiting   │ ──▶ Job waits in queue until due time
└─────────────┘
      │
      ▼
┌─────────────┐
│  Executing  │ ──▶ Job handler is invoked on target grain
└─────────────┘
      │
      ├──▶ Success ──▶ Job is removed
      │
      └──▶ Failure ──▶ Retry policy decides:
                        • Retry: Job is re-queued with new due time
                        • No Retry: Job is removed
```

<a id="configuration-reference"></a>

## 配置参考

### DurableJobsOptions

| 属性 | 类型 | 默认值 | 描述 |
|----------|------|---------|-------------|
| `ShardDuration` | `TimeSpan` | 1 分钟 | 每个作业分片的时间跨度。较小的值可降低延迟，但会增加开销。 |
| `MaxConcurrentJobsPerSilo` | `int` | 100 | 单个 Silo 上可同时执行的最大作业数。 |
| `ShouldRetry` | `Func<IJobRunContext, Exception, DateTimeOffset?>` | 指数退避重试 3 次 | 确定失败作业是否应重试。返回新的到期时间，或返回 `null` 表示不重试。 |

> **译者注（对应当前源码基准）：** 上表及架构概述保留了上游 README 的原始默认值，但与 DurableJobsOptions.cs 的实现不一致。实际 ShardDuration 默认为 1 小时，MaxConcurrentJobsPerSilo 默认为 10,000 × 处理器数量；默认重试函数在 DequeueCount 大于等于 5 时返回 null，否则按 2 的出队计数次方（秒）退避。请以源码及实际配置为准。

<a id="best-practices"></a>

## 最佳实践

1. **设置合理的并发限制**：防止资源耗尽
   ```csharp
   options.MaxConcurrentJobsPerSilo = 100; // 请根据工作负载调整并发上限
   ```

2. **实现幂等的作业处理程序**：作业可能被重试，请确保处理程序具有幂等性
   ```csharp
   public async Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
   {
       var jobId = context.Job.Id;
       // 检查作业是否已处理
       if (await _state.IsProcessed(jobId))
           return;
           
       // 处理作业
       await _state.MarkProcessed(jobId);
   }
   ```

3. **合理使用元数据**：保持元数据轻量
   ```csharp
   // 建议只存储标识符
   var metadata = new Dictionary<string, string> { ["OrderId"] = "12345" };
   
   // 避免存储大型对象
   var metadata = new Dictionary<string, string> { ["Order"] = JsonSerializer.Serialize(largeOrder) };
   ```

4. **处理取消**：遵循取消令牌
   ```csharp
   public async Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
   {
       await SomeLongRunningOperation(attemptCancellationToken);
   }
   ```

<a id="documentation"></a>

## 文档
如需更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [定时器与提醒器](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord 社区](https://aka.ms/orleans-discord)
- 关注 [@msftorleans](https://twitter.com/msftorleans) Twitter 帐号，获取 Orleans 公告
- 欢迎贡献！请查阅我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
