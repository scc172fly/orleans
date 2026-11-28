---
title: Grain 引用
description: 了解如何在 Orleans 中创建、使用和理解 Grain 引用。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-references"></a>

# Grain 引用

Grain 引用是一个生成的代理对象，实现 Grain 接口并指向某个逻辑 Grain。它包含 Grain 的 [标识](grain-identity.md) 及用于调用它的接口，但不包含激活实例的网络位置。

当 Orleans 停用、重新创建或迁移目标激活实例时，引用仍然有效。获取引用是本地操作，不会触发 Grain 激活。

<a id="get-a-reference"></a>

## 获取引用

从客户端、托管服务或 Grain 中调用 <xref:Orleans.IGrainFactory.GetGrain*>：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="counter_interface":::
:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_counter_reference":::

在派生自 <xref:Orleans.Grain> 的类中，使用其 `GrainFactory` 属性。在其他服务中，注入 <xref:Orleans.IGrainFactory> 或 <xref:Orleans.IClusterClient>。

Grain 引用可以序列化。因此可以将其作为 Grain 方法的参数传入、作为调用结果返回，或者包含在持久化状态中。

<a id="interface-to-type-resolution"></a>

## 从接口解析 Grain 类型

通常，接口和键足以让 Orleans 确定 Grain 类型。如果恰好有一个 Grain 类实现了该接口，Orleans 就会将该接口映射到这个类。

当多个类实现同一个接口时，优先使用不同的标记接口：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="marker_interfaces":::

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_marker_references":::

两个引用的键相同，但 Grain 类型不同，因此它们指向不同的逻辑 Grain。

在兼容性场景下，可以通过 <xref:Orleans.Metadata.DefaultGrainTypeAttribute> 为接口选择默认实现，也可以使用接受 Grain 类名前缀的 `GetGrain` 重载来消除实现歧义。显式标记接口通常更清晰，也更便于安全重构。

<a id="cast-a-reference-to-another-supported-interface"></a>

## 将引用转换为另一个受支持的接口

如果同一个 Grain 实现还支持其他 Grain 接口或扩展接口，请使用 <xref:Orleans.GrainExtensions.AsReference*>：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cast_grain_reference":::

这不会创建另一个 Grain，而是为同一个 Grain 标识创建另一个强类型代理。目标 Grain 类型必须支持所请求的接口。

在 Grain 内部，需要传递自身时，应传递自身的引用而不是 `this`：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="self_reference":::

<a id="reference-equality-and-storage"></a>

## 引用相等性和存储

多个代理对象可以指向同一个 Grain。如果需要比较标识，应比较逻辑标识，而不是依赖 CLR 对象标识。只有需要保留接口关系时才持久化引用；如果应用应在使用时解析当前接口，则持久化业务键。

<a id="advanced-resolve-from-grainid"></a>

## 高级用法：从 GrainId 解析引用

某些 `GetGrain` 重载接受 <xref:Orleans.Runtime.GrainId>。它们用于已经掌握解析后的 Grain 类型和编码键的框架或基础设施代码。大部分应用代码应使用强类型键的重载，以保留编译期的键和接口检查。
