<a id="microsoft-orleans-journaling-for-azure-storage"></a>

# Microsoft Orleans 日刊 Azure 储存

<a id="introduction"></a>

## 简介
Microsoft Orleans 日刊 Azure 储存提供 Azure 执行《公约》的储存、储存和储存 Orleans 能够进行日记和跟踪 grain 使用 Azure 储存作为后援商店。

黑球名称来自已配置的日记存储身份,不使用日记格式文件扩展 。 Azure 附加 blobs 将日记格式键存储在 blob 元数据中, 当选中的日记格式提供 MIME 类型时, 则以该内容类型创建。 WAL 浮点名称和检查站布罗名可以使用用户化 `AzureBlobJournalStorageOptions.GetWalBlobName` 和 `GetCheckpointBlobName`。每个附件的文字化为最多100个的一个附件块 MiB, 所以 Azure 将整批或无一批承诺; 大批在发出任何请求前失败。 替换上传检查点布, 并通过有条件的 WAL 更新发布 。

提供方登记登记也呼叫 `AddJournaling` 核心服务、耐久国家工厂和
激活生命周期整合。应用代码可以将 `IDurableStateManager` 注入普通
`Grain` 或使用 `DurableGrain` 作为方便基级。
`GetOrAddState<TState>(name)`、其打字助手或键入。对相同的请求也相同
返回同一对象,一个 `WriteStateAsync` 确认其共享日记批次。

日记元数据将存储的格式密钥显示为 `IJournalMetadata.FormatKey` 和
`JournalMetadata.FormatKey`. 供应商的元数据键、 浮标名称、 表格属性,
和格式键值通过此 API 命名更改保持稳定。

<a id="using-an-alternative-blob-layout"></a>

## 使用另选布束布局

默认情况下, WAL 的布线标名为 `wal/<journalId>` , 检查站的布线标名为 `checkpoints/<journalId>/<snapshotId>`。 单独的前缀允许目录发现直接选择 WAL 的布线, 使保留过的检查点不在列表页中。 配置 blob 的名称代表以使用替代布局, 如共享的前缀、 文件扩展、 租户专用路径或符合现有存储协议的名称 。 每个代表返回一个容器- 相对的布线名称, 检查站的名称应包括提供的快照代号以避免碰撞 。 分类发现使用了配置的容器中的默认的 WAL 版式; 参加查找的定制代表为每本日记提供 `wal/<journalId>` 。

```csharp
siloBuilder.AddAzureBlobJournalStorage(options =>
{
    options.GetWalBlobName = static journalId => $"journals/{journalId.Value}.wal";
    options.GetCheckpointBlobName = static (journalId, snapshotId) => $"journals/{journalId.Value}.{snapshotId}.chk";
});
```

<a id="azure-table-journal-storage"></a>

## Azure 表格日记账存储

软件包还提供 Azure 表执行, `AddAzureTableJournalStorage`。每个日记都存储为一个表格分区:头行携带日记清单,是乐观的con货币围栏,而日记字数则储存在按顺序排列当前生成数据的关键行中。每个附件在单个实体组交易中将其行与页眉更新一起进行,用单一分隔范围查询来恢复整个日记的重放,避免每个区块的间隔速度,以该节点作为附加 blob 重播的对象。由于附件受实体组交易限制(100 个实体和 4 MiB) ,单份期刊批数量不得超过 2 MiB,在发送任何请求之前,大批次失败;替换操作可以是任意大小,只有当标题更新时才能看到其行。

发布尝试失败后, 预设行会保留, 这样延迟的页眉更新仍然可以公布整整一代。 删除日记可回收分区保留的行 。

```csharp
siloBuilder.AddAzureTableJournalStorage(options =>
{
    options.TableName = "journal";
    options.TableServiceClient = tableServiceClient;
});
```

`AzureTableJournalStorageOptions.GetPartitionKey` 可用于应用自定义分区布局。 返回的密钥必须是每个日记独有的, 符合 Azure 表格分区密钥限制, 最多包含 1 024 个字符。 文首中存储了可选日记代号, 这样目录列表仍然与自定义映射保持准确性 。

<a id="catalog-enumeration"></a>

## 分类表列举

Azure 提供商都执行 `IJournalStorageCatalog.ListAsync`,在服务跨行顺序中递增返回 `JournalCatalogEntry` 值。每个条目都带有 `Id`的日记身份。 `JournalCatalogListOptions.Prefix` 是一个原始正统字符串前缀,可以在一段内结束;仅在命名空间内选择条目时使用尾随 `/` 。 `MinId` 和 `MaxId` 是包容性或非约束性框,默认每个框都是无限制的。所有限制都适用,并在查点开始时即刻列出。需要适当排序的消费者必须用 `StringComparer.Ordinal`排序选定的代号。提供者在获取下一页之前处理内部的服务延续并生成当前页面中的条目。

设定 `JournalCatalogListOptions.IncludeMetadata` 包括每个条目的格式、ETag和调用器拥有的财产 `Metadata`。在列名答复中一起观察了快照,其语义与 `GetMetadataAsync`;其ETATag可供应到 `UpdateMetadataAsync` 有条件更新。 组合列表列表要求元数据特性, 而表格列表列表列表则选择标题格式和序列式呼叫器元数据以及身份和时间戳。 两个提供者直接从列表页面中创建快照。 默认情况下, 点查工程识别 `Metadata` 设置至 `null`. `IncludeMetadata` 当开始计数时, 以区域选项快速切换。

Blob目录扫描配置的 `ContainerName` 并翻译附加名为 `wal/<journalId>` 的日记身份的 blobs。 当自定义命名代表或集装箱工厂生产相同的条目时,这种遍历同样适用。 请求使用 `wal/` , 之后是原始日记id前缀, 尽可能以 `MinId` 和 `MaxId` 的通用前缀缩小, 并在每页最多可达5000 blobs 。 检查点占据着自己的命名空间, 在排入行之前排除, 包括用于无登录目录查询。 恢复和检查站清理使用WAL 元数据中公布的完整的检查站名称 。

ASCII 限制使用 `GetBlobsOptions.StartFrom` 和与平坦命名空间和等级命名空间(HNS)同时使用的上端截断点。 `/` 在被约束的和本地列表前缀共享的前缀后,提供者在第一个字符前后宽放 `/`:下下界字符之前的结束值,上界替换为 `0` 。其他 ASCII 边框保留其精确值。例如,从 `a!` 中 `a/0` 开始于 `wal/a` 停止于 `wal/a0`供应商在提交之前先对照原始包容性规则界限检查每个候选人。

共享的前缀保持时间戳范围扫描的窄度。 跨越标点或目录边界的范围可以转移更多候选人。 使用结果时会发生上限终止: 提供者读过越过保守上限的页面, 然后完成跨圈。 空交叉点不发出请求。 同样的范围规划适用于使用配置的 Blob 客户端和普通列表请求的账户类型。

表列查点要求每个服务页面最多为1000页头行,读取语句中的身份 `JournalId` 属性。默认分区映射图接受可打印的 ASCII 日记 ids ()`0x20` 中 `0x7E`)和将每个字节编码为两个大写十六进制数字,保存正序顺序和原始前缀,包括部分段。因此,1,024个字符分隔键限制允许每个日记id最多512个字符。创建存储时会出现验证。这一限制属于默认的表格映射;自定义绘图和其他提供者保留其日记标识合同。默认映射查询将页眉行键与索引化的原始前缀分区密钥约束结合在一起。 `MinId`包容性和包容性 `MaxId`。查询框保留其原始的正则含义,包括存储 ASCII 字母以外的边框或超过存储的长度限制。分区键过滤器字串保持在 1 024 个字符之内:512 字元前缀选择其确切的代号,较长的前缀产生空结果,长处框与最初的512个字符相比,但比较要严格较低和包容性高处比较。空交叉点不发布查询。自定义绘图对金刚石图应用完善的 Unicode 界限。 `JournalId` 属性; 含有未偏移代孕的边框由本地强制执行。 这保存了正弦词, 可能需要对表格进行全色扫描。 未闭门查点还扫描表格上方的页眉。 在下发前再次检查限制, 一个数字预告可以跨过多个空页面或过滤页面 。

使用 `await foreach` 或处理提前停止的留存的计算器。 传递覆盖整个生命期的取消标记; 向呼叫者传播取消和服务错误 。

<a id="runtime-metrics"></a>

## 运行时间衡量

下 `Microsoft.Orleans` 以下列方式进行 `provider=azure_blob` 或 `provider=azure_table`,该库定义的简称。 `orleans-journaling-provider-catalog-pages` 包括空页在内的目录页面。 `orleans-journaling-provider-catalog-items` 本地过滤前先计数本地候选人, `orleans-journaling-provider-catalog-entries` 向调用者出示了数项。 无法检索页面和终端查点预发数, 并增加零页 。 `orleans-journaling-provider-retries` 使用约束的矩式 `reason` 标签。

现有 `orleans-journaling-azure-blob-*` 和 `orleans-journaling-azure-table-*` 应用程序通过它的喷射集成、仪器仪表图书馆或测量库为客户SDK提供遥测服务。 [Azure SDK诊断配置](https://learn.microsoft.com/dotnet/azure/sdk/logging)重新使用为已登记客户配置的诊断。请求/回复事件包括 HTTP 状态和 I/O 持续时间; 在应用程序边界配置收集和敏感内容处理; 应用程序供应的集装箱工厂保留对初始化和客户电话的控制。 服务方交易指标支持账单调节。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Durable.AzureStorage --prerelease
```

<a id="example---configuring-azure-storage-journaling"></a>

## 示例 - 配置 Azure 存储日记

日记提供者从 DI 中解析已注册 `BlobServiceClient` 。 您如何获得客户端取决于您的主机模式 。

<a id="authentication-options"></a>

### 验证选项

对于生产工作量,更喜欢 Microsoft Entra(Azure AD)具有 `DefaultAzureCredential` 的全权证书,而不是长寿命连接字符串:

```csharp
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddSingleton(_ =>
    new BlobServiceClient(
        new Uri("https://<your-account>.blob.core.windows.net"),
        new DefaultAzureCredential()));
```

如果您正在与 .NET Aspire(像捆绑的 `JournalingAzureBlobJson` 样本那样)融合, AppHost 就会发出一个连接字符串,由消耗项目通过 `AddAzureBlobServiceClient`解决。 自动发送线将开发中的本地模拟器证书和生产中的附加证书添加到本地模拟证书。

当地发展临时登记 `BlobServiceClient` 连接字符串( 如 Azurite) UseDevelopmentStorage 快捷键) 。 不要在源代码中嵌入生产连接字符串 。

<a id="wiring-it-into-the-silo"></a>

### 将之线连入至 silo

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

## 示例 - 在 Grain中使用日记
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

键盘构建参数在设置期间宣布了两个州。 Orleans 在安装之前收回
grain 方法运行。 `StateManager.GetOrAddDictionary<string, int>("shopping-cart")` 随后
返回注入的 `cart` 实例;在初始化失败后立即添加一个缺失的名称。
保留姓名 `"shopping-cart"` 和 `"version"` 部署之间保持稳定。 `WriteStateAsync`
双方通过共享管理人确认有待改变的变更。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [Azure 布球存储文档](https://learn.microsoft.com/azure/storage/blobs/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
