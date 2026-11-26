---
title: "自定义 grain 储存样本项目"
description: "探索自定义 grain 储存样板项目 .NET Orleans."
ms.date: 08/14/2026
ms.topic: tutorial
ai-usage: ai-assisted
---

<a id="custom-grain-storage"></a>

# 自定义 grain 存储

在宣言性行为者存储指导课程中,您学会了如何允许 grains 使用一个内置存储提供者将状态保存在一张 Azure 表格中。虽然 Azure 是存储数据的一个好地方,但有许多替代方法存在。有这么多支持它们的方法都行不通。相反, Orleans 的设计是为了通过写一个自定义的 grain 存储提供者来方便地添加对首选存储的支持。

在此教程中, 您将建立一个基于文件的简单 grain 存储服务 , 用于本地的单项silo 应用程序。 它在当地的文件系统中储存二进制国家记录, 并使用持续存在的ETags进行基本的乐观货币对等检查 。

<a id="get-started"></a>

## 开始

Orleans grain 存储提供者是执行 <xref:Orleans.Storage.IGrainStorage>的类别,包含在 [Microsoft.Orleans.Core](https://www.nuget.org/packages/Microsoft.Orleans.Core) NuGet 包中。该样本还执行 <xref:Orleans.ILifecycleParticipant`1>,其生命周期类型为 <xref:Orleans.Runtime.ISiloLifecycle>,以便可以在 silo 生命周期内初始化。从创建名为 `FileGrainStorage`的类别开始;完整、汇编的实施出现在教程末尾。

每种方法在 <xref:Orleans.Storage.IGrainStorage> 接口中执行相应的方法,接受一个用于基础状态类型的通用类型参数。

- <xref:Orleans.Storage.IGrainStorage.ReadStateAsync*?displayProperty=nameWithType>:读取 grain的状态。
- <xref:Orleans.Storage.IGrainStorage.WriteStateAsync*?displayProperty=nameWithType>:写出一个 grain的状态。
- <xref:Orleans.Storage.IGrainStorage.ClearStateAsync*?displayProperty=nameWithType>:清除 grain状态。

所有三种方法都得到同样的论据:

| 参数 | 意义的含义含义 |
| --- | --- |
| `stateName` | 此状态记录的逻辑名称。 对于 <xref:Orleans.Runtime.IPersistentState`1>, 这是以 <xref:Orleans.Runtime.PersistentStateAttribute>配置的国名; 遗留 <xref:Orleans.Grain`1> 状态使用 `state`。 A grain 可以有多个命名的国家记录, 所以可以在存储键中包含此值。 界面的旧版本称为该参数 `grainType`, 但并不描述国家对象的 .NET 类型 。 |
| `grainId` | 完整的 Orleans grain 身份,包括其 grain 类型和主键。将其与 `stateName` 合并以识别记录。不要只使用主键的密钥记录,因为不同的 grain 类型可以使用相同的密钥。 |
| `grainState` | 国家集装箱 Orleans 发往提供者。 <xref:Orleans.IGrainState`1.State> 属性包含申请状态, <xref:Orleans.IGrainState`1.ETag> 提供商的票面上带有乐观货币表示, <xref:Orleans.IGrainState`1.RecordExists> 表示是否存在记录。读写后会显示这些属性;写或清晰更新以反映出已完成的操作。 |
| `T` | 国家有效载荷的申报 .NET 类型。使用 `T` (或 `typeof(T)`) 进行序列和元数据类型。它不是一个记录标识符,可以由许多 grains 和命名的国家记录共享。 |

因此, `stateName` 和 `grainState.State?.GetType().Name` 不可互换。 首先是确定 grain逻辑状态记录之一, 并且与有效载荷的安装无关, 其次只是当前有效载荷的运行时间类型名称; 可能是 `null`, 当涉及多形态时可以不同于 `typeof(T)` , 在重构过程中也可以改变。 样本使用 `stateName` 和 `grainId` 来进行身份识别和代表对配置的序列器处理的有效载荷类型。

<xref:Orleans.ILifecycleParticipant`1.Participate*?displayProperty=nameWithType> 方法符合 silo的生命周期。

在开始实施执行前,在启动实施之前创建一个选项类,其中包含根目录的选项类别 grain 状态文件被维持。创建一个名为“%s”的选项文件 `FileGrainStorageOptions` 包含以下内容:

:::code source="snippets/custom-grain-storage/FileGrainStorageOptions.cs" id="file_grain_storage_options":::

创建选项类别后, 探索 `FileGrainStorage` 类的构建参数 :

- `storageName`:指定哪个 grains 应通过 <xref:Orleans.Providers.StorageProviderAttribute>,例如 `[StorageProvider(ProviderName = "File")]`,使用该储存提供者。
- `options`: 选项类刚刚创建 。
- `clusterOptions`:检索 <xref:Orleans.Configuration.ClusterOptions.ServiceId>所用的组群选项。
- `activatorProvider`:使用与 Orleans 序列化相同的激活规则,创建缺失或清理的状态实例。

<a id="initialize-the-storage"></a>

## 初始化存储

初始化存储,订阅 <xref:Orleans.ServiceLifecycleStage.ApplicationServices?displayProperty=nameWithType> 阶段的 `onStart` 函数。考虑以下 <xref:Orleans.ILifecycleParticipant`1.Participate*?displayProperty=nameWithType> 执行:

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="participate":::

`onStart` 函数在应用程序服务使用提供者之前创建根目录。

另外,从有长度限制的服务编号、 grain 类型、 grain 键和国名组成部分中得出固定长的名文件名:

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="getkeystring":::

<a id="read-state"></a>

## 读状态

要读取 grain 状态, 获取记录路径, 如果文件存在的话, 则阅读它的文件 。

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="readstateasync":::

记录页头含有一个持续不透明 `ETag`。设置 <xref:Orleans.IGrainState`1.RecordExists> 以显示读取时是否找到记录,并在没有记录时重置所有三个状态容器属性。

将有效载荷作为字节读取,使用 <xref:Orleans.Storage.IStorageProviderSerializerOptions.GrainStorageSerializer?displayProperty=nameWithType>使其消散,不转换文字,保留任意的序列输出。

<a id="write-state"></a>

## 写状态

国家写作与阅读国家相似。

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="writestateasync":::

使用 <xref:Orleans.Storage.IStorageProviderSerializerOptions.GrainStorageSerializer?displayProperty=nameWithType> 来生产二进制有效载荷。 比较调用器的 `ETag` 和持续牌子, 当它们不同时丢出一个 <xref:Orleans.Storage.InconsistentStateException> 。 成功写入创建了一个新的不透明 `ETag` 并写入记录文件 。

<a id="clear-state"></a>

## 清状态

如果文件存在, 清理状态涉及删除它 。

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="clearstateasync":::

删除已有记录之前, 请核实调用者的 `ETag` 是否匹配持续标记。 成功清除后将重置国家实例、 `ETag`和 <xref:Orleans.IGrainState`1.RecordExists>。

<a id="put-it-all-together"></a>

## 都一起拼起来

下一步,建立一个工厂,在创建 `FileGrainStorage` 实例以方便服务收藏登记的同时,可以对供货商名称的选项进行范围界定。

:::code source="snippets/custom-grain-storage/FileGrainStorageFactory.cs" id="file_grain_storage_factory":::

最后,在 <xref:Orleans.Hosting.ISiloBuilder> 和 <xref:Microsoft.Extensions.DependencyInjection.IServiceCollection>上创建扩展。它们配置了命名选项、登记配置验证和序列器默认,并使用 Orleans 存储注册添加供应商。

:::code source="snippets/custom-grain-storage/FileSiloBuilderExtensions.cs" id="file_silo_builder_extensions":::

下 Orleans 储存登记注册的检测发现 `FileGrainStorage` 执行 <xref:Orleans.ILifecycleParticipant`1> 用于 <xref:Orleans.Runtime.ISiloLifecycle> 登记其参与生命周期的情况。

:::code source="snippets/custom-grain-storage/FileSiloBuilderExtensions.cs" id="storage_registration":::

允许在 <xref:Orleans.Hosting.ISiloBuilder>上使用扩展名添加文件存储 :

:::code source="snippets/custom-grain-storage/Program.cs" id="custom_grain_storage_program":::

现在您可以使用 <xref:Orleans.Providers.StorageProviderAttribute>等 `[StorageProvider(ProviderName = "File")]`选择提供者,并将 grain 状态存储在选项中设置的根目录中。考虑全面执行 `FileGrainStorage`:

:::code source="snippets/custom-grain-storage/FileGrainStorage.cs" id="file_grain_storage":::
