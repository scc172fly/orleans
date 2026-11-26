---
title: "Google云火炉供应商"
description: "配置 Google Cloud Firshstore 用于 Orleans 集、 grain 目录、提醒和持久性。"
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="google-cloud-firestore-providers"></a>

# Google云火炉供应商

Orleans 提供下列谷歌云烟火储存集成:

| 能力 | 套包 |
|---|---|
| 集群成员和客户端发现 | [`Microsoft.Orleans.Clustering.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Firestore) |
| Grain 目录 | [`Microsoft.Orleans.GrainDirectory.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Firestore) |
| Grain 持久性 | [`Microsoft.Orleans.Persistence.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) |
| 提醒提醒 | [`Microsoft.Orleans.Reminders.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Firestore) |

创建 [`(default)` 以本地模式提供的消防站数据库](https://cloud.google.com/firestore/docs/create-database-server-client-library) 在启动组群之前先启动集群。 提供方使用 Google Cloud .NET 客户图书馆,因此使用 [应用程序默认证书 (ADC)](https://cloud.google.com/docs/authentication/provide-credentials-adc) 更倾向于在长寿服务账户钥匙上设定工作量身份或附属服务账户。

授予申请身份仅提供创建、阅读、更新、查询和删除提供者文件所需的 [消防局许可](https://cloud.google.com/firestore/native/docs/security/iam) 。

<a id="configure-a-silo"></a>

## 配置 silo

配置安装的每个消防站供应商, 使用相同的 Google Cloud 工程和根收集。 以下示例配置所有四个提供者 :

:::code language="csharp" source="../snippets/google-firestore/FirestoreConfiguration.cs" id="google_firestore_silo":::

配置用途:

- <xref:Orleans.Configuration.ClusterOptions.ClusterId> to partition cluster membership and grain-directory records.
- <xref:Orleans.Configuration.ClusterOptions.ServiceId> to partition reminders and persistent grain state.
- `RootCollectionName` 作为顶级消防室收藏。其默认值为 `Orleans`。

在部署的一生中保持 `ClusterId` 和 `ServiceId` 稳定。在部署不能共享成员、 grain 地点、提醒通知或状态时使用不同的值。

集群提供商读取成员行和表格版本的 [系列可实现的消防商店交易](https://cloud.google.com/firestore/docs/transaction-data-contention#serializable_isolation)。地形变化成员插入和更新原子写 silo 更改的行,单向推进版本行,因此阅读不能将一个地貌版的行与另一个版本的行合并。

<xref:Orleans.Hosting.FirestoreGrainDirectoryExtensions.UseFirestoreGrainDirectoryAsDefault*> 替换每个 grain 类型内置目录,该目录没有明确选择另一个目录。外部目录在目录操作中添加了消防站请求,因此在使用一个选项作为默认值之前先对激活-重工作量基准。

有关国家记录、序列器和清晰行为的详细情况,见 [Google云烟火堆 grain 持久性](../../grains/grain-persistence/google-firestore-storage.md)。

<a id="configure-an-external-client"></a>

## 配置外部客户端

外部客户使用组合组合软件包发现活动 Orleans 网关:

:::code language="csharp" source="../snippets/google-firestore/FirestoreConfiguration.cs" id="google_firestore_client":::

客户端的 `ClusterId`、 `ServiceId`、 `ProjectId`和 `RootCollectionName` 配置必须与 silo 配置匹配。

<a id="use-the-emulator"></a>

## 使用模拟器

设置 `EmulatorHost` 到消防站模拟端点, 如 `127.0.0.1:8080`。 模拟连接使用不安全的地方频道, 不使用 Google Clude 证书。 不要在生产中设置 `EmulatorHost` 。

[Firebase 本地模拟器套件](https://firebase.google.com/docs/emulator-suite/connect_firestore) 可在本地运行消防站。 [Google云烟火炉抽样](https://github.com/dotnet/orleans/tree/main/samples/GoogleFirestore) 包含启动模拟器和行使所有四个提供者的命令。

<a id="operational-guidance"></a>

## 业务指导

- 定位 silos 消防站数据库附近,以及消防站的存续账户,包括成员、提醒、目录和持久性作业。
- 监测请求错误、 延迟、 配额和计费。 根据预期集束率和启动率审查 [消防配额和限额](https://cloud.google.com/firestore/quotas) 。
- 根据申请回收要求,会员身份、网关、目录和提醒记录是业务数据,应与应用程序拥有的收藏分开。
- 将直接写作限制在供应商拥有的文件上。 将文件从 Orleans 外调出可能违反会籍、提醒、目录或乐观通货性变换。
- 试验滚动升级、 silo 损失、证书轮换、配额用尽和在生产部署前无法消防站。
