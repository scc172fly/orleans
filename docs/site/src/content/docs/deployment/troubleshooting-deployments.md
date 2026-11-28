---
title: 排查部署故障
description: 对 Orleans 部署和生产环境事件进行初步诊断。
ms.date: 08/15/2026
ms.topic: troubleshooting
ms.custom: devops
---

<a id="troubleshoot-deployments"></a>

# 排查部署故障

首先建立事件时间线。记录用户首次感知到的症状、部署或配置变更、成员资格事件、依赖服务事件及平台操作。在重启所有实例之前保留日志和跟踪记录。

<a id="stabilize-the-service"></a>

## 稳定服务

1. 停止正在进行的滚动发布或自动缩容。
1. 保留足够的健康容量及跨故障域的冗余。
1. 削减非必要负载，并禁用无界重试。
1. 隔离疑似存在问题的版本或依赖服务，但不要删除成员资格记录或状态。
1. 收集服务 ID、集群 ID、版本、Silo 名称、通告的终结点及提供程序的健康状态。

不要反复重启所有 Silo。同时重启会消除诊断证据、增加提供程序负载，并可能将局部性能下降演变成服务中断。

<a id="no-silo-can-start"></a>

## 没有 Silo 能够启动

检查：

- 配置解析、凭据、证书及时间同步。
- 与集群提供程序的连通性及访问授权。
- 服务 ID、集群 ID 及提供程序命名空间是否与预期环境匹配。
- 灾难或强制终止后遗留的过期成员资格记录。
- 启动探针截止时间及平台强制终止事件。
- 提供程序节流或法定人数不可用。

灾难恢复时使用新的集群 ID 或成员资格命名空间。在证明该集群中没有因网络分区而隔离的 Silo 仍然存活之前，不要删除成员资格记录。

<a id="silos-start-but-dont-form-one-cluster"></a>

## Silo 已启动，但未组成同一个集群

比较每个 Silo 的以下信息：

- 服务 ID 和集群 ID。
- 集群提供程序类型、终结点、数据库或表以及命名空间。
- 通告的 IP 地址和 Silo 端口。
- DNS 解析结果、网络策略、防火墙及服务网格行为。
- Orleans 包版本及应用程序版本。

从每个 Silo 所在的网络测试与所有已通告 Silo 终结点的 TCP 连通性。进程可能成功监听，却通告了其他节点无法访问的地址。

周期性请求超时、反复转发，或者尝试连接环回地址、主机本地容器地址或共享虚拟 IP，都表明可能存在通告终结点问题。比较成员资格记录中的终结点、容器绑定地址、已发布的主机端口，以及通过该映射实际到达的目标。诊断矩阵请参阅[跨多台主机在容器中运行 Orleans](containers.md)。

<a id="clients-cant-connect"></a>

## 客户端无法连接

客户端必须使用与 Silo 相同的服务 ID、集群 ID 和集群提供程序。确认提供程序返回活动网关，并且客户端网络能够访问每个已通告的网关终结点。

Web 负载均衡器或 Kubernetes Service 并不能使各个网关地址变得可达。请测试成员资格记录中存储的确切地址。

<a id="calls-fail-during-membership-changes"></a>

## 成员资格变更期间调用失败

当 Silo 离开集群或变得不可达时，可能发生 <xref:Orleans.Runtime.SiloUnavailableException>、超时或连接失败。Grain 引用仍然可用，Orleans 可以将后续调用路由到新的激活实例。

失败的调用具有**未知的执行结果**：它可能在响应丢失之前已经执行。只有操作具有幂等性或具备去重机制，且重试策略有界时，才应重试。参阅[故障处理](handling-failures.md)。

频繁的成员资格变动可能指向主机重启、存活探针阈值、资源耗尽、网络丢包、提供程序延迟或不兼容的滚动发布行为。将成员资格事件与平台事件及进程遥测数据进行关联分析。

<a id="high-latency-or-rejection-rate"></a>

## 延迟或拒绝率过高

检查：

- CPU 节流、调度器延迟、垃圾回收、内存压力及线程池饥饿。
- 热点 Grain 键、长时间运行的执行轮次、阻塞调用及扇出。
- 网关负载削减、被丢弃或过期的消息，以及套接字数量。
- 依赖服务延迟、节流、连接池、熔断器及重试量。
- 激活实例数量增长，以及 Silo 丢失后的重新激活。

只有确认提供程序及下游服务能够承受新增负载后，才增加容量。参阅[容量规划与扩缩容](capacity-planning.md)，避免因扩容而加剧重试风暴。

<a id="dependency-degradation"></a>

## 依赖服务降级

远程依赖服务不可用时，应保持存活检查正常。仅当应用程序无法安全处理任何新工作时，才通过就绪检查反映该情况。否则应将依赖服务标记为降级，并保留文档中规定的有限服务能力。

确认超时及熔断器机制正常工作，且重试流量有界。检查凭据是否过期、提供程序配额、DNS、TLS 验证及区域性事件。

<a id="shutdown-and-rollout-failures"></a>

## 关闭及滚动发布故障

如果计划内的实例替换期间调用失败：

- 确认开始关闭时，就绪状态变为 false。
- 比较 .NET 主机的关闭超时与平台的终止宽限期。
- 检查平台是否在强制终止进程之前发送优雅终止信号。
- 降低滚动发布的并发度，并保留额外副本容量。
- 验证新旧版本的契约、序列化器、存储架构及配置是否兼容。

参阅[优雅关闭与升级](upgrades.md)。

<a id="missing-or-unsafe-telemetry"></a>

## 遥测数据缺失或不安全

Orleans 通过 `Microsoft.Extensions.Logging` 记录日志，并使用 .NET 诊断 API 发布指标和跟踪记录。参阅 [Orleans 可观测性](../host/monitoring/index.md)。

确保遥测数据包含集群和部署标识，但不要记录机密信息，也不要将高基数 Grain 键作为指标维度。如果服务停止时遥测数据也会丢失，请将其发送至外部采集器，并为关闭时的遥测刷新设置有界截止时间。

<a id="kubernetes-checks"></a>

## Kubernetes 检查

使用以下命令：

```bash
kubectl get pods --namespace <namespace> --show-labels
kubectl describe pod --namespace <namespace> <pod-name>
kubectl logs --namespace <namespace> <pod-name> --previous
kubectl auth can-i list pods --namespace <namespace> --as system:serviceaccount:<namespace>:<service-account>
```

检查 Pod IP、标识标签、Downward API 环境变量、服务账户、角色绑定、探针失败、退出原因及资源节流情况。参阅[在 Kubernetes 上托管 Orleans](kubernetes.md)。

<a id="escalation-evidence"></a>

## 升级处理所需证据

收集一个有界时间窗口内的以下信息：

- 应用程序、Orleans、平台及提供程序日志。
- 首次症状出现前后的指标及跟踪记录。
- 成员资格快照及已通告的终结点。
- 已移除机密信息的部署清单及实际生效的配置。
- 运行时及应用程序版本。
- 能够重现故障的最小操作序列（如果有）。

明确说明哪些信息已经确认、哪些未知、哪些是推断结果。尤其是对于已超时的业务操作，除非应用程序已核实其执行结果，否则不要将其描述为确定失败。
