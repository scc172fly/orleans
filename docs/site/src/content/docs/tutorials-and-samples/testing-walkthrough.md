---
title: "测试 Orleans 应用程序的结束"
description: "从第一次 grain 测试到可重复使用的集束和表层测试的进展,使用 Orleans.TestingHost。"
ms.date: 08/11/2026
ms.topic: tutorial
---

<a id="test-an-orleans-application-end-to-end"></a>

# 测试 Orleans 应用程序的结束

这条通路构建了 Orleans 层数运行时间测试:第一次在工艺中进行集束试验,一个共享的集束固定体,以及一种地形变化。完整的、可建来源在文档的 `grains/snippets/testing/orleans-testing` 项目中。

<a id="choose-the-right-boundary"></a>

## 选择右边框

使用普通单位测试来进行纯应用逻辑, <xref:Orleans.TestingHost.InProcessTestCluster> 用于 Orleans 运行时间行为,生产供应商测试外部合同。

这些边界保持快速反馈和运行时的忠诚。

<a id="run-the-first-cluster-test"></a>

## 运行第一个集集测试

克隆存储库, 然后运行测试工程 :

```powershell
git clone https://github.com/dotnet/orleans.git
cd orleans
dotnet test --project .\docs\site\src\content\docs\grains\snippets\testing\orleans-testing\Sample.OrleansTesting\Sample.OrleansTesting.csproj --minimum-expected-tests 1
```

第一个测试创建了一个集束,部署它,从客户那里获得 grain 参考,发出呼叫并处置该组:

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTests.cs" id="basic_cluster_test":::

它可以验证代码生成、序列化、激活、信息发送和 grain 执行。 默认构建器启动两个 silos 和一个客户端, 其测试基础设施在模拟中快速运行 。

<a id="add-application-configuration"></a>

## 添加应用程序配置

生产 grains 通常依赖于通过依赖性注射注册的服务。配置所有主机,仅 silos,或只有使用匹配构建器范围的客户 :

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="configure_cluster":::

每个主机都拥有一个服务提供商。 注册服务类型后, 每个主机都会有一个实例。 注册被捕获的操作程序会故意在 silos 和客户之间共享, 因此共享测试双倍必须是安全的线性测试 。

每次配置改变后再运行测试。 通过测试设置来显示部署失败, 测试运行报告它们为失败 。

<a id="reuse-an-expensive-cluster"></a>

## 重新使用昂贵的集聚体

当多次测试需要相同组群配置时创建 xUnit 固定值:

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterFixture.cs" id="cluster_fixture":::

将固定点登记为收藏:

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterCollection.cs" id="cluster_collection":::

然后用每件试验中 已经启动的定着集

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/HelloGrainTestsWithFixture.cs" id="shared_cluster_test":::

每次测试都给 grain 独有的身份, 重置共享外部状态, 使每次测试均独立。

<a id="exercise-a-topology-change"></a>

## 改变地形变化

添加一个 silo,等待成员稳定下来,然后优雅地予以停止:

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/ClusterConfiguration.cs" id="change_topology":::

由测试及验证成员变更后的申请通知,

:::code language="csharp" source="../grains/snippets/testing/orleans-testing/Sample.OrleansTesting/TopologyTests.cs" id="topology_change_test":::

文档中的 `dotnet test` 命令执行此测试。 一个相同的进程组会籍会演算改变。 使用一个单独的环境来操作流程崩溃、 网络分割、套接运输和生产成员供应商 。

<a id="add-production-provider-tests"></a>

## 附加生产提供者测试

对于持久性、提醒、集群或流,增加一个选择入套件,其内容是:

1. 提供孤立的提供者实例或模拟器。
1. 配置组群,使用生产中使用的同一供应商扩展功能。
1. 使用独特的数据库、表格、流和 grain 识别符号。
1. 验证在 silo 重新启动时的行为。
1. 清理自有资源,即使一项主张失败。

将证书存放在试验环境的秘密设施中,并明确报告缺失的先决条件。关于 API 细节和忠诚界限,见 [测试 Orleans 应用](../grains/testing.md) 和 [TestingHost 结构架构](../implementation/testing.md)。
