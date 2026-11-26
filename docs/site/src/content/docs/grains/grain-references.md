---
title: "Grain 引用"
description: "创建、使用和原因 grain 参考 Orleans."
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-references"></a>

# Grain 引用

grain 引用是一个生成的代理服务器,它执行一个 grain 接口,并针对一个逻辑 grain。它包含 grain的 [身份身份](grain-identity.md) 和用来调用它的界面,但并不包含激活的网络位置。

当 Orleans 停止使用、重建或迁移目标激活时, 参考文件仍然有效。 获取引用是本地操作, 不启动 grain。

<a id="get-a-reference"></a>

## 获取参考

使用 <xref:Orleans.IGrainFactory.GetGrain*> 客户、托管服务或 grain:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="counter_interface":::
:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_counter_reference":::

在从 <xref:Orleans.Grain>产生的类别内,使用其 `GrainFactory` 属性。在其他服务中,输入 <xref:Orleans.IGrainFactory> 或 <xref:Orleans.IClusterClient>。

Grain 引用可以连号,可作为 grain 方法参数通过,从电话中返回,并持续使用。

<a id="interface-to-type-resolution"></a>

## 界面到类型分辨率

接口和密钥通常足以让 Orleans 识别 grain 类型。如果一个 grain 类完全执行该界面, Orleans 将界面映射到该类别。

当多个类别执行同一个界面时,更喜欢不同的标记界面:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="marker_interfaces":::

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_marker_references":::

这两种参考有相同的键,但有不同的 grain 类型,因此它们处理的是不同的逻辑 grains。

对于兼容性设想方案, <xref:Orleans.Metadata.DefaultGrainTypeAttribute> 可以为接口选择默认操作,并且 `GetGrain` 接受 grain 类名前缀可以分辨执行。 明确的标记界面通常更清楚, 且对重新构件更安全 。

<a id="cast-a-reference-to-another-supported-interface"></a>

## 输入引用到另一个支持的接口

如果同一 grain 执行支持另一 grain 接口或扩展界面,请使用 <xref:Orleans.GrainExtensions.AsReference*>:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cast_grain_reference":::

这不会创建不同的 grain。 它为同一 grain 身份创建了另一个输入的代理。 目标类型 grain 必须支持请求的接口 。

在 grain内,通过一个自引号而不是通过 `this`:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="self_reference":::

<a id="reference-equality-and-storage"></a>

## 参考平等与存储

多代理对象可以引用相同的 grain。比较身份需要时的逻辑身份而不是依赖 CLR 对象身份。只有在保留接口关系时才使用永久引用;当应用程序在使用时间解析当前界面时,持续域键。

<a id="advanced-resolve-from-grainid"></a>

## 高级: 从 GrainId产生的分辨率

有些 `GetGrain` 超载接受 <xref:Orleans.Runtime.GrainId>。这些功能用于已经具有解析 grain 类型和编码密钥的框架和基础设施代码。大多数应用程序代码应该使用输入的键超载,因为它保存了编辑时密钥和界面检查。
