---
title: "Google云烟火堆 grain 持久性"
description: "配置 Google Cloud Firshstore , 用作 Orleans grain 存储供应商 。"
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="google-cloud-firestore-grain-persistence"></a>

# Google云烟火堆 grain 持久性

安装 [`Microsoft.Orleans.Persistence.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) 软件包, 用 <xref:Orleans.Hosting.FirestoreStorageHostingExtensions.AddFirestoreGrainStorage*>配置一个命名的提供者:

:::code language="csharp" source="../../host/snippets/google-firestore/FirestoreConfiguration.cs" id="google_firestore_persistence":::

`ProjectId` 识别谷歌云项目, `RootCollectionName` 选择供应商的最高层收藏, `EmulatorHost` 将请求转向本地模拟器。数据库的创建、认证、 IAM、集成、提醒和模拟器设置见 [Google云火炉供应商](../../host/configuration-guide/google-firestore-providers.md)。

<a id="clear-behavior"></a>

## 清晰行为

<xref:Orleans.Persistence.Firestore.FirestoreStateStorageOptions.DeleteStateOnClear> controls how <xref:Orleans.Core.IStorage.ClearStateAsync*> handles a record:

- `false`,默认值,保留空文档并预发其ETag。
- `true` 删除文档。

两种模式都使用乐观的货币货币。 <xref:Orleans.Storage.InconsistentStateException> 而不是覆盖新记录。

<a id="serialization-and-record-size"></a>

## 序级化和记录大小

供应商将每个州的记录序列成一份消防站文件。 连载有效载荷、文档名称和元数据必须符合 [消防存储文档大小限制](https://cloud.google.com/firestore/quotas)。 在连续后测试生产形状状态,而不是仅仅依靠在模拟物体大小。

设置 <xref:Orleans.Persistence.Firestore.FirestoreStateStorageOptions.GrainStorageSerializer> 以自定义存储的演示文稿。 更改序列器不会重写现有文档, 因此替换必须读取先前的演示文稿或随同迁移 。

<a id="sample"></a>

## 抽样样本

[Google云烟火炉抽样](https://github.com/dotnet/orleans/tree/main/samples/GoogleFirestore) 配置持久性, 加上火苗集、 提醒和 grain 目录。 运行一次以写入状态并再次运行以观察持续计数器 。
