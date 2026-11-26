---
title: "自定义 Orleans 序列代码生成"
description: "延长 Orleans 定制 RPC 代码生成 grain- 呼叫返回类型和产生可援引的请求基地。"
ms.date: 08/20/2026
ms.topic: how-to
---

<a id="customize-orleans-serialization-code-generation"></a>

# 自定义 Orleans 序列代码生成

Orleans 生成执行 grain 调用的代理和请求类型。高级图书馆可以通过将返回类型与自定义可引用的请求基数挂钩来扩展产生 RPC表面的RPC表面。 返回类型定义了应用程序定位调用模式,而请求基数则界定了生成的请求如何进入 Orleans 运行时间以及目标结果如何成为响应。

当图书馆需要使用明确语义的调用抽象,超过 <xref:System.Threading.Tasks.Task>、 <xref:System.Threading.Tasks.ValueTask>或 <xref:System.Collections.Generic.IAsyncEnumerable`1>。当一个图书馆需要使用明确的语义的调用抽象时,该图书馆拥有完成、取消、故障、寿命、分配和同价货币合同的抽象信息。

完整应用程序,请参见习惯 grain- 呼叫返回类型输入 [样本目录](https://github.com/dotnet/orleans/blob/main/samples/README.md).

<a id="define-an-awaitable-return-type"></a>

## 定义待等待返回类型

以下 `GrainCall<T>` 是任务支持。 调用生成的代理请求会立即启动一个 Orleans 请求。 返回的值可以多次等待, 缓存其终端结果, 并通过任务传播远程失败 。

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="grain_call_return_type":::

<xref:Orleans.InvokableBaseTypeAttribute> registers the open generic family for proxies derived from <xref:Orleans.Runtime.GrainReference>. For every `GrainCall<T>` method, the generator closes `GrainCallRequest<T>` with the same `T`.

<a id="adapt-the-generated-request"></a>

## 调整生成请求

请求基地履行两项工作:

1. 在调用器上,其初始化器通过 <xref:Orleans.Runtime.IGrainReferenceRuntime> 提交生成的请求并返回应用程序的定位 `GrainCall<T>`。
2. 在目标方面,其 <xref:Orleans.Serialization.Invocation.IInvokable.Invoke*> 执行等待 grain 执行所返回的价值并创建 <xref:Orleans.Serialization.Invocation.Response>。

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="grain_call_request":::

<xref:Orleans.Invocation.ReturnValueProxyAttribute> tells the generated proxy to return `request.InitializeRequest(this)`. The initializer is therefore the handoff point which starts the operation or creates the application-facing adapter. Orleans validates that overload resolution selects an accessible, concrete, non-generic instance method with one by-value parameter accepting the generated proxy and a result implicitly convertible to the grain method's declared return type.

<xref:Orleans.GeneratedActivatorConstructorAttribute> selects a dependency-injected constructor for generated request activation. A parameterless constructor also works when the request base needs no services. Mark runtime-only fields with <xref:System.NonSerializedAttribute>; generated argument fields remain the serialized request payload.

<a id="use-the-return-type-in-a-grain-contract"></a>

## 在 grain 合同中使用返回类型

接口和执行使用自定义返回类型。生成的请求以相同的签名取代 `InvokeInner` ,而请求基点则决定其价值的完成和运输方式。

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="grain_call_contract":::

样本的合同是:

- **完成:** 代理初始化器立即提交一项请求,任务在 Orleans 收到回复
- **失败:** 同步 grain 的故障和在等待 `GrainCall<T>` 时产生的故障成为 Orleans 例外答复,并重新投到呼叫者手中。
- **取消:** a `CancellationToken` grain 参数参与合作取消 Orleans 调用。样本包装没有添加独立的注销源。
- **使用寿命:** 运行时在提交后拥有生成的请求。包装包保留代表该引用的任务 。
- **货币 :** 任务支持的包装支持多个等待结果, 观察相同的终端结果。 它代表一个引用, 却从未重新提交 。

用户可实施其他政策,包括懒惰的提交、流转或订阅。 指定这些政策作为公共回报类型合同的一部分,并计入 grain 启动寿命、处置、回压和被遗弃消费者。

<a id="registration-locations-and-precedence"></a>

## 登记地点和优先地点

<xref:Orleans.InvokableBaseTypeAttribute> can appear in four places:

| 登记地点 | 目的目的宗旨 |
| --- | --- |
| 适用于 grain 方法的属性类型 | 选择带有此属性的方法的行为 。 |
| 返回类型 | 定义返回类型库拥有的普通适配器。 |
| 集会 | 连接独立图书馆拥有的返回类型和代理基地。 |
| 通过 <xref:Orleans.DefaultInvokableBaseTypeAttribute> 的代理基底  | 定义代理服务器的内建返回家庭 。 |

分辨率以两个传球运行。 Orleans 首先考虑精确构造的返回类型匹配, 然后是开源匹配。 在每一个传球中,优先级为方法属性、 返回类型、 组装注册和代理默认。 因此, 准确的组装登记优先于开放的通用方法登记 。

每个登记都覆盖到代理服务器基础的原始通用定义范围内。 `GrainReference` 映射不影响另一个代理级别。组装注册可以添加映射,包括由引用的适配器组件提供的映射,无法取代代理用户的内置默认映射。

同一注册。在同一胜选地点,不同的可援引基地产生一种按类型和组装身份排列的确定性建筑诊断。这保持了参考顺序,以适应变化所产生的行为。

<a id="exact-and-open-generic-mappings"></a>

## 精确和开放基因绘图

精确的绘图关联关系 一种构建返回类型,如 `GrainCall<int>`, 并有一个请求基。 它取代了 `GrainCall<>`的开放式映射 。

开放通用返回映射需要具有相同性的一个开放式通用请求基础。 Orleans 用返回类型参数关闭请求基础,并核实所有通用限制。 封闭的请求基地不能为开放的返回家庭服务。

使用精确的绘图来进行专门协议或优化。 保持开放式映射作为全家族的合同, 这样新的构建返回类型会得到一致的行为 。

<a id="validation-requirements"></a>

## 审定要求

生成方在消费汇编中验证了选定的请求基础:

- 它是一种无障碍、非静态、不密封的阶级。
- 其通用性与公开返回类型相符,而所编的类别论证满足了其限制。
- 所产生的请求可援引无障碍无参数构造器,包括明确的任择或 `params` 构建器,或标有 <xref:Orleans.GeneratedActivatorConstructorAttribute>的无障碍建筑器。
- 依赖性注射的构造器具有副价值参数,并通过生成的衍生制造器毫不含糊地结合。
- A <xref:Orleans.Invocation.ReturnValueProxyAttribute> 初始化器从生成的代理类型绑定并返回已声明的自定义返回类型。

将诊断作为延期合同失效处理,确定登记站点,以便图书馆能够纠正其公布的地图。

<a id="cross-assembly-libraries"></a>

## 交叉组装图书馆

适应器包可以登记独立集会的种类:

:::code language="csharp" source="../snippets/compiled/Serialization/CustomGrainCallReturnType.cs" id="cross_assembly_registration":::

消费项目必须参考返回型所有人、代理所有人和适配组装。发电机读取源和引用的组件的组装属性,然后验证可访问性和对消费汇编的约束性。公共类型和成员给每个消费者都提供相同的地图; `internal` 成员要求与每个生成的代装建立明确的朋友组合关系。

发布返回类型、 请求基准、 注册以及任何必需的序列元数据, 作为版本兼容单位。 验证在不同的组装和部署中产生代理的消费者, 由调用者和[ 运行相邻软件包版本的 silos 生成 。

<a id="identity-serialization-and-compatibility"></a>

## 身份、序列化和兼容性

生成的请求类型是序列式信件体。 Orleans 向其提供一个复合身份,包括使用标记、代理身份、 grain 接口类型和方法身份。方法身份来自 <xref:Orleans.IdAttribute>、 <xref:Orleans.AliasAttribute>或确定性签名散列。当CLR名称更改时,明确化名保存身份。

自定义请求基控职业行为, 而生成的成员持有方法参数和目标发送元数据。 保持序列成员身份和别名稳定,并在滚动升级期间保持参数和结果类型兼容。 修改绘图可以改变生成的请求的基础行为,使其在调用者和 silo 版本之间,即使 grain 方法签名没有变化。

相同的 Orleans.Serialization 编码器、复印机、活动器和转换器过程生成了请求参数和响应值。在发布调适器库前先审查 [内部序列和代码生成](../implementation/serialization.md) 。

<a id="related-customization-surfaces"></a>

## 相关定制面

- [自定义序列化](../host/configuration-guide/serialization-customization.md) ,加上 <xref:Orleans.Serialization.Serializers.IGeneralizedCodec>、 <xref:Orleans.Serialization.Cloning.IGeneralizedCopier>和类型过滤器。
- [配置序列序列化](../host/configuration-guide/serialization-configuration.md) 和通过 <xref:Orleans.Serialization.ISerializerBuilder>登记编码器、复印机、活化器、转换器或外部序列器。
- [宣布不可改变的类型](../host/configuration-guide/serialization-immutability.md) 与 <xref:Orleans.ImmutableAttribute>.
- [生成序列器和稳定身份](code-generation.md) 与 <xref:Orleans.GenerateSerializerAttribute>, <xref:Orleans.IdAttribute>,和 <xref:Orleans.AliasAttribute>.
- [检查或修改生成的请求参数](interceptors.md) 内 grain 呼叫过滤器。
- [使用生成的请求元数据列表](request-scheduling.md).
