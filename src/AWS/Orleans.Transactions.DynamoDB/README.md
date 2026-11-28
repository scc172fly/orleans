<a id="microsoft-orleans-transaction-for-dynamodb"></a>

# Microsoft Orleans DynamoDB 事务提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans DynamoDB 事务提供程序使用 Amazon DynamoDB 为 Microsoft Orleans 提供 Grain 事务功能。
这使 Grain 能够在分布式环境中使用 DynamoDB 作为底层存储执行事务。

序列化后的事务状态与元数据必须各自符合 DynamoDB 的 400 KB 项大小上限。写入批次会进行拆分，以符合 DynamoDB 每个事务最多 100 个操作且受影响的项总大小不超过 4 MB 的限制。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Transactions.DynamoDB
```
