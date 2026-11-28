---
title: Orleans 中的序列化
description: 了解 .NET Orleans 中的序列化和自定义序列化器。
ms.date: 05/23/2025
ms.topic: overview
uid: orleans-serialization
---

<a id="serialization-in-orleans"></a>

# Orleans 中的序列化

Orleans 中使用的序列化大体分为两种：

- **Grain 调用序列化**：用于序列化传入 Grain 及从 Grain 返回的对象。
- **Grain 存储序列化**：用于序列化写入存储系统及从存储系统读取的对象。

本文主要介绍通过 Orleans 内置的序列化框架实现 Grain 调用序列化。[Grain 存储序列化器](#grain-storage-serializers)一节介绍 Grain 存储序列化。

有关生成代码流水线、运行时类型清单、传输格式标识及编解码器分派的内部机制，请参阅[序列化与代码生成内部机制](../../implementation/serialization.md)。

<a id="use-orleans-serialization"></a>

## 使用 Orleans 序列化

Orleans 内置了先进且可扩展的序列化框架，称为 **Orleans.Serialization**。该框架的设计目标如下：

- **高性能**：序列化器针对性能进行了设计和优化。更多细节请参阅[此演示文稿](https://www.youtube.com/watch?v=kgRag4E6b4c)。
- **高保真度**：序列化器能够忠实表示 .NET 类型系统的大部分特性，包括泛型、多态性、继承层次结构、对象标识和循环对象图。不支持指针，因为指针无法在进程之间移植。
- **灵活性**：可通过创建[*代理类型*](#surrogates-for-serializing-foreign-types)，或委托给 **System.Text.Json**、**Newtonsoft.Json**、**MessagePack** 和 **Google.Protobuf** 等外部序列化库，自定义序列化器以支持第三方库。
- **版本容错性**：序列化器允许应用程序类型随时间演进，支持：
  - 添加和移除成员
  - 派生子类
  - 扩宽和缩窄数值类型（例如 `int` 与 `long` 之间、`float` 与 `double` 之间的转换）
  - 重命名类型

在序列化器中，高保真地表示类型并不常见，因此有几点值得进一步说明：

1. **动态类型和任意多态性**：Orleans 不限制 Grain 调用传递的类型，并保留实际数据类型的动态特性。例如，Grain 接口中的某个方法声明为接受 <xref:System.Collections.IDictionary>，但发送方在运行时传递 <xref:System.Collections.Generic.SortedDictionary`2>，接收方实际收到的就是 `SortedDictionary`（即使“静态契约”即 Grain 接口并未指定这一行为）。

1. **保持对象标识**：如果同一个对象在 Grain 调用的实参中多次传递，或者从实参间接多次引用，Orleans 只会序列化它一次。在接收方，Orleans 正确恢复所有引用，使原本指向同一对象的两个引用在反序列化后仍指向同一对象。例如，假设 Grain A 向 Grain B 发送包含 100 个条目的字典，其中 10 个键在 A 中都指向同一个对象 `obj`。如果不保留对象标识，B 收到的字典包含 100 个条目，其中这 10 个键分别指向 `obj` 的 10 个不同副本。如果保留对象标识，B 中的字典与 A 中的字典具有相同的引用关系，这 10 个键均指向同一个对象 `obj`。请注意，.NET 默认字符串哈希码实现会针对每个进程进行随机化，因此字典、哈希集合等集合中的值顺序可能无法保留。

为了支持版本容错性，序列化器要求显式指定要序列化的类型和成员。我们尽量简化了这一过程。使用 <xref:Orleans.GenerateSerializerAttribute?displayProperty=nameWithType> 标记所有可序列化类型，指示 Orleans 为这些类型生成序列化代码。之后，可以使用内置的代码修复功能为类型中要序列化的成员添加所需的 <xref:Orleans.IdAttribute?displayProperty=nameWithType>，如下所示：

:::image type="content" source="media/generate-serializer-code-fix.gif" alt-text="动画演示当标有 GenerateSerializerAttribute 的类型缺少成员的 IdAttribute 标注时，编辑器如何提示并应用代码修复。" lightbox="media/generate-serializer-code-fix.gif":::

以下是 Orleans 可序列化类型的示例，演示如何应用相关特性。

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="basic_employee_class":::

Orleans 支持继承，并分别序列化继承层次结构的各个层级，使它们能够拥有各自的成员 ID。

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="inheritance_publication_book":::

在上述代码中，请注意 `Publication` 和 `Book` 都具有带 `[Id(0)]` 的成员，尽管 `Book` 派生自 `Publication`。这是 Orleans 推荐的做法，因为成员标识符的作用域是继承层级，而不是整个类型。可以分别在 `Publication` 和 `Book` 中添加或移除成员，但应用程序部署后，若无特殊处理，不能将新的基类插入继承层次结构中。

Orleans 还支持序列化带有 `internal`、`private` 和 `readonly` 成员的类型，例如以下类型：

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="custom_struct_private_readonly":::

默认情况下，Orleans 通过编码类型的完整名称来序列化类型。可以通过添加 <xref:Orleans.AliasAttribute?displayProperty=nameWithType> 覆盖此行为。这样，序列化时使用的类型名称可以在底层类重命名或迁移到其他程序集时保持稳定。类型别名具有全局作用域，同一应用程序中不能存在两个值相同的别名。对于泛型类型，别名值必须包含泛型参数数量，且该数字之前带有一个反引号；例如，`MyGenericType<T, U>` 可以使用别名 <code>[Alias("mytype\`2")]</code>。

<a id="serializing-record-types"></a>

## 序列化 `record` 类型

Orleans 根据 record 主构造函数成员的参数位置分配隐式 ID。对于已部署的类型，应保持参数顺序不变，以维持滚动升级期间以及与存储和流中的序列化实例之间的兼容性。主构造函数成员与普通成员在序列化表示中占据不同的区段，因此在类型主体中定义的成员可以复用相同的数字 ID。

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="record_primary_constructor":::

将 <xref:Orleans.GenerateSerializerAttribute.IncludePrimaryConstructorParameters> 设置为 `false`，通过显式 <xref:Orleans.IdAttribute> 注解选择可序列化成员。

<a id="generating-for-record-structs-in-referenced-assemblies"></a>

### 为引用程序集中的 record struct 生成序列化代码

当 SDK 项目使用 <xref:Orleans.GenerateCodeForDeclaringAssemblyAttribute> 为被引用程序集生成序列化代码时，Orleans 会读取类型编译后的元数据。对于具有自定义访问器的 `record struct`，请在声明该类型的程序集中显式指定主构造函数成员：

:::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="record_struct_metadata_constructor":::

`IncludePrimaryConstructorParameters = true` 启用主构造函数成员区段。`[method: ActivatorUtilitiesConstructor]` 将 <xref:Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute> 应用于主构造函数，从元数据中选取其参数，即使该类型还存在其他构造函数也能正确识别。只标注一个构造函数。Orleans 根据名称和类型将其参数与属性匹配，并通过自定义 init 属性的 setter 恢复其值。这些注解也适用于可变的 `record struct` 类型。

使用能够在主构造函数上生成以方法为目标的特性的编译器来编译契约程序集。如果 `CS0657` 针对 `[method: ActivatorUtilitiesConstructor]` 发出编译器警告，请使用较新的 .NET SDK，或直接将 `[ActivatorUtilitiesConstructor]` 应用于显式声明的构造函数，并保持相同的参数名称、类型和 ID。

如果 `ORLEANS0106` 针对导入的、具有自定义访问器的 record struct 报告“没有为任何候选可序列化成员分配字段 ID”（no field ids were assigned to any candidate serializable members），请在声明类型上添加上述两个注解，然后重新构建契约程序集。这些注解提供生成器所需的构造函数标识和成员包含信息。

添加这些元数据时，保留现有参数顺序以及任何参数级 `[Id(n)]` 注解。这样可保留主构造函数成员区段及其字段 ID，并与根据 record 源代码生成的序列化表示保持一致。属性级 `[Id(n)]` 选择的是普通成员区段；如果对已部署的主构造函数成员选择这种布局，即使数字 ID 不变，也会改变其传输格式标识。

<a id="messagepack-serialization"></a>

## MessagePack 序列化

可使用 [MessagePack](https://github.com/neuecc/MessagePack-CSharp)作为 Orleans 的外部序列化器。MessagePack 是高性能的二进制序列化格式，与 JSON 相比能够生成更小的有效载荷，同时保持快速的序列化和反序列化速度。

<a id="when-to-use-messagepack"></a>

### 何时使用 MessagePack

以下情况下可考虑使用 MessagePack：

- 需要与支持 MessagePack 的非 .NET 系统互操作
- 类型已使用 MessagePack 特性（`[MessagePackObject]`、`[Key]`）进行标注
- 希望获得比基于 JSON 的序列化器更小的有效载荷
- 需要标准化的二进制格式

对于大多数 Orleans 应用程序，推荐使用默认 Orleans 序列化器，因为它提供更高的保真度（支持 .NET 类型系统）、对象标识保留和自动序列化器生成能力。

<a id="install-the-messagepack-serializer-package"></a>

### 安装 MessagePack 序列化器包

将 MessagePack 序列化器包添加到项目中：

```dotnetcli
dotnet add package Microsoft.Orleans.Serialization.MessagePack
```

<a id="configure-messagepack-serialization"></a>

### 配置 MessagePack 序列化

使用 `AddMessagePackSerializer` 扩展方法配置 MessagePack 序列化：

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_basic_config":::

`isSerializable` 委托控制由 MessagePack 序列化器处理哪些类型。不符合该谓词的类型回退到默认 Orleans 序列化器。

### MessagePackCodecOptions

可以使用 `MessagePackCodecOptions` 配置 MessagePack 序列化器：

| 属性 | 类型 | 默认值 | 说明 |
|----------|------|---------|-------------|
| `SerializerOptions` | `MessagePackSerializerOptions` | `MessagePackSerializerOptions.Standard` | 用于序列化的 MessagePack 序列化器选项。 |
| `AllowDataContractAttributes` | `bool` | `false` | 为 `true` 时，允许使用 MessagePack 序列化带有 `[DataContract]` 标记的类型。 |
| `IsSerializableType` | `Func<Type, bool?>` | `null` | 用于确定某类型是否应由 MessagePack 序列化的委托。 |
| `IsCopyableType` | `Func<Type, bool?>` | `null` | 用于确定某类型是否应由 MessagePack 复制的委托。 |

<a id="example-configure-with-options"></a>

### 示例：使用选项配置

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_with_options":::

<a id="define-messagepack-types"></a>

### 定义 MessagePack 类型

由 MessagePack 序列化的类型应使用 MessagePack 特性：

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_type_definition":::

随后可在 Grain 接口中使用这些类型：

:::code source="./snippets/serialization/MessagePackExamples.cs" id="messagepack_grain_interface":::

<a id="serializer-comparison"></a>

### 序列化器比较

| 特性 | Orleans 原生序列化器 | MessagePack | System.Text.Json |
|---------|----------------|-------------|------------------|
| 格式 | 二进制 | 二进制 | 文本（JSON） |
| .NET 类型保真度 | 优秀 | 良好 | 有限 |
| 对象标识保留 | 支持 | 不支持 | 不支持 |
| 有效载荷大小 | 小 | 最小 | 最大 |
| 跨平台互操作 | 仅 .NET | 任意 MessagePack 客户端 | 任意 JSON 客户端 |
| 版本容错性 | 支持 | 支持 | 支持 |
| 所需配置 | 无 | 显式特性 | 显式特性 |

<a id="surrogates-for-serializing-foreign-types"></a>

## 用于序列化外部类型的代理类型

有时需要在 Grain 之间传递并非完全由你控制的类型。这种情况下，在应用程序代码中手动与自定义类型相互转换可能并不实用。Orleans 为这类场景提供代理类型。代理类型代替其目标类型进行序列化，并具备与目标类型相互转换的功能。下面是外部类型及对应代理类型和转换器的示例：

:::code source="./snippets/serialization/SurrogateExamples.cs" id="surrogate_value_type":::

在上述代码中：

- `MyForeignLibraryValueType` 是在所使用的库中定义、并非由你控制的类型。
- `MyForeignLibraryValueTypeSurrogate` 是映射到 `MyForeignLibraryValueType` 的代理类型。
- <xref:Orleans.RegisterConverterAttribute> 指定 `MyForeignLibraryValueTypeSurrogateConverter` 作为这两种类型之间的转换器。该类实现 <xref:Orleans.IConverter`2> 接口。

Orleans 支持序列化继承层次结构中的类型（派生自其他类型的类型）。如果外部类型可能出现在类型层次结构中（例如作为自有类型的基类），还必须实现 <xref:Orleans.IPopulator`2?displayProperty=nameWithType> 接口。请参阅以下示例：

:::code source="./snippets/serialization/SurrogateExamples.cs" id="surrogate_class_with_populator":::

<a id="versioning-rules"></a>

## 版本控制规则

修改类型时遵循以下规则，即可获得版本容错支持。如果你熟悉 Google Protocol Buffers（Protobuf）等系统，这些规则会比较熟悉。

<a id="compound-types-class--struct"></a>

### 复合类型（`class` 和 `struct`）

- 支持继承，但不支持修改对象的继承层次结构。不能添加、更改或移除某个类的基类。
- 除下文“数值类型”一节介绍的某些数值类型外，不能更改字段类型。
- 可以在继承层次结构的任意层级添加或移除字段。
- 不能更改字段 ID。
- 字段 ID 在类型层次结构的每个层级中必须唯一，但基类和派生类之间可以复用。例如，`Base` 类可声明 ID 为 `0` 的字段，`Sub : Base` 类也可声明另一个具有相同 ID（`0`）的字段。

<a id="numerics"></a>

### 数值类型

- 不能更改数值字段的*有符号性*。
  - `int` 与 `uint` 之间的转换无效。
- 可以更改数值字段的*位宽*。
  - 例如，支持从 `int` 转换到 `long`，或从 `ulong` 转换到 `ushort`。
  - 缩窄位宽的转换中，如果字段运行时的值导致溢出，则会抛出异常。
  - 从 `ulong` 转换到 `ushort` 仅在运行时值小于 `ushort.MaxValue` 时受支持。
  - 从 `double` 转换到 `float` 仅在运行时值位于 `float.MinValue` 和 `float.MaxValue` 之间时受支持。
  - `decimal` 的情况类似，其取值范围比 `double` 和 `float` 都窄。

<a id="copiers"></a>

## 复制器

Orleans 默认强调安全性，包括防止某些类型的并发错误。特别是，默认情况下 Orleans 会立即复制 Grain 调用中传递的对象。Orleans.Serialization 提供此复制功能。将 <xref:Orleans.GenerateSerializerAttribute?displayProperty=nameWithType> 应用于类型时，Orleans 也会为该类型生成复制器。Orleans 不会复制带有 <xref:Orleans.ImmutableAttribute> 标记的类型或单个成员。更多细节请参阅[Orleans 中不可变类型的序列化](./serialization-immutability.md)。

<a id="serialization-best-practices"></a>

## 序列化最佳实践

- ✅ **应当**使用 `[Alias("my-type")]` 特性为类型指定别名。具有别名的类型可以重命名而不破坏兼容性。
- ❌ **不要**将 `record` 改成普通的 `class`，反之亦然。record 除普通成员外还具有主构造函数成员，因此 record 和 class 的序列化表示不同，两者不能互换。
- ❌ **不要**将新类型插入可序列化类型的现有继承层次结构中。不得为现有类型添加新的基类，但可以安全地为现有类型添加新的派生类。
- ✅ **应当**将 <xref:System.SerializableAttribute> 的使用替换为 <xref:Orleans.GenerateSerializerAttribute> 及相应的 <xref:Orleans.IdAttribute> 声明。
- ✅ **应当**使每个类型的成员 ID 均从零开始。派生类和基类中的 ID 可以安全地重叠。下例中的两个属性的 ID 都等于 `0`。

    :::code language="csharp" source="snippets/serialization/BasicTypes.cs" id="best_practices_id_overlap":::

- ✅ **应当**根据需要扩宽数值成员类型。可以从 `sbyte` 扩宽到 `short`，再到 `int`，最后到 `long`。
  - 可以缩窄数值成员类型，但如果实际值无法由缩窄后的类型正确表示，就会发生运行时异常。例如，`int.MaxValue` 无法用 `short` 字段表示，因此将 `int` 字段缩窄为 `short` 时，若遇到这种值，就可能发生运行时异常。
- ❌ **不要**更改数值类型成员的有符号性。例如，不得将成员类型从 `uint` 改为 `int`，或从 `int` 改为 `uint`。

<a id="grain-storage-serializers"></a>

## Grain 存储序列化器

Orleans 包含由存储提供程序支持的 Grain 持久化模型，可通过 <xref:Orleans.Grain`1.State?displayName=nameWithType> 属性访问，或向 Grain 注入一个或多个 <xref:Orleans.Runtime.IPersistentState`1> 值来使用。通用的 <xref:Orleans.Storage.IGrainStorageSerializer> 接口为各提供程序提供统一的状态序列化自定义方式。受支持的存储提供程序采用在其选项类上设置 <xref:Orleans.Storage.IStorageProviderSerializerOptions.GrainStorageSerializer?displayProperty=nameWithType> 属性的模式，例如：

- <xref:Orleans.Configuration.DynamoDBStorageOptions.GrainStorageSerializer?displayProperty=nameWithType>
- <xref:Orleans.Configuration.AzureBlobStorageOptions.GrainStorageSerializer?displayProperty=nameWithType>
- <xref:Orleans.Configuration.AzureTableStorageOptions.GrainStorageSerializer?displayProperty=nameWithType>
- <xref:Orleans.Configuration.AdoNetGrainStorageOptions.GrainStorageSerializer>

Grain 存储序列化当前默认使用 `Newtonsoft.Json` 序列化状态。可在配置时修改该属性以替换它。以下示例使用 [OptionsBuilder\<TOptions\>](https://learn.microsoft.com/dotnet/core/extensions/options#optionsbuilder-api)演示这一过程：

:::code language="csharp" source="snippets/serialization/GrainStorageExamples.cs" id="grain_storage_serializer_config":::

更多信息请参阅 [OptionsBuilder API](https://learn.microsoft.com/dotnet/core/extensions/options#optionsbuilder-api)。
