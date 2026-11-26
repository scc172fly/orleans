---
title: "汇编时间生成的背景"
description: "注册 Orleans 用于 NativeAOT."
ms.topic: how-to
---

<a id="compile-time-generated-contexts"></a>

# 汇编时间生成的背景

A <xref:Orleans.Serialization.GeneratedContext> 登记固定构造的封闭类型图表 Orleans 使用上下文来发布 NativeAOT 日期 时 .NET 10年后,本地汇编者可以汇编所需的每份通用执行文件。

<a id="declare-the-type-graph"></a>

## 声明类型图

应用 <xref:Orleans.GeneratedContextTypeAttribute`1> 来应用从 `GeneratedContext`中衍生的顶级、非通用部分类别。 属性类型参数的参数使用 C# 11 或以后的选项, 声明一种封闭根型。 编译者执行封闭属性类型参数。 生成器遵循序列化模型成员和收集元素, 登记所有支持依赖性。 根如 `List<Dictionary<string, int>>` 包括列表、 字典、 字符串和整数执行 。

示范图书馆出口该背景,并使用 `AddGeneratedContext`进行应用程序登记。图书馆生成工厂时提供完整的源信息;消费者根据通常的参考组装编制文件,并将这些工厂从实施组装中执行。应用可以将图书馆背景与其自身背景结合起来。

:::code language="csharp" source="snippets/serializer-contexts/SerializerContextExample.cs" id="serializer_context_declaration":::

模型使用 <xref:Orleans.GenerateSerializerAttribute> 和稳定的 <xref:Orleans.IdAttribute> 成员识别符号。参考组件提供生成的编码器和复印机,其实际构建元数据决定服务建设。 NativeAOT 实施使用直接的成员访问或静态存取器;JIT应用程序还支持生成的动态存取器。

<a id="link-library-contexts"></a>

## 链接库背景

应用 <xref:Orleans.GeneratedContextImportAttribute`1> 来包含图书馆的背景。包括背景提供其申报的模型根及其完整的序列依赖性。消费者可以生成一个本地信封或收藏图,其中含有这些根,同时根据图书馆正常的参考组装进行汇编。

:::code language="csharp" source="snippets/serializer-contexts/ConsumerSerializerContextExample.cs" id="serializer_context_link":::

注册消费者背景。 它生成的配置包括登记本地图之前的图书馆背景:

:::code language="csharp" source="snippets/serializer-contexts/ConsumerSerializerContextExample.cs" id="serializer_context_link_usage":::

各种情况都表明确切的封闭根,例如 `DocumentationPayload<int>`。在消费者需要另一个封闭图形时,为图书馆环境添加另一根根。包括形成一个循环图,使每个供应的根都有自定义背景。链接链供应源可中转配置,共享链接可以一次配置每个类型最易显示的提供者密钥。模式依赖周期保留其序列化和复制支持。背景要求无障碍无参数构建。生成器报告 `ORLEANS0115` ,包括周期、无效链接或超过1,024个环境的图形或128个嵌套链接。

生产者拥有完整的依赖知识,并创造了工厂。 私人会员身份、复制政策和服务领域都留在拥有者集会中。 本地的收藏依赖者跨越了不透明的链接,静态封闭持有者使用固定式封闭持有者来保护循环建设。

<a id="models-requiring-external-code-generation"></a>

## 需要外部代码生成的模型

生成另一个组装原始模型的序列器时读取了它们的执行元数据,包括私人存储和记录施工事实。这种模型组件使用 `ProduceReferenceAssembly=false`,允许正常的项目引用提供所需的完整元数据。产生和输出自身背景的集成可以产生参考集。

外部模型图在没有链接上下文的情况下提供,还需要完全生成的模型、编码和复印元数据。 `ORLEANS0115` 确定一个仅参考的不足输入。最好在自有组装中公布该图表,并包括上下文。

元元数据发现还遵循通用参数、阵列元素、已安装接口和声明类型。它保存可获取的别名元元数据,而独立于编码器注册。这一跨轨有1 024个封闭元数据和128个嵌套依赖性分别的限制;扩大了通用界面形状的生成范围 `ORLEANS0115` 已经访问过的类型终止了有限的元数据周期。

支持的图表包括原始叶子编码器,其无参数构造、生成的昆虫、 `List<T>`, `Dictionary<TKey, TValue>`、无效值类型和单维零基零基阵列。生成模型使用默认构造和 `object` 类的基数。 NativeAOT 模型钩、自定义激活和更多的收集家庭在被纳入上下文之前,需要相应的执行支持。 `ORLEANS0115` 土著出版物的诊断表明,实施需要无法使用的运行时间代码生成。

<a id="register-and-use-the-context"></a>

## 使用上下文

<xref:Orleans.Serialization.ServiceCollectionExtensions.AddGeneratedContext*> 登记正常 <xref:Orleans.Serialization.Serializer> 和 <xref:Orleans.Serialization.DeepCopier> 服务。分辨率首先使用关闭工厂,然后登记执行元数据和依赖注射。重复电话结合了各种情况。重复的封闭注册使用第一批登记的实施。

:::code language="csharp" source="snippets/serializer-contexts/SerializerContextExample.cs" id="serializer_context_usage":::

上下文封闭的序列器登记在常见类型解密时使用的分类名称中播种。 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddAllowedType*> 授权使用格式化的名称,而更多名称则在应用保存元数据时通过反省来解析。类型名称过滤器和部件验证适用于两种分辨率形式。编码器和复印机保存 Orleans 实地识别符号、参考跟踪和深复制隔离。对象周期和共用引用在恢复或复制图内保留其身份。

生成的建筑价值序列器和实地编码器共用一个库式代码器。支持的非密封模型登记其生成的基准码和基础复印机合同,作为用于实地序列和深重复制的相同具体实例的别名。封闭通用模型包括由其产生的实施要求的具体阵列服务,而直接的字数序列和复制保留了优化的字数组执行。

<a id="dependency-construction-and-concurrency"></a>

## 依赖建筑和货币

生成厂家在创建已知的封闭式混凝土编码和复印机。生成模型代码保留了具体依赖区。 回归性依赖性再利用通过特定供应商、电话人了解服务分辨率堆和静态闭合持有者进行部分构建的场景。 依赖性持有人在拆卸前确认其供应商,因此依赖性注射生成的呼叫器在循环解析期间保持同样的所有权。 构造者保留这些参考文献。 在施工结束后进行序列化、复制、激活和可选浅本查询。 可选择的复印机先询问后隐藏完成的浅本结果,然后询问并复制每次重复使用的结果。

每个服务供应商每个执行类型都隐藏一个已完成实例。 调用者识别器分辨率堆的瞬时工程范围拥有待处理的服务并输入缓存索引。 当关闭工厂加入封闭工厂并在外部建筑商成功后公布完整的依赖图时,它会获得供方的缓存锁。 如果建设失败, 范围将丢弃未完成的图表并释放其状态; 下一个解决方案将构建新的依赖关系, 保存先前完成的服务。 由建构者捕获的嵌套故障被重新抛出作为发布前的原始错误。 在出现这种情况之前, 一个工厂周期会遇到错误, 并附有使用调用者识别生成代码的编码助手或静态封闭持有人来连接循环构造依赖的指南。 序列会和复制背景可以独立跟踪每次操作的参考身份 。

封闭工厂和基于元数据激活构成同一分辨率管道中的循环依赖图。在工厂交易期间创建的自动服务和缓存条目参与其出版和回滚。当元数据构建器进入关闭工厂时,该外部元数据构建器完成后,该出版物即为公布。普通元数据启动通过应用程序的固定供应商解决其依赖注射服务,然后关闭工厂加入这个图表。

建筑期间,依赖关系通过供应商拥有的序列合同、元数据登记实施和在 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddSerializerService*>注册的明确封闭工厂解决。生成模型的建构者可以在同一图中组成注册收集编码和与封闭元素工厂相配的复印机。使用一个返回被捕获实例的工厂来登记共同实例,或者提供一个明确供应外部依赖关系的封闭建筑厂。这保持了每个待决图形依赖性的稳定,即使应用程序从可变服务库中建立多个服务供应商。施工期间的外部依赖注射检查产生了这一登记指南并错过了未结图表。外部建筑、服务外表面代表普通和关键地向应用程序的建设服务提供商进行外观,保存其登记快照、选择规则和寿命。

以生成的货单、附加说明的人工登记或打字产生的依赖性保存的反射激活使用构造器。 NativeAOT 在封闭式本地代码扎根时可以实现通用实施。背景工厂使汇编者能看到这些即时数据。普通元数据构建者分配其调用机之前需要的前置价值类型持有人使用静闭式建造器或依赖注射厂。对无法使用的本地即时设备运行时间的请求会提供注册指导;将封闭图添加到环境或供应关闭型工厂中。

<a id="diagnose-unsupported-input"></a>

## 不支持的诊断性输入

<a id="serializer-initialization-and-interface-collections"></a>

### 串行器初始化和界面收藏

严格 NativeAOT 使用 `AddGeneratedContext` 上面描述的带有支持的封闭型图。 <xref:Orleans.Serialization.ServiceCollectionExtensions.AddSerializer*> 本地出版物分析了这些附加路径,包括运行时间激活和元数据要求。 这两种注册方法将这两个路径与环境封闭的工厂结合起来。

界面收集解析器绘制了一种公认的编码类型,用于已经存在于其基型元数据中的封闭式收集接口。 NativeAOT 可以解决这种根代码类型的元数据,同时保留现有的收藏别名和电线表示方式。一个被宣布为接口收集的上下文或模版成员生成 `ORLEANS0115`,识别该依赖性并列出所支持的混凝土收集形状。声明支持在上下文图表中的具体采集,并在运行时登记使用的每一具体类型。

`ORLEANS0114` 识别无效的上下文声明。 `ORLEANS0115` 识别不支持的封闭类型和所需更改。声明在支持运行时图中可能发生的每一种具体类型,包括通过其他根部引入的类型。

<xref:Orleans.Serialization.CodecNotFoundException> identifies an unregistered runtime type or serialization service. Register its closed graph before requesting serialization or copying.

生成的词典登记用 `EqualityComparer<TKey>.Default`将词典序列化。一个数值或序列有效载荷的定制比较器产生 `NotSupportedException`。具有自定义比较器要求的应用可以通过 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddSerializer*>注册一个对等器识别封闭编码。

收藏工厂将其元素依赖性与选定的每类注册挂钩, 并保留根字典和列表中的词典的比较行为。 上下文第一登记保留了上下文默认比对两种路径的实施。

NativeAOT 可执行文件允许本地出版物:

:::code language="xml" source="snippets/serializer-contexts/NativeContextPublish.props" id="serializer_context_native_publish":::

将这些属性导入可执行项目,并使用 `AddGeneratedContext`.JIT和 NativeAOT 应用程序使用相同的生成的一解管来登记上下文。注册应用所使用的完整图,以便每个应用程序都要求提供编码、复印机和本地通用实施。

保存库中央本地烟雾矩阵操作严格 `Contexts`, `Factories`,和 `InterfaceCollections` 设想的假设情景 .NET 10,包括封闭元数据激活、扎根的通用化、界面收集编码元数据以及连同生成的登记进行反射类型查询。
