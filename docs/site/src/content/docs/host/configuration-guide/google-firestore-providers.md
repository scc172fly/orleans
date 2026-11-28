---
title: Google Cloud Firestore 提供程序
description: 为 Orleans 集群、Grain 目录、提醒器和持久化配置 Google Cloud Firestore。
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="google-cloud-firestore-providers"></a>

# Google Cloud Firestore 提供程序

Orleans 提供以下 Google Cloud Firestore 集成：

| 功能 | 包 |
|---|---|
| 集群成员资格和客户端网关发现 | [`Microsoft.Orleans.Clustering.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Firestore) |
| Grain 目录 | [`Microsoft.Orleans.GrainDirectory.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Firestore) |
| Grain 持久化 | [`Microsoft.Orleans.Persistence.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) |
| 提醒器 | [`Microsoft.Orleans.Reminders.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Firestore) |

启动集群之前，请[以原生模式创建 `(default)` Firestore 数据库](https://cloud.google.com/firestore/docs/create-database-server-client-library)。提供程序使用 Google Cloud .NET 客户端库，因此连接 Google Cloud 时使用[应用默认凭据（ADC）](https://cloud.google.com/docs/authentication/provide-credentials-adc)。与长期有效的服务账号密钥相比，优先使用工作负载标识或附加的服务账号。

仅向应用的标识授予创建、读取、更新、查询及删除提供程序文档所需的 [Firestore IAM 权限](https://cloud.google.com/firestore/native/docs/security/iam)。

<a id="configure-a-silo"></a>

## 配置 Silo

为每个已安装的 Firestore 提供程序配置相同的 Google Cloud 项目和根集合。以下示例配置全部四种提供程序：

:::code language="csharp" source="../snippets/google-firestore/FirestoreConfiguration.cs" id="google_firestore_silo":::

该配置使用：

- <xref:Orleans.Configuration.ClusterOptions.ClusterId> 对集群成员资格及 Grain 目录记录进行分区。
- <xref:Orleans.Configuration.ClusterOptions.ServiceId> 对提醒器及持久化 Grain 状态进行分区。
- `RootCollectionName` 作为顶级 Firestore 集合。其默认值为 `Orleans`。

在整个部署生存期内保持 `ClusterId` 和 `ServiceId` 稳定。当部署之间不得共享成员资格、Grain 位置、提醒器或状态时，请使用不同的值。

集群提供程序在[可串行化的 Firestore 事务](https://cloud.google.com/firestore/docs/transaction-data-contention#serializable_isolation)中读取成员资格记录及表版本。改变拓扑的成员资格插入和更新会以原子方式写入变更后的 Silo 行，并单调递增版本行，因此读取操作不会将某个拓扑版本的行与另一个版本的版本号混合。

<xref:Orleans.Hosting.FirestoreGrainDirectoryExtensions.UseFirestoreGrainDirectoryAsDefault*> 会替换未显式选择其他目录的所有 Grain 类型所使用的内置目录。外部目录会在目录操作中增加 Firestore 请求，因此将其作为默认目录之前，请对激活密集型工作负载进行基准测试。

有关状态记录、序列化器及清除行为的详细信息，请参见 [Google Cloud Firestore Grain 持久化](../../grains/grain-persistence/google-firestore-storage.md)。

<a id="configure-an-external-client"></a>

## 配置外部客户端

外部客户端使用集群包发现处于活跃状态的 Orleans 网关：

:::code language="csharp" source="../snippets/google-firestore/FirestoreConfiguration.cs" id="google_firestore_client":::

客户端的 `ClusterId`、`ServiceId`、`ProjectId` 和 `RootCollectionName` 必须与 Silo 配置匹配。

<a id="use-the-emulator"></a>

## 使用模拟器

将 `EmulatorHost` 设置为 Firestore 模拟器终结点，例如 `127.0.0.1:8080`。模拟器连接使用不安全的本地通道，且不使用 Google Cloud 凭据。不要在生产环境中设置 `EmulatorHost`。

[Firebase 本地模拟器套件](https://firebase.google.com/docs/emulator-suite/connect_firestore) 可以在本地运行 Firestore。[Google Cloud Firestore 示例](https://github.com/dotnet/orleans/tree/main/samples/GoogleFirestore) 包含启动模拟器及测试全部四种提供程序的命令。

<a id="operational-guidance"></a>

## 运维指南

- 将 Silo 部署在靠近 Firestore 数据库的位置，并考虑 Firestore 延迟对成员资格、提醒器、目录和持久化操作的影响。
- 监控请求错误、延迟、配额及计费。根据预期的集群规模和激活速率评估 [Firestore 配额和限制](https://cloud.google.com/firestore/quotas)。
- 根据应用恢复要求备份持久化状态。成员资格、网关、目录和提醒器记录属于运维数据，应与应用拥有的集合隔离。
- 限制直接写入提供程序拥有的文档。在 Orleans 之外修改这些文档可能违反成员资格、提醒器、目录或乐观并发控制的不变量。
- 在生产部署之前测试滚动升级、Silo 丢失、凭据轮换、配额耗尽及 Firestore 不可用等情况。
