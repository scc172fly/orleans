---
title: "Silo 元元数据"
description: "通知 Orleans silos 安置和申请决定。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="silo-metadata"></a>

# Silo 元元数据

<a id="key-concepts"></a>

Silo 元数据是每个 silo都公布的不可更改的字符串到字符串地图。使用它来描述与职位安排有关的能力,如区域、硬件、作用或保留类型。

元数据支持 [grain 放置过滤器](../../grains/grain-placement-filtering.md)。 不要在其中设置证书、经常改变的健康数据或大型有效载荷。

<a id="configuration"></a>

## 配置

<a id="configure-silo-metadata-with-configuration"></a>

### 配置配置的配置 silo 元数据

<a id="example-appsettingsjson-configuration"></a>

<xref:Orleans.Runtime.MembershipService.SiloMetadata.SiloMetadataHostingExtensions.UseSiloMetadata*> 原文为 `Orleans:Metadata`:

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

环境变量可以设定具体的部署值,例如:

```text
Orleans__Metadata__cloud.region=westus3
Orleans__Metadata__role=recommendations
```

<a id="configuring-silo-metadata-in-code"></a>

### 将 silo 元数据配置为代码

<a id="example-direct-code-configuration"></a>

您也可以在程序上提供价值:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="configure_silo_metadata":::

元数据为 silo 实例的寿命固定。重新启动 silo 以公布已更改的值。

<a id="usage"></a>

## 用法

<a id="access-metadata-for-a-specific-silo"></a>

### 特定 silo访问元数据

<a id="example-access-metadata-for-a-silo"></a>

将 <xref:Orleans.Runtime.MembershipService.SiloMetadata.ISiloMetadataCache> 注入到 silo 服务或 Orleans 组件中:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="read_silo_metadata":::

缓存跟随集成, 从活动 silos获取元数据。 <xref:Orleans.Runtime.MembershipService.SiloMetadata.ISiloMetadataCache.GetSiloMetadata*> 返回本地缓存值, 这样它不会在请求路径上添加远程调用 。

<a id="define-a-metadata-contract"></a>

## 界定元数据合同

- 使用稳定、 命名间距键, 如 `cloud.region` 或 `hardware.accelerator`。
- 将关键名称和价值作为申请合同处理。
- 定义滚动部署期间缺失或未知值的行为 。
- 当它们喂养位置或遥测时, 保持低心智状态值 。
- 确保 silos 与所有所需的放置过滤器匹配。

元数据补充多种不同的 silo 配置:使用 <xref:Orleans.Configuration.GrainTypeOptions.Classes> 时 silo 无法主机主机 grain 类, 元数据放置过滤器, 当它可以容纳该类时, 但它应该根据能力选择 。
