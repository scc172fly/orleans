---
title: 对 Orleans 应用程序进行端到端测试
description: 使用 Orleans.TestingHost，从第一个 Grain 测试逐步扩展到可复用的集群测试和拓扑变更测试。
ms.date: 08/11/2026
ms.topic: tutorial
---

<a id="test-an-orleans-application-end-to-end"></a>

# 对 Orleans 应用程序进行端到端测试

本教程分层构建 Orleans 运行时测试：第一个进程内集群测试、共享集群测试夹具以及拓扑变更测试。完整且可构建的源代码位于文档的 `grains/snippets/testing/orleans-testing` 项目中。

<a id="choose-the-right-boundary"></a>

## 选择正确的测试边界

对纯应用程序逻辑使用普通单元测试，对 Orleans 运行时行为使用 <xref:Orleans.TestingHost.InProcessTestCluster>，对外部契约使用生产提供程序测试。

这些测试边界既能保持快速反馈，又能保证运行时行为的真实性。

<a id="run-the-first-cluster-test"></a>

## 运行第一个集群测试

克隆仓库，然后运行测试项目：

```powershell
git clone https://github.com/dotnet/orleans.git
cd orleans
dotnet test --project .\docs\site\src\content\docs\grains\snippets\testing\orleans-testing\Sample.OrleansTesting\Sample.OrleansTesting.csproj --minimum-expected-tests 1
```

第一个测试创建并部署集群，通过客户端获取 Grain 引用，执行一次调用，最后释放集群：

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTests.cs" id="basic_cluster_test":::

这将一起验证代码生成、序列化、激活、消息分派及 Grain 实现。默认构建器使用快速的内存测试基础设施启动两个 Silo 和一个客户端。

<a id="add-application-configuration"></a>

## 添加应用程序配置

生产环境中的 Grain 通常依赖通过依赖注入注册的服务。请使用对应的构建器作用范围配置所有宿主、仅 Silo 或仅客户端：

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="configure_cluster":::

每个宿主都拥有自己的服务提供程序。注册服务类型会为每个宿主创建一个实例。注册捕获的实例则会有意让 Silo 和客户端共享同一个实例，因此共享的测试替身必须是线程安全的。

每次配置变更后都重新运行测试。让部署失败通过测试初始化过程传播，以便测试运行结果将其报告为失败。

<a id="reuse-an-expensive-cluster"></a>

## 复用启动成本较高的集群

当多个测试需要完全相同的集群配置时，创建一个 xUnit 测试夹具：

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterFixture.cs" id="cluster_fixture":::

将夹具注册为测试集合：

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterCollection.cs" id="cluster_collection":::

然后让每个测试使用夹具中已启动的集群：

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTestsWithFixture.cs" id="shared_cluster_test":::

为每个测试分配唯一的 Grain 标识，重置共享的外部状态，并保证每个测试不依赖执行顺序。

<a id="exercise-a-topology-change"></a>

## 测试拓扑变更

添加一个 Silo，等待成员资格稳定，然后优雅停止该 Silo：

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="change_topology":::

在测试中调用该辅助方法，并在成员资格变更之后验证一次应用程序调用：

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/TopologyTests.cs" id="topology_change_test":::

文档中的 `dotnet test` 命令会执行此测试。同一进程内的集群可用于测试成员资格变更。若要测试进程崩溃、网络分区、套接字传输及生产成员资格提供程序，请使用独立环境。

<a id="add-production-provider-tests"></a>

## 添加生产提供程序测试

对于持久化、提醒器、集群成员资格或流，请添加按需启用的测试套件，该套件：

1. 预配独立的提供程序实例或模拟器。
1. 使用与生产环境相同的提供程序扩展方法配置集群。
1. 使用唯一的数据库、表、流及 Grain 标识符。
1. 验证 Silo 重启前后的行为。
1. 即使断言失败，也清理测试所拥有的资源。

将凭据存储在测试环境的机密管理设施中，并明确报告缺失的先决条件。有关 API 详细信息和测试真实性边界，请参阅 [测试 Orleans 应用程序](../grains/testing.md) 和 [TestingHost 架构](../implementation/testing.md)。
