---
title: "测试 Orleans 应用"
description: "测试 Orleans 应用 InProcessTestCluster、共享定着物、动态 silos和分离单元试验。"
ms.date: 08/20/2026
ms.topic: how-to
---

<a id="test-orleans-applications"></a>

# 测试 Orleans 应用

Orleans 应用软件受益于一个以上级别的测试:

| 试验水平 | 最适合 |
| --- | --- |
| 孤立单位试验 | 纯应用逻辑和制造者注射合作者 |
| <xref:Orleans.TestingHost.InProcessTestCluster> | 多数 grain 测试,包括 grain 电话、启动、时间安排、序列化、依赖注射和集群行为 |
| [OrleansTestKit](https://github.com/OrleansContrib/OrleansTestKit) | 基本安排-行为资产测试 grain 其正确性独立于 Orleans 附表和汇率 |
| 与生产供应商的测试组群 | 外部系统合同事项所涉的存储、集群、提醒和流 |

默认为 <xref:Orleans.TestingHost.InProcessTestCluster> 用于 grain 代码。它为 Orleans 运行时行为。 测试直接抽取的计算和应用服务 。 请使用 OrleansTestKit 当测试作者控制顺序和同步时。

<a id="create-an-in-process-test-cluster"></a>

## 创建进程内测试集集

在测试工程中安装 [`Microsoft.Orleans.TestingHost`](https://www.nuget.org/packages/Microsoft.Orleans.TestingHost/) 软件包:

```dotnetcli
dotnet add package Microsoft.Orleans.TestingHost
```

对于新的 Orleans 测试,应选择 <xref:Orleans.TestingHost.InProcessTestClusterBuilder>。构建集、调用 <xref:Orleans.TestingHost.InProcessTestCluster.DeployAsync*>、使用客户端并在测试后处置组:

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTests.cs" id="basic_cluster_test":::

默认构建器启动两个 silos , 客户端使用模拟运输、 测试会籍和测试 grain 目录。 这些替代程序可以快速测试, 但不会复制套接字运输或生产成员提供商 。 请查看 [TestingHost 结构架构](../implementation/testing.md) 的完整忠诚度界限 。

<a id="configure-hosts-silos-and-clients"></a>

## 配置主机、 silos和客户端

构建者披露了不同的配置范围:

- <xref:Orleans.TestingHost.InProcessTestClusterBuilder.ConfigureHost*> 适用于每个 silo 主机和客户主机。
- <xref:Orleans.TestingHost.InProcessTestClusterBuilder.ConfigureSilo*> 适用于每一种 silo。
- <xref:Orleans.TestingHost.InProcessTestClusterBuilder.ConfigureClient*> 适用于分组客户端。

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="configure_cluster":::

每个主机都有自己的自控注射容器。 注册一种类型每容器创建一吨单吨。 登记一个被捕获的例子, 例如, 故意与客户和所有 silos共享该例子。 因此, 共享的可变测试双倍必须安全线 。

<a id="reuse-a-cluster-across-tests"></a>

## 交叉测试再使用集集

集束启动比单项单元测试更昂贵。 测试框架生命周期支持允许以同样的配置进行测试, 并重新使用一个集束 。

### xUnit

创建 xUnit 收藏集固定线 :

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterFixture.cs" id="cluster_fixture":::

将固定值登记为收藏,并将该收藏适用于共享组的每个测试类:

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterCollection.cs" id="cluster_collection":::
:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTestsWithFixture.cs" id="shared_cluster_test":::

<a id="mstest"></a>

### MSDST 测试

在一个专门测试项目中共享集群配置的团体MSTest类。

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/MSTestClusterFixture.cs" id="mstest_cluster_fixture":::

测试通过固定装置进入部署的集群:

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/MSTestClusterFixture.cs" id="mstest_shared_cluster_test":::

共享集束测试不能取决于执行命令。 每次测试都给 grain 身份, 并重置任何共享外部状态。 当套件需要不兼容的 silo 或供应商配置时使用单独的固定装置 。

<a id="change-cluster-topology"></a>

## 变化群群表

在一个测试运行期间,一个在工艺中的集可以添加并删除 silos :

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="change_topology":::

等待生命稳定,然后才能坚持取决于新会员观的行为。 <xref:Orleans.TestingHost.InProcessTestCluster.StopSiloAsync*> 执行优雅的停止。 过度的流程损失、网络分割和生产运输行为需要一种能明确引入这些失败模式的杠杆。

<a id="maintain-existing-testcluster-suites"></a>

## 维持现有的 `TestCluster` 套件

<xref:Orleans.TestingHost.TestCluster> remains available for suites built around class-based configurators such as <xref:Orleans.TestingHost.ISiloConfigurator>. Its built-in hosts run in process; a custom silo-creation delegate is required for another isolation model. New Orleans tests should generally use <xref:Orleans.TestingHost.InProcessTestCluster> for its delegate-based configuration and direct access to each host's service provider.

<a id="choose-the-runtime-boundary"></a>

## 选择运行时间边界

当一个断言取决于运行时间行为时使用 <xref:Orleans.TestingHost.InProcessTestCluster> 。该组群通过 Orleans 运行时间生成启动,并保留旋转时间、连接、序列化、依赖注射、持久性整合、定位、信息路径、定时器、提醒和生命周期行为。

纯粹域域域逻辑可以被抽进普通等级或服务,直接测试。 Orleans 集束测试可以通过依赖性注射取代应用程序所属合作者,而运行时间则继续创建和执行 grain.

当测试涵盖其外部系统合同、同货币行为或恢复活动时,配置生产存储或提醒提供者。

<a id="use-orleanstestkit-for-a-basic-single-activation-test"></a>

## 使用 OrleansTestKit 进行基本的单活化测试

[OrleansTestKit](https://github.com/OrleansContrib/OrleansTestKit) 是一个社区项目,在 OrleansContrib 组织。它为一个 grain 激活和用品测试实施,以激活身份、持久性状态、 grain 引用、计时器、提醒、串流。 grain 测试作者控制顺序和同步。 这个边界适用于单一方法的基本安排- 行为- 评估测试,其结果取决于注入值并记录到串通者互动。 OrleansTestKit 用于 Orleans 应用程序所使用的主版 。

在测试工程中安装软件包 :

```dotnetcli
dotnet add package OrleansTestKit
```

下列 grain 使用字符串身份对另一个 grain 进行响应,并在拨打前坚持一个项目:

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ShoppingCartGrain.cs" id="testkit_grain":::

将测试等级从 `TestKitBase`登记:持续状态和 grain 探测器在 `Silo` 在创建 grain 具有测试特性:

:::code language="csharp" source="snippets/testing/orleans-testing/Sample.OrleansTesting/ShoppingCartGrainTests.cs" id="testkit_grain_test":::

测试核查 grain状态变异、存储写请求书、来自密钥的 grain 参考和发出呼叫。 OrleansTestKit 使用模拟激活环境引用一个 grain ,并解决合作 grains 作为探测器的问题。

使用 <xref:Orleans.TestingHost.InProcessTestCluster> 用于 grains 等待工作、同时打电话、使用回拨或中继、协调多重激活或取决于序列化、生命周期、放置、定时器、提醒、流或信息路径。运行时间拥有这些行为,且在工艺中的集保存其执行模式。

OrleansTestKit [读读](https://github.com/OrleansContrib/OrleansTestKit/blob/main/README.md) 文档版本兼容性和包件设置。其 [测试套套装](https://github.com/OrleansContrib/OrleansTestKit/tree/main/test/OrleansTestKit.Tests/Tests) 演示了固定动画API和可用于单激活测试的测试双倍。
