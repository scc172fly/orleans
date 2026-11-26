---
title: "Grain 身份"
description: "理解 grain 类型、键,以及 GrainId 数值以内值 Orleans."
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-identity"></a>

# Grain 身份

每个 grain 都有稳定的逻辑特性,其来源是:

1. 确定履行的 **grain 类型**。
1. **grain 键**,其中确定了该类型中的一种逻辑 grain 。

Orleans 表示 <xref:Orleans.Runtime.GrainId>的完整身份。 A `GrainId` 显示 `Type` 和 `Key`;它不显示 `PrimaryKey` 属性。应用代码通常使用打字 [grain 引用](grain-references.md) 和关键助手,而不是直接构建 `GrainId` 值。

<a id="choose-a-key-type"></a>

## 选择密钥类型

声明 grain 接口上的键形:

| 接口界面 | 向 `GetGrain` 提供密钥  |
|---|---|
| <xref:Orleans.IGrainWithStringKey> | `string` |
| <xref:Orleans.IGrainWithGuidKey> | <xref:System.Guid> |
| <xref:Orleans.IGrainWithIntegerKey> | `long` |
| <xref:Orleans.IGrainWithGuidCompoundKey> | <xref:System.Guid> 加上字符串扩展名 |
| <xref:Orleans.IGrainWithIntegerCompoundKey> | `long` 加上字符串扩展名 |

从应用程序的域名中选择密钥, 如客户名、 设备 ID 或账户号。 Grain 密钥由 grain 类型所覆盖, 因此两种不同的 grain 类型可以使用相同的密钥而不必提及相同的 grain。

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="string_key_interface":::
:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_string_key_grain":::

使用固定键,例如 `"default"` ,当应用程序有意处理一种类型的逻辑 grain 时。这是一个公约,而不是单独的单吨特性。

<a id="read-the-current-grains-key"></a>

## 读取当前 grain的密钥

在 grain内,使用匹配其接口的密钥助手:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="read_string_key":::

其他助手包括 `GetPrimaryKey()`、 `GetPrimaryKeyLong()`和超载,以返回复数键的字符串扩展。

运行时身份也可以从 `this.GetGrainId()` 或 `((IGrainBase)this).GrainContext.GrainId`获得。使用该格式用于需要 grain 类型和编码键并列的基础设施代码。

<a id="grain-type-names"></a>

## Grain 类型名称

按公约, Orleans 从执行类中产生一个 grain 类型名称。当类型名称必须稳定时使用 <xref:Orleans.GrainTypeAttribute> ,而不需与 CLR 类别名称分开:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="stable_grain_type":::

将直线 grain 类型名称作为耐久识别符号处理。修改已部署类型名称创建了不同的逻辑 grain 命名空间,并可以切断现有引用或从新执行中持续的数据。

<a id="work-with-grainid"></a>

## GrainId工作

高级基础设施可以分析或创建未类型身份 :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="create_grain_id":::

优先使用应用代码中的 `IGrainFactory.GetGrain<TGrainInterface>(key)` 。输入引用既包含逻辑特性,也包含用来调用它的界面,光 `GrainId` 不提供可调用的合同。

<a id="why-identity-is-logical"></a>

## 为何身份是合乎逻辑的

A grain 身份不是程序地址 Orleans 能够激活 grain 任何兼容 silo,当闲置时停止使用它,然后在其他地方再创建。 grain 位置独立是 grain 安全通过电话引用,并持续使用。
