<a id="microsoft-orleans-s3-journaling"></a>

# Microsoft Orleans S3 日刊

`Microsoft.Orleans.Durable.S3` 提供由亚马逊S3快递区一号目录桶支持的 `IJournalStorage` 实施。

提供商使用S3快递(Express)写作(`WriteOffsetBytes`用于当地发展和S3类兼容服务测试,如 SeaweedFS, 设置 `UseS3ExpressAppend = false`, `UseConditionalDelete = false`,和 `StorageClass = null` 使用可移植的有条件写作和删除。

S3集成测试在一个容器中运行 SeaweedFS ,并使用有条件改写附件、ETA-条件删除、元数据更新和同时撰写。 CI 预拉和两个测试装置都使用了相同的文摘官方图像。

应为AWS S3快递一区提前创建桶。 `CreateBucketIfNotExists` 用于当地模拟器。

每个附件都是最多5GB(5,000,000,000字节)的有条件单向上传,因此S3储存整个部分或其中的任何一部分,而较大的部分在发出任何请求之前就失效。替换者在同一限制内上传一个检查站对象,并通过一个有条件WAL上传予以公布。如果有有条件重写的附件,整个WAL(包括附件)都保持在这一限制之内。元数据更新使用有条件单向上传重新填写当前WAL,因此元数据更新与同时写入的内容发生冲突。发布一个检查点,以便在替换对象超过S3的5GB(5,000,000,000 字节)单上载限时更新元数据之前将 WAL压缩。

`AddS3JournalStorage` 还要求 `AddJournaling` 登记核心服务和激活生命周期
整合。应用代码输入 `IDurableStateManager` 并宣布在
使用 `GetOrAddState<TState>(name)` 或其输入助手的初始化。键入注入返回
同一命名对象; `DurableGrain` 提供一个方便基级。在回收后,现有状态
可供查找和突变使用, 同时立即添加缺失的名字失败。 请等待
`WriteStateAsync` 来确认经理共享的日记批次

独立消费者使用 `IJournaledStateManagerFactory.CreateStandalone(JournalId)` 获取
独立 `IJournaledStateManager` 所有人合同。
通知后,在有打电话者提供家属的家属处登记。 `RegisterStateMachine`等待
使用回收状态前`InitializeAsync` 。 `WriteStateAsync` 确认日记写,
`TryGetStateMachine` 找到已注册的部件。 处理所有者停止日记处理并
组成部分、依赖关系和用于建造这些日记资源的任何范围仍然存在。
用户拥有;日记所有人从不创建或处置DI范围。

<a id="object-layout"></a>

## 对象布局

`GetObjectKey` 将逻辑日记id映射到其基本对象密钥(默认情况下的身份映射)。

- 沃尔: `wal/<base-key>`
- 检查点: `checkpoints/<base-key>/<snapshot-id>`

例如,《日刊》 `jobs/00001234` 使用 `wal/jobs/00001234` 和根据 `checkpoints/jobs/00001234/`设置的检查站物体。检查点名称存储在WAL元数据中。目录要求总是保存在 `wal/`之下,因此检查点不会使用列表页。

S3 快速目录桶受益于斜线限定的前缀。 无顺序列表扫描选定的 WAL 目录并保留每一份过期的对应日记账,不管时间有多老旧。 应用程序可以提供等级基键,只要其前缀和反向映射符合目录合同。

<a id="catalog-enumeration"></a>

## 分类表列举

`IJournalStorageCatalog.ListAsync` 返回 S3 跨序中的 `JournalCatalogEntry` 值。每个条目的 `Id` 均为日记身份。S3 条目总是无效 `Metadata`,包括当 `JournalCatalogListOptions.IncludeMetadata = true`: `ListObjectsV2` 无法同时预测完整的日记格式、ETag和调用者拥有的属性时。计算从不添加单独的每期日报元数据请求。在需要元数据时,明确呼叫 `GetMetadataAsync` 。

元数据元数据通过 `IJournalMetadata.FormatKey` 和
`JournalMetadata.FormatKey`. 此命名更改保留了对象键、 提供者元数据键,
和存储的格式键值。

`JournalCatalogListOptions.Prefix` 是原始正弦字符串前缀, 包括部分段; 使用尾随 `/` 来选择仅位于命名空间内的条目。 `MinId` 和 `MaxId` 提供无限制的包容性正弦框。 当点数开始并检查后, 所有限制都会被快速地显示。 空交叉点不发出请求。 需要适当顺序的消费者必须用 `StringComparer.Ordinal`排序选定的代号 。

提供者内部处理 `ListObjectsV2` 续 ,并要求每个页面最多1000个对象。 `UseOrderedListing` 默认为 `false`,匹配 S3 Express 目录桶。 每个本地端前缀都以 `wal/`开头,包括未预设的目录请求。目录模式将原始本地端前缀扩展至其最近的斜线终止目录边界,至少保留 `wal/`。目录桶没有顺序,也不支持 `StartAfter`:供应商扫描选定命名空间和过滤器的两边框,不停止在第一个未来代号上。

仅为一般用途桶或兼容服务设定 `UseOrderedListing = true` , 保证按常规订购 `ListObjectsV2` 结果和 `StartAfter` 支持。 在默认身份 `GetObjectKey` 映射的情况下, 命令模式发送 `wal/` 加上原始前缀, 尽可能由两个边框的共同前缀进一步缩小。 由于 `StartAfter` 是独家的, ASCII 下界使用严格较早的标记: `wal/` 加上最后字符被删除的下界。 该标记只有在本地前缀之后某部分才发出, 而包容性最小值仍然在本地检查。 随后的请求将返回的不透明 `ContinuationToken` 单用于恢复转动, 包括空白页后。 供应商在 `wal/<MaxId>` 后停止安全 ASCII 最大值。 非 ASCII 界限在没有不安全本地键寻找或切断时过滤。 没有本地上端参数, 因此过关页面不会被获取, 但晚页是 。 `UseS3ExpressAppend` 并不意味着列表的保证 。

自定义 `GetObjectKey` 映射也必须配置 `GetObjectKeyPrefix` 以明确预设目录列表。 任意天体键映射功能无法安全地适用于日记前缀。 明确的前缀映像器必须返回一个非空白的 **基本天体钥匙前缀** , 包括部分段在内的每个匹配日记都包含该选项; 不包含 `wal/`。 提供方先输入 `wal/`, 然后目录模式将结果扩大为斜线边界。 在列出前丢失或空前缀配置丢弃; 未预先设定的列表, 包括只用边框查询, 仍然没有地图机运行。 即使在有命令的模式下, 自定义绘图从不使用基于身份的本地键下端或上端边框 。

```csharp
options.GetObjectKey = id => $"journals/{id.Value}";
options.GetObjectKeyPrefix = prefix => $"journals/{prefix.Value}";
options.TryParseJournalId = key => key.StartsWith("journals/", StringComparison.Ordinal)
    ? new JournalId(key["journals/".Length..]) : null;
```

`TryParseJournalId` 在目录条 `wal/`之后收到基质对象键。在解析后,卡尼-WAL验证仍然适用。检查点位于选定命名空间之外; `wal/` 下的别名和无关物体在过滤前仍消耗本地页面的空间。

| 列出模式 | 原原前缀 | 下下约束 | 上约束 |
| --- | --- | --- | --- |
| 有序、默认身份制图 | `wal/` +原前缀/共同区域前缀 | 严格早期的 ASCII `StartAfter` 标记,当它缩小前缀时 | 跨越 ASCII `wal/<MaxId>` 后停止  |
| Directory/unordered | `wal/` 下近距离斜线终止目录  | 本地过滤 | 本地过滤; 整个选中的命名空间被穿行 |
| 自定义绘图 | `wal/` +清晰映射基准前缀,以目录模式扩展 | 本地过滤 | 本地过滤;没有身份-顺序假设 |

客户端的跨行内存与当前本地页面成比例。 分子推进可以跨越多个过滤或空页, 存储服务决定扫描工作、 延时和回溯。 数字观察活桶; 同时修改后S3列出语义。 使用随后的查点发现后来的变化并容忍变化中反复出现的身份 。

提前停止时让点名员丢弃, 并使用一个取消标记, 覆盖其寿命。 取消和服务故障会通过查点传播 。

<a id="metrics"></a>

## 计量

`Microsoft.Orleans` 仪表记录目录和清晰提供者的回溯。 此库将 `s3` 定义为其 `provider` 标记的短名:

| 计量 | 计量度量 | 其他标签 |
| --- | --- | --- |
| `orleans-journaling-provider-catalog-entries` | 向消费者提供的目录条目 | 无无无无 |
| `orleans-journaling-provider-catalog-pages` | 以目录遍历收到的页数,包括空页 | 无无无无 |
| `orleans-journaling-provider-catalog-items` | 本地过滤前的本地目录候选人 | 无无无无 |
| `orleans-journaling-provider-retries` | 提供者- 供应者循环回转 | `reason` |

每个人即使其对象收藏为空,也都收到目录页加注。 失败的页面检索和终端查点预发数增加了零页。 比较本地候选人与交付条目以评估过滤工作。

重试理由为 `metadata_conflict` 和 `checkpoint_collision`,在提供者进行另一次尝试时记录。目录条目计数只产生身份;原项目计数包括从当前页面过滤的候选人。早期处置和取消保留已记录的工作。

应用程序用品要求通过主机集成、仪器仪表图书馆或 [AWS SDK 诊断配置](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/Util/TLoggingConfig.html)进行遥测。重新使用主机配置的客户仪器。应用程序拥有诊断收集和敏感反应处理;服务方交易量度支持账单调节。资源身份属于受保护日志和痕迹;测量标记仍然被封存。

现有的 `orleans-journaling-s3-operations`、 `orleans-journaling-s3-operation-duration`和 `orleans-journaling-s3-operation-bytes` 衡量标准保留其名称、标记和行为,包括直接构建的储存实例。
