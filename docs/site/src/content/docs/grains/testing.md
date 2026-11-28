---
title: 测试 Orleans 应用程序
description: 使用 InProcessTestCluster、共享测试夹具、动态 Silo 和隔离式单元测试来测试 Orleans 应用程序。
ms.date: 08/20/2026
ms.topic: how-to
---

<a id="test-orleans-applications"></a>

# 测试 Orleans 应用程序

Orleans 应用程序可以从多个层次的测试中受益：

| 测试层次 | 最适用的场景 |
| --- | --- |
| 隔离式单元测试 | 纯应用程序逻辑及通过构造函数注入的协作对象 |
| <xref:Orleans.TestingHost.InProcessTestCluster> | 大多数 Grain 测试，包括 Grain 调用、激活、调度、序列化、依赖注入和集群行为 |
| [OrleansTestKit](https://github.com/OrleansContrib/OrleansTestKit) | 单个 Grain 激活实例的基本准备、执行、断言测试，其正确性不依赖 Orleans 调度和并发机制 |
| 使用生产提供程序的测试集群 | 外部系统契约至关重要的存储、集群成员资格、提醒器和流处理 |

Grain 代码默认使用 <xref:Orleans.TestingHost.InProcessTestCluster> 进行测试。这为 Orleans 运行时行为提供了保真度最高的测试边界。对于提取出来的计算逻辑及应用服务，直接进行测试。当测试编写者负责控制执行顺序和同步时，使用 OrleansTestKit。

<a id="create-an-in-process-test-cluster"></a>

## 创建进程内测试集群

在测试项目中安装 [`Microsoft.Orleans.TestingHost`](https://www.nuget.org/packages/Microsoft.Orleans.TestingHost/) 包：

```dotnetcli
dotnet add package Microsoft.Orleans.TestingHost
```

编写新的 Orleans 测试时，优先使用 <xref:Orleans.TestingHost.InProcessTestClusterBuilder>。构建集群，调用 <xref:Orleans.TestingHost.InProcessTestCluster.DeployAsync*>，使用其客户端，并在测试完成后释放集群：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTests.cs" id="basic_cluster_test":::

默认构建器使用内存传输、测试成员资格服务和测试 Grain 目录来启动两个 Silo 及一个客户端。这些替代实现使测试速度更快，但无法复现套接字传输或生产成员资格提供程序的行为。完整的保真度边界参见 [TestingHost 体系结构](../implementation/testing.md)。

<a id="configure-hosts-silos-and-clients"></a>

## 配置宿主、Silo 和客户端

构建器提供了彼此独立的配置作用域：

- <xref:Orleans.TestingHost.InProcessTestClusterBuilder.ConfigureHost*> 应用于每个 Silo 宿主以及客户端宿主。
- <xref:Orleans.TestingHost.InProcessTestClusterBuilder.ConfigureSilo*> 应用于每个 Silo。
- <xref:Orleans.TestingHost.InProcessTestClusterBuilder.ConfigureClient*> 应用于集群客户端。

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="configure_cluster":::

每个宿主都有自己的依赖注入容器。注册一个类型会在每个容器中创建一个单例。注册捕获的实例（如示例所示）则是有意让客户端与所有 Silo 共享该实例。因此，共享的可变测试替身必须是线程安全的。

<a id="reuse-a-cluster-across-tests"></a>

## 在多个测试之间复用集群

启动集群的成本高于执行隔离式单元测试。测试框架的生命周期支持允许配置相同的测试复用一个集群。

### xUnit

创建 xUnit 集合夹具：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterFixture.cs" id="cluster_fixture":::

将夹具注册到集合中，并对共享该集群的每个测试类应用此集合：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterCollection.cs" id="cluster_collection":::
:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTestsWithFixture.cs" id="shared_cluster_test":::

<a id="mstest"></a>

### MSTest

将共享集群配置的 MSTest 测试类集中到专用测试项目中。使用程序集生命周期方法为该项目部署一个集群：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/MSTestClusterFixture.cs" id="mstest_cluster_fixture":::

测试通过夹具访问已部署的集群：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/MSTestClusterFixture.cs" id="mstest_shared_cluster_test":::

共享集群的测试不得依赖执行顺序。为每个测试分配不同的 Grain 标识，并重置所有共享外部状态。测试套件需要不兼容的 Silo 或提供程序配置时，应使用独立的夹具。

<a id="change-cluster-topology"></a>

## 更改集群拓扑

进程内集群可以在测试运行期间添加和移除 Silo：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="change_topology":::

在断言依赖新成员资格视图的行为之前，应等待存活状态趋于稳定。<xref:Orleans.TestingHost.InProcessTestCluster.StopSiloAsync*> 执行正常停止。若要测试进程突然丢失、网络分区以及生产传输行为，需使用明确引入这些故障模式的测试工具。

<a id="maintain-existing-testcluster-suites"></a>

## 维护现有的 `TestCluster` 测试套件

<xref:Orleans.TestingHost.TestCluster> 仍可用于围绕 <xref:Orleans.TestingHost.ISiloConfigurator> 等基于类的配置器构建的测试套件。其内置宿主在进程内运行；采用其他隔离模式时，需要自定义 Silo 创建委托。新的 Orleans 测试通常应使用 <xref:Orleans.TestingHost.InProcessTestCluster>，以利用基于委托的配置方式，并直接访问每个宿主的服务提供程序。

<a id="choose-the-runtime-boundary"></a>

## 选择运行时边界

当断言依赖运行时行为时，使用 <xref:Orleans.TestingHost.InProcessTestCluster>。集群通过 Orleans 运行时创建激活实例，并保留轮次调度、交错执行、序列化、依赖注入、持久化集成、放置、消息路由、定时器、提醒器及生命周期行为。

可以将纯领域逻辑提取到普通类或服务中，并直接进行测试。保持这一边界独立于 Orleans 运行时抽象。集群测试可通过依赖注入替换由应用程序管理的协作对象，同时仍由运行时创建和执行 Grain。

测试涉及存储或提醒器提供程序的外部系统契约、并发行为或重启恢复时，请配置生产环境使用的提供程序。

<a id="use-orleanstestkit-for-a-basic-single-activation-test"></a>

## 使用 OrleansTestKit 进行基本的单激活实例测试

[OrleansTestKit](https://github.com/OrleansContrib/OrleansTestKit) 是 OrleansContrib 组织中的社区项目。它为单个 Grain 激活实例创建测试夹具，并提供激活实例标识、持久化状态、Grain 引用、定时器、提醒器和流的测试实现。测试在自己的执行上下文中调用 Grain 代码，因此由测试编写者控制执行顺序与同步。此边界适合测试单个方法的基本准备、执行、断言流程，该方法的结果依赖注入值及记录下来的协作对象交互。OrleansTestKit 的主版本应与应用程序使用的 Orleans 主版本匹配。

在测试项目中安装该包：

```dotnetcli
dotnet add package OrleansTestKit
```

以下 Grain 使用其字符串标识寻址另一个 Grain，并在发起调用前持久化一个项：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ShoppingCartGrain.cs" id="testkit_grain":::

测试类继承自 `TestKitBase`。在使用测试标识创建 Grain 之前，在 `Silo` 上注册持久化状态及 Grain 探针：

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ShoppingCartGrainTests.cs" id="testkit_grain_test":::

测试验证 Grain 的状态变更、存储写入请求、由键派生的 Grain 引用及出站调用。OrleansTestKit 使用其模拟的激活实例上下文调用单个 Grain，并将协作 Grain 解析为探针。

对于等待异步工作、进行并发调用、使用可重入或交错执行、协调多个激活实例，或者依赖序列化、生命周期、放置、定时器、提醒器、流或消息路由的 Grain，请使用 <xref:Orleans.TestingHost.InProcessTestCluster>。这些行为由运行时负责，进程内集群保留其执行模型。

OrleansTestKit 的 [README](https://github.com/OrleansContrib/OrleansTestKit/blob/main/README.md) 说明了版本兼容性与包配置。其 [测试套件](https://github.com/OrleansContrib/OrleansTestKit/tree/main/test/OrleansTestKit.Tests/Tests) 演示了单激活实例测试的夹具 API 及可用的测试替身。
