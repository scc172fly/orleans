---
title: 编译期生成的上下文
description: 为 NativeAOT 注册 Orleans 序列化类型的封闭类型图。
ms.topic: how-to
---

<a id="compile-time-generated-contexts"></a>

# 编译期生成的上下文

<xref:Orleans.Serialization.GeneratedContext> 注册由静态构造的 Orleans 编解码器及深拷贝器支持的封闭类型图。在 .NET 10 及更高版本上使用 NativeAOT 发布序列化组件时，请使用上下文，使原生编译器能够编译每个所需的泛型实现。

<a id="declare-the-type-graph"></a>

## 声明类型图

将 <xref:Orleans.GeneratedContextTypeAttribute`1> 应用于派生自 `GeneratedContext` 的顶级非泛型分部类。使用 C# 11 或更高版本时，此特性的类型实参声明一个封闭的根类型。编译器要求特性类型实参必须是封闭类型。生成器沿已序列化的模型成员及集合元素遍历，注册所有受支持的依赖项。例如，根类型 `List<Dictionary<string, int>>` 包含列表、字典、字符串及整数的实现。

将上下文与其所提供封闭类型图的模型一起声明。模型库导出该上下文，应用使用 `AddGeneratedContext` 注册它。库利用完整源代码信息生成工厂；使用者针对其普通引用程序集编译，并执行其实现程序集中的工厂。应用可将库上下文与自身的上下文组合使用。

:::code language="csharp" source="snippets/serializer-contexts/SerializerContextExample.cs" id="serializer_context_declaration":::

模型使用 <xref:Orleans.GenerateSerializerAttribute> 以及稳定的 <xref:Orleans.IdAttribute> 成员标识符。被引用的程序集提供生成的编解码器及复制器，其实际构造函数元数据决定服务构造方式。NativeAOT 实现使用直接成员访问或静态访问器；JIT 应用还支持生成的动态访问器。

<a id="link-library-contexts"></a>

## 链接库上下文

应用 <xref:Orleans.GeneratedContextImportAttribute`1>，以包含库的上下文。被包含的上下文提供其声明的模型根类型及完整序列化依赖项。使用者可针对库的普通引用程序集编译，同时生成包含这些根类型的本地封装模型或集合类型图。

:::code language="csharp" source="snippets/serializer-contexts/ConsumerSerializerContextExample.cs" id="serializer_context_link":::

注册使用者的上下文。其生成的配置先包含库上下文，再注册本地类型图：

:::code language="csharp" source="snippets/serializer-contexts/ConsumerSerializerContextExample.cs" id="serializer_context_link_usage":::

被包含的上下文声明确切的封闭根类型，例如 `DocumentationPayload<int>`。当使用者需要另一个封闭类型图时，向库上下文添加另一个根类型。包含关系形成无环图，因此每个提供的根类型都有归属上下文。链接链以传递方式提供根类型，共享链接仅配置每个类型清单提供程序键一次。模型依赖循环仍支持序列化及复制。上下文要求存在可访问的无参构造函数。包含关系存在循环、链接无效，或图超过 1,024 个上下文或 128 层嵌套链接时，生成器报告 `ORLEANS0115`。

生产者拥有完整的依赖信息和生成的工厂。私有成员 ID、复制策略及服务字段保留在所属程序集内。跨越不透明链接的本地集合依赖项使用静态封闭的持有器，以保留递归构造能力。

<a id="models-requiring-external-code-generation"></a>

## 需要外部代码生成的模型

为其他程序集的原始模型生成序列化器时，需要读取其实现元数据，包括私有存储信息及 record 类型构造信息。此类模型程序集使用 `ProduceReferenceAssembly=false`，使普通项目引用能够提供所需的完整元数据。自行生成并导出上下文的程序集可以生成引用程序集。

未提供链接上下文的外部模型类型图也需要完整的生成模型、编解码器及复制器元数据。`ORLEANS0115` 表示仅引用程序集形式的输入不足。优先在所属程序集内声明该类型图，并包含其上下文。

元数据发现还会遍历泛型实参、数组元素、已实现的接口及声明类型。它独立于编解码器注册保留可访问的别名元数据。此遍历具有单独的限制，即 1,024 个封闭元数据类型和 128 层嵌套依赖；不断扩展的泛型接口形态会触发 `ORLEANS0115`，并提示声明有限的类型元数据。已访问类型可终止有限的元数据循环。

受支持的类型图包括采用无参构造的基元叶节点编解码器、生成的枚举、`List<T>`、`Dictionary<TKey, TValue>`、可空值类型及从零开始索引的一维数组。生成的模型使用默认构造方式，类以 `object` 为基类。NativeAOT 使用现有模型生成器的直接访问及静态访问器支持。模型挂钩、自定义激活及其他集合类型族需要对应的实现支持，才能纳入上下文。若依赖项不受支持，或图超过 1,024 个封闭类型，生成器报告 `ORLEANS0115`。原生发布会诊断被引用实现是否需要不可用的运行时代码生成能力。

<a id="register-and-use-the-context"></a>

## 注册并使用上下文

<xref:Orleans.Serialization.ServiceCollectionExtensions.AddGeneratedContext*> 注册常规的 <xref:Orleans.Serialization.Serializer> 和 <xref:Orleans.Serialization.DeepCopier> 服务。解析优先使用封闭工厂，然后使用已注册的实现元数据及依赖注入。重复调用会组合上下文。封闭注册重复时，使用最先注册的实现。

:::code language="csharp" source="snippets/serializer-contexts/SerializerContextExample.cs" id="serializer_context_usage":::

上下文中的封闭序列化器注册将反序列化期间使用的类型名称预先填入通用类型解析器的缓存。<xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddAllowedType*> 授权格式化的名称；当应用保留了其他名称对应的元数据时，可通过反射解析这些名称。类型名称筛选器及组件验证适用于两种解析方式。编解码器及复制器保留 Orleans 字段标识符、引用跟踪和深拷贝隔离性。对象循环及共享引用在还原或复制后的对象图中保持其标识。

生成的结构体值序列化器和字段编解码器共享同一个规范编解码器实例。受支持的非密封模型将其生成的基类编解码器及基类复制器契约注册为别名，指向字段序列化及深拷贝所用的同一具体实例。封闭泛型模型包含其生成实现所请求的具体数组服务，而直接字节数组序列化及复制仍使用经过优化的字节数组实现。

<a id="dependency-construction-and-concurrency"></a>

## 依赖构造与并发

生成的工厂构造已知的封闭具体编解码器和复制器。生成的模型代码保留其具体依赖字段。递归依赖通过特定于提供程序且感知调用方的服务解析栈及静态封闭持有器，复用部分构造的实例。依赖持有器在解包前识别其提供程序，因此由依赖注入创建的调用方在递归解析期间保持相同的归属关系。构造函数保留这些引用。序列化、复制、激活及可选的浅复制查询均在构造完成后运行。可选复制器在首次被查询时缓存其已完成的浅复制结果，后续查询及复制复用该实例专属的结果。

每个服务提供程序针对每种实现类型缓存一个已完成的实例。感知调用方的解析栈上的临时构造作用域拥有待完成服务及类型化缓存索引。封闭工厂加入时，该作用域获取提供程序的缓存锁，并在最外层构造函数成功后发布完整依赖图。若构造失败，作用域丢弃待完成的图并释放其状态；下一次解析会构造新的依赖项，同时保留此前已完成的服务。若构造函数捕获嵌套故障，作用域会在发布前重新抛出原始错误。如果实例尚不可用时遇到工厂循环，依赖图将以异常结束，并提示使用感知调用方的生成代码辅助工具或静态封闭持有器处理递归构造函数依赖。序列化器会话及复制上下文独立跟踪每次操作的引用标识。

封闭工厂及基于元数据的激活在同一解析管线中组合循环依赖图。工厂事务期间创建的自动服务及缓存条目参与该事务的发布与回滚。当元数据构造函数进入封闭工厂时，发布发生在该外层元数据构造函数完成之后。在封闭工厂加入依赖图之前，普通元数据激活通过应用已构建的提供程序解析其依赖注入服务。

构造期间，通过提供程序拥有的序列化契约、以元数据注册的实现，以及使用 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddSerializerService*> 注册的显式封闭工厂解析依赖项。生成的模型构造函数可在同一图中组合已注册的集合编解码器、复制器及封闭元素工厂。使用返回捕获实例的工厂注册共享实例，或提供显式供应外部依赖的封闭构造工厂。这样，即使应用通过可变服务集合构建多个服务提供程序，每个待完成依赖图的依赖项仍保持稳定。构造期间执行外部依赖注入查找会给出上述注册指南，并使待完成的图以异常结束。在构造过程之外，服务外观将普通及键控查找委托给应用已构建的服务提供程序，保留其注册快照、选择规则及生存期。

基于反射的激活使用由生成清单、带注解的手动注册或类型化生成依赖所保留的构造函数。封闭原生代码已被设为根时，NativeAOT 可以实例化泛型实现。上下文工厂使编译器能够看到这些实例化操作。在普通元数据构造函数分配调用方之前所需的前向值类型持有器，使用静态封闭的构造或依赖注入工厂。在运行时请求不可用的原生实例化时，会给出注册指南；请将其封闭类型图添加至上下文，或提供封闭工厂。

<a id="diagnose-unsupported-input"></a>

## 诊断不受支持的输入

<a id="serializer-initialization-and-interface-collections"></a>

### 序列化器初始化及接口集合

对于严格的 NativeAOT 发布，请使用 `AddGeneratedContext` 配合上述受支持的封闭类型图初始化序列化器。<xref:Orleans.Serialization.ServiceCollectionExtensions.AddSerializer*> 启用自动程序集发现和通用编解码器；原生发布会分析这些额外路径，包括其运行时激活及元数据要求。同时调用两种注册方法会将这些路径与上下文的封闭工厂组合。

接口集合解析器将可识别的编解码器类型映射到其基类型元数据中已经存在的封闭集合接口。NativeAOT 可以解析已设为根的编解码器类型的此类元数据，同时保留现有集合别名及传输格式。声明为接口集合的上下文根类型或模型成员会触发 `ORLEANS0115`，指出该依赖项并列出受支持的具体集合形态。请在上下文类型图中声明受支持的具体集合，并注册运行时所使用的每种具体类型。

`ORLEANS0114` 表示上下文声明无效。`ORLEANS0115` 指出不受支持的封闭类型以及所需变更。声明受支持的运行时类型图中可能出现的所有具体类型，包括通过额外根类型引入的类型。

<xref:Orleans.Serialization.CodecNotFoundException> 表示运行时类型或序列化服务未注册。在请求序列化或复制之前，请注册其封闭类型图。

生成的字典注册使用 `EqualityComparer<TKey>.Default` 序列化字典。若值或序列化有效载荷中包含自定义比较器，将抛出 `NotSupportedException`。需要自定义比较器的应用可通过 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddSerializer*> 注册感知比较器的封闭编解码器。

在上下文之前注册该编解码器及复制器，使其成为被选定的实现。集合工厂将元素依赖绑定至每种类型选定的注册，使根字典与列表内字典具有相同的比较器行为。先注册上下文则使这两条路径均保留上下文的默认比较器实现。

对于 NativeAOT 可执行程序，请启用原生发布：

:::code language="xml" source="snippets/serializer-contexts/NativeContextPublish.props" id="serializer_context_native_publish":::

将这些属性导入可执行项目，并使用 `AddGeneratedContext` 注册上下文。JIT 和 NativeAOT 应用使用相同的生成实现优先的解析管线。注册应用使用的完整类型图，确保每个所需的编解码器、复制器及原生泛型实现均可用。

将裁剪及 AOT 警告保持为错误。仓库的集中式原生冒烟测试矩阵在 .NET 10 上测试严格的 `Contexts`、`Factories` 及 `InterfaceCollections` 场景，包括封闭元数据激活、设为根的泛型实例化、接口集合编解码器元数据，以及与生成注册并存的反射类型查找。
