---
title: "事件外包配置"
description: "配置 JournaledGrain 日志一致性和在 Orleans中存储。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="event-sourcing-configuration"></a>

# 事件外包配置

参考参考资料 `Microsoft.Orleans.EventSourcing` 从 grain 执行项目。 Grain 界面项目不需要那个包 除非他们在合同中暴露了"Eptic Surcing"类型

<a id="register-a-log-consistency-provider"></a>

## 登记一个符合逻辑的提供者

在 silo上登记一个或多个提供者:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="register_log_consistency":::

现有的登记方法包括:

- <xref:Orleans.Hosting.StateStorageSiloBuilderExtensions.AddStateStorageBasedLogConsistencyProvider*>
- <xref:Orleans.Hosting.LogStorageSiloBuilderExtensions.AddLogStorageBasedLogConsistencyProvider*>
- <xref:Orleans.Hosting.CustomStorageSiloBuilderExtensions.AddCustomStorageBasedLogConsistencyProvider*>

每个文件都有 `AsDefault` 格式。如果注册的是一个默认日志一致性提供者和默认 grain 存储提供者,则可以省略提供者属性。

<a id="select-providers-on-a-grain"></a>

## 在 grain上选择提供者

国家储存和日志储存使用标准 grain 储存提供者:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="select_log_consistency_provider":::

供应商名称必须与每个供应商的注册登记完全吻合 silo 能够激活 grain.

自定义存储不使用 <xref:Orleans.Storage.IGrainStorage>。 grain 安装 <xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2> ,并拥有存储操作:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="custom_storage_grain":::

<a id="multi-cluster-responsibility"></a>

## 多专题组责任

自定义存储拥有多组部署所需的书写地形规则。 `primaryCluster` 注册参数由提供方保留,但并不限制提交文件、配置 Orleans 多组网络、复制存储或提供失败。在应用和储存实施中执行任何单作者或区域文字规则。
