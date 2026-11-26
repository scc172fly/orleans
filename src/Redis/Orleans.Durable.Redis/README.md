<a id="microsoft-orleans-journaling-for-redis"></a>

# Microsoft Orleans 日刊 Redis

<a id="introduction"></a>

## 简介

Microsoft Orleans 日刊 Redis 提供 Redis- 支持执行 Orleans 日刊存储和目录摘要。 Orleans 持久工作可以使用 Orleans 日刊作为辅助商店,这样这个提供者可以通过日记层保持持久的作业状态。

提供商将每期期刊作为 Redis 字符串数据加 Redis 散列元数据。 每期杂志读取并突变使用Lua原子脚本, 所以每个附件或替换是完整或完全的文字, 是否全部写入。 每期期刊都是一册 Redis 字符串, 所以整个日记, 包括附件在内, 符合服务器的字符串大小限制( NAME OF TRANSLATORS) (SUPI)`proto-max-bulk-len`, 默认时512 MB;在脚本更改任何密钥之前超过失败的写作。《日刊》发现扫描每个连接主机上的元数据键 Redis 服务器。默认的密钥映射保护在密钥中的日记代号,因此发现时不会读取元数据 hashes。配置 Redis 具有适当附件A和附件B `appendfsync` 设置,根据您申请所需的持久性保障。

<a id="getting-started"></a>

## 入门

安装软件包 :

```shell
dotnet add package Microsoft.Orleans.Durable.Redis --prerelease
```

配置 silo:

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

设置 `Orleans:Journaling:{name}:ProviderType` 到 `Redis` 以配置名为日记存储提供者。 请使用 `Default` 作为 grain 日记的名称。 如果 Redis 连接已在自属注射中注册, 请使用 `Orleans:Journaling:{name}:ServiceKey`选择有键的客户端。 每个条目都有自己的连接和存储选项 。

`AddRedisJournalStorage` 还通过 `AddJournaling`登记核心服务。
`IDurableStateManager` 改为 grain ,并声明在建造期间与
`GetOrAddState<TState>(name)` 或其打字助手,例如 `GetOrAddValue<int>("count")`。
Orleans 在应用方法运行之前收回这些状态。
对象,并且 `DurableGrain` 仍为方便基类。在初始化后, `GetOrAdd` 解决
现有状态; 立即添加一个缺失的名称失败。 全经理的等待 `WriteStateAsync`
返回成功前确认日记账。

<a id="journal-discovery"></a>

## 日记发现

使用 `IJournalStorageCatalog.ListAsync` 具有可选 `JournalCatalogListOptions` 以通过日记 id 前缀过滤:

```csharp
await foreach (var entry in catalog.ListAsync(
    new JournalCatalogListOptions { Prefix = JournalId.Create("jobs") },
    cancellationToken))
{
    var journalId = entry.Id;
}
```

`JournalCatalogListOptions.Prefix` 与原始日记id(使用正序 `StartsWith`) 匹配, 包括部分段。例如, `new JournalId("jobs/2026/0")` 既符合 `jobs/2026/01` ,也符合 `jobs/2026/09`。 `JournalCatalogListOptions.MinId` 和 `MaxId` 添加包容性的正则下界和上界;它们的默认值是无限的。所有过滤器都一起应用,当计数开始时被截取。空交叉点不进行扫描或元数据读取 。

条目包含日记 `Id` 和 null `Metadata`,包括何时要求 `IncludeMetadata` 。
Redis 密钥扫描供应身份;需要完整的元数据检索的调用器
`IJournalStorage.GetMetadataAsync`. 列名保留了其现有的关键扫描和卡门式标识读数费用。

`IJournalMetadata.FormatKey` 和 `JournalMetadata.FormatKey` 暴露了存储的格式密钥。
今 API 国名假 Redis 元数据字段、键布局和格式键值不变。

默认 `GetKeyName` 映射后,发现提供了从编码原始前缀开始的元数据键的逃出 `SCAN MATCH` 模式。 `MinId` 和 `MaxId` 的共同前缀可以进一步缩小该模式的范围。返回的密钥包含日记 id, 该日记 ID无需 `HGET` 请求就在当地对两个边框进行解码和过滤。 错误的匹配密钥是错误, 不是静悄悄忽略的条目 。

自定义 `GetKeyName` 映射无法安全将逻辑日记前缀或边框转换为 Redis 关键名称。 因此发现后, 扫描配置的密钥前缀的所有元数据键, 并同时读取其[ 最多128个批次的 `$journal-id` 字段中的可复制日记代号, 然后本地应用过滤器。 缺少的元数据会被原子检查以区分是否腐败 。

发现产量id,在跨部顺序中,不对整类目录进行排序或缓冲。 Redis `SCAN` 没有顺序排列,因此遇到未来id并不等于跨部。需要适当顺序的消费者必须使用 `StringComparer.Ordinal`对选定的id进行排序。在跨行过程中,用 O(N) 可见内存来抑制重复的id。元数据键不会保留用于整个跨部,因此自定义绘图可以导致重复扫描结果的重复元数据读取。

**`SCAN MATCH` 仍然在环绕着 Redis 服务器上的密钥空间 。** 缩小前置缩放会减少返回的密钥和客户端工作, 而不是服务器侧键空间扫描。 任意的正统区域会通过用户端过滤, 而不是通过索引来查找。 The the exption Redis `SCAN COUNT` 250 是提示值, 不是严格响应大小或服务器工作约束值。 一个数字推进器可以穿行许多空的或不匹配的扫描, 并且 Redis/ 客户端扫描缓冲不受元数据批量大小的束缚。 发现观察现场存储而不是快照。 在默认映射中, 同时删除的元数据密钥仍然可以生成日记id; 当消费者阅读时必须处理日记时不再存在。

存储错误和取消会传播,而不使用供应商级的回溯、重新启动或成功退缩。断开主机或没有主机是一个错误;在较早的代号被解禁后,服务器可能发生故障。在早期停止时将数字器丢弃。扫描和元数据操作之间检查取消情况,但不会中止飞行中的 Redis 元数据请求。

<a id="catalog-metrics"></a>

## 文表度

扫描密钥的 `Microsoft.Orleans` 计记 `orleans-journaling-provider-catalog-items`
用于计算所产身份的目录和 `orleans-journaling-provider-catalog-entries`
过滤和重复压缩后,使用 `provider=redis`,即
空逻辑范围生成
零测量;早期处置和取消处理保留消费键和交付进入
计数。

现有日记标准供应储存时间和结果。
通过其主机集成或仪器仪表图书馆进行遥测。
在同一连接多路驱动器上[StackExchange.Redis 特征分析](https://seredis.dev/Profiling_v2)
重新使用现有的客户端仪表和范围
共享连接保留其自用电话
寿命和诊断配置。

<a id="redis-key-layout"></a>

## Redis 密钥布局

键有窗体 `<keyPrefix>:journal:{<SHA256(keyName)>}:<encodedKeyName>:metadata` 或具有 a 的同一基数 `:data` 后缀 。 此布局适用于默认和自定义 `GetKeyName` 映射。 可逆的密钥名不在 SHA - 256 hash 标签之外, 保存 Redis 每期期刊数据和元数据集集合合合的Lua原子操作的数据和元数据。 URI 退出编码 Unicode 标语,而 `%uXXXX` 使用四个大写十六进制数字编码每个未受发酵的 UTF-16 代用方代码。 `%25`,因此原始日记ids 圆形和脱落序列和替换字符的相区别。 `$journal-id` 使用相同的编码来保存身份 Redis 字符串传输和自定义的绘图发现。本地扫描模式在配置键前缀中逃脱了球形字符,并在应用 ordinal 过滤器之前扩大了部分代孕前缀。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件

- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [Redis 文档](https://redis.io/docs/latest/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
