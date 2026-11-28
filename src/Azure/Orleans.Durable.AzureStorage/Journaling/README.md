<a id="microsoft-orleans-journaling-for-azure-storage"></a>

# Microsoft Orleans 的 Azure Storage 日志提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage 日志提供程序提供 Orleans Journaling 的 Azure Storage 实现，使应用程序能够以 Azure Storage 为后端存储，对 Grain 操作进行日志记录和跟踪。

Blob 名称根据配置的日志存储标识派生，不使用日志格式的文件扩展名。Azure 追加 Blob 在 Blob 元数据中保存日志格式键；若选定的日志格式提供 MIME 类型，则以该内容类型创建 Blob。可通过 `AzureBlobJournalStorageOptions.GetWalBlobName` 和 `GetCheckpointBlobName` 自定义 WAL Blob 名称和检查点 Blob 名称。每次追加均作为一个最大 100 MiB 的追加块写入，因此 Azure 要么提交整个批次，要么完全不提交；较大的批次会在发送任何请求之前失败。替换操作上传一个检查点 Blob，并通过一次条件 WAL 更新发布该检查点。

提供程序注册还会调用 `AddJournaling`，注册核心服务、持久化状态工厂和激活生命周期集成。应用程序代码可以将 `IDurableStateManager` 注入普通 `Grain`，也可以使用 `DurableGrain` 作为便捷基类。通过 `GetOrAddState<TState>(name)`、其强类型辅助方法或键控注入，在初始化之前声明状态。对相同名称的兼容请求会返回同一个对象，而一次 `WriteStateAsync` 即可确认它们共享的日志批次。

日志元数据通过 `IJournalMetadata.FormatKey` 和 `JournalMetadata.FormatKey` 公开其存储的格式键。虽然 API 名称发生了变更，但提供程序已持久化的元数据键、Blob 名称、Table 属性和格式键值保持不变。

<a id="using-an-alternative-blob-layout"></a>

## 使用其他 Blob 布局

默认情况下，WAL Blob 名称为 `wal/<journalId>`，检查点 Blob 名称为 `checkpoints/<journalId>/<snapshotId>`。独立的前缀使目录发现能够直接选择 WAL Blob，避免保留的检查点出现在列举结果页中。配置 Blob 命名委托即可采用其他布局，例如共享前缀、文件扩展名、租户专用路径，或与现有存储约定一致的名称。每个委托返回相对于容器的 Blob 名称；检查点名称应包含提供的快照 ID，以避免冲突。目录发现使用配置容器中的默认 WAL 布局；参与目录发现的自定义委托需要为每个日志生成 `wal/<journalId>`。

```csharp
siloBuilder.AddAzureBlobJournalStorage(options =>
{
    options.GetWalBlobName = static journalId => $"journals/{journalId.Value}.wal";
    options.GetCheckpointBlobName = static (journalId, snapshotId) => $"journals/{journalId.Value}.{snapshotId}.chk";
});
```

<a id="azure-table-journal-storage"></a>

## Azure Table 日志存储

此包还提供 Azure Table 实现 `AddAzureTableJournalStorage`。每个日志存储在一个 Table 分区内：头部行携带日志清单，并充当乐观并发控制的隔离屏障；日志字节存储在数据行中，行键按序号排列当前世代的数据。每次追加均在一个实体组事务中同时提交追加数据行和头部更新；恢复时只需一次分区范围查询即可重放整个日志，避免追加 Blob 重放时逐块读取的节奏限制。由于追加受实体组事务上限（100 个实体和 4 MiB）约束，单个日志批次不得超过 2 MiB，较大的批次会在发送任何请求之前失败；替换操作不受此大小限制，其数据行仅在头部更新发布这些行时才变为可见。

发布尝试失败后，保留已暂存的数据行，使延迟到达的头部更新仍可发布一个完整的世代。删除日志会回收该分区中保留的数据行。

```csharp
siloBuilder.AddAzureTableJournalStorage(options =>
{
    options.TableName = "journal";
    options.TableServiceClient = tableServiceClient;
});
```

可使用 `AzureTableJournalStorageOptions.GetPartitionKey` 应用自定义分区布局。返回的键必须对于每个日志唯一、符合 Azure Table 分区键限制，且最多包含 1,024 个字符。规范日志 ID 存储在头部中，因此即使采用自定义映射，目录列举仍然准确。

<a id="catalog-enumeration"></a>

## 目录枚举

两种 Azure 提供程序均实现 `IJournalStorageCatalog.ListAsync`，按服务遍历顺序增量返回 `JournalCatalogEntry` 值。每个条目通过 `Id` 携带其日志标识。`JournalCatalogListOptions.Prefix` 是按序数比较的原始字符串前缀，可以在某个段的内部结束；若仅选择命名空间内部的条目，则使用末尾带 `/` 的前缀。`MinId` 和 `MaxId` 是包含边界值的序数比较上下界，默认均不限制。所有约束同时生效，并在枚举开始时生成快照。需要按到期时间排序的使用方必须使用 `StringComparer.Ordinal` 对选定的 ID 排序。提供程序在内部处理服务的分页续传，并先产出当前页中的条目，再获取下一页。

设置 `JournalCatalogListOptions.IncludeMetadata`，可使每个条目的 `Metadata` 包含其格式、ETag 和调用方拥有的属性。该快照在同一个列举响应中观察得到，其语义与 `GetMetadataAsync` 相同；可将其 ETag 提供给 `UpdateMetadataAsync` 以执行条件更新。Blob 列举请求元数据特征，而 Table 列举在选择标识和时间戳的同时，还选择头部的格式及序列化后的调用方元数据。两种提供程序都直接根据列举结果页构建快照。默认情况下，枚举仅投影标识，并将 `Metadata` 设为 `null`。`IncludeMetadata` 与范围选项一同在枚举开始时生成快照。

Blob 目录扫描配置的 `ContainerName`，将名为 `wal/<journalId>` 的追加 Blob 解释为日志标识。当自定义命名委托或容器工厂生成相同条目时，此遍历方式同样适用。请求使用 `wal/` 后接原始日志 ID 前缀，并在可能时利用 `MinId` 和 `MaxId` 的共同前缀进一步缩小范围，每页最多返回 5000 个 Blob。检查点占用独立的命名空间，因此在分页之前即被排除，即使目录查询未设置范围限制也一样。恢复和检查点清理使用 WAL 元数据中发布的完整检查点名称。

对于 ASCII 边界，使用 `GetBlobsOptions.StartFrom` 和上界截断条件缩小遍历范围，两者同时适用于平面命名空间和分层命名空间（HNS）。HNS 递归列举会将 `/` 排在其他字符之前。在某个边界值与服务原生列举前缀共有的前缀之后，提供程序在第一个小于或等于 `/` 的字符处放宽边界：下界在该字符之前结束，而上界将该字符替换为 `0` 并在此处结束。其他 ASCII 边界保留其确切值。例如，从 `a!` 到 `a/0` 的查询从 `wal/a` 开始，并在 `wal/a0` 之后停止。提供程序在产出每个候选项前，都会按原始的包含式序数比较边界重新检查候选项。非 ASCII 边界在本地强制执行。

共同前缀使时间戳范围扫描保持在较小的范围内。跨越标点符号或目录边界的范围可能会传输额外的候选项。上界终止发生在消费结果时：提供程序读取跨越保守上界的结果页，随后结束遍历。约束的交集为空时，不发送任何请求。两种帐户类型均使用配置的 Blob 客户端和普通列举请求，采用相同的范围规划方式。

Table 枚举每个服务结果页最多请求 1000 个头部行，并从规范的头部属性 `JournalId` 读取标识。默认分区映射接受可打印 ASCII 日志 ID（`0x20` 至 `0x7E`），将每个字节编码为两个大写十六进制数字，从而保留序数排序和原始前缀语义（包括不完整的段）。因此，1,024 字符的分区键上限意味着每个日志 ID 最多允许 512 个字符。验证发生在创建存储时。这一限制属于默认 Table 映射；自定义映射和其他提供程序仍遵循各自的日志 ID 约定。默认映射查询将头部行键与基于索引的分区键约束组合，分别应用原始前缀、包含边界值的 `MinId` 和 `MaxId`。查询边界保留其原始序数比较语义，包括超出已存储 ASCII 字符集合或已存储 ID 长度上限的边界。分区键筛选字面量保持在 1,024 字符以内：512 字符的前缀仅选择完全匹配的 ID，更长的前缀产生空结果，更长的边界则与其前 512 个字符进行比较，其中下界使用严格大于比较，上界使用包含式小于或等于比较。交集为空时不发起查询。自定义映射将格式正确的 Unicode 边界应用于规范 `JournalId` 属性；包含不成对代理项的边界在本地强制执行。这保留了序数比较的范围语义，但可能需要扫描整张表。未限定范围的枚举也会扫描整张表中的头部。产出条目之前会再次检查约束，而枚举器的一次推进可能跨越多个空页或经过筛选的结果页。

使用 `await foreach`，或者在提前停止时释放自行保留的枚举器。传入覆盖整个遍历生命周期的取消令牌；取消和服务错误会传播给调用方。

<a id="runtime-metrics"></a>

## 运行时指标

`Microsoft.Orleans` 指标仪表使用 `provider=azure_blob` 或 `provider=azure_table` 标签记录目录遍历，这些短名称由此库定义。`orleans-journaling-provider-catalog-pages` 统计收到的目录结果页数，包括空页。`orleans-journaling-provider-catalog-items` 统计这些结果页在本地筛选前的原生候选项数量，`orleans-journaling-provider-catalog-entries` 统计实际产出给调用方的条目数量。获取结果页失败以及枚举器到达末尾的推进操作均不增加页数。`orleans-journaling-provider-retries` 使用取值范围受限的 `reason` 标签统计提供程序显式重试的次数。

现有 `orleans-journaling-azure-blob-*` 和 `orleans-journaling-azure-table-*` 指标继续记录存储延迟、结果和字节数。应用程序通过 Aspire 集成、遥测插桩库或 [Azure SDK 诊断配置](https://learn.microsoft.com/dotnet/azure/sdk/logging) 提供客户端 SDK 遥测数据。复用已为注册客户端配置的诊断机制。请求和响应事件包含 HTTP 状态以及 I/O 耗时；在应用程序边界配置数据采集和敏感内容处理。由应用程序提供的容器工厂保留对其初始化和客户端调用的控制权。服务端事务指标可用于账单核对。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
```

<a id="example---configuring-azure-storage-journaling"></a>

## 示例：配置 Azure Storage 日志存储

日志提供程序从依赖注入（DI）容器解析已注册的 `BlobServiceClient`。获取该客户端的方式取决于应用程序的宿主模型。

<a id="authentication-options"></a>

### 身份验证选项

对于生产工作负载，优先通过 `DefaultAzureCredential` 使用 Microsoft Entra（Azure AD）凭据，而不是长期有效的连接字符串：

```csharp
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddSingleton(_ =>
    new BlobServiceClient(
        new Uri("https://<your-account>.blob.core.windows.net"),
        new DefaultAzureCredential()));
```

若要与 .NET Aspire 集成（如随附的 `JournalingAzureBlobJson` 示例所示），AppHost 会提供一个连接字符串，使用方项目通过 `AddAzureBlobServiceClient` 解析该连接字符串。Aspire 在开发环境中配置本地模拟器凭据，在生产环境中配置基于 Entra 的凭据。

对于临时本地开发，可以通过连接字符串注册 `BlobServiceClient`（例如使用 Azurite 的 UseDevelopmentStorage 简写形式）。不要将生产环境连接字符串嵌入源代码。

<a id="wiring-it-into-the-silo"></a>

### 将其接入 Silo

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Configuration;
using Orleans.Durable.Journaling.Json;
using System.Text.Json.Serialization;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyGrainNamespace;

[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ulong))]
internal partial class JournalJsonContext : JsonSerializerContext;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Azure Storage as a journaling provider
            .AddAzureBlobJournalStorage(optionsBuilder =>
            {
                optionsBuilder.Configure((options, serviceProvider) => options.BlobServiceClient = serviceProvider.GetRequiredService<BlobServiceClient>());
            })
            // JSON Lines is the default journaling format. Register metadata for all journaled payload types.
            .UseJsonJournalFormat(JournalJsonContext.Default);
    });

var host = await builder.StartAsync();

// Get a reference to the grain
var shoppingCart = host.Services.GetRequiredService<IGrainFactory>()
    .GetGrain<IShoppingCartGrain>("user1-cart");

// Use the grain
await shoppingCart.UpdateItem("apple", 5, 0);
await shoppingCart.UpdateItem("banana", 3, 1);

// Get and print the cart contents
var (contents, version) = await shoppingCart.GetCart();
Console.WriteLine($"Shopping cart (version {version}):");
foreach (var item in contents)
{
    Console.WriteLine($"- {item.Key}: {item.Value}");
}

// Wait for the application to terminate
await host.WaitForShutdownAsync();
```

<a id="example---using-journaling-in-a-grain"></a>

## 示例：在 Grain 中使用日志存储
```csharp
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Durable.Journaling;

namespace MyGrainNamespace;

public interface IShoppingCartGrain : IGrainWithStringKey
{
    ValueTask<(bool success, long version)> UpdateItem(string itemId, int quantity, long version);
    ValueTask<(Dictionary<string, int> Contents, long Version)> GetCart();
    ValueTask<long> GetVersion();
    ValueTask<(bool success, long version)> Clear(long version);
}

public class ShoppingCartGrain(
    [FromKeyedServices("shopping-cart")] IDurableDictionary<string, int> cart,
    [FromKeyedServices("version")] IDurableValue<long> version) : DurableGrain, IShoppingCartGrain
{
    private readonly IDurableValue<long> _version = version;

    public async ValueTask<(bool success, long version)> UpdateItem(string itemId, int quantity, long version)
    {
        if (_version.Value != version)
        {
            // Conflict
            return (false, _version.Value);
        }

        if (quantity == 0)
        {
            cart.Remove(itemId);
        }
        else
        {
            cart[itemId] = quantity;
        }

        _version.Value++;
        await WriteStateAsync();
        return (true, _version.Value);
    }

    public ValueTask<(Dictionary<string, int> Contents, long Version)> GetCart() => new((cart.ToDictionary(), _version.Value));
    public ValueTask<long> GetVersion() => new(_version.Value);

    public async ValueTask<(bool success, long version)> Clear(long version)
    {
        if (_version.Value != version)
        {
            // Conflict
            return (false, _version.Value);
        }

        cart.Clear();
        _version.Value++;
        await WriteStateAsync();
        return (true, _version.Value);
    }
}
```

带键的构造函数参数在设置阶段声明两个状态。Orleans 在 Grain 方法运行之前恢复它们。随后调用 `StateManager.GetOrAddDictionary<string, int>("shopping-cart")` 会返回已注入的 `cart` 实例；初始化完成后尝试添加不存在的名称将立即失败。在不同部署之间保持名称 `"shopping-cart"` 和 `"version"` 稳定。`WriteStateAsync` 通过共享管理器确认两个状态中待提交的更改。

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [Azure Blob Storage 文档](https://learn.microsoft.com/azure/storage/blobs/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。
