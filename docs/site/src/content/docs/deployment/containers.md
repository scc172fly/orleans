---
title: "运行 Orleans 多个主机的集装箱"
description: "配置多主机集装箱部署的常规 Orleans 端点 。"
ms.date: 08/15/2026
ms.topic: how-to
ms.custom: devops
---

<a id="run-orleans-in-containers-across-multiple-hosts"></a>

# 运行 Orleans 多个主机的集装箱

A 音 A Orleans 组合集只有在每个 silo 所有其他 silos 外部可直接达到。 Orleans 集装箱发现、共同会员表和公布后的主机港口本身不能创造这些网络路径。

尽可能使用私人网络、集装箱重叠或每个工作负荷网络界面。 不要将 Orleans silo 或网关端暴露在公共互联网上。

<a id="choose-an-endpoint-model"></a>

## 选择终点模式

每个 silo 都有单独的监听和广告的终点:

| 设置设置设置设置 | 意义的含义含义 | 典型集装箱价值 |
| --- | --- | --- |
| <xref:Orleans.Configuration.EndpointOptions.SiloListeningEndpoint> | 集装箱内为 silo 交通开放的地址和港口 | `0.0.0.0:11111` |
| <xref:Orleans.Configuration.EndpointOptions.GatewayListeningEndpoint> | 集装箱内为客户交通开放的地址和港口 | `0.0.0.0:30000` |
| <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress> | 人均 --silo 地址,供同侪和客户拨打 | 易变私人集装箱、任务、舱或主机地址 |
| <xref:Orleans.Configuration.EndpointOptions.SiloPort> |  储存入会港口 Silo | 直接路由或公布的 silo 端口 |
| <xref:Orleans.Configuration.EndpointOptions.GatewayPort> | G网关端端口返回客户 | 直接路由或公布的网关端口 |

绑定到 `0.0.0.0` 或 `::` 只打开本地界面。通配符地址不能被广告公布, Orleans 也不从容器捆绑中推断主机已公布的端口 。

优先选择其中一种模式:

- **直接每个集装箱地址:** 向每个集装箱提供来自所有 silo 和客户网络的私人地址,并公布该地址在集装箱内外的相同港口。
- **/silo 主机-港口映射:** 公布主机的私人地址和独有的已出版的 silo 和网关端对。

不要广告上刊登共享负载平衡器、 侵入虚拟 IP 或服务名称, 以连接到不同的复制件。 Orleans 成员资格指定了特定的 silo, 不是可互换的后端组合 。

<a id="configure-listening-and-advertised-endpoints"></a>

## 配置监听和广告的端点

向部署平台上每个复制品传送广告地址和公布的港口。

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_endpoint_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_endpoints":::

例如,集装箱可以监听 `0.0.0.0:11111` 和 `0.0.0.0:30000` 广告发布私人主机映像 `10.0.8.24:21111` 和 `10.0.8.24:23000`彼此之间 silo 连接必须能够连接到 `10.0.8.24:21111`,以及每个外部 Orleans 客户端必须能够连接到 `10.0.8.24:23000`.

如果地址或已公布的港口是动态分配的,在开始 Orleans 之前发现这些地址或港口并在加入该 silo 成员时保持其稳定。替代实例可以使用不同的终点,但必须结合新一代 silo 。

<a id="provide-private-bidirectional-connectivity"></a>

## 提供私营双向连接

验证完整的网络矩阵:

1. 从每个 silo 网络连接到每一个广告的 silo 终点。
1. 连接到每个广告网关端点
1. 使用主机端口翻译时, 请测试所有主机或集装箱以及远程主机的已公布的端点。 有些网络地址的翻译实施不支持所需的发型路径 。
1. 扩大规模、按比例计算、更换主机和滚动部署后,重复测试。

防火墙规则、安全小组、网络政策和服务政策必须允许 silos之间长期的TCP双向连接。客户只需要网关接入,而不是 silo-港口准入。两个港口都限制在可信赖的工作量上,当网络不是可信任的边界时使用 [Orleans 运输层安全](../host/transport-layer-security.md) 。

<a id="understand-discovery-versus-connectivity"></a>

## 理解发现与连接

集群供应商,如 DynamoDB、 Azure 表格存储、 ADO.NET、 Redis、领事或 ZooKeeper 等,协调成员和网关发现。它记录了参与者广告的终点;它不代理 Orleans 交通,建立路线,或者核实这些端点是可达到的。

因此,所有 silos 都能够成功地读写相同的会籍表而组群仍然无法沟通。 同样,客户可以发现活跃的网关,然后暂停时间,因为其网络无法达到一个或多个广告网关端点。

HTTP 入侵和应用服务发现是分开的。 前端负载平衡器可以将 HTTP 请求发送到申请中,但 Orleans silo 和网关连接仍必须到达从分组成员中选出的特定成员。

<a id="account-for-container-platform-behavior"></a>

## 集装箱平台行为账户

- 通常只对主机具有实际意义。 除非平台明确连接主机之间的线路, 否则不要在主机上进行广告宣传。
- 所公布的端口只有在主机地址是可变的、制图是一个 silo独有和每个所需源网络的绘图工作的情况下才可用。
- 在一个东道主上多个 silos 需要不同的广告 silo 和网关端口,只要它们共享主机地址。
- 动态或共享的负载平衡映射, 可以选择任何复制品, 无法保存 silo 身份, 也不适合作为广告上的终点 。

对于多克引擎来说,用户定义的 [重叠网络](https://docs.docker.com/engine/network/drivers/overlay/) 可以提供跨主机集装箱连接;独立集装箱仍要求参与的东道方使用Swarm模式。 拥有一个主机桥和已公布的港口,明确刊登东道方私人地址和每个集装箱独特的已出版港口的广告。

在亚马逊ECS, [`awsvpc` 任务联网](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/task-networking.html) 分配每一项任务,都指定了自己的网络接口和私人地址,这些地址与直接地址模式相匹配。其他网络模式要求明确验证主机-港口的独特性、路线安排和安全组规则。 DynamoDB 成员表可以在 AWS上发现 silos ,但并不能弥补无法触及的任务或主机端点。

对于精心设计和管理的平台,还应审查 [平台要求](platform-guides.md)、 [东道主主 Orleans 日期 时 Kubernetes](kubernetes.md)和 [东道主主 Orleans 日期 时 Azure 集装箱集装箱](deploy-to-azure-container-apps.md)。

<a id="complete-platform-qualification"></a>

## 完整的平台资格

本页为集装箱平台建立端点和路线模式。 选定的管弦乐队或管理服务也需要提供其余的生产机制:

- 工作量身份或管理下的秘密和证书交付。
- 启动、准备、存活、依赖性健康,以及有节制的终止期限。
- 资源政策、测试的最低 silo 计数、缩放控制和故障域定位。
- 双环滚动替换或隔离的蓝色绿色集群,具有兼容的国家处理方式。
- 中央日志、计量仪、跟踪、部署身份、供应商遥测和事故准入。
- 具有可重复验证的已更新基础设施和工作量配置。

使用 [选择部署目标](choose-deployment-target.md) 来维持目标导航。对于另一个管弦乐队,在生产流量之前完成 [所需平台](platform-guides.md) 和 [生产准备状态清单](production-readiness.md) 。

<a id="diagnose-forwarding-loops-and-timeouts"></a>

## 诊断性转发循环和超时

不仅从会员提供健康开始,

1. 记录每个过程有效公布的 silo 端点、公布网关端点和收听端点。
1. 检查成员记录,确认每个活跃行都有一个独特、预期的、可循的 silo 终点。
1. 从每个 silo 集装箱测试TCP连接到每一个已公布活动的 silo 端点。从客户网络测试每一个已公布的网关端点。
1. 比较集装箱目标港口、主机或平台制图、防火墙规则以及广告港口。
1. 替换实例后发生相应的故障, 并仅在确认旧 silo 无法在分割区运行之后才能删除过期成员。

使用平台的 TCP 测试工具。 例如:

```bash
nc -vz 10.0.8.24 21111
```

```powershell
Test-NetConnection 10.0.8.24 -Port 21111
```

共同指标包括:

| 症状 | 可能终点问题 |
| --- | --- |
| 会员身份是健康的,但电话在提出请求时暂停到期 | 发现成功,但广告上公布的运输终点被封锁或无法通行。 |
| 日志或成员显示 `127.0.0.1`、主机-本地桥址或集装箱目标港口 | silo 公告其捆绑式地址或港口,而不是同行可达映射。 |
| 多个活动 silos 广告用相同的地址和端口 | 共享负载平衡器或非独有主机映射无法选择要绘制的 silo。 |
| 重复转发、连接尝试或超时信息,以代表当地或目的地的终点为目标 silo | 广告身份与该 silo的可达端点不符。 信件可以重新检索或转发, 而不能达到预定程序 。 |
| TCP 连接成功, 但交通达到另一个复制 | 代理、 反向或负载平衡器正在将 Orleans 运输连接分布在复制品之间。 |

首先纠正终点映射。 增加响应超时或改变集群提供者不会成为无法到达的广告终端路由表。 关于更广泛的事件分类,请见 [故障解决部署](troubleshooting-deployments.md)。
