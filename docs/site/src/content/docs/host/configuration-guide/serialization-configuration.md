---
title: Orleans 中的序列化配置
description: 了解如何在 .NET Orleans 中配置序列化。
ms.date: 08/10/2026
ms.topic: how-to
uid: orleans-serialization-configuration
---

<a id="serialization-configuration-in-orleans"></a>

# Orleans 中的序列化配置

Orleans 的序列化配置是整体系统设计的重要组成部分。虽然 Orleans 提供合理的默认设置，你也可以根据应用需求配置序列化。对于主机之间的数据传输，<xref:Orleans.Serialization?displayProperty=fullName> 支持将序列化委托给其他序列化器，例如 [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) 和 [System.Text.Json](https://www.nuget.org/packages/System.Text.Json)。可以遵循这些实现的模式，为其他序列化器添加支持。对于 Grain 存储，最好使用 <xref:Orleans.Storage.IGrainStorageSerializer> 配置自定义序列化器。

<a id="configure-orleans-to-use-newtonsoftjson"></a>

## 配置 Orleans 使用 `Newtonsoft.Json`

若要配置 Orleans 使用 `Newtonsoft.Json` 序列化某些类型，首先引用 [Microsoft.Orleans.Serialization.NewtonsoftJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.NewtonsoftJson) NuGet 包。然后配置序列化器，指定它负责处理的类型。在以下示例中，我们指定 `Newtonsoft.Json` 序列化器负责 `Example.Namespace` 命名空间中的所有类型。

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="configure_newtonsoft_json":::

在上例中，调用 <xref:Orleans.Serialization.SerializationHostingExtensions.AddNewtonsoftJsonSerializer*> 添加了使用 `Newtonsoft.Json.JsonSerializer` 序列化及反序列化值的支持。所有需要处理这些类型的客户端都必须执行类似的配置。

对于标记了 <xref:Orleans.GenerateSerializerAttribute> 的类型，Orleans 优先使用生成的序列化器，而不是 `Newtonsoft.Json` 序列化器。

<a id="configure-orleans-to-use-systemtextjson"></a>

## 配置 Orleans 使用 `System.Text.Json`

或者，若要配置 Orleans 使用 `System.Text.Json` 序列化你的类型，请引用 [Microsoft.Orleans.Serialization.SystemTextJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson) NuGet 包。然后配置序列化器，指定它负责处理的类型。在以下示例中，我们指定 `System.Text.Json` 序列化器负责 `Example.Namespace` 命名空间中的所有类型。

- 安装 [Microsoft.Orleans.Serialization.SystemTextJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson) NuGet 包。
- 使用 <xref:Orleans.Serialization.SerializationHostingExtensions.AddJsonSerializer*> 方法配置序列化器。

与 <xref:Orleans.Hosting.ISiloBuilder> 交互时，请参考以下示例：

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="configure_system_text_json":::

<a id="authorize-type-name-resolution"></a>

## 授权类型名称解析

注册外部序列化器会确定哪个编解码器能够处理某个值，但此操作本身并不授权 Orleans 解析该序列化器接受的所有 CLR 类型名称。类型名称解析是独立的安全边界，<xref:Orleans.Serialization.Configuration.TypeManifestOptions.AllowAllTypes?displayProperty=nameWithType> 默认为 `false`。

这种区别在 `IReadOnlyList<TriggerRule>` 等多态签名中尤其明显，其中 `TriggerRule` 是抽象类型，值由 `System.Text.Json` 处理。注册 JSON 序列化器并显式信任应用类型：

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="allow_type":::

<xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddAllowedType*> 使用 Orleans 的运行时类型名称格式化器，包括对已构造泛型类型及嵌套泛型类型的处理。为保持兼容性，仍支持 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AllowedTypes> 字符串集合，其中包含按 Orleans 格式表示的运行时类型名称。优先使用 `AddAllowedType`，而不是手动构造这些名称。

如果信任应用程序集中的所有类型，可以改为允许该程序集：

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="allow_assembly":::

程序集信任逐个组件生效。已构造泛型类型需要经过授权的泛型定义和获准的类型实参。Orleans 使用清单注册、配置名称、程序集信任及筛选器，独立于实参对泛型定义进行授权。带程序集限定的查找通过指定程序集解析，包括 CLR 类型转发。

对于基于策略的信任，注册 <xref:Orleans.Serialization.ITypeNameFilter>，以便在 Orleans 加载对应类型之前评估名称：

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="application_type_name_filter":::

通过依赖注入注册该筛选器：

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="register_type_name_filter":::

筛选器返回 `true` 表示允许，返回 `false` 表示拒绝，返回 `null` 表示不作判断。显式加入 `AllowedTypes` 的类型具有权威性。对于其他名称，任何 `ITypeNameFilter` 的拒绝优先于其他类型名称筛选器和程序集信任。<xref:Orleans.Serialization.ITypeFilter> 在基于名称的检查没有肯定结果时，提供基于已解析 `Type` 的后备检查；在此后备检查中，拒绝优先。格式化及解析均应用这些检查，包括对已构造泛型类型的组成部分及数组元素类型的检查。

`ITypeFilter` 可以将某个特定的封闭泛型类型作为完整的构造类型显式授权，包括筛选器未作判断的实参。Orleans 仍会检查每个实参是否被拒绝，包括位于未知实参之后的实参。

作为兼容性的例外机制，可以禁用此边界：

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="allow_all_types":::

> [!WARNING]
> `AllowAllTypes` 会绕过类型名称验证（包括自定义筛选器），并允许所有能够解析的类型。仅在完全信任序列化输入时使用。优先允许单个类型或受信任的程序集。

有关完整的信任边界和数据验证指南，请参见[序列化安全性](../../security/serialization.md)。
