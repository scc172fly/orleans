<a id="microsoft-orleans-durable-jobs"></a>

# Microsoft Orleans 持久工作

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久就业为安排在特定时间执行的一次性工作提供了分布式、可扩展的制度。 Orleans 为经常性任务设计的提醒, " 持久工作 " 是未来一次性活动的理想,如任用通知、延迟处理、排定工作流程步骤和时间触发。

**关键字特性:**
- **至少有一次一次性执行**:工作计划至少运行一次
- **持久性**:工作在 grain 停止和 silo 重新开始后得以生存
- **分配**:任务在 silos之间自动分布和重新平衡
- **可靠**:失败的工作可以自动地用可配置的政策重新审理。
- **丰富元元数据**:每项工作的相关自定义元数据
- **易可耐久可撤销**:取消请求阻止今后尝试;已经进行的尝试可能仍然完成

<a id="getting-started"></a>

## 入门

<a id="installation"></a>

### 安装
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
```

对于具有持久性的生产设想情况,还应安装一个储存供应商:

```shell
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
```

<a id="configuration"></a>

### 配置

<a id="using-in-memory-storage-developmenttesting"></a>

#### 使用内存储(开发/测试)
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        // Configure in-memory Durable Jobs (no persistence)
        .UseInMemoryDurableJobs();
});

await builder.Build().RunAsync();
```

<a id="using-azure-storage-production"></a>

#### 使用 Azure 存储(生产)
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        // Configure Azure Storage Durable Jobs
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
                // Duration of each job shard (jobs are partitioned by time)
                options.ShardDuration = TimeSpan.FromMinutes(5);

                // Load eligible shards within this horizon and check at this interval
                options.ShardLoadLookaheadPeriod = TimeSpan.FromMinutes(10);
                options.ShardCheckInterval = TimeSpan.FromMinutes(5);
                
                // Maximum number of jobs that can execute concurrently on each silo
                options.MaxConcurrentJobsPerSilo = 100;
                
                // Custom retry policy
                options.ShouldRetry = (context, exception) =>
                {
                    // Retry up to 3 times with exponential backoff
                    if (context.DequeueCount < 3)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, context.DequeueCount));
                        return DateTimeOffset.UtcNow.Add(delay);
                    }
                    return null; // Don't retry
                };
            });
        });
});
```

<a id="select-named-journal-storage"></a>

## 选择命名日记存储

`UseJournaledDurableJobs` 日期 时 `ISiloBuilder` 或 `IServiceCollection` 安装
日记硬管理器和持久工作 JSON 独立运行元数据
存储后端后端 。 注册一个目录化日记账提供者, 然后选择它
与 `DurableJobsOptions.ActiveProviderName`。默认名称是 `"Default"`。
`DrainingProviderNames` 初始为空 。

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

命名的Blob、表 Redis、S3和挥发性登记,装订储存、目录、
以及同一配置命名空间的州管理工厂。
将默认提供者留为 grain 日记帐独立设置。保持每个物理
以一个选定名称注册的命名空间。 `UseInMemoryDurableJobs`,
`UseAzureBlobDurableJobs`和 `UseAzureTableDurableJobs` 仍为方便
默认绑定的设置构成。
使用持续储存恢复。

`"Default"` 存储套件保留现有的未命名后端选项
管道; 非默认连接, 如 `"jobs-a"` 使用命名后端选项 。

大多数部署都选择一个提供者。 然后进行不刻录的已知硬查找
其内容如下:不论其他日记提供者如何,供应商的元数据直接来自提供商。
注册为 grain 状态。在迁移过程中,发现涵盖文字和
排水提供者,和不刻板的已知ID查寻 读写提供者先,
无法使用的提供者会显示一个搜索
失败; 缺勤要求对所有选定的提供者进行成功检查。

新计划使用在写器提供者中创建的碎片。
原供应商 拥有、重播、执行、再试、重新安排
取消、压缩和删除。时间戳/ GUID 碎片代号,
`jobs/shards/<id>` 路径和自供应商独立连号 `DurableJob`
将控件保持不变。 碎片有一个权威存储位置
整个生命期。

<a id="cut-over-and-retire-a-provider"></a>

### 裁员并退休

启动时定有装订条件。

1. 部署绑定和选择 A 进行写作和 B , 排入 **每一**
   时间安排 silo 之前的 silo B创造工作。
2. 将 B 推出为写作, A 展开为排水。 A 配置 silos 仍然可以创建
   展开期间的碎片。 每次排程时, 剪切完成 silo
   B和先前的日程安排电话已经打完。
   如果需要严格的切换边界,则在更改期间进行。
3. 排水要求列出, 写明, 索赔,
   耐久更新、重试、取消、压缩和删除。
4. 在所有作家都切开后,
   `IDurableJobsStorageInspector.InspectAsync("jobs-a", cancellationToken)`.
   解决所有剩余的碎片,包括未来日期、自有、有毒和
   重复根据《公约》第19条提出的成功库存清单
   后端的现场列名行为。
5. 只有在经过核实的完整排出量后才能从 `DrainingProviderNames` 删除 A。
   只有在其他消费者也完成之后,才将装订/储存设备退役。

检查员返回 `DurableJobsStorageStatus` ,与 `ProviderName`
`IsWriteProvider`, `ShardCount`, `OwnedShardCount`, `PoisonedShardCount`,
`UnrecognizedShardCount`,和无效 `OldestShardStartTime` /
`NewestShardStartTime`。它读到完整的目录简况
使用只读化目录和元数据操作的进程。
检查失败时, 显示 **未知** 。 计数描述碎片, 每一个
其中有许多工作可以包含许多;拥有/投入的计算可能重叠。
要求成功完成全零库存,加上完成的集束范围作家
切开。

若要倒回, 保留两个提供者选择, 并切换给 A 和 B 的 A 写入
B. 保留储存命名空间和
在整个移民过程中,

见 [重新开动的移徙抽样](../../samples/DurableJobsMigration/README.md)
耐久 Azure 模拟器储存、留存的手吊注销和当地
清单核实和 [移徙指南](../../docs/site/src/content/docs/grains/journaling/durable-jobs-migration.md)
用于部署和监测细节。

<a id="shard-discovery-and-lookahead"></a>

## 硬发现和眼神

silo 发现启动时间在 `DurableJobsOptions.ShardLoadLookaheadPeriod`之内的碎片
默认的外观是十分钟。
发现碎片一旦进入 `ShardActivationBufferPeriod`,便开始处理。
`DurableJobsOptions.ShardCheckInterval` 控制定期发现和可写硬化清除
输入或退出活动集的 A silo 也触发
支票和 silo 被宣布死亡,故死人是死的 silo碎片一旦在《公约》
成员宣布它已死亡。
外观头部接受非负向期限; 零选择起始时间在或
在当前时间之前。发现视野以 `DateTimeOffset.MaxValue`上限。
检查间隔允许1至4294967294毫秒的长度。

硬化日记用名称, 例如
`jobs/shards/20260909T1200000000000Z-<unique-id>`. 世界协调时的固定宽开工时间
位于独一后缀前, 所以正序名称顺序是 散开启动时间顺序。 每次扫描
列表的生 `jobs/shards/` 包含一个包含 `JournalCatalogListOptions.MaxId` 环绕着眼线的,
范围包括每个较早的启动时间,包括长期逾期未完成的工作
未来散落区。 在候选元数据读取前, 由目录过滤未来的散存身份 。

每次定期或会员资格检查都开始对每个选定的
供应商候选人拥有最先定购的和索赔总预算。
发现按配置顺序列出选定目录,请求元数据
缓冲每个供应商的候选人,直到该查点成功为止。
完成或错误、发现命令和复制成功
结果,然后使用每份提供
与ETag 进行自有快照,或在无法获取时读取当前元数据。
当地民众的简快照片 silo 发现后再用隐藏的碎片或读作
更新描述符决定了资格、所有权,
以及任何在开审新案之前必须提出的有条件债权。
最老的索赔先处理,要求快照的ETag 有条件更新,所以
用于提供持久就业的供应商
元数据ETags和强制实施有条件更新;缺少的ETag表面是一个发现错误。
指定碎片在打开时交付,允许在稍后期间执行
索赔预算限制新的索赔;
预算用完后,当地拥有的碎片仍符合资格。

回收一个碎片重现工作,核实当地所有权,并长期附加
将所有者记录在单独日记流中,
其大小取决于 silo 重度大于
提供者的单附加件限制。 此内容承诺建立
新的作家存储的同货币边界:
与日记数对照的更改,新的所有人从回收的中得到进步
状态。在承诺检测超置后,再次核实所有权
当地同时开启一个股份
新的日记从第一位作家开始
每一个打电话者的取消都结束了自己的等待。 一个活跃的加入者重新输入共享
尝试完成清理后, 其启动电话已取消打开 。
持续性日记账提供者实施有条件内容突变;普通元数据说明和
同样的所有者关闭保留了他们仅使用元数据重试的行为。

数据表提供者使用其储存能力,应用原始前缀和范围限制。
时间戳表还支持较窄的日/小时前缀和包容性 `MinId`
选择特定时间窗口的调用器间隔和 `MaxId` 间隔。
硬化命名空间开始启动, 所有逾期未完成的工作都有资格获得。
Azure 表格默认绘图使用索引化关键区域。 Azure 并订购普通用途
S3 列表可以寻求下限,停在上界。 S3 Express 和 Redis 过滤时间
发现命令选中的名称本身
存储列表工作和请求
长期性仍依赖供应商。 Blob、表和多波动目录提供元数据快照
身份; S3和 Redis 要求单独列出候选元数据。

搜索者拥有其查点和选定身份组,直至完成。
一份目录上的不合格弃置,其部分结果为供应商的部分结果;转让失败跳过其
剩余候选人。错误随供应商名称报告,以后核对重试
恢复的延迟期包括每个选定的目录列表请求
配置请求超时和重试限制
客户符合回收延迟要求。
已经送交当地管理人员,在观察到取消之前跟踪记录,并
继续使用其执行寿命周期。
通过上市、元数据和日记业务的注销流动。

短期外观期缩短,减少早期装载已回收碎片。
增加扫描频率,减少等待新插入或新符合条件的碎片的时间。
公共 `JobShardManager.AssignJobShardsAsync` 方法收集的相同命令发现的
将全部结果列表中的数据输入。

<a id="shutdown-lifecycle"></a>

## 关闭关闭寿命周期

安排日程、取消申请和启动使用共享
`Orleans.Internal.AdmissionGate` 功用来源 `Orleans.Core` 进入无锁门。
调用者将返回的只读数符号保留在本地 `using` ,并检查 `Entered`
开始工作; 处置释放入场。
每个确认的代号都有一个拥有者 来处理它一次
在同一行动中保留一个计数,并观察关闭旗;
初步检查拒绝者
也就是说,在加加薪前要关闭,所以更多的抵达者将留下排水量。
时间安排和取消申请的审批工作将持续到完成,
包括所有权查寻和远程取消路径。
直至其执行任务公布并排队。
关闭关闭时以原子方式设置关闭的旗帜,通过 `CloseAsync`、取消信号到
飞行请求,等待这些行动,然后拍下运行的碎片
并取消执行。 在关闭继续耗水的同时, 记录回回复失败
请求,等待执行,释放碎片。

成功安排和取消时写着保留结果,
即使取消的竞赛随着完成而争盘, 创建仍然拥有所有 。 然后关闭
等待着主动扫荡和每一个被承认的碎片的处决和清理。它不登记
缓存的碎片, 使用关闭标记仍然不活动, 然后将其处理 。
日记提供商释放了另一个 silo 用于要求和删除空的覆盖的碎片
碎片,包括在请求取消后完成的创建。

<a id="usage-examples"></a>

## 使用率示例

<a id="basic-job-scheduling"></a>

### 基本工作时间安排

<a id="1-implement-the-idurablejobhandler-interface"></a>

#### 1. 实施 IDurableJobHandler 接口
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
            // No future attempt will start. An already-running attempt may still complete.
            _durableJob = null;
        }
    }

    // This method is called when the durable job executes
    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var userId = this.GetPrimaryKeyString();
        var message = context.Job.Metadata?["Message"];

        _logger.LogInformation(
            "Sending notification to user {UserId}: {Message} (Job: {JobId}, Run: {RunId}, Attempt: {DequeueCount})",
            userId, message, context.Job.Id, context.RunId, context.DequeueCount);

        // Send the notification here
        // If this throws an exception, the job can be retried based on your retry policy
        
        _durableJob = null;
        return Task.CompletedTask;
    }
}
```

<a id="2-order-workflow-with-multiple-jobs"></a>

#### 2. 多种工作有秩序的工作流程
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
        
        // Create the order
        await _orderService.CreateOrderAsync(orderId, details);
        
        // Schedule delivery reminder for 24 hours before delivery
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

        // Schedule order expiration if payment not received
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

### 高级方案

<a id="job-with-retry-logic"></a>

#### 重试逻辑任务
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
            throw; // Let the retry policy handle it
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment processing failed with permanent error");
            throw; // This will not be retried if the retry policy returns null
        }
    }
}
```

<a id="tracking-job-completion"></a>

#### 跟踪工作完成情况
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
        // Execute the workflow step...
        
        // Mark as complete
        if (_pendingJobs.TryRemove(context.Job.Id, out var tcs))
        {
            tcs.SetResult();
        }

        return Task.CompletedTask;
    }
}
```

<a id="how-it-works"></a>

## 如何运作

<a id="architecture-overview"></a>

### 架构概览
1. **裁断工作**:任务被分割成基于时间的碎片(默认值:1分钟窗口)
2. **硬片所有权**:每块碎片都由单一 silo 拥有,供执行
3. **自动重新平衡**:当一个 silo 失败时,其碎片会自动被重新分配给健康 silos
4. **命令执行**:在碎片内,按适当时间处理工作
5. **货币货币管制**: : 国家 `MaxConcurrentJobsPerSilo` 设置同时执行职务的限制

<a id="job-lifecycle"></a>

### 工作生命周期
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

## 配置引用

### DurableJobsOptions

| 财产 | 类型类型 | 默认默认 | 说明 |
|----------|------|---------|-------------|
| `ShardDuration` | `TimeSpan` | 1分钟1分 | 每一工作强度的长度。 较小值会减少潜伏,但增加管理费用。 |
| `MaxConcurrentJobsPerSilo` | `int` | 100 | 最多可同时执行 silo的作业数。 |
| `ShouldRetry` | `Func<IJobRunContext, Exception, DateTimeOffset?>` | 3次重试,并进行删除。 | 确定是否要重新尝试一个失败的工作。 返回新的到期时间或 `null` 以不重试 。 |

<a id="best-practices"></a>

## 最佳做法

1. **设定合理合理的货币限额**防止资源耗用
   ```csharp
   options.MaxConcurrentJobsPerSilo = 100; // Adjust based on your workload
   ```

2. **实施具有能力的专家工作处理员**:可以重新找工作,确保操作员是一等的
   ```csharp
   public async Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
   {
       var jobId = context.Job.Id;
       // Check if already processed
       if (await _state.IsProcessed(jobId))
           return;
           
       // Process job...
       await _state.MarkProcessed(jobId);
   }
   ```

3. **明智使用元数据**:保持元数据的轻量
   ```csharp
   // Good: Store IDs
   var metadata = new Dictionary<string, string> { ["OrderId"] = "12345" };
   
   // Bad: Store large objects
   var metadata = new Dictionary<string, string> { ["Order"] = JsonSerializer.Serialize(largeOrder) };
   ```

4. **处理取消**:尊重注销符号
   ```csharp
   public async Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
   {
       await SomeLongRunningOperation(attemptCancellationToken);
   }
   ```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [计时和提醒](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
