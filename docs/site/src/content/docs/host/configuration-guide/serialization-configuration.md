---
title: "Orleans中的序列配置"
description: "学习如何配置 .NET Orleans中的序列。"
ms.date: 08/10/2026
ms.topic: how-to
uid: orleans-serialization-configuration
---

<a id="serialization-configuration-in-orleans"></a>

# Orleans中的序列配置

Orleans 中的序列配置是整个系统设计的关键部分。虽然 Orleans 提供了合理的默认,但您可以配置序列化以满足应用程序的需要。在发送主机之间数据时, <xref:Orleans.Serialization?displayProperty=fullName> 支持将数据下放给其他序列器,例如 [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) 和 [System.Text.Json](https://www.nuget.org/packages/System.Text.Json)。您可以通过遵循这些执行设定的模式来添加对其它序列器的支持。对于 grain 存储,最好使用 <xref:Orleans.Storage.IGrainStorageSerializer> 来配置自定义序列器。

<a id="configure-orleans-to-use-newtonsoftjson"></a>

## 配置 Orleans 使用 `Newtonsoft.Json`

要配置 Orleans , 使用 `Newtonsoft.Json`来序列某些类型, 请先参考 [Microsoft.Orleans.Serialization.NewtonsoftJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.NewtonsoftJson) NuGet 软件包。 然后, 配置序列器, 指定要负责的类型 。 在下面的例子中, 我们指定 `Newtonsoft.Json` 序列器负责 `Example.Namespace` 命名空间的所有类型 。

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="configure_newtonsoft_json":::

在前一例子中, <xref:Orleans.Serialization.SerializationHostingExtensions.AddNewtonsoftJsonSerializer*> 的呼叫增加了使用 `Newtonsoft.Json.JsonSerializer`进行序列和取消值的支持。您必须对需要处理这些类型的所有客户都执行类似的配置。

对于标记为 <xref:Orleans.GenerateSerializerAttribute>的类型, Orleans 比 `Newtonsoft.Json` 序列器更倾向于生成的序列器。

<a id="configure-orleans-to-use-systemtextjson"></a>

## 配置 Orleans 使用 `System.Text.Json`

或者,配置 Orleans 以使用 `System.Text.Json` 来对类型进行序列排序,请参考 [Microsoft.Orleans.Serialization.SystemTextJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson) NuGet 软件包。然后,配置序列器,指定其负责的类别。在下面的例子中,我们具体说明, `System.Text.Json` 序列器负责 `Example.Namespace` 名称空间的所有类型。

- 安装 [Microsoft.Orleans.Serialization.SystemTextJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson) NuGet 软件包。
- 使用 <xref:Orleans.Serialization.SerializationHostingExtensions.AddJsonSerializer*> 方法配置序列器。

在与 <xref:Orleans.Hosting.ISiloBuilder>互动时,考虑以下示例:

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="configure_system_text_json":::

<a id="authorize-type-name-resolution"></a>

## 授权类型名称分辨率

注册外部序列器时选择了可以处理值的编码器。 它本身并不授权 Orleans 解决该序列器所接受的每一 CLR 类型名称。 类型名称分辨率是一个单独的安全边界, 而 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AllowAllTypes?displayProperty=nameWithType> 默认为 `false`。

这种区分对于诸如 `IReadOnlyList<TriggerRule>`,其中 `TriggerRule` 抽象是抽象的,数值由 `System.Text.Json`登记簿 JSON 序列化器并明确信任应用程序类型:

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="allow_type":::

<xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddAllowedType*> 使用 Orleans运行时类型名称来表示物质,包括对于已建构和嵌套的通用类型。 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AllowedTypes> 字符串集仍然支持兼容性,并包含 Orleans- 格式化的运行时间类型名称。最好使用 `AddAllowedType` 而不是手工构建这些名称。

如果应用程序组装的每一种类型都可信, 请允许该组装 :

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="allow_assembly":::

组装信任应用各组成部分。 构建的通用类型需要授权的一般定义和核准的参数。 Orleans 使用舱单登记、配置的名称、组装信托和过滤器,以授权该定义独立于其论点。

政策信任登记 <xref:Orleans.Serialization.ITypeNameFilter> 之前的地名 Orleans 装入相应的类型:

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="application_type_name_filter":::

登记有依赖注射的过滤器:

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="register_type_name_filter":::

过滤器返回 `true` ,以便允许、 `false` 拒绝或当它没有意见时拒绝,或者 `null` 。明确添加到 `AllowedTypes` 的类型具有权威性。对于其他名称,任何 `ITypeNameFilter` 的拒绝优先于其它类型名称过滤器和组装信任。 <xref:Orleans.Serialization.ITypeFilter> 在基于名称的检查没有肯定结果时提供解答-`Type` 退让;拒绝在后退中获胜。格式化和区分都应用这些检查,包括用于构建通用组件和矩阵元素类型的检查。

`ITypeFilter` 可明确准许某种封闭式通用类型作为完整的解释,包括过滤器没有意见的论点。 Orleans 仍然检查所有否认的理由,包括在未知论点之后提出的论据。

兼容性可以解密,可以禁用边界:

:::code language="csharp" source="snippets/serialization/TypeNameResolutionExamples.cs" id="allow_all_types":::

> [!WARNING]
> `AllowAllTypes` 绕过类型名称验证,包括自定义过滤器,并允许任何可解析的类型。 只有在序列输入完全可信时才使用它。 最好允许单个类型或信任的组件。

完全信任约束和数据验证指南,见 [序列序列化安全](../../security/serialization.md).
