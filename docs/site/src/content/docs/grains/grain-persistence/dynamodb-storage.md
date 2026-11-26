---
title: "亚马逊 DynamoDB grain 持久性"
description: "配置亚马逊 DynamoDB 作为 Orleans grain 存储提供者。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="amazon-dynamodb-grain-persistence"></a>

# 亚马逊 DynamoDB grain 持久性

安装 [`Microsoft.Orleans.Persistence.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.DynamoDB) 软件包, 用 <xref:Orleans.Hosting.DynamoDBGrainStorageSiloBuilderExtensions.AddDynamoDBGrainStorage*>配置一个命名的提供者:

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_dynamodb_storage":::
[.NET 证书和剖分解链的AWS SDK](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html) 提供证书时, <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.AccessKey> 和 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.SecretKey> 尚未确定。在生产中,更喜欢工作量证书,如IAM的作用而不是长寿命钥匙。 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.ProfileName>、 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.AccessKey>、 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.SecretKey>和 <xref:Orleans.Persistence.DynamoDB.DynamoDBClientOptions.Token> 在部署环境需要明确的 SDK 配置时可以使用。配置请参见 [AWS CLI 配置文件参考](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-files.html) 。

<a id="capacity-and-lifecycle"></a>

## 能力和生命周期

<xref:Orleans.Configuration.DynamoDBStorageOptions.UseProvisionedThroughput> selects provisioned-capacity behavior. When enabled, configure <xref:Orleans.Configuration.DynamoDBStorageOptions.ReadCapacityUnits> and <xref:Orleans.Configuration.DynamoDBStorageOptions.WriteCapacityUnits>. <xref:Orleans.Configuration.DynamoDBStorageOptions.CreateIfNotExists> and <xref:Orleans.Configuration.DynamoDBStorageOptions.UpdateIfExists> allow provider-managed table changes, but infrastructure-managed provisioning is usually preferable in production.

<xref:Orleans.Configuration.DynamoDBStorageOptions.DeleteStateOnClear> controls whether clearing state deletes the item or resets it. <xref:Orleans.Configuration.DynamoDBStorageOptions.TimeToLive> advances the item's expiration on every write; enable it only when disappearing state is valid application behavior.

<a id="service-identity-and-upgrades"></a>

## 服务身份和升级

<xref:Orleans.Configuration.DynamoDBStorageOptions.ServiceId> prefixes the key of every grain state as `{ServiceId}_{grainId}`. Configure it explicitly for each DynamoDB grain storage provider and keep it stable across deployments that share the same logical application state. Startup validation reports an <xref:Orleans.Runtime.OrleansConfigurationException> when the value is unset.

对于新部署,选择一个稳定的应用程序标识符,例如 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。当升级现有部署时,保留用于写成状态的值。如果部署使用了先前的空字符串默认,则明确设置 `ServiceId` 至 `string.Empty` 代码或 `""` 配置中,可以保持读写相同键。选择不同的值可选择不同的状态命名空间;在前缀下访问现有状态需要单独的数据迁移。

提供商使用乐观的货币,

<a id="serialization"></a>

## 逐步化

设置 <xref:Orleans.Configuration.DynamoDBStorageOptions.GrainStorageSerializer> 以自定义存储的演示文稿。 更改序列器不会重写现有项目, 因此替换时必须读取先前的演示文稿或随附迁移 。
