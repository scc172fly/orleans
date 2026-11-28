<a id="microsoft-orleans-s3-journaling"></a>

# Microsoft Orleans S3 日志化存储

`Microsoft.Orleans.Durable.S3` 提供以 Amazon S3 Express One Zone 目录存储桶为后端的 `IJournalStorage` 实现。

该提供程序使用 S3 Express 的追加写入（`WriteOffsetBytes`）向预写日志（WAL）追加数据。对于使用 SeaweedFS 等 S3 兼容服务的本地开发和测试，请设置 `UseS3ExpressAppend = false`、`UseConditionalDelete = false` 和 `StorageClass = null`，以使用可移植的条件写入和删除操作。

S3 集成测试在容器中运行 SeaweedFS，并验证通过条件重写实现的追加、基于 ETag 的条件删除、元数据更新和并发写入。CI 预拉取步骤和两个测试夹具使用同一个通过摘要固定版本的官方镜像。

对于 AWS S3 Express One Zone，应提前创建存储桶。`CreateBucketIfNotExists` 适用于本地模拟器。

每次追加都是一次不超过 5 GB（5,000,000,000 字节）的条件式单对象上传，因此 S3 要么存储整个分段，要么完全不存储；超限分段会在发送任何请求前失败。替换操作先在相同大小限制内上传检查点对象，再通过一次条件式 WAL 上传发布该检查点。使用条件重写追加时，整个 WAL（包括此次追加）均须满足此限制。元数据更新通过条件式单对象上传重写当前 WAL，因此会与并发内容写入冲突。如果替换对象会超过 S3 的 5 GB（5,000,000,000 字节）单次上传限制，请先发布检查点以压实 WAL，再更新元数据。检查点快照也受相同的上传限制约束。

`AddS3JournalStorage` 还会调用 `AddJournaling` 来注册核心服务和激活生命周期
集成。应用程序代码注入 `IDurableStateManager`，并在初始化前通过
`GetOrAddState<TState>(name)` 或其强类型辅助方法声明持久化状态。键控注入返回
同一个命名对象；`DurableGrain` 提供便捷的基类。恢复后，可以查找和修改已有状态，
而添加尚不存在的名称会立即失败。等待
`WriteStateAsync`，以确认管理器共享日志批次的写入。

独立使用者通过 `IJournaledStateManagerFactory.CreateStandalone(JournalId)` 获取
独立的 `IJournaledStateManager` 所有者契约。他们使用调用方提供的依赖项显式构造状态机组件，
通过 `RegisterStateMachine` 注册这些组件，并在使用恢复后的状态前等待
`InitializeAsync`。`WriteStateAsync` 确认日志写入，
`TryGetStateMachine` 查找已注册的组件。释放所有者将停止日志处理并
释放日志资源。组件、依赖项以及构造它们所用的任何作用域仍由
调用方拥有；日志所有者从不创建或释放 DI 作用域。

<a id="object-layout"></a>

## 对象布局

`GetObjectKey` 将逻辑日志 ID 映射到其基础对象键（默认采用恒等映射）。WAL 与检查点对象使用独立的命名空间：

- WAL：`wal/<base-key>`
- 检查点：`checkpoints/<base-key>/<snapshot-id>`

例如，日志 `jobs/00001234` 使用 `wal/jobs/00001234`，其检查点对象位于 `checkpoints/jobs/00001234/` 下。目录请求始终限定在 `wal/` 下，因此检查点不会占用列表分页空间。

S3 Express 目录存储桶可受益于以斜杠分隔的前缀。无序列表扫描所选 WAL 目录并保留每个匹配的逾期日志，无论它逾期多久。应用程序可以提供分层基础键，前提是其前缀映射和反向映射满足目录契约。

<a id="catalog-enumeration"></a>

## 目录枚举

`IJournalStorageCatalog.ListAsync` 按 S3 遍历顺序逐步返回 `JournalCatalogEntry` 值。每个条目的 `Id` 是日志标识。S3 条目的 `Metadata` 始终为 null，即使设置了 `JournalCatalogListOptions.IncludeMetadata = true`：`ListObjectsV2` 无法同时投影完整的日志格式、ETag 和调用方拥有的属性。枚举绝不会额外发出针对单个日志的元数据请求。需要元数据时，请显式调用 `GetMetadataAsync`。

元数据通过 `IJournalMetadata.FormatKey` 和
`JournalMetadata.FormatKey` 公开已存储的格式键。此命名变更保留对象键、提供程序元数据键
以及已存储的格式键值。

`JournalCatalogListOptions.Prefix` 是按序数比较的原始字符串前缀，允许匹配部分路径段；使用末尾的 `/` 可仅选择命名空间内部的条目。`MinId` 和 `MaxId` 提供包含端点的序数比较边界，默认不限制。枚举开始时对所有约束取快照，并在产出每个条目前检查约束。交集为空时不发送请求。需要按到期顺序处理的使用者必须使用 `StringComparer.Ordinal` 对所选 ID 排序。

提供程序在内部处理 `ListObjectsV2` 的后续分页，每页最多请求 1000 个对象。`UseOrderedListing` 默认为 `false`，与 S3 Express 目录存储桶一致。每个原生前缀均以 `wal/` 开头，即使目录请求没有指定前缀。目录模式将原始原生前缀扩展到最近的以斜杠结尾的目录边界，至少保留 `wal/`。目录存储桶无序且不支持 `StartAfter`：提供程序扫描所选命名空间，并过滤上下界，不会在遇到第一个未来 ID 时停止。

仅对于通用存储桶或保证 `ListObjectsV2` 结果按字典序排列且支持 `StartAfter` 的兼容服务，才设置 `UseOrderedListing = true`。使用默认恒等 `GetObjectKey` 映射时，有序模式发送 `wal/` 加原始前缀，并在可能时通过上下界的公共前缀进一步缩小范围。由于 `StartAfter` 不包含端点，ASCII 下界使用严格早于它的标记：`wal/` 加去除最后一个字符的下界。仅在该标记排在原生前缀之后时才发送它，包含端点的最小值仍在本地检查。后续请求仅使用返回的不透明 `ContinuationToken` 恢复遍历，包括空页之后。对于安全的 ASCII 上界，提供程序在越过 `wal/<MaxId>` 后停止。非 ASCII 边界通过过滤处理，不使用不安全的原生键定位或截止。不存在原生上界参数，因此会获取跨越上界的页面，但不会获取之后的页面。`UseS3ExpressAppend` 并不意味着列表顺序有保证。

使用自定义 `GetObjectKey` 映射时，如果目录列表显式指定了前缀，还必须配置 `GetObjectKeyPrefix`。任意对象键映射函数无法安全地应用于日志前缀。显式前缀映射器必须返回非空的**基础对象键前缀**，覆盖所有匹配日志，包括部分路径段；不要包含 `wal/`。提供程序在前面加上 `wal/`，随后目录模式将结果扩展到斜杠边界。缺少前缀配置或前缀为空会在列举前引发异常；不带前缀的列表（包括仅指定边界的查询）在没有映射器时仍可工作。即使在有序模式中，自定义映射也从不使用基于恒等映射的原生键上下界。

```csharp
options.GetObjectKey = id => $"journals/{id.Value}";
options.GetObjectKeyPrefix = prefix => $"journals/{prefix.Value}";
options.TryParseJournalId = key => key.StartsWith("journals/", StringComparison.Ordinal)
    ? new JournalId(key["journals/".Length..]) : null;
```

目录去除 `wal/` 后，`TryParseJournalId` 接收基础对象键。解析之后仍会执行规范 WAL 验证。检查点位于所选命名空间之外；`wal/` 下的别名和无关对象在过滤前仍会占用原生页面空间。

| 列表模式 | 原生前缀 | 下界 | 上界 |
| --- | --- | --- | --- |
| 有序，默认恒等映射 | `wal/` + 原始前缀/公共范围前缀 | 能缩小前缀范围时使用严格早于下界的 ASCII `StartAfter` 标记 | 越过 ASCII `wal/<MaxId>` 后停止 |
| 目录/无序 | `wal/` 下最近的以斜杠结尾的目录 | 本地过滤 | 本地过滤；遍历整个所选命名空间 |
| 自定义映射 | `wal/` + 显式映射的基础前缀，在目录模式下扩展 | 本地过滤 | 本地过滤；不假定恒等映射的顺序 |

客户端遍历所需内存与当前原生页面大小成正比。枚举器的一次推进可能跨过多个被过滤的页面或空页；存储服务决定扫描工作量、延迟和重试行为。枚举观察实时存储桶；并发变更遵循 S3 列表语义。使用后续枚举发现之后的变更，并容忍变更期间重复出现的标识。

提前停止时释放枚举器，并使用覆盖其生命周期的取消令牌。取消和服务故障通过枚举传播。

<a id="metrics"></a>

## 指标

`Microsoft.Orleans` 度量器记录目录遍历和显式提供程序重试。本库定义 `s3` 为 `provider` 标签使用的简称：

| 指标 | 度量内容 | 其他标签 |
| --- | --- | --- |
| `orleans-journaling-provider-catalog-entries` | 向使用者交付的目录条目 | 无 |
| `orleans-journaling-provider-catalog-pages` | 目录遍历收到的页面，包括空页 | 无 |
| `orleans-journaling-provider-catalog-items` | 本地过滤前的原生目录候选项 | 无 |
| `orleans-journaling-provider-retries` | 提供程序循环中的重试 | `reason` |

每收到一个目录页面，页计数器都会递增，即使对象集合为空。获取页面失败和到达枚举末尾的推进均不增加页数。比较原生候选项与已交付条目的数量，可评估过滤工作量。

重试原因为 `metadata_conflict` 和 `checkpoint_collision`，在提供程序再次尝试时记录。目录条目只统计实际产出的标识；原生候选项计数包含当前页面中被过滤掉的候选项。提前释放和取消不会丢弃已记录的工作量。

应用程序通过宿主集成、插桩库或 [AWS SDK 诊断配置](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/Util/TLoggingConfig.html) 提供请求遥测。复用宿主已配置的客户端插桩。应用程序负责诊断数据收集和敏感响应处理；服务端事务指标支持账单核对。资源标识应记录在受保护的日志和跟踪中；指标标签的取值范围保持有限。

已有的 `orleans-journaling-s3-operations`、`orleans-journaling-s3-operation-duration` 和 `orleans-journaling-s3-operation-bytes` 指标保持原有名称、标签和行为，包括直接构造的存储实例。
