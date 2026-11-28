---
title: Google Cloud Firestore Grain 持久化
description: 将 Google Cloud Firestore 配置为 Orleans Grain 存储提供程序。
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="google-cloud-firestore-grain-persistence"></a>

# Google Cloud Firestore Grain 持久化

安装 [`Microsoft.Orleans.Persistence.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) 包，并通过 <xref:Orleans.Hosting.FirestoreStorageHostingExtensions.AddFirestoreGrainStorage*> 配置具名提供程序：

:::code language="csharp" source="../../host/snippets/google-firestore/FirestoreConfiguration.cs" id="google_firestore_persistence":::

`ProjectId` 标识 Google Cloud 项目，`RootCollectionName` 选择提供程序的顶层集合，`EmulatorHost` 将请求重定向到本地模拟器。有关数据库创建、身份验证、IAM、集群成员管理、提醒器及模拟器设置，请参阅 [Google Cloud Firestore 提供程序](../../host/configuration-guide/google-firestore-providers.md)。

<a id="clear-behavior"></a>

## 清除行为

<xref:Orleans.Persistence.Firestore.FirestoreStateStorageOptions.DeleteStateOnClear> 控制 <xref:Orleans.Core.IStorage.ClearStateAsync*> 处理记录的方式：

- `false` 是默认值，保留空文档并更新其 ETag。
- `true` 删除文档。

两种模式均使用乐观并发控制。基于过期版本的写入或清除操作会引发 <xref:Orleans.Storage.InconsistentStateException>，而不会覆盖较新的记录。

<a id="serialization-and-record-size"></a>

## 序列化与记录大小

提供程序将每条状态记录序列化为一个 Firestore 文档。序列化有效载荷、文档名称及元数据必须符合 [Firestore 文档大小限制](https://cloud.google.com/firestore/quotas)。应在序列化后测试符合生产数据形态的状态，而不能仅依赖内存中对象的大小。

设置 <xref:Orleans.Persistence.Firestore.FirestoreStateStorageOptions.GrainStorageSerializer> 以自定义存储表示形式。更换序列化器不会重写现有文档，因此新的序列化器必须能够读取旧表示形式，或者同时进行迁移。

<a id="sample"></a>

## 示例

[Google Cloud Firestore 示例](https://github.com/dotnet/orleans/tree/main/samples/GoogleFirestore) 同时配置了持久化、Firestore 集群成员管理、提醒器及 Grain 目录。运行一次以写入状态，再次运行即可观察已持久化的计数器。
