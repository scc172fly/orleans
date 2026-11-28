<a id="microsoft-orleans-durable-jobs-for-azure-storage"></a>

# Microsoft Orleans 的 Azure Storage 持久化作业提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage 持久化作业提供程序通过 Azure Blob 或 Azure Table 日志提供程序持久化已调度的作业。作业在 Silo 重启、Grain 停用以及集群重新配置后仍然存在。`UseAzureBlobDurableJobs` 配置使用追加 Blob 的预写日志（WAL）和使用块 Blob 的检查点；`UseAzureTableDurableJobs` 配置 Table 日志头部和数据世代。两者均注册基于日志的分片管理器以及持久化作业的 JSON 序列化元数据。

<a id="getting-started"></a>

## 入门

<a id="installation"></a>

### 安装
通过 NuGet 安装此包及核心包：

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
```

<a id="configuration"></a>

### 配置

选择 `UseAzureBlobDurableJobs` 并配置 `AzureBlobJournalStorageOptions`，或选择 `UseAzureTableDurableJobs` 并配置 `AzureTableJournalStorageOptions`。两者均支持通过 `ISiloBuilder` 和 `IServiceCollection` 注册。Table 配置接受 `TableServiceClient` 和 `TableName`；Blob 配置接受 `BlobServiceClient` 和 `ContainerName`。集群管理使用适当的 Table 服务，包括日志存储采用高级 BlockBlobStorage 帐户时使用独立的标准帐户。

[持久化作业日志演练项目](../../../playground/DurableJobsJournaling/README.md) 提供可运行的 Blob/Table 后端选择示例。[Azure 提供程序基准测试](../../../test/Benchmarks/Journaling/Azure/README.md) 在工作量受限且资源所有权明确的条件下测量追加、检查点、恢复和目录查询等工作负载。

<a id="named-providers-and-account-migration"></a>

### 命名提供程序与帐户迁移

若要显式选择存储，请注册 `AddAzureBlobJournalStorage("jobs-a", configure)` 或 `AddAzureTableJournalStorage("jobs-a", configure)`，并调用 `UseJournaledDurableJobs(options => options.ActiveProviderName = "jobs-a")`。支持构建器和服务集合两种 API。每个名称均拥有独立的后端选项、存储提供程序、目录和状态管理器工厂。命名注册不会影响默认的 Grain 日志存储。

若要将新增工作迁移到另一个帐户、容器或表，请在 `"jobs-a"` 名下保留 A 的原始命名空间，并将 B 的命名空间注册为 `"jobs-b"`：

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

同一模式也适用于不同 Table 名称或客户端，以及从 Blob 切换到 Table 的情形。每个物理命名空间都应注册在一个选定的名称下；在某个名称仍包含工作时修改其帐户或映射，会改变查找这些作业的位置。

仅选择一个提供程序时，未缓存的分片查找直接读取其元数据。同时选择 B 和 A 时，发现过程读取两个目录，已知 ID 的查找则根据需要先检查 B，再检查 A。现有作业的重试、重新调度、取消和删除等操作仍写入 A。新分片仅在 B 中创建。分片 ID 和 `DurableJob` 句柄仍基于时间戳和 GUID，且与提供程序无关。存储错误与成功确认不存在的结果始终明确区分。

启用 B 的写入之前，先向所有负责调度的 Silo 部署两个选定的存储绑定，以 A 为写入提供程序、B 为排空提供程序。随后滚动部署，以 B 为写入提供程序、A 为排空提供程序。这些设置在启动时生效。若需要严格的切换边界，应暂停调度；否则仍采用 A 配置的 Silo 可以继续创建 A 中的分片，直至滚动部署及这些 Silo 已接纳的调度请求全部完成。

在整个排空期间，保持 A 的身份具有列举、读取、条件元数据更新、日志写入、压实以及删除权限。这些权限使现有作业能够继续执行，并使其分片能够被清理。保留 WAL/检查点或 Table 分区映射，以及恢复两个提供程序所需的读取器。

使用 `IDurableJobsStorageInspector.InspectAsync("jobs-a", cancellationToken)` 获取完整的 `jobs/shards/` 清单，其中包括未来到期的工作及因数据或处理故障而无法正常执行的工作。清单读取失败时应报告为未知。在 **所有写入方** 均完成切换后，必须取得成功且完整的清单，确认分片总数为零（包括无法识别的条目），然后才能移除 A。停用旧提供程序需要将这些实时观察结果与集群范围内写入方切换完成的独立证据相结合。

[迁移示例](../../../samples/DurableJobsMigration/README.md) 使用磁盘后端的 Azurite、两个 Blob 命名空间、独立的准备和排空进程，以及本地清单报告。有关回滚和停用旧提供程序的条件，请参阅[核心迁移指南](../../Orleans.DurableJobs/README.md#cut-over-and-retire-a-provider)。

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

#### 使用托管标识（推荐用于生产环境）
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

#### 使用高级选项
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

## 使用示例

<a id="email-scheduling-with-cancellation"></a>

### 可取消的邮件调度
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

### 包含多个调度步骤的订单工作流
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

## 工作原理

<a id="storage-architecture"></a>

### 存储架构
每个持久化作业分片均具有日志标识和持久化的所有权元数据。基于日志的分片管理器通过存储目录发现分片标识，并使用配置的日志格式恢复状态。

Blob 提供程序在配置的容器内将 WAL 存储于 `wal/{journalId}`，并将检查点存储于 `checkpoints/{journalId}/{snapshotId}`。Table 提供程序将日志头部和数据世代存储于配置的表中。提供程序元数据用于选定已提交的检查点或世代；调用方元数据携带持久化作业分片的发现和所有权属性。

<a id="shard-ownership-and-high-availability"></a>

### 分片所有权与高可用性
1. **乐观并发**：多个 Silo 尝试取得同一分片的所有权时，ETag 可防止相互冲突的更新。
2. **所有权转移**：某个 Silo 发生故障时，其他 Silo 会检测到故障并认领失去所有者的分片。
3. **创建方优先**：创建分片的 Silo 在失去该分片的所有权后，具有重新认领该分片的优先权。
4. **自动清理**：处理完成后，空分片会被自动删除。

<a id="job-lifecycle-with-azure-storage"></a>

### 使用 Azure Storage 时的作业生命周期
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

## 性能注意事项

<a id="concurrency-settings"></a>

### 并发设置
```csharp
services.Configure<DurableJobsOptions>(options =>
{
    // Adjust based on your workload and Azure Storage limits
    options.MaxConcurrentJobsPerSilo = 50;
});
```

<a id="storage-costs"></a>

### 存储成本
应将 WAL/检查点存储字节数或 Table 数据世代、追加批次、恢复读取、检查点发布和清理、所有权元数据更新，以及目录列举的分页次数纳入成本计算。标准 Blob 与高级 Blob 使用相同的提供程序操作，但帐户定价和服务特性不同。

使用 `orleans-journaling-provider-catalog-pages`、`catalog-items` 和 `catalog-entries`（后两者使用相同前缀）比较目录遍历及实际返回的结果。`orleans-journaling-provider-retries` 统计提供程序显式重试的次数。基准测试将这些计数器与独立测得的操作次数、结果、有效载荷字节数和延迟配合使用。

通过宿主管理的 Azure SDK 诊断、Aspire 集成或其他应用程序检测机制，配置请求次数、耗时和结果的采集。结合 Azure 服务指标以及 SDK 的重试和上传行为，核对事务成本。

<a id="monitoring-and-troubleshooting"></a>

## 监控与故障排查

<a id="enable-logging"></a>

### 启用日志记录
```csharp
builder.Logging.AddFilter("Orleans.Durable.Jobs", LogLevel.Information);
builder.Logging.AddFilter("Orleans.Durable.Jobs.AzureStorage", LogLevel.Information);
```

<a id="key-metrics-to-monitor"></a>

### 需要监控的关键指标
- **分片分配耗时**：认领尚未分配的分片所有权所需的时间。
- **作业执行延迟**：从到期时间到实际执行的时间差。
- **重试率**：需要重试的作业百分比。
- **Blob 操作数**：每分钟读取、写入及删除操作的次数。

<a id="security-best-practices"></a>

## 安全最佳实践

<a id="use-managed-identity"></a>

### 使用托管标识
```csharp
var credential = new DefaultAzureCredential();
var blobServiceClient = new BlobServiceClient(storageAccountUri, credential);
```

<a id="network-security"></a>

### 网络安全
- 启用防火墙规则以限制访问。
- 使用专用终结点以增强安全性。
- 考虑集成 Azure Virtual Network。

<a id="access-control"></a>

### 访问控制
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

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Azure Blob Storage 文档](https://learn.microsoft.com/azure/storage/blobs/)
- [Orleans 持久化作业核心包](../../Orleans.DurableJobs/README.md)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。
