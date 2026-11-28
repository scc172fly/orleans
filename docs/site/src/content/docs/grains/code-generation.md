---
title: Orleans 源码生成
description: 了解 Orleans 在构建时为 Grain 和序列化生成代码的机制。
ms.date: 09/01/2026
ms.topic: concept-article
---

<a id="orleans-source-generation"></a>

# Orleans 源码生成

Orleans 在构建时生成 Grain 代理、方法分派代码、序列化器和复制器。应用程序代码不存在运行时或初始化期间的代码生成流程。

<a id="reference-the-sdk"></a>

## 引用 SDK

使用与项目角色相匹配的包：

- 客户端应用程序使用 [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client)。
- Silo 应用程序使用 [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server)。
- 包含 Grain 契约、实现或可序列化类型的类库使用 [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk)。

这些包包含源码生成器和分析器。

<a id="grain-contracts"></a>

## Grain 契约

生成器通过 Grain 接口和实现所继承的 Orleans 基础接口发现它们。对于不受支持的方法签名、不可访问的类型、多个取消令牌参数以及其他契约错误，生成器会报告构建诊断。

支持的 Grain 方法返回类型为 <xref:System.Threading.Tasks.Task>、<xref:System.Threading.Tasks.Task`1>、<xref:System.Threading.Tasks.ValueTask> 和 <xref:System.Threading.Tasks.ValueTask`1>。生成器会为这些方法生成强类型引用和调用类。

<a id="serializable-types"></a>

## 可序列化类型

使用 <xref:Orleans.GenerateSerializerAttribute> 标记需要跨 Grain 边界传递或由 Orleans 存储的应用数据，并为序列化成员指定稳定的字段 ID：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="serializable_purchase_order":::
ID 是传输和存储契约的一部分。部署后不要复用或重新编号这些 ID。需要独立于 CLR 名称的稳定序列化类型别名时，使用 <xref:Orleans.AliasAttribute>。

<a id="hot-reload-for-serializable-members"></a>

## 可序列化成员的热重载

在开发构建中启用适用于热重载的代码生成结构：

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <OrleansHotReload>true</OrleansHotReload>
</PropertyGroup>
```

生成器为序列化器和复制器的字段赋予稳定标识，并在已有生成实例首次使用新增的具体编解码器与复制器依赖项时解析它们。因此，运行中的应用可以应用受支持的 .NET 热重载更新，添加强类型 `[Id]` 成员到标记了 `[GenerateSerializer]` 的类与记录中，包括其具体可序列化类型在同一次更新中新增的成员。

保持每个 `[Id]` 值稳定且唯一。仅新增成员的更新可保留现有传输契约。对现有成员类型、ID、类型层次结构、泛型结构、Grain 接口及生成的可调用请求类型所作的变更，必须遵循相应的 .NET 热重载及 Orleans 契约兼容性要求。如果更新通过 `object` 或接口等多态声明，或通过容器的元素类型引入了新类型，则需要重新启动应用以重建序列化器清单。

当 `OrleansHotReload` 未设置或为 `false` 时，生成器会为常规应用构建生成采用立即初始化的 readonly 字段。

<a id="generate-code-for-external-types"></a>

## 为外部类型生成代码

如果项目必须为其他位置声明且可以访问的类型生成序列化器，请使用 <xref:Orleans.GenerateCodeForDeclaringAssemblyAttribute>：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="generate_external_contract":::
尽可能在类型自身所在的位置维护序列化标注。为外部声明程序集生成代码会扩大兼容性维护范围，并可能增加构建输出体积。

<a id="other-net-languages"></a>

## 其他 .NET 语言

有关端到端互操作示例，请参阅 [Orleans F# 示例](https://learn.microsoft.com/samples/dotnet/samples/orleans-fsharp-sample/) 和 [Orleans Visual Basic 示例](https://learn.microsoft.com/samples/dotnet/samples/orleans-vb-sample/)。

<a id="inspect-diagnostics-and-output"></a>

## 检查诊断与输出

应将 Orleans 分析器和生成器的诊断视为契约错误。对于遵循该元数据的编辑器，Orleans 使用 `EditorBrowsable(Never)` 标记生成的实现类型。生成的文件保留其源位置，以支持诊断和调试；仍可通过编译器生成文件工具及直接导航到生成符号定义的方式查看这些文件。

Roslyn 在对引用程序集中的类型提供补全时遵循类型级 `EditorBrowsable`。只要生成符号的实现语法树参与编译，成员补全、同程序集的类型补全和实现搜索就仍包含这些生成符号。

生成器通过 Roslyn 的 `RegisterImplementationSourceOutput` 注册序列化器、复制器、激活器、代理、调用辅助程序、运行时元数据以及序列化器上下文的注册方法体。最小化的序列化器上下文声明使用常规源码输出补全开发者编写的 partial 类型，支持编辑器类型检查，包括引用了省略实现输出的项目的使用方。宿主决定是否在编译中包含实现输出；常规构建包含完整的生成实现。上下文结构及受支持契约的诊断使用常规输出。需要生产方生成的编解码器或复制器元数据的诊断则随实现输出一同提供。

XML 文档包含开发者编写的 API 注释。文档网站的 API 参考使用生成代码特性筛除生成的实现类型和成员，同时保留由开发者编写的 partial 类型，例如生成的上下文。

可选的 [Orleans 契约兼容性分析器](grain-versioning/contract-compatibility-analyzer.md) 将 Grain RPC 标识与签名记录到 `OrleansContracts.txt` 中，以便在滚动升级之前审查契约变更。

定义自定义 Grain 调用返回类型的库可以扩展生成的代理及可调用请求模型。请参阅 [自定义 Orleans 序列化代码生成](code-generation-customization.md)。

有关序列化规则和版本容错，请参阅 [Orleans 序列化](../host/configuration-guide/serialization.md)。
