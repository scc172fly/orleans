---
title: 提供程序开发架构
description: 使用命名服务、配置绑定、验证、生命周期及运行时扩展契约设计 Orleans 提供程序。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="provider-authoring-architecture"></a>

# 提供程序开发架构

Orleans 提供程序将外部系统或替代运行时实现适配到定义好的契约。存储、集群成员资格、提醒器、Grain 目录及持久化流具有不同的数据平面接口，但其托管架构遵循相同模式，建立在 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options) 和 [依赖注入](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview) 之上：

1. 绑定命名配置节；
1. 注册命名服务和选项；
1. 在宿主启动期间验证配置；
1. 当资源需要初始化时参与生命周期；以及
1. 显式报告失败，而不是静默降级到其他后端。

<a id="configuration-driven-provider-builders"></a>

## 由配置驱动的提供程序构建器

<xref:Orleans.Providers.IProviderBuilder`1> 是 Orleans 配置与 Silo 或客户端构建器之间的桥梁：

提供程序包使用程序集元数据将提供程序类型字符串及类别与构建器关联。宿主根据配置选择该构建器，传入提供程序名称和配置节，让构建器调用与代码优先配置相同的公共注册 API。

Azure Queue 流实现展示了这一模式：<xref:Orleans.Hosting.AzureQueueStreamProviderBuilder> 实现了 <xref:Orleans.Hosting.ISiloBuilder> 和 <xref:Orleans.Hosting.IClientBuilder> 两种构建器。请参阅其 [实现](https://github.com/dotnet/orleans/blob/main/src/Azure/Orleans.Streaming.AzureStorage/Hosting/AzureQueueStreamProviderBuilder.cs)。

<a id="named-service-composition"></a>

## 命名服务组合

许多类别的提供程序允许存在多个实例。因此，提供程序名称是服务标识、选项标识、日志标识和运行时查找的一部分。<xref:Orleans.Hosting.NamedServiceConfigurator> 及其派生配置器注册带键组件和命名选项，而不创建私有服务提供程序。

注册扩展方法应当：

- 当提供程序类别支持命名时，要求名称非空；
- 通过 `AddOptions<T>(name)` 或 Orleans 配置器注册选项；
- 使用相同的键注册契约及实现；
- 仅对真正共享的默认项使用 <xref:Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd*>；以及
- 在验证器及诊断信息中包含名称。

为命名组件解析无键单例，可能导致第一个提供程序的选项泄漏到其他所有提供程序中。

<a id="separate-control-plane-and-data-plane"></a>

## 分离控制平面与数据平面

提供程序构建器和选项构成控制平面。运行时契约构成数据平面：

| 提供程序类别 | 数据平面契约 |
| --- | --- |
| 集群成员资格 | <xref:Orleans.IMembershipTable> |
| Grain 存储 | <xref:Orleans.Storage.IGrainStorage> |
| Grain 目录 | <xref:Orleans.GrainDirectory.IGrainDirectory> |
| 提醒器表 | <xref:Orleans.IReminderTable> |
| 持久化流 | <xref:Orleans.Streams.IQueueAdapterFactory> 及其适配器组件 |

不要让配置方面的考虑削弱数据平面契约。例如，无论凭据来自连接字符串、带键 SDK 客户端还是托管标识，成员资格提供程序都必须保持条件更新与版本顺序语义。

<a id="validation"></a>

## 验证

选项验证应在 Silo 加入集群或开始接收流量之前失败。Orleans 提供程序通常注册 <xref:Orleans.IConfigurationValidator>，这样验证过程就可以涵盖普通数据注解验证无法表达的命名选项及服务依赖项。

至少验证以下内容：

- 必需的端点、客户端或凭据来源；
- 互斥的配置形式；
- 提供程序特定的命名和取值范围约束；
- 配对组件之间的兼容性；以及
- 运行时契约所要求的能力。

避免使用宽泛的异常处理将不可访问的后端转换为空结果。空成员资格表、缺失的 Grain 状态或空流队列都有领域语义，绝不能表示被吞掉的基础设施故障。

<a id="lifecycle-and-ownership"></a>

## 生命周期和所有权

分配客户端、接收器、租约或后台代理的提供程序应实现或注册 <xref:Orleans.ILifecycleParticipant`1>。在必需的运行时服务就绪后初始化，并在这些服务消失之前停止。

必须明确资源所有权。如果应用程序提供带键 SDK 客户端，提供程序通常不应释放不归自己所有的对象。如果提供程序为每个队列创建接收器，则应在队列所有权转移时停止并释放这些接收器。

持久化流提供程序展示了分阶段的生命周期组合：初始化时创建适配器，活动阶段启动拉取代理，然后在关闭前停止代理。请参阅 <xref:Orleans.Providers.Streams.Common.PersistentStreamProvider.Participate*?displayProperty=nameWithType> 及其 [实现](https://github.com/dotnet/orleans/blob/main/src/Orleans.Streaming/PersistentStreams/PersistentStreamProvider.cs)。

成员资格调用方使用 <xref:Orleans.IMembershipTable> 中支持取消的方法执行初始化、读取及写入。内置提供程序将取消令牌传递给支持取消的后端 API，并限制对不接受令牌的 SDK 操作的等待时间，同时观察随后发生的异常。自定义提供程序根据其后端操作和资源生命周期实现取消。取消可能与已经提交的写入发生竞争，因此后续更新必须遵循条件写入及表版本约束。

返回已经完成的后端操作结果，并在启动更多 I/O 之前检查取消请求。映射已经返回的结果可以保留该操作的实际结果。

共享的成员资格刷新会一直存续到成员资格管理器被释放。各调用方拥有各自的等待过程，而定期维护及其排队的清理请求则随 Silo 生命周期停止。这样在关闭期间成员资格读取仍然可用。

成员资格表 RPC 独立于应用程序参数传播取消请求。每个接收方按照自身实现处理取消。

当生命周期回调必须执行取消或清理逻辑时，通过 <xref:System.Threading.Tasks.Task.Run*> 调度该回调，并在回调内部将取消令牌传给操作。此后由回调自身决定如何通过取消完成其工作。

<a id="membership-snapshots"></a>

## 成员资格快照

使用 <xref:Orleans.IMembershipTable.ReadAllAsync*> 在同一个原子快照中获取成员资格记录及对应表版本。<xref:Orleans.MembershipTableData.TryGet*> 从该快照选择 Silo 的记录及行 ETag。

条件写入使用从已观察快照取得的 <xref:Orleans.TableVersion.Next*>，并返回写入是否成功。在选择下一次变更之前刷新快照，以获取当前表 ETag。行 ETag 遵循各提供程序的元数据策略，可能在仅更新心跳时发生变化。规范更新要求目标行存在且表 ETag 匹配；额外的行条件不得受心跳影响。

<a id="testing-a-provider"></a>

## 测试提供程序

契约测试除了成功往返操作之外，还应覆盖：

- 并发条件更新及拒绝过期版本；
- 重复注册或重复投递行为；
- 取消和超时传播；
- 启动验证；
- 后端不可用且不发生静默回退；
- 生命周期停止后的资源清理；
- 多个命名实例的选项隔离；以及
- 已存储或已传输数据的滚动升级兼容性。

请使用 [TestingHost 架构](testing.md) 了解测试集群替换了哪些运行时服务。依赖真实后端的提供程序测试应明确其先决条件，并且不应将模拟器较弱的一致性保证视为符合生产契约的证据。

<a id="membership-provider-conformance"></a>

### 成员资格提供程序契约符合性测试

`Microsoft.Orleans.Clustering.TestKit` 为 <xref:Orleans.IMembershipTable> 提供独立于测试框架的测试。为每个直接测试场景创建新的夹具，使用共享同一后端的独立提供程序句柄以及该夹具隔离的集群 ID。夹具负责初始化和清理。

提供夹具针对原始后端存储范围的只读删除探针。该工具包在删除前检查已填充的数据，并在所有者被释放之前验证原生删除或存储失效。删除操作结束该操作历史；后续生成的测试用例使用新的夹具和所有者。

将直接运行器的方法公开为单独测试，并使用创建新夹具的工厂运行 Accordant 生成的测试套件。测试覆盖现有行上的原子表版本 CAS、一致的规范读取、心跳不干扰表版本及此前捕获的更新输入、Dead 状态行清理，以及集群隔离。行 ETag 是提供程序定义的元数据，可能在心跳之后变化。原始存活状态读取可能滞后，整行写入可能覆盖心跳。提供程序原生检测验证每次定期心跳仅执行一次盲写存活状态，不进行任何前置读取或比较并交换操作。保留失败诊断信息，以便重现生成的操作历史。[包 README](https://github.com/dotnet/orleans/blob/main/src/Orleans.Clustering.TestKit/README.md) 提供了工厂示例和场景清单。
