---
title: Amazon DynamoDB Grain 持久化
description: 将 Amazon DynamoDB 配置为 Orleans Grain 存储提供程序。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="amazon-dynamodb-grain-persistence"></a>

# Amazon DynamoDB Grain 持久化

安装 [`Microsoft.Orleans.Persistence.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.DynamoDB) 包，并通过 <xref:Orleans.Hosting.DynamoDBGrainStorageSiloBuilderExtensions.AddDynamoDBGrainStorage*> 配置具名提供程序：

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_dynamodb_storage":::
[AWS SDK for .NET 的凭据和配置文件解析链](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html) 在未设置 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.AccessKey> 和 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.SecretKey> 时提供凭据。在生产环境中，优先使用 IAM 角色等工作负载凭据，而不是长期密钥。部署环境需要显式配置 SDK 时，可使用 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.ProfileName>、<xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.AccessKey>、<xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.SecretKey> 和 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.Token>。有关配置文件设置，请参阅 [AWS CLI 配置文件参考](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-files.html)。

<a id="capacity-and-lifecycle"></a>

## 容量和生命周期

<xref:Orleans.Configuration.DynamoDBStorageOptions.UseProvisionedThroughput> 选择预置容量模式。启用时，配置 <xref:Orleans.Configuration.DynamoDBStorageOptions.ReadCapacityUnits> 和 <xref:Orleans.Configuration.DynamoDBStorageOptions.WriteCapacityUnits>。<xref:Orleans.Configuration.DynamoDBStorageOptions.CreateIfNotExists> 和 <xref:Orleans.Configuration.DynamoDBStorageOptions.UpdateIfExists> 允许提供程序管理表变更，但生产环境通常更适合由基础设施统一预配。

<xref:Orleans.Configuration.DynamoDBStorageOptions.DeleteStateOnClear> 控制清除状态时删除条目还是重置条目。<xref:Orleans.Configuration.DynamoDBStorageOptions.TimeToLive> 会在每次写入时延后条目的过期时间；仅在状态消失符合应用程序预期行为时启用此选项。

<a id="service-identity-and-upgrades"></a>

## 服务标识与升级

<xref:Orleans.Configuration.DynamoDBStorageOptions.ServiceId> 以 `{ServiceId}_{grainId}` 的形式为每个 Grain 状态键添加前缀。请为每个 DynamoDB Grain 存储提供程序显式配置该值，并在共享同一逻辑应用程序状态的各次部署间保持稳定。未设置该值时，启动验证会报告 <xref:Orleans.Runtime.OrleansConfigurationException>。

对于新部署，选择稳定的应用程序标识，例如 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。升级现有部署时，保留最初写入状态时使用的值。如果部署采用了此前的空字符串默认值，请在代码中显式将 `ServiceId` 设置为 `string.Empty`，或在配置中使用 `""`，以继续读写相同的键。选择其他值将切换到不同的状态命名空间；若要在该前缀下访问现有状态，必须单独迁移数据。

提供程序使用乐观并发控制，并拒绝基于过期版本的写入。

<a id="serialization"></a>

## 序列化

设置 <xref:Orleans.Configuration.DynamoDBStorageOptions.GrainStorageSerializer> 以自定义存储表示形式。更换序列化器不会重写现有条目，因此新的序列化器必须能够读取旧表示形式，或者同时进行迁移。
