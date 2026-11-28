---
title: Silo 元数据
description: 为 Orleans Silo 添加元数据，供放置和应用程序决策使用。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="silo-metadata"></a>

# Silo 元数据

<a id="key-concepts"></a>

Silo 元数据是由每个 Silo 发布的不可变字符串到字符串映射。使用它描述与放置相关的能力，例如区域、硬件、角色或预留资源类型。

元数据支持 [Grain 放置筛选](../../grains/grain-placement-filtering.md)。不要在其中存放凭据、频繁变化的健康数据或大型有效载荷。

<a id="configuration"></a>

## 配置

<a id="configure-silo-metadata-with-configuration"></a>

### 通过配置设置 Silo 元数据

<a id="example-appsettingsjson-configuration"></a>

<xref:Orleans.Runtime.MembershipService.SiloMetadata.SiloMetadataHostingExtensions.UseSiloMetadata*> 读取 `Orleans:Metadata`：

```json
{
  "Orleans": {
    "Metadata": {
      "cloud.region": "westus3",
      "hardware.accelerator": "gpu",
      "role": "recommendations"
    }
  }
}
```

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="silo_metadata_from_configuration":::

环境变量可以设置特定于部署的值，例如：

```text
Orleans__Metadata__cloud.region=westus3
Orleans__Metadata__role=recommendations
```

<a id="configuring-silo-metadata-in-code"></a>

### 在代码中配置 Silo 元数据

<a id="example-direct-code-configuration"></a>

也可以通过编程方式提供值：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="configure_silo_metadata":::

元数据在 Silo 实例的整个生命周期内保持固定。若要发布变更后的值，请重启 Silo。

<a id="usage"></a>

## 使用方法

<a id="access-metadata-for-a-specific-silo"></a>

### 访问特定 Silo 的元数据

<a id="example-access-metadata-for-a-silo"></a>

将 <xref:Orleans.Runtime.MembershipService.SiloMetadata.ISiloMetadataCache> 注入 Silo 服务或 Orleans 组件：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="read_silo_metadata":::

缓存跟随集群成员资格的变更，并从活动 Silo 获取元数据。<xref:Orleans.Runtime.MembershipService.SiloMetadata.ISiloMetadataCache.GetSiloMetadata*> 返回本地缓存值，因此不会给请求路径增加远程调用。

<a id="define-a-metadata-contract"></a>

## 定义元数据契约

- 使用稳定的、带命名空间的键，例如 `cloud.region` 或 `hardware.accelerator`。
- 将键名称和值视为应用程序契约。
- 定义滚动部署期间缺失值或未知值的处理行为。
- 当这些值用于放置或遥测时，保持值的基数较低。
- 确保有足够多的 Silo 满足每个必需的放置筛选器。

元数据是异构 Silo 配置的补充：当 Silo 无法承载某个 Grain 类时使用 <xref:Orleans.Configuration.GrainTypeOptions.Classes>；当 Silo 可以承载该类，但应基于其能力进行选择时，使用基于元数据的放置筛选器。
