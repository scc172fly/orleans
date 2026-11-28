---
title: 事件溯源配置
description: 在 Orleans 中配置 JournaledGrain 日志一致性和存储。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="event-sourcing-configuration"></a>
# 事件溯源配置

在 Grain 实现项目中引用 `Microsoft.Orleans.EventSourcing`。除非 Grain 接口项目的契约公开了事件溯源类型，否则接口项目不需要引用此包。

<a id="register-a-log-consistency-provider"></a>
## 注册日志一致性提供程序

在 Silo 上注册一个或多个提供程序：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="register_log_consistency":::

可用的注册方法如下：

- <xref:Orleans.Hosting.StateStorageSiloBuilderExtensions.AddStateStorageBasedLogConsistencyProvider*>
- <xref:Orleans.Hosting.LogStorageSiloBuilderExtensions.AddLogStorageBasedLogConsistencyProvider*>
- <xref:Orleans.Hosting.CustomStorageSiloBuilderExtensions.AddCustomStorageBasedLogConsistencyProvider*>

每种方法都有相应的 `AsDefault` 形式。如果已注册默认日志一致性提供程序和默认 Grain 存储提供程序，则可省略提供程序特性。

<a id="select-providers-on-a-grain"></a>
## 为 Grain 选择提供程序

状态存储和日志存储均使用标准的 Grain 存储提供程序：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="select_log_consistency_provider":::

提供程序名称必须与每个能够激活该 Grain 的 Silo 上的注册名称完全匹配。

自定义存储不使用 <xref:Orleans.Storage.IGrainStorage>。Grain 实现 <xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2>，并自行负责存储操作：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="custom_storage_grain":::

<a id="multi-cluster-responsibility"></a>
## 多集群部署的职责

自定义存储负责多集群部署所需的写入拓扑规则。提供程序保留注册时传入的 `primaryCluster` 参数，但该参数不会限制提交、配置 Orleans 多集群网络、复制存储或提供故障转移能力。应用程序及存储实现必须自行强制执行任何单写入者或区域写入规则。
