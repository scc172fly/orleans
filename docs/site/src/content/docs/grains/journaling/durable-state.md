---
title: 使用持久化状态
description: 使用持久化值和集合构建采用实验性 Orleans Journaling 功能的 Grain。
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="use-durable-state"></a>

# 使用持久化状态

在 Silo 项目中安装 `Microsoft.Orleans.Durable` 预发行包。安装[日志存储提供程序](configuration.md#choose-a-storage-provider)，并在激活使用持久化状态的 Grain 之前完成配置。

<a id="define-a-durable-grain"></a>

## 定义持久化 Grain

将 <xref:Orleans.Durable.Journaling.IDurableStateManager> 注入普通的 <xref:Orleans.Grain>，并在构造期间声明具名状态组件。管理器只创建每个组件一次，Orleans 在应用程序方法运行之前恢复 Grain 的状态：

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingSnippets.cs" id="composed_shopping_cart":::

标准管理器使用激活实例的 <xref:Orleans.Runtime.IGrainContext> 构造时，会在依赖解析返回之前自行注册到 Grain 生命周期中。在构造期间或同步激活初始化期间声明持久化状态组件。<xref:Orleans.Runtime.GrainLifecycleStage.SetupState> 阶段的恢复在 <xref:Orleans.Grain.OnActivateAsync*> 和请求处理之前完成。这种组合方式同样适用于应用程序自定义的 Grain 基类。

字典的修改立即对当前激活实例可见。等待 <xref:Orleans.Durable.Journaling.IDurableStateManager.WriteStateAsync*> 完成，确立 Grain 状态待处理修改的持久化确认点。Grain 操作应接受 <xref:System.Threading.CancellationToken>，并将其传递给状态管理器的调用，使取消与调用方操作的生命周期一致。

此组合示例使用仓库源码进行编译，因此能够验证由构造过程负责的生命周期注册。

<xref:Orleans.Durable.Journaling.IDurableStateManager.GetOrAddState*> 接受应用程序约定的接口，例如 `IDurableDictionary<string, int>`。已注册的工厂提供相应实现。<xref:Orleans.Durable.Journaling.DurableStateManagerExtensions> 中的辅助方法提供相同的访问方式，且方法名称更易于发现。

<a id="use-keyed-injection-and-the-convenience-base-class"></a>

### 使用键控注入与便捷基类

<xref:Orleans.Durable.Journaling.DurableGrain> 提供类型为 `IDurableStateManager` 的 <xref:Orleans.Durable.Journaling.DurableGrain.StateManager> 属性，以及受保护的转发辅助方法 <xref:Orleans.Durable.Journaling.DurableGrain.WriteStateAsync*>。当状态组件的名称固定时，可使用 <xref:Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute> 注入该组件：

:::code language="csharp" source="./snippets/journaling/JournalingBasics.cs" id="keyed_durable_counter":::

使用键 `"count"` 注入 `IDurableValue<int>` 与调用 `stateManager.GetOrAddValue<int>("count")`，无论解析顺序如何，在同一个管理器中都会返回同一个对象。两种构造路径均使用管理器的注册表和已配置的格式。当便捷基类的辅助方法适合应用程序时，可以选用该基类；通过构造函数注入进行组合，则可让现有 Grain 继承体系获得相同的标准恢复行为。

<a id="select-a-state-type"></a>

## 选择状态类型

| 状态类型 | 初始化阶段的辅助方法 | 内存 API | 记录到日志的操作 |
| --- | --- | --- | --- |
| <xref:Orleans.Durable.Journaling.IDurableValue`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddValue*> | 单个可变值 | 设置 |
| <xref:Orleans.Durable.Journaling.IDurableDictionary`2> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddDictionary*> | <xref:System.Collections.Generic.IDictionary`2> | 设置、移除、清空、快照 |
| <xref:Orleans.Durable.Journaling.IDurableList`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddList*> | <xref:System.Collections.Generic.IList`1> 加上 `AddRange` | 添加、插入、设置、移除、清空、快照 |
| <xref:Orleans.Durable.Journaling.IDurableQueue`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddQueue*> | 队列操作 | 入队、出队、清空、快照 |
| <xref:Orleans.Durable.Journaling.IDurableSet`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddSet*> | <xref:System.Collections.Generic.ISet`1> | 添加、移除、清空、快照 |
| <xref:Orleans.Durable.Journaling.IDurableLog`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddLog*> | 通过纪元和序号定位的仅追加条目、读取操作和草稿 | 追加、提交草稿、截除旧条目、清空、封存、快照 |
| <xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddTaskCompletionSource*> | 持久化任务完成状态 | 完成、出错或取消 |
| <xref:Orleans.Runtime.IPersistentState`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddPersistentState*> | 记录式状态 | 设置或清除带版本的状态值 |

同一管理器注册的所有具名状态组件共享待写日志和写入确认边界。一个调用方发起的写入可能包含其他交错执行的调用方暂存的更改。先完成可能失败的工作，再暂存修改，并在返回成功之前等待 `WriteStateAsync` 完成。取消调用方的等待不会停止已经入队的写入，该写入仍将继续直至获得存储操作结果。日志操作失败会使管理器进入隔离状态；新的激活实例将恢复实际的持久化结果。有关故障处理和安全暂存，请参阅[运行时行为与一致性](runtime-behavior.md)。

与其他 Grain 或外部服务进行协调需要应用层协议，例如幂等机制、收件箱或发件箱。

当 `TrySetResult`、`TrySetException` 或 `TrySetCanceled` 成功时，<xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1> 会更改内存中的状态。它的 `Task` 在写入确认该状态或恢复过程重放该状态后完成，从而使等待方能够观察到已持久化的完成状态。

若要向调用方以流的形式传递更改，或从持久化日志传输尚未持久化的草稿，请参阅[观察状态并流式传输草稿](observe-state.md)。

<a id="keep-state-names-and-schemas-stable"></a>

## 保持状态名称与架构稳定

提供给管理器或键控服务的名称用于跨激活实例和部署标识持久化状态组件。遵循以下规则：

- 确保每个名称在 Grain 内唯一。
- 修改构造函数或重构字段时保留名称。
- 滚动升级期间，确保 JSON 键、值和记录的架构保持向后读取兼容性。
- 使用裁剪或 Native AOT 时，将每种 JSON 有效载荷类型注册到配置的源生成序列化程序上下文中。
- 如果回滚可能重新引入已移除的状态组件，请在[退役宽限期](runtime-behavior.md#retire-a-named-state)内保留其定义。

名称采用序数（Ordinal）比较。对同一名称和兼容应用程序约定的重复请求返回同一实例。若该名称所对应的约定或封闭泛型类型不兼容，请求会立即失败。不受支持的应用程序约定会产生明确的工厂注册错误。

在构造期间或同步激活初始化期间（初始化开始之前）声明所有状态组件。初始化成功后使用恢复的状态。之后调用 `GetOrAddState` 只能解析已有组件；名称不存在时立即失败，且不会改变注册表。使用 <xref:Orleans.Durable.Journaling.IDurableStateManager.TryGetState*> 可在不创建组件的情况下查找状态。对于运行时变化的键，应将它们放入已声明的持久化字典中，而不是为每个键创建新的具名组件。

<a id="compose-an-activation-scoped-feature"></a>

## 组合激活作用域内的功能组件

可重用的功能组件可以独立于 Grain 的构造函数和基类拥有持久化状态。在 <xref:Orleans.Runtime.IConfigureGrainTypeComponents> 的实现中，使用 <xref:Orleans.Metadata.GrainClassMap> 针对每种 Grain 类型选择一次 Grain 实现，然后注册共享初始化操作。此示例选择现有的购物车接口并记录激活次数：

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingSnippets.cs" id="journaled_feature":::

将功能组件注册为作用域服务，将配置器注册为单例服务：

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingSnippets.cs" id="journaled_feature_registration":::

同步操作从 <xref:Orleans.Runtime.IGrainContext.ActivationServices> 解析功能组件，并注册其生命周期参与者。解析功能组件的依赖项时，如有需要会构造标准状态管理器；管理器在依赖解析返回之前自行注册到生命周期。初始化操作可以是激活实例首次解析其管理器或状态的地方。功能组件使用位于 `SetupState` 之后、<xref:Orleans.Runtime.GrainLifecycleStage.Activate> 之前的阶段，从而在应用程序的激活过程开始之前递增已恢复的状态。

每个参与者应当只有一个负责其注册的所有者。多个并发激活实例共享初始化操作；从提供的上下文中解析特定于激活实例的数据，并保持共享回调无状态。有关执行顺序和失败行为，请参阅[共享激活初始化](../grain-lifecycle.md#shared-activation-setup)和[Journaling 激活与恢复](runtime-behavior.md#activation-and-recovery)。

<a id="use-journal-backed-persistent-state"></a>

## 使用日志支持的持久化状态

在初始化期间，通过键控注入或 `GetOrAddPersistentState<T>(name)` 获取日志支持的 <xref:Orleans.Runtime.IPersistentState`1>。其熟悉的 `State`、`WriteStateAsync` 和 `ClearStateAsync` 成员通过与持久化集合相同的日志管理器进行写入。由于激活初始化阶段已经重放 Grain 日志，`ReadStateAsync` 直接从已恢复的内存状态完成读取。

与其他持久化状态组件一样，应使用唯一的键控服务名称。`ETag` 是日志支持的状态恢复出的版本，`RecordExists` 指示是否存在已存储的值。

<a id="implement-a-custom-journaled-state"></a>

## 实现自定义日志化状态

定义面向 Grain 的状态约定，并实现该约定及 <xref:Orleans.Durable.Journaling.IStateMachine>。使用 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddStateMachine*> 在 <xref:Microsoft.Extensions.DependencyInjection.IServiceCollection> 上注册映射，将应用程序约定和实现作为两个类型实参。这两种类型均为引用类型。在 Silo 配置中，调用 `siloBuilder.AddJournaling()` 完成核心初始化，调用 `siloBuilder.Services.AddStateMachine<TState, TImplementation>()` 注册 Grain 状态组件映射。注册存储提供程序时已执行核心初始化。Grain 代码在初始化期间通过 `GetOrAddState<TState>(name)` 获取组件。管理器利用现有激活作用域构造并注册一次实现实例，然后绑定其日志流。

无参数的注册重载通过依赖注入解析实现的构造函数依赖项。当构造过程需要状态名称时，使用工厂重载：其回调接收所属服务提供程序和请求的状态名称，并返回实现实例。

状态机协议负责操作编码、快照、重放和内存中的临时记录管理：

| 成员 | 职责 |
| --- | --- |
| <xref:Orleans.Durable.Journaling.IStateMachine.Reset*> | 重置内存状态并绑定提供的日志流写入器。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.ReplayEntry*> | 在恢复期间应用已记录的操作。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.WritePendingEntries*> | 将待处理操作输出到提供的写入器。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.WriteSnapshot*> | 输出重建当前内容所需的状态。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.OnRecoveryCompleted*> | 在应用程序使用之前完成重建。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted*> | 发布依赖存储确认的效果。 |

实现运行于一个逻辑 Grain 线程上。恢复采用新实例和重放。日志操作失败后，管理器保持隔离状态，其所有者创建新的管理器和状态实例。

<a id="own-a-standalone-journal"></a>

## 管理独立日志

<xref:Orleans.Durable.Journaling.IJournaledStateManager> 是日志所有者的约定，独立于面向 Grain 的 `IDurableStateManager`。它提供 <xref:Orleans.Durable.Journaling.IJournaledStateManager.RegisterStateMachine*>、<xref:Orleans.Durable.Journaling.IJournaledStateManager.TryGetStateMachine*>、<xref:Orleans.Durable.Journaling.IJournaledStateManager.InitializeAsync*>、<xref:Orleans.Durable.Journaling.IJournaledStateManager.WriteStateAsync*>、用于删除整个日志的 <xref:Orleans.Durable.Journaling.IJournaledStateManager.DeleteStateAsync*>、异步释放功能，以及 <xref:Orleans.Durable.Journaling.IJournaledStateManager.PendingWriteByteCount> 诊断信息。

当集成功能独立于 Grain 激活实例拥有日志时，使用 <xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory.CreateStandalone*>。它返回日志所有者的约定接口。使用调用方提供的依赖项构造状态机组件，并在初始化之前注册这些组件。在使用其内容之前等待恢复完成，在报告持久化更改之前等待写入完成：

:::code language="csharp" source="./snippets/journaling/JournalingBasics.cs" id="standalone_durable_state":::

该示例显式构造一个实现 `IStateMachine` 和 <xref:Orleans.Durable.Journaling.IDurableValueCommandHandler`1> 的计数器组件。为其提供的 <xref:Orleans.Durable.Journaling.IDurableValueCommandCodec`1> 必须与日志配置的写入格式相对应。该组件应用重放的设置命令，并为待处理写入和快照输出当前值。

调用方拥有手动提供的状态组件及其依赖项，包括构造它们所用的任何作用域。释放日志所有者会停止处理并释放日志资源；调用方负责安排组件及依赖项的释放。独立日志所有者从不创建或释放 DI 作用域。使用 `TryGetStateMachine` 查找注册到该所有者的组件。

对于 Grain，默认管理器在同一个激活作用域对象上实现两个独立接口。`IDurableStateManager` 提供 `GetOrAdd`、类型化查找和写入操作；`IJournaledStateManager` 提供日志所有权操作。运行时管理的激活作用域控制 DI 创建的状态组件、其依赖项及管理器的生命周期。
