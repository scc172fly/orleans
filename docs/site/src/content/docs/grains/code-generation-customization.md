---
title: 自定义 Orleans 序列化代码生成
description: 使用自定义 Grain 调用返回类型和生成的可调用请求基类扩展 Orleans RPC 代码生成。
ms.date: 08/20/2026
ms.topic: how-to
---

<a id="customize-orleans-serialization-code-generation"></a>

# 自定义 Orleans 序列化代码生成

Orleans 会生成实现 Grain 调用的代理和请求类型。高级库可以将某种返回类型与自定义可调用请求基类关联，以扩展生成的 RPC 接口。返回类型定义面向应用程序的调用模型，请求基类则定义生成的请求如何进入 Orleans 运行时，以及如何将目标方法的结果转换为响应。

当库需要一种具有超出 <xref:System.Threading.Tasks.Task>、<xref:System.Threading.Tasks.ValueTask> 或 <xref:System.Collections.Generic.IAsyncEnumerable`1> 的明确语义的调用抽象时，可以使用此扩展点。该库负责定义此抽象的完成、取消、失败、生命周期、分配及并发契约。

完整应用示例请参阅 [示例目录](https://github.com/dotnet/orleans/blob/main/samples/README.md) 中的自定义 Grain 调用返回类型条目。

<a id="define-an-awaitable-return-type"></a>

## 定义可等待的返回类型

下面的 `GrainCall<T>` 以 Task 为基础。调用生成的代理会立即启动一次 Orleans 请求。返回值可被多次等待，并缓存终态结果，通过 Task 传播远程失败。

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="grain_call_return_type":::

<xref:Orleans.InvokableBaseTypeAttribute> 为继承自 <xref:Orleans.Runtime.GrainReference> 的代理注册开放泛型类型族。对于每个返回 `GrainCall<T>` 的方法，生成器将 `GrainCallRequest<T>` 用相同的 `T` 作为类型实参构造为封闭类型。

<a id="adapt-the-generated-request"></a>

## 适配生成的请求

请求基类承担两项职责：

1. 在调用方，其初始化器通过 <xref:Orleans.Runtime.IGrainReferenceRuntime> 提交生成的请求，并返回面向应用程序的 `GrainCall<T>`。
2. 在目标端，其 <xref:Orleans.Serialization.Invocation.IInvokable.Invoke*> 实现等待 Grain 实现返回的值，并创建一个 <xref:Orleans.Serialization.Invocation.Response>。

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="grain_call_request":::

<xref:Orleans.Invocation.ReturnValueProxyAttribute> 指示生成的代理返回 `request.InitializeRequest(this)`。因此，初始化器是启动操作或创建面向应用程序适配器的交接点。Orleans 验证重载解析是否选中了一个可访问、具体、非泛型的实例方法：该方法拥有一个按值传递且能够接收生成代理的参数，并且返回值可隐式转换为 Grain 方法声明的返回类型。

<xref:Orleans.GeneratedActivatorConstructorAttribute> 为生成的请求激活选择使用依赖注入的构造函数。当请求基类不需要任何服务时，无参数构造函数也可以使用。使用 <xref:System.NonSerializedAttribute> 标记仅供运行时使用的字段；生成的参数字段仍然是序列化请求的有效载荷。

<a id="use-the-return-type-in-a-grain-contract"></a>

## 在 Grain 契约中使用该返回类型

接口和实现均使用自定义返回类型。生成的请求使用相同签名重写 `InvokeInner`，而请求基类决定其返回值如何完成及传输。

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="grain_call_contract":::

示例的契约如下：

- **完成：** 代理初始化器立即提交一次请求，Task 在 Orleans 响应到达时完成。
- **失败：** Grain 同步抛出的异常及等待 `GrainCall<T>` 时产生的失败会转化为 Orleans 异常响应，并向调用方重新抛出。
- **取消：** Grain 的 `CancellationToken` 参数参与 Orleans 协作式调用取消。示例包装器不添加独立的取消源。
- **生命周期：** 提交后，运行时拥有生成的请求。包装器保留表示该次调用的 Task。
- **并发：** 以 Task 为基础的包装器支持多个等待者观察同一个终态结果。它表示一次调用，绝不会重复提交该调用。

自定义适配器可以实现其他策略，包括延迟提交、流式传输或订阅。应将这些策略作为公开返回类型契约的一部分明确规定，并考虑 Grain 激活实例的生命周期、释放、背压以及放弃消费的使用方。

<a id="registration-locations-and-precedence"></a>

## 注册位置及优先级

<xref:Orleans.InvokableBaseTypeAttribute> 可以出现在四种位置：

| 注册位置 | 用途 |
| --- | --- |
| 应用于 Grain 方法的特性类型 | 为带有该特性的方法选择行为。 |
| 返回类型 | 定义该返回类型所属库提供的常规适配器。 |
| 程序集 | 将由独立库拥有的返回类型与代理基类关联。 |
| 通过 <xref:Orleans.DefaultInvokableBaseTypeAttribute> 注册到代理基类 | 定义代理内置的返回类型族。 |

解析过程分为两轮。Orleans 首先考虑构造后的返回类型的精确匹配，再考虑开放泛型匹配。每一轮的优先级依次为：方法特性、返回类型、程序集注册、代理默认映射。因此，程序集中的精确注册优先于方法上的开放泛型注册。

每项注册的作用域均限定为代理基类原始泛型定义。针对 `GrainReference` 的映射不会影响另一个代理继承体系。程序集注册可以添加映射，包括由被引用的适配器程序集提供的映射，但不能替换代理内置的默认映射。

相同注册会合并。如果在同一获胜位置存在不同的可调用请求基类，则会产生按类型标识及程序集标识确定性排序的构建诊断，从而避免引用顺序改变生成的行为。

<a id="exact-and-open-generic-mappings"></a>

## 精确映射与开放泛型映射

精确映射将一个构造后的返回类型（例如 `GrainCall<int>`）与一个请求基类关联。它会覆盖针对 `GrainCall<>` 的开放映射。

开放泛型返回类型映射要求请求基类也是开放泛型，并具有相同的泛型参数数量。Orleans 使用返回类型的类型实参构造请求基类，并验证所有泛型约束。封闭的请求基类无法服务整个开放返回类型族。

对于专用协议或优化，可以使用精确映射。保留开放映射作为整个类型族的契约，以使新增的构造返回类型具有一致行为。

<a id="validation-requirements"></a>

## 验证要求

生成器会在使用方编译中验证所选请求基类：

- 它必须是可访问的、非静态且非密封的类。
- 其泛型参数数量必须与开放返回类型相同，且构造后的类型实参必须满足其约束。
- 生成的派生请求必须能够调用可访问的无参数构造函数（包括无歧义、可不传参的可选参数或 `params` 构造函数），或者调用标记了 <xref:Orleans.GeneratedActivatorConstructorAttribute> 的可访问构造函数。
- 使用依赖注入的构造函数具有按值传递的参数，且能够通过生成的派生类构造函数无歧义地绑定。
- <xref:Orleans.Invocation.ReturnValueProxyAttribute> 初始化器能够从生成的代理类型完成绑定，并返回声明的自定义返回类型。

应将这些诊断视为扩展契约错误。诊断会指出注册位置，以便库修正其发布的映射。

<a id="cross-assembly-libraries"></a>

## 跨程序集的库

适配器包可以注册来自不同程序集的类型：

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="cross_assembly_registration":::

使用方项目必须引用返回类型所属程序集、代理所属程序集及适配器程序集。生成器从源程序集和被引用的程序集读取程序集特性，然后在使用方编译中验证可访问性及绑定。公开类型和成员可以为每个使用方提供相同映射；`internal` 成员则需要与每个生成代理的程序集建立显式友元程序集关系。

将返回类型、请求基类、注册及所需的序列化器元数据作为一个版本化的兼容性单元发布。验证在不同程序集中生成代理的使用方，以及调用方和 Silo 分别运行相邻包版本的部署场景。

<a id="identity-serialization-and-compatibility"></a>

## 标识、序列化与兼容性

生成的请求类型就是序列化的消息正文。Orleans 为其赋予复合标识，其中包含调用标记、代理标识、Grain 接口类型和方法标识。方法标识来自 <xref:Orleans.IdAttribute>、<xref:Orleans.AliasAttribute> 或确定性的签名哈希。显式别名可以在 CLR 名称改变时保留标识。

自定义请求基类控制调用行为，生成的成员保存方法参数及目标分派元数据。保持序列化成员 ID 与别名稳定，并在滚动升级期间维持参数及结果类型的兼容性。即使 Grain 方法签名没有改变，更改映射也可能改变不同版本调用方与 Silo 所使用的生成请求的基类行为。

生成的请求参数和响应值由同一套 Orleans.Serialization 编解码器、复制器、激活器及转换器处理。发布适配器库之前，请参阅 [序列化与代码生成内部机制](../implementation/serialization.md)。

<a id="related-customization-surfaces"></a>

## 相关自定义扩展点

- 使用 [自定义序列化](../host/configuration-guide/serialization-customization.md) 中的 <xref:Orleans.Serialization.Serializers.IGeneralizedCodec>、<xref:Orleans.Serialization.Cloning.IGeneralizedCopier> 和类型筛选器。
- [配置序列化](../host/configuration-guide/serialization-configuration.md)，通过 <xref:Orleans.Serialization.ISerializerBuilder> 注册编解码器、复制器、激活器、转换器或外部序列化器。
- 使用 [声明不可变类型](../host/configuration-guide/serialization-immutability.md) 中的 <xref:Orleans.ImmutableAttribute>。
- 使用 [生成序列化器和稳定标识](code-generation.md) 中的 <xref:Orleans.GenerateSerializerAttribute>、<xref:Orleans.IdAttribute> 和 <xref:Orleans.AliasAttribute>。
- 在 Grain 调用筛选器中 [检查或修改生成的请求参数](interceptors.md)。
- [使用生成的请求元数据进行调度](request-scheduling.md)。
