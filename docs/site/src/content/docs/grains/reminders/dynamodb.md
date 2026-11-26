---
title: "配置亚马逊 DynamoDB 提醒"
description: "配置耐久 Orleans 提醒存储器,与亚马逊 DynamoDB连接。"
ms.date: 08/20/2026
ms.topic: how-to
---

<a id="configure-amazon-dynamodb-reminders"></a>

# 配置亚马逊 DynamoDB 提醒

安装 [`Microsoft.Orleans.Reminders.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.DynamoDB) 键调用 <xref:Orleans.Hosting.DynamoDBSiloBuilderReminderExtensions.UseDynamoDBReminderService*> 上每 silo.

A silo 使用 DynamoDB 成员身份和提醒的配置独立地配置了提供方:

:::code language="csharp" source="../../snippets/compiled/Grains/DynamoDBReminderSnippets.cs" id="configure_dynamodb_reminders":::

<xref:Orleans.Configuration.DynamoDBClusteringOptions> and <xref:Orleans.Configuration.DynamoDBReminderStorageOptions> are separate typed options. Set the AWS region through each options instance, as shown above. A silo which uses another membership provider configures that provider alongside <xref:Orleans.Hosting.DynamoDBSiloBuilderReminderExtensions.UseDynamoDBReminderService*>.

<xref:Orleans.Configuration.ClusterOptions.ServiceId> identifies the application's reminder records and must remain stable across deployments that share a reminder table. Use distinct table names for cluster membership and reminders because each provider manages a different schema.

<a id="configure-aws-credentials"></a>

## 配置 AWS 证书

当 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.AccessKey> 和 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.SecretKey> 未定时, [.NET 证书和剖分解链的AWS SDK](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html) 将提供全权证书。在生产方面,倾向于工作量证书,如IAM的作用而不是长寿钥匙。 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.ProfileName>、 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.AccessKey>、 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.SecretKey>和 <xref:Orleans.Reminders.DynamoDB.DynamoDBClientOptions.Token> 支持部署环境需要明确的SDK配置。

<a id="configure-table-capacity-and-lifecycle"></a>

## 配置表容量和生命周期

示例使用按需能力和基础设施管理表。将 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.UseProvisionedThroughput> 设为 `true` ,并为提供的能力配置 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.ReadCapacityUnits> 和 <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.WriteCapacityUnits> 。

<xref:Orleans.Configuration.DynamoDBReminderStorageOptions.CreateIfNotExists> and <xref:Orleans.Configuration.DynamoDBReminderStorageOptions.UpdateIfExists> allow the provider to create the reminder table and update its provisioned capacity. Infrastructure-managed provisioning keeps table lifecycle and capacity changes in the deployment workflow.
