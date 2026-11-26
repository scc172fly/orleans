---
title: "Orleans中的序列化"
description: "学习 .NET Orleans中的序列化和自定义序列化器。"
ms.date: 05/23/2025
ms.topic: overview
uid: orleans-serialization
---

<a id="serialization-in-orleans"></a>

# Orleans中的序列化

Orleans使用两种广泛的序列:

- **Grain 调用序列号**:用于对出入 grains的物体进行序列号。
- **Grain 储存的序列化**:用于将物品与储存系统相连接和从储存系统进行序列化。

本条大部分条款侧重于: grain 通过《公约》所载 Orleans. The [存储序列器Grain](#grain-storage-serializers) 一节讨论 grain 储存序列化。

生成编码管道、运行时间清单、电线识别和编码器内部发送,见 [内部的序列化和代码生成](../../implementation/serialization.md)。

<a id="use-orleans-serialization"></a>

## 使用 Orleans 序列号

Orleans 包括高级和可扩展的序列化框架,称为 **Orleans.Serialization**. 纳入《公约》的 Orleans 其目的是实现下列目标:

- **高性能**:序列器的设计优化是为了进行性能。更多详情见 [本介绍的本介绍](https://www.youtube.com/watch?v=kgRag4E6b4c)。
- **高度忠诚**:序列器忠实地代表了大多数 .NET类型系统,包括支持非专利、多形态、继承等级、对象身份和周期图。指针不支持,因为它们不是跨过程的便携式。
- **灵活性**:您可以自定义序列器,通过创建 [*替代*](#surrogates-for-serializing-foreign-types) 或委托外部序列图书馆支持第三方图书馆,例如 **System.Text.Json**、 **Newtonsoft.Json**、 **MessagePack**和 **Google.Protobuf**。
- **版本容忍**:序列器允许申请类型随着时间变化,支持:
  - 增加和删除成员
  - 子分类
  - 数字扩大和缩小(e.g., `int` 至/从 `long`、 `float` 到/从 `double`)
  - 重命名类型

以高度忠诚形式呈现各种类型的序列器,对于系列制片厂来说相当罕见。

1. **动态类型和任意多形态**: Orleans 不执行对在 grain 调用中传递的类型的限制,并保持实际数据类型的动态性质。这意味着,例如,如果宣布 grain 接口中的某种方法接受 <xref:System.Collections.IDictionary>,但发件人通过 <xref:System.Collections.Generic.SortedDictionary`2>时,接收者确实得到了 `SortedDictionary` (尽管“静态合同”/grain 接口没有具体说明这种行为)。

1. **维护对象身份**: 如果在 grain 调用参数中同一对象多次通过, 或者间接从参数中间接指向不止一次, Orleans 将它序列一次。 在接收方, Orleans 将所有引用都恢复正确, 以使同一对象的两点在解除音质后仍然指向同一个对象。 在以下情景中保存对象身份很重要 : 想象 grain A 发送一个字典, 包括100个条目到 grain B, 字典点中的10个密钥到同一对象, `obj`。 没有保留对象身份, B 将会收到一个100个条目的字典, 并有10个关键点指向 `obj`10个不同的克隆。 在保存对象身份时, B 边上的字典与A 侧的十个密钥看起来完全相似, 指向单一对象 `obj`。 注意由于在 .NET 的默认字符串执行代码, 每个进程可能

为了支持版本容忍度, 连载器要求您明确哪些类型和成员被序列。 我们试图尽可能让它不痛苦。 标记所有可以连续的种类 <xref:Orleans.GenerateSerializerAttribute?displayProperty=nameWithType> 指示 Orleans 生成类型序列器代码。一旦您完成此操作, 您可以使用包含的代码修正添加所需的代码 <xref:Orleans.IdAttribute?displayProperty=nameWithType> 依你们类型可连号成员名单排列的,如下文所示:

:::image type="content" source="media/generate-serializer-code-fix.gif" alt-text="现有代码修正的动画图像 GenerateSerializerAttribute 当包含类型不包含 IdAttribute以其成员为主" lightbox="media/generate-serializer-code-fix.gif":::

这里的例子是一个在 Orleans中可以连号的类型,说明如何应用属性。

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="basic_employee_class":::

Orleans 支持继承,并单独将等级体系中的单个层次序列排列,允许它们有不同的成员身份。

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="inheritance_publication_book":::

在前一代码中,请注意 `Publication` 和 `Book` 都有成员 `[Id(0)]`,尽管 `Book` 来自 `Publication`。这是 Orleans 的建议做法,因为成员识别符号的范围是继承等级,而不是整个类型。你可以独立地从 `Publication` 和 `Book` 增聘成员,但申请一旦未经特别考虑就部署后,你不能在等级制度中插入一个新的基级。

Orleans 也支持与 `internal`、 `private`和 `readonly` 成员进行序列类型的排序,例如这类类型:

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="custom_struct_private_readonly":::

默认 Orleans 将您的全名编码成您类型序列序列。 您可以添加 <xref:Orleans.AliasAttribute?displayProperty=nameWithType>。如果这样做的结果是,使用一个具有重新命名能力的名称来对底层类别进行重命名或在组件之间移动,从而将类型序列化。则别名是全球范围的,在应用程序中不能有两个同值的别名。对于通用类型,别名数值必须包括以反向键前的通用参数数;例如, `MyGenericType<T, U>` 可能具有别名 <code>["我类型"2"]</code>.

<a id="serializing-record-types"></a>

## 串行 `record` 类型

Orleans 指定隐含的ID, 以按其参数位置记录主构件成员。 保存已部署类型的参数顺序,以便在滚动升级期间保持兼容性,并与储存和流中序列式的事件保持一致。 主要构件成员和普通成员占据了序列式代表的单独部分, 因此在机构内定义的成员可以重新使用相同的数字代号。

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="record_primary_constructor":::

将 <xref:Orleans.GenerateSerializerAttribute.IncludePrimaryConstructorParameters> 设为 `false` ,以便使用明确的 <xref:Orleans.IdAttribute> 说明选择可连号的成员。

<a id="generating-for-record-structs-in-referenced-assemblies"></a>

### 生成引用集会中记录台站

当SDK项目使用 <xref:Orleans.GenerateCodeForDeclaringAssemblyAttribute>生成引用组装的序列代码时, Orleans 读取该类型的汇编元数据。对于有自定义存取器的 `record struct` ,请在宣布该类型的组装中明确说明主构人成员:

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="record_struct_metadata_constructor":::

`IncludePrimaryConstructorParameters = true` 启用主元数部分。 `[method: ActivatorUtilitiesConstructor]` 对主建构人应用 <xref:Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute> , 即使类型有其他构建器, 也从元数据中选择参数。 注释精确地显示一个构建器。 Orleans 将参数与名称和类型属性匹配,并通过设置恢复定制的英寸属性。 这些说明也适用于可变 `record struct` 类型。

与一个编译器一起汇编合同组装,该编译器在主建构器上排放方法目标属性。 `CS0657` 日期 时 `[method: ActivatorUtilitiesConstructor]`,使用流 .NET SDK 或应用 `[ActivatorUtilitiesConstructor]` 直接对一个明确宣布的建筑师,且其参数名称、类型和身份识别码相同。

用于 `ORLEANS0106` 报告进口海关检索记录台的“未向任何可连号候选成员指定实地识别码”,在申报类型上加上说明,并重建合同组装。它们提供发电机使用的建筑师身份和成员包容信息。

添加此元数据时保留现有参数顺序和任何参数级别 `[Id(n)]` 注释。 这将保存主成员部分及其字段标识, 匹配记录源产生的序列。 属性级别 `[Id(n)]` 选择普通成员部分; 选择部署的主要构件成员的版式更改其线条身份, 即使数字标识没有改变 。

<a id="messagepack-serialization"></a>

## MessagePack 序号

您可以使用 [MessagePack](https://github.com/neuecc/MessagePack-CSharp) 作为 Orleans的外部序列器。 MessagePack 是一种高性能二进制序列格式,在保持快速序列和解除音量速度的同时,产生比 JSON 较小的有效载荷。

<a id="when-to-use-messagepack"></a>

### 何时使用 MessagePack

考虑在下列时间使用 MessagePack :

- 您需要与非( ).NET 支持系统 MessagePack
- 您已经拥有带有 MessagePack 属性(`[MessagePackObject]`, `[Key]`)的附加说明类型。
- 您想要比 JSON- 基串录器小一些有效载荷大小
- 您需要一个标准的二进格式

对于大多数 Orleans 应用程序,建议采用默认的 Orleans 序列器,因为它提供更高的忠诚度(.NET 类型系统支持)、目标身份保存和自动序列生成。

<a id="install-the-messagepack-serializer-package"></a>

### 安装 MessagePack 序列化器软件包

将 MessagePack 序列增殖器软件包添加到您的工程中:

```dotnetcli
dotnet add package Microsoft.Orleans.Serialization.MessagePack
```

<a id="configure-messagepack-serialization"></a>

### 配置 MessagePack 序列化

要配置 MessagePack 序列化,请使用 `AddMessagePackSerializer` 扩展方法:

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_basic_config":::

`isSerializable` 代表控制由 MessagePack 序列器处理的类型。与此前置序列器不匹配的类型可追溯到默认的 Orleans 序列器。

### MessagePackCodecOptions

您可以配置 MessagePack 使用序列器 `MessagePackCodecOptions`:

| 财产 | 类型类型 | 默认默认 | 说明 |
|----------|------|---------|-------------|
| `SerializerOptions` | `MessagePackSerializerOptions` | `MessagePackSerializerOptions.Standard` | 用于序列化的 MessagePack 序列化器选项。 |
| `AllowDataContractAttributes` | `bool` | `false` | 何时 `true`,允许使用 `[DataContract]` 使用 MessagePack. |
| `IsSerializableType` | `Func<Type, bool?>` | `null` | 代表确定某一类型是否应按 MessagePack序号编号。 |
| `IsCopyableType` | `Func<Type, bool?>` | `null` | 代表确定是否应当将某一类型复制到 MessagePack。 |

<a id="example-configure-with-options"></a>

### 示例: 配置选项

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_with_options":::

<a id="define-messagepack-types"></a>

### 定义 MessagePack 类型

由 MessagePack 序列排序的类型应该使用 MessagePack 属性:

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_type_definition":::

然后,您可以在 grain 接口中使用这些类型:

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_grain_interface":::

<a id="serializer-comparison"></a>

### 串级器比较

| 特征 | Orleans 原生 | MessagePack | System.Text.Json |
|---------|----------------|-------------|------------------|
| 格式格式格式格式格式格式格式 | 二进二进 | 二进二进 | 文本 (JSON) |
| .NET 类型忠诚 | 好极了 | 好 | 有限 |
| 对象身份 | 是 是 | 无 | 无 |
| 有效负载大小 | 小 | 最小 | 最大 |
| Cross-platform |  仅 .NET | 任何 MessagePack 客户端 | 任何 JSON 客户端 |
| 版本容忍度 | 是 是 | 是 是 | 是 是 |
| 需要设置 | 无无无无 | 明确的属性 | 明确的属性 |

<a id="surrogates-for-serializing-foreign-types"></a>

## 外国类型序列化的代理

有时,您可能需要在无法完全控制的 grains 之间传递类型。在这些情况下,在应用代码中手动转换到或从定制型号可能不切实际。 Orleans 为这些情况提供了解决办法:代用类型。代用类型被序列化,可以转换成目标型号。请参考以下外国类型的例子和相应的代用和转换器:

:::code source="./snippets/serialization/SurrogateExamples.cs" id="surrogate_value_type":::

在前一编码中:

- `MyForeignLibraryValueType` 是一个在消费库中定义的、不受控制的类型。
- `MyForeignLibraryValueTypeSurrogate` 是 `MyForeignLibraryValueType`的代位型号绘图。
- <xref:Orleans.RegisterConverterAttribute> specifies that `MyForeignLibraryValueTypeSurrogateConverter` acts as a converter to map between the two types. The class implements the <xref:Orleans.IConverter`2> interface.

Orleans 支持类型等级分类中类型类型的序列化(类型来自其他类型)。如果某一外国类型可能出现在类型等级体系中(e.g.),作为您所属类型中的某个类型的基础类别,则必须额外实施 <xref:Orleans.IPopulator`2?displayProperty=nameWithType> 接口。请参考以下例子:

:::code source="./snippets/serialization/SurrogateExamples.cs" id="surrogate_class_with_populator":::

<a id="versioning-rules"></a>

## 版本规则

版本容忍支持, 只要您在修改类型时遵守一套规则。 如果您熟悉 Google 协议缓冲(Protobuf) 等系统, 这些规则将会很熟悉 。

<a id="compound-types-class--struct"></a>

### 化合物类型 (`class` & `struct`)

- 继承是支持的, 但改变对象的继承等级却不支持。 您不能添加、 更改或删除一个类的基本等级 。
- 除下文 *数字* 一节所述的某些数字类型外,您不能更改字段类型。
- 您可以在继承等级的任意一点添加或删除字段。
- 您不能更改字段 ID 。
- 字段标识必须是类型等级中每个级别的独特, 但可以在基级和亚类之间重新使用。 例如, `Base` 类可以声明有ID `0`的字段, 而 `Sub : Base` 类可以声明同一ID `0`的不同字段。

<a id="numerics"></a>

### 数值

- 您不能更改数字字段的 *签名性质* 。
  - `int` 和 `uint` 之间的转换无效。
- 你可以改变 *宽宽* 中的数字字段。
  - E.g.,支持从 `int` 转换为 `long` 或 `ulong` 转换为 `ushort` 。
  - 如果字段的运行时间值造成溢出,则缩小宽度的转换会排除例外。
  - 从 `ulong` 转换为 `ushort` 只有在运行时值低于 `ushort.MaxValue`时才被支持。
  - 从 `double` 转换为 `float` 只有在运行时间值在 `float.MinValue` 和 `float.MaxValue`之间时才支持。
  - `decimal`的类似情况,其范围较窄,与 `double` 和 `float`相近。

<a id="copiers"></a>

## 复印机

Orleans 默认地促进安全,包括某些类同货币错误的安全。特别是, Orleans 立即复制在 grain 调用中通过的对象。 Orleans.Serialization 便利了此复制。当您对类型应用 <xref:Orleans.GenerateSerializerAttribute?displayProperty=nameWithType> 时, Orleans 也为该类型生成复印机。 Orleans 避免复制类型或以 <xref:Orleans.ImmutableAttribute>标记的单个成员。详情见 [Orleans中不可变型的序列化](./serialization-immutability.md)。

<a id="serialization-best-practices"></a>

## 逐步化最佳做法

- **Do** 使用 `[Alias("my-type")]` 属性给您类型的别名。有别名的类别可以重新命名,不断开兼容性。
- ❌ **不 不** 变化a `record` 至一经常 `class` 记录和分类的表示不完全相同,因为记录除了正式成员之外还有主要建筑师成员;因此两者不能互换。
- ❌ **不 不** 将新类型添加到一个可序列类型的现有类型等级结构中。您不能对现有类型添加新的基类。您可以安全地为现有类型添加一个新的子类。
- ✅ **do 做 Do Do** 取代用 <xref:System.SerializableAttribute> 与 <xref:Orleans.GenerateSerializerAttribute> 相应 <xref:Orleans.IdAttribute> 声明。
- **Do** 将每个类型的成员标识以零开始。子类中的标识及其基本等级可以安全地重叠。以下示例中的两个属性的标识均等于 `0`。

    :::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="best_practices_id_overlap":::

- **需要时,请** 扩大成员种类。您可以将 `sbyte` 扩大到 `short` ,将 `int` 扩大到 `long`。
  - 您可以缩小成员类型, 但如果所观察到的值无法被缩放的类型正确代表, 则会导致运行时间例外。 例如, `int.MaxValue` 不能由 `short` 字段来表示, 所以将 `int` 字段缩小到 `short` 字段, 如果遇到此值, 可能导致运行时间例外 。
- ❌ **不 不** 更改数字类型成员符号的符号。您不能更改成员的类型 `uint` 至 `int` 或 `int` 至 `uint`例如,

<a id="grain-storage-serializers"></a>

## 存储序列器Grain

Orleans 包括一个由提供商支持的持久性模型 grains,通过 <xref:Orleans.Grain`1.State?displayName=nameWithType> 或通过注入一个或多个 <xref:Orleans.Runtime.IPersistentState`1> 数值中 grain普通用途 <xref:Orleans.Storage.IGrainStorageSerializer> 支持的存储供应商使用一种模式,包括设置 <xref:Orleans.Storage.IStorageProviderSerializerOptions.GrainStorageSerializer?displayProperty=nameWithType> 提供商选项类别上的属性,例如:

- <xref:Orleans.Configuration.DynamoDBStorageOptions.GrainStorageSerializer?displayProperty=nameWithType>
- <xref:Orleans.Configuration.AzureBlobStorageOptions.GrainStorageSerializer?displayProperty=nameWithType>
- <xref:Orleans.Configuration.AzureTableStorageOptions.GrainStorageSerializer?displayProperty=nameWithType>
- <xref:Orleans.Configuration.AdoNetGrainStorageOptions.GrainStorageSerializer>

Grain 存储序列化目前默认为 `Newtonsoft.Json` 序列化状态。 您可以在配置时间修改该属性来替换此状态。 以下示例用 [OptionsBuilder\<TOptions\>](https://learn.microsoft.com/dotnet/core/extensions/options#optionsbuilder-api)来证明这一点:

:::code language="csharp" source="snippets/serialization/GrainStorageExamples.cs" id="grain_storage_serializer_config":::

详情见 [OptionsBuilder API](https://learn.microsoft.com/dotnet/core/extensions/options#optionsbuilder-api)。
