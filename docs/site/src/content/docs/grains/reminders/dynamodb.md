---
title: 配置 Amazon DynamoDB 提醒器
description: 使用 Amazon DynamoDB 配置持久化 Orleans 提醒器存储。
ms.date: 08/20/2026
ms.topic: how-to
---

<a id="configure-amazon-dynamodb-reminders"></a>

# 配置 Amazon DynamoDB 提醒器

安装 [`Microsoft.Orleans.Reminders.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.DynamoDB) 包，并在每个 Silo 上调用 <xref:Orleans.Hosting.DynamoDBSiloBuilderReminderExtensions.UseDynamoDBReminderService*>。

如果 Silo 同时使用 DynamoDB 存储集群成员资格和提醒器，则应分别配置两个提供程序：

:::code language="csharp" source="../../snippets/compiled/Grains/DynamoDBReminderSnippets.cs" id="configure_dynamodb_reminders":::

<xref:Orleans.Configuration.DynamoDBClusteringOptions> 和 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions> 是独立的强类型选项。按照上例，通过各自的选项实例设置 AWS 区域。使用其他集群成员资格提供程序的 Silo 则应同时配置该提供程序和 <xref:Orleans.Hosting.DynamoDBSiloBuilderReminderExtensions.UseDynamoDBReminderService*>。

<xref:Orleans.Configuration.ClusterOptions.ServiceId> 用于标识应用程序的提醒器记录，在共享提醒器表的各次部署之间必须保持稳定。集群成员资格和提醒器应使用不同的表名，因为各提供程序管理不同的表结构。

<a id="configure-aws-credentials"></a>

## 配置 AWS 凭据

当 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.AccessKey> 和 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.SecretKey> 未设置时，由 [AWS SDK for .NET 凭据和配置文件解析链](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html) 提供凭据。生产环境中应优先使用 IAM 角色等工作负载凭据，而非长期密钥。<xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.ProfileName>、<xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.AccessKey>、<xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.SecretKey> 和 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.Token> 支持需要显式配置 SDK 的部署环境。

<a id="configure-table-capacity-and-lifecycle"></a>

## 配置表容量和生命周期

示例采用按需容量模式以及由基础设施管理的表。若使用预置容量模式，则将 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.UseProvisionedThroughput> 设置为 `true`，并配置 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.ReadCapacityUnits> 和 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.WriteCapacityUnits>。

<xref:Orleans.Configuration.DynamoDBReminderStorageOptions.CreateIfNotExists> 和 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.UpdateIfExists> 允许提供程序创建提醒器表及更新其预置容量。由基础设施负责预置资源，可以将表的生命周期与容量变更纳入部署流程。
