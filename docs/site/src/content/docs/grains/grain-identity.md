---
title: Grain 标识
description: 了解 Orleans 中的 Grain 类型、键和 GrainId 值。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-identity"></a>

# Grain 标识

每个 Grain 都具有稳定的逻辑标识，由以下两部分组成：

1. **Grain 类型**，用于标识其实现。
1. **Grain 键**，用于标识该类型中的某一个逻辑 Grain。

Orleans 使用 <xref:Orleans.Runtime.GrainId> 表示完整标识。`GrainId` 公开 `Type` 和 `Key`，并不公开 `PrimaryKey` 属性。应用代码通常使用强类型的 [Grain 引用](grain-references.md) 和键辅助方法，而不是直接构造 `GrainId` 值。

<a id="choose-a-key-type"></a>

## 选择键类型

在 Grain 接口上声明键的形式：

| 接口 | 传给 `GetGrain` 的键 |
|---|---|
| <xref:Orleans.IGrainWithStringKey> | `string` |
| <xref:Orleans.IGrainWithGuidKey> | <xref:System.Guid> |
| <xref:Orleans.IGrainWithIntegerKey> | `long` |
| <xref:Orleans.IGrainWithGuidCompoundKey> | <xref:System.Guid> 加字符串扩展部分 |
| <xref:Orleans.IGrainWithIntegerCompoundKey> | `long` 加字符串扩展部分 |

根据应用的业务领域选择键，例如客户 ID、设备 ID 或账户编号。Grain 键的作用域是 Grain 类型，因此两种不同的 Grain 类型可以使用相同的键，而不会指向同一个 Grain。

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="string_key_interface":::
:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_string_key_grain":::

当应用有意只访问某个类型的一个逻辑 Grain 时，可以使用固定键，例如 `"default"`。这是一种使用约定，而不是独立的单例功能。

<a id="read-the-current-grains-key"></a>

## 读取当前 Grain 的键

在 Grain 内部，使用与其接口匹配的键辅助方法：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="read_string_key":::

其他辅助方法包括 `GetPrimaryKey()`、`GetPrimaryKeyLong()`，以及可返回复合键字符串扩展部分的重载。

还可以通过 `this.GetGrainId()` 或 `((IGrainBase)this).GrainContext.GrainId` 获取运行时标识。对于需要同时访问 Grain 类型和编码后的键的基础设施代码，请使用这种形式。

<a id="grain-type-names"></a>

## Grain 类型名称

按照约定，Orleans 从实现类推导 Grain 类型名称。当类型名称必须保持稳定、不依赖 CLR 类名时，请使用 <xref:Orleans.GrainTypeAttribute>：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="stable_grain_type":::

将显式声明的 Grain 类型名称视为持久标识符。更改已经部署的类型名称会创建不同的逻辑 Grain 命名空间，可能使现有引用或持久化数据无法关联到新的实现。

<a id="work-with-grainid"></a>

## 使用 GrainId

高级基础设施代码可以解析或创建不带类型约束的标识：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="create_grain_id":::

在应用代码中优先使用 `IGrainFactory.GetGrain<TGrainInterface>(key)`。强类型引用同时携带逻辑标识和用于调用的接口，而单独的 `GrainId` 不提供可调用的契约。

<a id="why-identity-is-logical"></a>

## 为什么标识是逻辑标识

Grain 标识不是进程地址。Orleans 可以在任何兼容的 Silo 上激活 Grain，在其空闲时停用它，随后在其他位置重新创建它。整个过程中，引用始终指向同一个逻辑 Grain。正是这种位置无关性，使 Grain 引用能够安全地在调用中传递，并持久化以供以后使用。
