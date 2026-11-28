<a id="microsoft-orleans-journaling-for-redis"></a>

# Microsoft Orleans Redis 日志化存储

<a id="introduction"></a>

## 简介

Microsoft Orleans Journaling for Redis 提供由 Redis 支持的 Orleans 日志化存储与目录抽象实现。Orleans 持久化作业可以将 Orleans Journaling 用作后端存储，因此本提供程序能够通过日志化层持久化作业状态。

提供程序将每个日志存储为 Redis 字符串数据及 Redis 哈希元数据。单个日志的读取和变更使用原子 Lua 脚本，因此每次追加或替换要么完整写入，要么完全不写入。每个日志是一个 Redis 字符串，因此整个日志（包括此次追加）必须符合服务器的字符串大小限制（`proto-max-bulk-len`，默认为 512 MB）；超限写入会在脚本修改任何键之前失败。日志发现扫描每台已连接的 Redis 主节点上的元数据键。默认键映射在键中保留日志 ID，因此发现过程无需读取元数据哈希。请根据应用程序所需的持久性保证配置 Redis 持久化，例如启用 AOF 并选择合适的 `appendfsync` 设置。

<a id="getting-started"></a>

## 入门

安装该包：

```shell
dotnet add package Microsoft.Orleans.Durable.Redis --prerelease
```

配置 Silo：

```csharp
using Orleans.Durable.Journaling;
using StackExchange.Redis;

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        .AddRedisJournalStorage(options =>
        {
            options.ConfigurationOptions = ConfigurationOptions.Parse("localhost:6379");
        });
});
```

将 `Orleans:Journaling:{name}:ProviderType` 设置为 `Redis`，以配置命名日志存储提供程序。对于 Grain 日志化存储，使用 `Default` 作为名称。如果 Redis 连接已经在依赖注入中注册，通过 `Orleans:Journaling:{name}:ServiceKey` 选择键控客户端。每个条目都有各自的连接和存储选项。

`AddRedisJournalStorage` 还通过 `AddJournaling` 注册核心服务。将
`IDurableStateManager` 注入 Grain，在构造期间使用
`GetOrAddState<TState>(name)` 或其强类型辅助方法（例如 `GetOrAddValue<int>("count")`）声明状态。
Orleans 在应用程序方法运行之前恢复这些状态。键控注入解析到同一个命名
对象，`DurableGrain` 仍是一个便捷基类。初始化后，`GetOrAdd` 解析
已有状态；添加不存在的名称会立即失败。返回成功前，等待 `WriteStateAsync`，以获得整个管理器范围内的
日志写入确认。

<a id="journal-discovery"></a>

## 日志发现

使用 `IJournalStorageCatalog.ListAsync`，并可选提供 `JournalCatalogListOptions`，以按日志 ID 前缀进行过滤：

```csharp
await foreach (var entry in catalog.ListAsync(
    new JournalCatalogListOptions { Prefix = JournalId.Create("jobs") },
    cancellationToken))
{
    var journalId = entry.Id;
}
```

`JournalCatalogListOptions.Prefix` 使用序数比较的 `StartsWith` 匹配原始日志 ID，包括部分路径段。例如，`new JournalId("jobs/2026/0")` 同时匹配 `jobs/2026/01` 和 `jobs/2026/09`。`JournalCatalogListOptions.MinId` 和 `MaxId` 增加包含端点的序数比较上下界；默认值表示不限制。所有过滤条件同时应用，并在枚举开始时取快照。交集为空时不执行扫描或元数据读取。

条目包含日志 `Id` 和 null 值的 `Metadata`，即使请求了 `IncludeMetadata` 也是如此。
Redis 键扫描提供标识；需要完整元数据的调用方使用
`IJournalStorage.GetMetadataAsync` 获取元数据。列表操作仍具有原来的键扫描和规范 ID 读取成本。

`IJournalMetadata.FormatKey` 和 `JournalMetadata.FormatKey` 公开已存储的格式键。
这一 API 名称保持 Redis 元数据字段、键布局和格式键值不变。

使用默认 `GetKeyName` 映射时，发现过程为以编码后的原始前缀开头的元数据键提供转义后的 `SCAN MATCH` 模式。`MinId` 和 `MaxId` 的公共前缀可进一步缩小该模式的匹配范围。返回的键包含日志 ID；在本地解码并按上下界过滤，无需 `HGET` 请求。格式不正确的匹配键会导致错误，而非被静默忽略。

自定义 `GetKeyName` 映射不能安全地将逻辑日志前缀或边界转换为 Redis 键名。因此，发现过程扫描配置键前缀下的所有元数据键，并以最多 128 个并发读取为一批，从其 `$journal-id` 字段读取规范日志 ID，再在本地应用过滤条件。以原子方式检查缺失的元数据，以区分删除和数据损坏。

发现过程按遍历顺序逐步产出 ID，无需对整个目录排序或缓冲。Redis `SCAN` 是无序的，因此遇到未来 ID 不会结束遍历。需要按到期顺序处理的使用者必须使用 `StringComparer.Ordinal` 对所选 ID 排序。遍历过程中去除重复 ID：对于 N 个不同的匹配 ID，使用 O(N) 的已见 ID 记录内存。整个遍历过程不会保留所有元数据键，因此自定义映射可能对重复扫描结果多次读取元数据。

**`SCAN MATCH` 仍会在服务器上遍历 Redis 键空间。** 缩小前缀范围可减少返回的键和客户端工作量，但不会减少服务器端键空间扫描。任意序数比较范围在客户端过滤，而非通过索引定位。Redis `SCAN COUNT` 的值 250 只是提示，不是严格的响应大小或服务器工作量上限。枚举器的一次推进可能跨过许多空扫描结果或不匹配的扫描结果，且 Redis/客户端的扫描缓冲不受元数据批次大小限制。发现过程观察实时存储，而非快照。使用默认映射时，被并发删除的元数据键仍可能产出其日志 ID；使用者必须处理读取时日志已经不存在的情况。

存储错误和取消直接传播，不进行提供程序级重试、重新开始或返回成功的回退处理。主节点断开连接或没有任何主节点均属于错误；后续服务器可能在先前的 ID 已经产出后发生故障。提前停止时释放枚举器。扫描和元数据操作之间会检查取消，但不会中止正在执行的 Redis 元数据请求。

<a id="catalog-metrics"></a>

## 目录指标

`Microsoft.Orleans` 度量器通过 `orleans-journaling-provider-catalog-items` 记录目录遍历
消耗的扫描键，并通过 `orleans-journaling-provider-catalog-entries` 记录经过过滤和去重后
产出的标识。两者都使用 `provider=redis`，其中 redis 是
本库定义的简称。空逻辑范围不产生
度量记录；提前释放和取消保留已消耗键和已交付条目的
计数。

已有日志化指标提供存储延迟和操作结果。应用程序通过托管集成或遥测插桩库提供命令
遥测。针对性诊断时，
同一个连接多路复用器上的 [StackExchange.Redis 性能分析](https://seredis.dev/Profiling_v2)
提供命令排队、发送和响应耗时。复用已有的客户端遥测插桩机制，并将
额外性能分析会话限定于所调查的工作。共享连接仍由调用方管理
生命周期和诊断配置。

<a id="redis-key-layout"></a>

## Redis 键布局

键采用 `<keyPrefix>:journal:{<SHA256(keyName)>}:<encodedKeyName>:metadata` 的形式，或者采用相同基础键加 `:data` 后缀。这一布局同时适用于默认和自定义 `GetKeyName` 映射。可逆键名位于 SHA-256 哈希标签之外，使每个日志的数据和元数据在 Redis Cluster 中位于同一哈希槽，以便执行原子 Lua 操作。URI 转义编码 Unicode 标量值，`%uXXXX` 则用四位大写十六进制数字编码每个未配对的 UTF-16 代理代码单元。字面百分号被转义为 `%25`，因此原始日志 ID 可往返转换，并且不会与转义序列或替换字符混淆。存储的 `$journal-id` 使用相同编码，以便在 Redis 字符串传输和自定义映射发现过程中保留标识。原生扫描模式会转义配置键前缀中的 glob 通配字符，并在本地应用序数比较过滤之前扩展含部分代理代码单元的前缀。

<a id="documentation"></a>

## 文档

- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [Redis 文档](https://redis.io/docs/latest/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告
- 欢迎贡献！请查阅我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
