---
title: "使用耐久状态"
description: "建立一个具有耐久值和收藏的实验 Orleans 日志 grain 。"
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="use-durable-state"></a>

# 使用耐久状态

安装释放前的安装 `Microsoft.Orleans.Durable` 软件包中 silo 安装 [日记日日日日记存储供应商](configuration.md#choose-a-storage-provider) 激活前配置 grains 使用耐久状态。

<a id="define-a-durable-grain"></a>

## 定义耐久 grain

将 <xref:Orleans.Durable.Journaling.IDurableStateManager> 注入普通 <xref:Orleans.Grain> ,并宣布在施工期间指定国家部件。经理一次性创建每个组件, Orleans 在应用方法运行前恢复 grain状态:

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingSnippets.cs" id="composed_shopping_cart":::

标准管理者本身参加 grain 与激活装置一起建造的寿命周期 <xref:Orleans.Runtime.IGrainContext>宣布在施工或同步启动装置期间的持久性状态部件。 <xref:Orleans.Runtime.GrainLifecycleStage.SetupState> 之前的完成时间 <xref:Orleans.Grain.OnActivateAsync*> 同一构成与申请所属机构相同。 grain 基本类。

字典突变在当前激活时立即可见。 等待 <xref:Orleans.Durable.Journaling.IDurableStateManager.WriteStateAsync*> 确定待变异到 grain状态。 <xref:System.Threading.CancellationToken> 日期 时 grain 并透过国家经理电话进行操作和流动 所以取消在呼叫者行动寿命之后

组成示例是根据存储库来源汇编的,因此它采用建筑商拥有的生命周期注册。

<xref:Orleans.Durable.Journaling.IDurableStateManager.GetOrAddState*> 接受申请合同,例如 `IDurableDictionary<string, int>`.注册工厂提供执行。 <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions> 帮手也提供可发现姓名的相同准入。

<a id="use-keyed-injection-and-the-convenience-base-class"></a>

### 使用键注射和方便基级

<xref:Orleans.Durable.Journaling.DurableGrain> supplies a <xref:Orleans.Durable.Journaling.DurableGrain.StateManager> property typed as `IDurableStateManager` and a protected <xref:Orleans.Durable.Journaling.DurableGrain.WriteStateAsync*> forwarding helper. Inject a state component with <xref:Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute> when its name is fixed:

:::code language="csharp" source="./snippets/journaling/JournalingBasics.cs" id="keyed_durable_counter":::

注射用键式注射 `IDurableValue<int>` 键与密钥 `"count"` 和 `stateManager.GetOrAddValue<int>("count")` 在一个管理器内返回同一对象, 任意一个解析顺序。 两个构造路径都使用管理员的注册和配置格式。 当它的助手符合应用程序时选择方便基; 构建或输入的构成给出了现有 grain 同一标准的回收行为等级

<a id="select-a-state-type"></a>

## 选择状态类型

| 状态类型 | 设置时间帮助器 | In-memory API | 日刊业务 |
| --- | --- | --- | --- |
| <xref:Orleans.Durable.Journaling.IDurableValue`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddValue*> | 一个可变值 | 设定 |
| <xref:Orleans.Durable.Journaling.IDurableDictionary`2> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddDictionary*> | <xref:System.Collections.Generic.IDictionary`2> | 设置、删除、清除、快照 |
| <xref:Orleans.Durable.Journaling.IDurableList`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddList*> | <xref:System.Collections.Generic.IList`1> 加上 `AddRange` | 添加、插入、设置、删除、清除、快照 |
| <xref:Orleans.Durable.Journaling.IDurableQueue`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddQueue*> | 列业务 | 内容、删除、清晰、快照 |
| <xref:Orleans.Durable.Journaling.IDurableSet`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddSet*> | <xref:System.Collections.Generic.ISet`1> | 添加、删除、清晰、快照 |
| <xref:Orleans.Durable.Journaling.IDurableLog`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddLog*> | 以时间和序列号处理的仅附加条目,读取和草稿 | 附录、承诺草案、修整、清晰、密封、快照 |
| <xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddTaskCompletionSource*> | 持久任务完成 | 完成、过错或取消 |
| <xref:Orleans.Runtime.IPersistentState`1> | <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions.GetOrAddPersistentState*> | 记录式状态 | 设置或清除已版本的状态值 |

注册到经理处的所有命名国家组成部分都共享其未完成的日记和写入确认边界。 打电话者可以写入由互连电话员进行的修改。 在变异发生之前准备可错的工作并等待 `WriteStateAsync` 成功返回前, 取消呼叫者的等待会留下一个已经排队的文字, 记录到存储结果。 失败的日记操作将经理围起来; 重新激活可以恢复持久结果 。 [运行时行为和一致性](runtime-behavior.md) 处理和安全中转失败。

与另一个 grain 或外部服务的协调要求有申请协议,如应急、收件箱或发件箱。

<xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1> 成功时的记忆状态发生变化, `TrySetResult`、 `TrySetException`或 `TrySetCanceled` 。在写完之后, `Task` 将确认状态或恢复重放,让服务员观察耐久完成情况。

向调用者流变化,或从耐用日志流出挥发性草案,见 [观察州和河汇草案](observe-state.md)。

<a id="keep-state-names-and-schemas-stable"></a>

## 保持州名和政体稳定

提供给经理或按键服务的名称确定了在启动和部署过程中的持久性状态组成部分。

- 在 grain内保持每个名称独一。
- 修改构建器或重构字段时保存名称。
- 保持 JSON 键、 值和记录在滚动升级期间可向后读取的公式。
- 在配置的源生成序列器背景下,在剪裁或使用本地自动取款机时,将每个 JSON 有效载荷类型都登记在已配置的源产生序列器上。
- 在回滚可以重新出现时,通过 [退休宽限期](runtime-behavior.md#retire-a-named-state) 保留删除的状态元件定义。

名称使用正统比较。重复要求命名和兼容的申请合同返回相同的情况。不相容的合同或该名称的封闭通用类型立即失效。无支持的申请合同产生明显的工厂登记错误。

在初始化开始之前, 宣布建设或同步激活设置期间的所有状态组件。 启动成功后使用回收状态 。 后来 `GetOrAddState` 调用解决了现有部件; 缺少的名称在不更改注册的情况下立即失效 。 请使用 <xref:Orleans.Durable.Journaling.IDurableStateManager.TryGetState*> 进行查找而不创建 。 在已宣布的耐用字典中输入运行时间比对键, 而不是为每个密钥创建一个新的命名元件 。

<a id="compose-an-activation-scoped-feature"></a>

## 形成一个激活范围特性

可再使用特性可以独立于 grain和基级。选择 grain 执行一次 grain 使用类型 <xref:Orleans.Metadata.GrainClassMap> 中 <xref:Orleans.Runtime.IConfigureGrainTypeComponents> 执行后, 然后登记一个共享设置动作。 此示例选择现有的购物卡接口并记录激活计数 :

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingSnippets.cs" id="journaled_feature":::

将标定范围特征登记为单子,配置为单子:

:::code language="csharp" source="../../snippets/compiled-durable/Grains/JournalingSnippets.cs" id="journaled_feature_registration":::

同步动作解决了该特性, 从 <xref:Orleans.Runtime.IGrainContext.ActivationServices> 中注册了其生命周期参与者。 必要时, 解决该特性的依附关系可以构建标准国家管理员, 且该管理员在解析返回之前自行注册。 设置行动可以是启动该特性的第一个地方, 可以解决其管理员或状态问题。 该特性使用 `SetupState` 之后和 <xref:Orleans.Runtime.GrainLifecycleStage.Activate> 之前的一个阶段, 从而在应用程序激活开始前递增恢复状态 。

保持每个参与者的注册所有人一人。 设置行动通过同时激活共享; 解决从提供的背景中提供的启动特定数据,并保持共同回调无国籍者。 见 [共享激活集共享设置](../grain-lifecycle.md#shared-activation-setup) 和 [清点启动和恢复](runtime-behavior.md#activation-and-recovery) 关于订单和故障行为。

<a id="use-journal-backed-persistent-state"></a>

## 使用日记支持的持久性状态

获取日记支持 <xref:Orleans.Runtime.IPersistentState`1> 通过键入注射或通过键入注射或 `GetOrAddPersistentState<T>(name)` 设置期间, 它熟悉 `State`, `WriteStateAsync`,和 `ClearStateAsync` 成员通过与耐久收藏相同的日记管理器写作。 `ReadStateAsync` 已经恢复的记忆状态中的完整完整的完整, 因为激活设置重放 grain 日记。

使用一个与您对另一个耐久状态组件完全相同的独有的密钥服务名。 `ETag` 是日记支持状态回收版本, 并 `RecordExists` 表示是否有存储值。

<a id="implement-a-custom-journaled-state"></a>

## 执行自定义日记状态

定义a grain- 履行国家合同和执行该合同, <xref:Orleans.Durable.Journaling.IStateMachine>登记 <xref:Microsoft.Extensions.DependencyInjection.IServiceCollection> 与 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddStateMachine*> 两种类型都是参考类型。 silo 配置,调用 `siloBuilder.AddJournaling()` 核心设置和 `siloBuilder.Services.AddStateMachine<TState, TImplementation>()` 排排 grain 储存提供者登记已经完成了核心设置。 Grain 代码以 `GetOrAddState<TState>(name)` 设置期间。 管理者一旦利用现有启动范围, 即构建并登记实施程序, 然后绑定日记流 。

无参数注册超载解决了执行方的建筑师依赖性,使其免于依赖注射。 当建设需要国家名称时,请使用工厂超载:它的回召接收拥有服务供应商,而要求的国家名则返回实施。

州-机械协议拥有操作编码、快照、重播和波动性簿记:

| 成员成员 | 责任 |
| --- | --- |
| <xref:Orleans.Durable.Journaling.IStateMachine.Reset*> | 重置记忆状态并绑定所提供的日记流撰写器 。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.ReplayEntry*> | 在恢复期间应用记录操作。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.WritePendingEntries*> | 将待处理的操作发送到提供作者。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.WriteSnapshot*> | 国家必须重新建立当前内容。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.OnRecoveryCompleted*> | 在应用使用前完成重建 。 |
| <xref:Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted*> | 发布效果取决于储存确认。 |

执行时使用一个逻辑 grain 线索。 恢复使用新实例和重播。 在日记操作失败后, 管理员仍被围住, 其所有人创建了新的管理员和状态实例 。

<a id="own-a-standalone-journal"></a>

## 拥有独立日记

<xref:Orleans.Durable.Journaling.IJournaledStateManager> is the journal-owner contract, independent of the grain-facing `IDurableStateManager`. It provides <xref:Orleans.Durable.Journaling.IJournaledStateManager.RegisterStateMachine*>, <xref:Orleans.Durable.Journaling.IJournaledStateManager.TryGetStateMachine*>, <xref:Orleans.Durable.Journaling.IJournaledStateManager.InitializeAsync*>, <xref:Orleans.Durable.Journaling.IJournaledStateManager.WriteStateAsync*>, whole-journal <xref:Orleans.Durable.Journaling.IJournaledStateManager.DeleteStateAsync*>, asynchronous disposal, and <xref:Orleans.Durable.Journaling.IJournaledStateManager.PendingWriteByteCount> diagnostics.

使用 <xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory.CreateStandalone*> 当整合后拥有一份日记独立于 grain 激活。 它返回所有者合同。 构造有呼叫者提供依赖性的国家机器部件, 在启动前对其进行登记。 在使用内容之前等待恢复, 并在报告持久更改前等待写作 :

:::code language="csharp" source="./snippets/journaling/JournalingBasics.cs" id="standalone_durable_state":::

示例明确构建了一个执行 `IStateMachine` 和 <xref:Orleans.Durable.Journaling.IDurableValueCommandHandler`1>的反元件。 它提供的 <xref:Orleans.Durable.Journaling.IDurableValueCommandCodec`1> 必须符合日记配置的写格式。 该元件应用重放设置的命令,并发布其当前值用于待发写作和快照 。

调用者拥有手动提供的状态组件及其依赖性, 包括用于构建这些部件的任何范围。 处置日记主停止处理和发布日记资源; 调用者安排组件和依赖性处置。 独立所有人从未创建或处置DI范围。 使用 `TryGetStateMachine` 来找到在该所有者注册的部件 。

对于 grains,默认管理器在同一个激活范围对象上执行独立接口。 `IDurableStateManager` 用品 `GetOrAdd`、打字查询和写作; `IJournaledStateManager` 供应日记名所有权操作。运行时自控的启动范围控制了DI创建的国家组件的寿命、其依赖性以及管理员。
