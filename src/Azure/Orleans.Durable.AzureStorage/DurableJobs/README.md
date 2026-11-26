<a id="microsoft-orleans-durable-jobs-for-azure-storage"></a>

# Microsoft Orleans 就业的持久就业机会 Azure 储存

<a id="introduction"></a>

## 简介
Microsoft Orleans 就业的持久就业机会 Azure 通过以下方式长期保留在 Azure 布布或 Azure 日记表提供商。 silo 重新开始, grain 停止使用和集群重组。 `UseAzureBlobDurableJobs` 配置附加式蓝色WAL和块形蓝色检查点; `UseAzureTableDurableJobs` 配置表日记页眉和数据代代。两者都登记了日记支持的碎片管理器和长期工作 JSON 序列元数据。

<a id="getting-started"></a>

## 入门

<a id="installation"></a>

### 安装
要使用此软件包,请通过 NuGet 安装该软件包以及核心软件包:

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
```

<a id="configuration"></a>

### 配置

选择 `UseAzureBlobDurableJobs` ,使用 `AzureBlobJournalStorageOptions`,或
`UseAzureTableDurableJobs` 与 `AzureTableJournalStorageOptions`两种支持
`ISiloBuilder` 和 `IServiceCollection` 登记。
a `TableServiceClient` 和 `TableName`;布束配置接受
`BlobServiceClient` 和 `ContainerName`。
包括一个单独的标准账户,用于
溢价 BlockBlobStorage 账户。

下 [持久工作日记操场](../../../playground/DurableJobsJournaling/README.md)
提供可运行的布洛/ 表后端选择。
[Azure 提供商基准](../../../test/Benchmarks/Journaling/Azure/README.md)
与受捆绑工作有关的附加、检查站、恢复和目录工作量
以及明确的资源所有权。

<a id="named-providers-and-account-migration"></a>

### 名列提供者和账户移徙

用于明确储存选择,登记
`AddAzureBlobJournalStorage("jobs-a", configure)` 或
`AddAzureTableJournalStorage("jobs-a", configure)` 和调用
`UseJournaledDurableJobs(options => options.ActiveProviderName = "jobs-a")`.
支持建筑商和服务收集的API,每个名字都有自己的
后端选项、 存储提供者、 目录和国营管理工厂。 命名
注册后不设缺省 grain 日记账独立。

将新工程转到另一个账户、集装箱或表格,保留A的原版
在 `"jobs-a"` 下注册命名空间,并在 `"jobs-b"`下登记B的命名空间:

```csharp
siloBuilder
    .AddAzureBlobJournalStorage("jobs-a", options =>
    {
        options.BlobServiceClient = originalAccountClient;
        options.ContainerName = "jobs";
    })
    .AddAzureBlobJournalStorage("jobs-b", options =>
    {
        options.BlobServiceClient = newAccountClient;
        options.ContainerName = "jobs";
    })
    .UseJournaledDurableJobs(options =>
    {
        options.ActiveProviderName = "jobs-b";
        options.DrainingProviderNames.Add("jobs-a");
    });
```

同一模式与不同的表格名称/客户或组合表一起工作。
。在选定名称下登记每个物理命名空间;修改
包含工作更改的账户或绘图, 这些任务可以
查找。

使用一个选定的提供者,未剪切的碎片搜索直接读取其元数据。
发现B+A选择了,发现内容既包括目录,也包括已知身份查勘。
现有工作,包括重试、重新安排时间表、取消、
和删除后,继续写入 A。新的碎片仅以 B. 散列ID创建
和 `DurableJob` 处理器保持时间戳/基于通用数字数据交换工具且独立提供。
储存错误与成功缺勤有区别。

在启用 B 写入前, 请将选中的绑定都安装到所有日程列表 silos
A 以书写提供商为A, B 以排水。 然后将B 以书写提供商为B
A 排水。这些设置在启动时生效。
严格的削减边界;否则,A形 silos 可保持创建 A
碎片一直到推出和他们同意的日程安排请求完成为止

保留 A 的身份授权列名, 读作:有条件元数据更新
整个排水管的日记、缩写和删除。这些权限
现有工作可以不断进步,其碎片可以被清理。
保存 WAL/检查点或表格分区绘图和
读者需要恢复两个供应商。

使用 `IDurableJobsStorageInspector.InspectAsync("jobs-a", cancellationToken)`
用于完整 `jobs/shards/` 清单,包括未来日期和中毒工作。
以未知身份报告库存失报情况。 **所有作家** 已经切开,
要求成功完成完整的库存,其全部碎片数为零,
A. 退休结合了这些内容
与完成的全集群作者的独立证据
切开。

下 [移徙抽样](../../../samples/DurableJobsMigration/README.md)
使用磁盘支持的 Azurite、两个布洛命名空间、单独的准备/排水程序,
和当地清单报告。
[核心移徙核心指南](../../Orleans.DurableJobs/README.md#cut-over-and-retire-a-provider)
反弹和退休标准。

<a id="using-connection-string"></a>

#### 使用连接字符串
```csharp
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseAzureStorageClustering(options => options.ConfigureTableServiceClient("YOUR_STORAGE_ACCOUNT_URI"))
        .UseAzureBlobDurableJobs(options =>
        {
            options.BlobServiceClient = new BlobServiceClient("YOUR_AZURE_STORAGE_CONNECTION_STRING");
            options.ContainerName = "durable-jobs";
        });
});

await builder.Build().RunAsync();
```

<a id="using-managed-identity-recommended-for-production"></a>

#### 使用管理身份( 建议生产)
```csharp
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseAzureStorageClustering(options => options.ConfigureTableServiceClient("YOUR_STORAGE_ACCOUNT_URI"))
        .UseAzureBlobDurableJobs(options =>
        {
            var credential = new DefaultAzureCredential();
            options.BlobServiceClient = new BlobServiceClient(
                new Uri("https://youraccount.blob.core.windows.net"),
                credential);
            options.ContainerName = "durable-jobs";
        });
});

await builder.Build().RunAsync();
```

<a id="with-advanced-options"></a>

#### 带有高级选项
```csharp
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseAzureStorageClustering(options => options.ConfigureTableServiceClient(connectionString))
        .UseAzureBlobDurableJobs(options =>
        {
            options.BlobServiceClient = new BlobServiceClient(connectionString);
            // Use different containers for different environments
            options.ContainerName = $"durable-jobs-{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.ToLowerInvariant()}";
        })
        .ConfigureServices(services =>
        {
            services.Configure<DurableJobsOptions>(options =>
            {
                // Shard duration: balance between latency and storage overhead
                options.ShardDuration = TimeSpan.FromMinutes(5);
                
                // Control concurrency to prevent overwhelming the system
                options.MaxConcurrentJobsPerSilo = 50;
                
                // Custom retry policy with exponential backoff
                options.ShouldRetry = (context, exception) =>
                {
                    // Don't retry on permanent failures
                    if (exception is ArgumentException or InvalidOperationException)
                        return null;
                    
                    // Exponential backoff with max 3 retries
                    if (context.DequeueCount < 3)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, context.DequeueCount));
                        return DateTimeOffset.UtcNow.Add(delay);
                    }
                    
                    return null;
                };
            });
        });
});
```

<a id="usage-example"></a>

## 用法示例

<a id="email-scheduling-with-cancellation"></a>

### 以取消方式发送电子邮件排程
```csharp
using Orleans;
using Orleans.Durable.Jobs;

public interface IEmailGrain : IGrainWithStringKey
{
    Task ScheduleEmail(string subject, string body, DateTimeOffset sendTime);
    Task CancelScheduledEmail(CancellationToken requestCancellationToken);
}

public class EmailGrain : Grain, IEmailGrain, IDurableJobHandler
{
    private readonly ILocalDurableJobManager _jobManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailGrain> _logger;
    private DurableJob? _durableEmailJob;

    public EmailGrain(
        ILocalDurableJobManager jobManager,
        IEmailService emailService,
        ILogger<EmailGrain> logger)
    {
        _jobManager = jobManager;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task ScheduleEmail(string subject, string body, DateTimeOffset sendTime)
    {
        var emailAddress = this.GetPrimaryKeyString();
        var metadata = new Dictionary<string, string>
        {
            ["Subject"] = subject,
            ["Body"] = body
        };

        _durableEmailJob = await _jobManager.ScheduleJobAsync(
            new ScheduleJobRequest
            {
                Target = this.GetGrainId(),
                JobName = "SendEmail",
                DueTime = sendTime,
                Metadata = metadata
            },
            CancellationToken.None);

        _logger.LogInformation(
            "Scheduled email to {EmailAddress} for {SendTime} (JobId: {JobId})",
            emailAddress, sendTime, _durableEmailJob.Id);
    }

    public async Task CancelScheduledEmail(CancellationToken requestCancellationToken)
    {
        if (_durableEmailJob is null)
        {
            _logger.LogWarning("No scheduled email to cancel");
            return;
        }

        var cancellationRequested = await _jobManager.CancelAsync(_durableEmailJob, requestCancellationToken);
        if (cancellationRequested)
        {
            _logger.LogInformation(
                "Email job {JobId} cancellation request recorded; no future attempt will start",
                _durableEmailJob.Id);
            // An already-running attempt may still complete.
            _durableEmailJob = null;
        }
        else
        {
            _logger.LogWarning(
                "Cancellation request was not recorded for email job {JobId} (it may have already completed)",
                _durableEmailJob.Id);
        }
    }

    public async Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var emailAddress = this.GetPrimaryKeyString();
        var subject = context.Job.Metadata?["Subject"];
        var body = context.Job.Metadata?["Body"];

        _logger.LogInformation(
            "Sending email to {EmailAddress} (Job: {JobId}, Attempt: {Attempt})",
            emailAddress, context.Job.Id, context.DequeueCount);

        try
        {
            await _emailService.SendEmailAsync(emailAddress, subject, body, attemptCancellationToken);
            _logger.LogInformation("Email sent successfully to {EmailAddress}", emailAddress);
            _durableEmailJob = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {EmailAddress}", emailAddress);
            throw; // Let the retry policy handle it
        }
    }
}
```

<a id="order-workflow-with-multiple-scheduled-steps"></a>

### 工作顺序,多档期
```csharp
public interface IOrderGrain : IGrainWithGuidKey
{
    Task PlaceOrder(OrderDetails order);
    Task CancelOrder();
}

public class OrderGrain : Grain, IOrderGrain, IDurableJobHandler
{
    private readonly ILocalDurableJobManager _jobManager;
    private readonly IOrderService _orderService;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrderGrain> _logger;
    private OrderDetails? _orderDetails;

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

    public async Task PlaceOrder(OrderDetails order)
    {
        _orderDetails = order;
        var orderId = this.GetPrimaryKey();

        // Create the order
        await _orderService.CreateOrderAsync(orderId, order);
        _logger.LogInformation("Order {OrderId} created for customer {CustomerId}", orderId, order.CustomerId);

        // Schedule payment reminder after 1 hour
        var paymentReminderTime = DateTimeOffset.UtcNow.AddHours(1);
        await _jobManager.ScheduleJobAsync(
            new ScheduleJobRequest
            {
                Target = this.GetGrainId(),
                JobName = "PaymentReminder",
                DueTime = paymentReminderTime,
                Metadata = new Dictionary<string, string>
                {
                    ["Step"] = "PaymentReminder",
                    ["CustomerEmail"] = order.CustomerEmail
                }
            },
            CancellationToken.None);

        // Schedule order expiration after 24 hours
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

        _logger.LogInformation(
            "Scheduled payment reminder for {ReminderTime} and expiration for {ExpirationTime}",
            paymentReminderTime, expirationTime);
    }

    public async Task CancelOrder()
    {
        var orderId = this.GetPrimaryKey();
        await _orderService.CancelOrderAsync(orderId);
        _orderDetails = null;
        _logger.LogInformation("Order {OrderId} canceled", orderId);
    }

    public async Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var step = context.Job.Metadata!["Step"];
        var orderId = this.GetPrimaryKey();

        _logger.LogInformation(
            "Executing workflow step {Step} for order {OrderId} (Attempt: {Attempt})",
            step, orderId, context.DequeueCount);

        switch (step)
        {
            case "PaymentReminder":
                await HandlePaymentReminder(context, attemptCancellationToken);
                break;

            case "OrderExpiration":
                await HandleOrderExpiration(attemptCancellationToken);
                break;

            default:
                _logger.LogWarning("Unknown workflow step: {Step}", step);
                break;
        }
    }

    private async Task HandlePaymentReminder(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var orderId = this.GetPrimaryKey();
        var order = await _orderService.GetOrderAsync(orderId, attemptCancellationToken);
        
        if (order?.Status == OrderStatus.Pending)
        {
            var customerEmail = context.Job.Metadata!["CustomerEmail"];
            var emailGrain = _grainFactory.GetGrain<IEmailGrain>(customerEmail);
            
            await emailGrain.ScheduleEmail(
                "Payment Reminder",
                $"Your order {orderId} is awaiting payment. Please complete your purchase within 23 hours.",
                DateTimeOffset.UtcNow);

            _logger.LogInformation("Payment reminder sent for order {OrderId}", orderId);
        }
        else
        {
            _logger.LogInformation(
                "Skipping payment reminder for order {OrderId} - status is {Status}",
                orderId, order?.Status);
        }
    }

    private async Task HandleOrderExpiration(CancellationToken attemptCancellationToken)
    {
        var orderId = this.GetPrimaryKey();
        var order = await _orderService.GetOrderAsync(orderId, attemptCancellationToken);
        
        if (order?.Status == OrderStatus.Pending)
        {
            await _orderService.CancelOrderAsync(orderId, attemptCancellationToken);
            _logger.LogInformation("Order {OrderId} expired and canceled", orderId);

            // Notify customer
            var emailGrain = _grainFactory.GetGrain<IEmailGrain>(order.CustomerEmail);
            await emailGrain.ScheduleEmail(
                "Order Expired",
                $"Your order {orderId} has expired due to pending payment.",
                DateTimeOffset.UtcNow);
        }
        else
        {
            _logger.LogInformation(
                "Order {OrderId} did not expire - status is {Status}",
                orderId, order?.Status);
        }
    }
}

// Supporting types
public class OrderDetails
{
    public string CustomerId { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public decimal Amount { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}
```

<a id="how-it-works"></a>

## 如何运作

<a id="storage-architecture"></a>

### 存储结构
每一持久工作碎片都有日记身份,并持续拥有所有权元数据。
日记支持的碎片管理器在存储中发现有碎片身份
单表和回收通过配置的日记格式状态。

供应商Blob公司将WAL存放在 `wal/{journalId}` 检查站和检查站关检查站的检查站设置的检查站和检查站站及检查站检查站检查站和检查站
配置容器内`checkpoints/{journalId}/{snapshotId}` 。
表格提供者存储配置中的日记头和数据代数
表格中提供方元数据选择承诺的检查站或生成点;调用者
元数据包含持久工作碎片的发现和所有权属性。

<a id="shard-ownership-and-high-availability"></a>

### 硬所有权和高可用性
1. **最佳货币**:当多个 silos 试图要求硬块时,ETags防止相互矛盾的更新
2. **所有权转让**:当 silo 失败时,其他 silos 发现失败并声称是孤儿碎片
3. **创建者优先级**:创建碎片的 silo 如果失去所有权,则优先收回
4. **自动清除**:在处理后自动删除空碎片

<a id="job-lifecycle-with-azure-storage"></a>

### 具有 Azure 储存的作业生命周期
```
┌─────────────────────┐
│  Job Scheduled      │ ──▶ Committed to the shard journal
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│  Waiting in Shard   │ ──▶ Persisted in Azure journal storage
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│  Shard Owned        │ ──▶ Silo acquires ownership via metadata update
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│  Job Executed       │ ──▶ Handler invoked on target grain
└─────────────────────┘
         │
         ├──▶ Success ──▶ Completion persisted in the journal
         │
         └──▶ Failure ──▶ Retry: Updated due time in the journal
                          No Retry: Job entry removed
```

<a id="performance-considerations"></a>

## 业绩考量

<a id="concurrency-settings"></a>

### 货币汇兑设置
```csharp
services.Configure<DurableJobsOptions>(options =>
{
    // Adjust based on your workload and Azure Storage limits
    options.MaxConcurrentJobsPerSilo = 50;
});
```

<a id="storage-costs"></a>

### 储存费用
存储的 WAL/ 检查点字节或表格代代、 附加批次的账户,
回收读物、检查站出版物和清理、所有权元数据更新
标准与溢价公司使用同一供应商
业务的账户定价和服务特点不同。

使用 `orleans-journaling-provider-catalog-pages`、 `catalog-items`、
`catalog-entries` (同前注)比较目录的跨
交付成果。 `orleans-journaling-provider-retries` 直列数
提供重试。 基准对这些对齐这些对面的
测量的运行量、结果、有效载荷字节和潜伏。

通过主机自有 Azure SDK 配置请求的计算、 时间和结果
诊断、误差整合或其他应用仪器。
与 Azure 服务度量和SDK重试的调整
上传行为。

<a id="monitoring-and-troubleshooting"></a>

## 监测和排除问题

<a id="enable-logging"></a>

### 启用日志
```csharp
builder.Logging.AddFilter("Orleans.Durable.Jobs", LogLevel.Information);
builder.Logging.AddFilter("Orleans.Durable.Jobs.AzureStorage", LogLevel.Information);
```

<a id="key-metrics-to-monitor"></a>

### 监测的关键度量
- **硬质指定时间**:要求拥有未指派碎片所有权的时间
- **工作执行期限**:在规定时间与实际执行之间的时间
- **再试率**:需要重新尝试的工作所占百分比
- **球形操作**:每分钟读/写/删除操作数

<a id="security-best-practices"></a>

## 安保最佳做法

<a id="use-managed-identity"></a>

### 使用管理身份
```csharp
var credential = new DefaultAzureCredential();
var blobServiceClient = new BlobServiceClient(storageAccountUri, credential);
```

<a id="network-security"></a>

### 网络安保网
- 启用防火墙规则限制访问
- 使用私人终端点加强安保
- 考虑 Azure 虚拟网络整合

<a id="access-control"></a>

### 出入控制
```csharp
// Minimum required permissions:
// - Storage Blob Data Contributor (for read/write/delete operations)
// - Or custom role with:
//   - Microsoft.Storage/storageAccounts/blobServices/containers/read
//   - Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read
//   - Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write
//   - Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Azure 布球存储文档](https://learn.microsoft.com/azure/storage/blobs/)
- [Orleans 持久就业核心一揽子方案](../../Orleans.DurableJobs/README.md)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
