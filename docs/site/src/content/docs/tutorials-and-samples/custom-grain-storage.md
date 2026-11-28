---
title: 自定义 Grain 存储示例项目
description: 了解使用 .NET Orleans 编写的自定义 Grain 存储示例项目。
ms.date: 08/14/2026
ms.topic: tutorial
ai-usage: ai-assisted
---

<a id="custom-grain-storage"></a>

# 自定义 Grain 存储

在声明式 Actor 存储教程中，你学习了如何使用内置存储提供程序之一，将 Grain 状态存储在 Azure 表中。虽然 Azure 非常适合存储数据，但也存在许多其他方案。存储方案数量众多，不可能全部提供内置支持。因此，Orleans 的设计允许通过编写自定义 Grain 存储提供程序，轻松添加对首选存储方案的支持。

本教程将为本地单 Silo 应用程序构建一个简单的基于文件的 Grain 存储提供程序。它将二进制状态记录存储在本地文件系统中，并使用持久化 ETag 进行基本的乐观并发检查。

<a id="get-started"></a>

## 入门

Orleans Grain 存储提供程序是实现 <xref:Orleans.Storage.IGrainStorage> 的类，该接口包含在 [Microsoft.Orleans.Core](https://www.nuget.org/packages/Microsoft.Orleans.Core) NuGet 包中。本示例还实现 <xref:Orleans.ILifecycleParticipant`1>，其生命周期类型为 <xref:Orleans.Runtime.ISiloLifecycle>，以便在 Silo 生命周期中初始化。首先创建名为 `FileGrainStorage` 的类；教程末尾提供完整且可编译的实现。

各方法均实现 <xref:Orleans.Storage.IGrainStorage> 接口中的对应方法，接受用于表示底层状态类型的泛型类型参数。这些方法如下：

- <xref:Orleans.Storage.IGrainStorage.ReadStateAsync*?displayProperty=nameWithType>：读取 Grain 状态。
- <xref:Orleans.Storage.IGrainStorage.WriteStateAsync*?displayProperty=nameWithType>：写入 Grain 状态。
- <xref:Orleans.Storage.IGrainStorage.ClearStateAsync*?displayProperty=nameWithType>：清除 Grain 状态。

这三个方法接收相同的参数：

| 参数 | 含义 |
| --- | --- |
| `stateName` | 此状态记录的逻辑名称。对于 <xref:Orleans.Runtime.IPersistentState`1>，它是由 <xref:Orleans.Runtime.PersistentStateAttribute> 配置的状态名称；旧式 <xref:Orleans.Grain`1> 状态使用 `state`。一个 Grain 可以拥有多个命名状态记录，因此要将此值纳入存储键。旧版本接口将此参数称为 `grainType`，但它并不描述状态对象的 .NET 类型。 |
| `grainId` | 完整的 Orleans Grain 标识，包括 Grain 类型和主键。将其与 `stateName` 结合以标识记录。不要仅使用主键作为记录键，因为不同 Grain 类型可以使用相同的键。 |
| `grainState` | Orleans 传递给提供程序的状态容器。其中 <xref:Orleans.IGrainState`1.State> 属性包含应用程序状态，<xref:Orleans.IGrainState`1.ETag> 携带提供程序的乐观并发令牌，<xref:Orleans.IGrainState`1.RecordExists> 指示记录是否存在。读取操作填充这些属性；写入或清除操作更新这些属性，以反映已完成的操作。 |
| `T` | 状态有效载荷声明的 .NET 类型。使用 `T`（或 `typeof(T)`）处理序列化及类型元数据。它不是记录标识符，可由多个 Grain 和命名状态记录共享。 |

因此，`stateName` 与 `grainState.State?.GetType().Name` 不可互换。前者标识 Grain 的某个逻辑状态记录，不随有效载荷实现变化而改变。后者只是当前有效载荷的运行时类型名称，可能为 `null`，在涉及多态时可能不同于 `typeof(T)`，也可能在重构时变化。本示例使用 `stateName` 和 `grainId` 标识记录，并将有效载荷类型的处理委托给所配置的序列化器。

<xref:Orleans.ILifecycleParticipant`1.Participate*?displayProperty=nameWithType> 方法订阅 Silo 生命周期。

在开始实现之前，创建一个选项类，其中包含持久化 Grain 状态文件的根目录。创建名为 `FileGrainStorageOptions` 的选项文件，内容如下：

:::code source="snippets/custom-grain-storage/FileGrainStorageOptions.cs" id="file_grain_storage_options":::

创建选项类后，了解 `FileGrainStorage` 类的构造函数参数：

- `storageName`：通过 <xref:Orleans.Providers.StorageProviderAttribute> 指定哪些 Grain 应使用此存储提供程序，例如 `[StorageProvider(ProviderName = "File")]`。
- `options`：刚创建的选项类。
- `clusterOptions`：用于获取 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 的集群选项。
- `activatorProvider`：按照与 Orleans 序列化相同的激活规则，创建缺失或已清除的状态实例。

<a id="initialize-the-storage"></a>

## 初始化存储

要初始化存储，使用 `onStart` 函数订阅 <xref:Orleans.ServiceLifecycleStage.ApplicationServices?displayProperty=nameWithType> 阶段。参见以下 <xref:Orleans.ILifecycleParticipant`1.Participate*?displayProperty=nameWithType> 实现：

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="participate":::

`onStart` 函数在应用程序服务使用提供程序之前创建根目录。

另外，根据以长度为边界编码的服务 ID、Grain 类型、Grain 键及状态名称各组成部分，派生固定长度的文件名：

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="getkeystring":::

<a id="read-state"></a>

## 读取状态

要读取 Grain 状态，先派生其记录路径，如果文件存在则读取文件。

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="readstateasync":::

记录头包含持久化的不透明 `ETag`。设置 <xref:Orleans.IGrainState`1.RecordExists> 以指示读取操作是否找到了记录；如果记录不存在，则重置状态容器的全部三个属性。

将有效载荷读取为字节，并使用 <xref:Orleans.Storage.IStorageProviderSerializerOptions.GrainStorageSerializer?displayProperty=nameWithType> 反序列化，以原样保留任意序列化器输出，无需文本转换。

<a id="write-state"></a>

## 写入状态

写入状态与读取状态类似。

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="writestateasync":::

使用 <xref:Orleans.Storage.IStorageProviderSerializerOptions.GrainStorageSerializer?displayProperty=nameWithType> 生成二进制有效载荷。将调用方的 `ETag` 与持久化令牌比较，如果不同则抛出 <xref:Orleans.Storage.InconsistentStateException>。成功写入时会创建新的不透明 `ETag`，并写入记录文件。

<a id="clear-state"></a>

## 清除状态

清除状态包括在文件存在时将其删除。

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="clearstateasync":::

删除现有记录前，验证调用方的 `ETag` 与持久化令牌是否匹配。成功清除时会重置状态实例、`ETag` 和 <xref:Orleans.IGrainState`1.RecordExists>。

<a id="put-it-all-together"></a>

## 整合所有内容

接下来创建一个工厂，使选项可以按提供程序名称划分作用域，同时创建 `FileGrainStorage` 实例，以便向服务集合注册。

:::code source="snippets/custom-grain-storage/FileGrainStorageFactory.cs" id="file_grain_storage_factory":::

最后，在 <xref:Orleans.Hosting.ISiloBuilder> 和 <xref:Microsoft.Extensions.DependencyInjection.IServiceCollection> 上创建扩展方法。它们配置命名选项、注册配置验证和默认序列化器，并使用 Orleans 存储注册机制添加提供程序。

:::code source="snippets/custom-grain-storage/FileSiloBuilderExtensions.cs" id="file_silo_builder_extensions":::

Orleans 存储注册机制检测到 `FileGrainStorage` 实现了生命周期类型为 <xref:Orleans.Runtime.ISiloLifecycle> 的 <xref:Orleans.ILifecycleParticipant`1>，并注册其生命周期参与逻辑。

:::code source="snippets/custom-grain-storage/FileSiloBuilderExtensions.cs" id="storage_registration":::

这样便可以使用 <xref:Orleans.Hosting.ISiloBuilder> 上的扩展方法添加文件存储：

:::code source="snippets/custom-grain-storage/Program.cs" id="custom_grain_storage_program":::

现在，可以通过 <xref:Orleans.Providers.StorageProviderAttribute> 选择该提供程序，例如 `[StorageProvider(ProviderName = "File")]`，它会将 Grain 状态存储到选项中设置的根目录。参见 `FileGrainStorage` 的完整实现：

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="file_grain_storage":::
