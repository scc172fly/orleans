---
title: "故障解决部署"
description: "特吉 Orleans 部署和生产事故。"
ms.date: 08/15/2026
ms.topic: troubleshooting
ms.custom: devops
---

<a id="troubleshoot-deployments"></a>

# 故障解决部署

以一个时间框架开始。 记录第一个用户可见的症状、 部署或配置变化、 成员资格事件、 抚养事件和平台行动 。 在重新开始每个事件之前保存日志和跟踪 。

<a id="stabilize-the-service"></a>

## 稳定服务

1. 停止主动推出或自动缩放。
1. 保持足够的健康能力和故障场冗余。
1. 切换可选装载并禁用无约束的累列 。
1. 孤立可疑的不良版本或依赖性,不删除成员资格或状态。
1. 获取服务身份、群组身份、版本、 silo 名称、广告的终点和提供者健康。

不要重复重启全部 silos。同时重新启动会移除证据,增加供应商负荷,并可以将部分降解变成断电。

<a id="no-silo-can-start"></a>

## 无法启动 silo

检查 :

- 配置解析、 证书、 证书和时钟同步 。
- 集群提供商的连通性和授权。
- 服务 ID、 C组 ID 和提供者命名空间是否匹配预期环境 。
- 会员在灾害或被迫终止后出现停滞记录。
- 启动探测器的最后期限和平台杀人事件
- 供应商的减速或无法达到法定人数。

使用新的集束识别或成员名称空间进行灾难恢复。 在您证明没有从该组中分离出来的 silo 能够保存之前, 不要删除会籍记录 。

<a id="silos-start-but-dont-form-one-cluster"></a>

## 开始Silos 但不能组成一个组

比较每个 silo:

- 服务身份和群集身份。
- 集群提供方类型、端点、数据库或表格和命名空间。
- 广告发布IP地址和 silo 端口。
- DNS结果、网络政策、防火墙 和服务行为
- Orleans 软件包和应用版本。

从每个 silo 网络测试TCP连接到每一个广告的 silo 终点。一个过程可以成功聆听,同时发布地址不能达到同行。

定期请求超时、反复转发或连接尝试回溯、主机-本地集装箱地址或共享虚拟IP表示一个已公布的终点问题。将会员端点与集装箱装订、公布的主机港口和通过该映射达到的实际目的地进行比较。 诊断矩阵见 [运行 Orleans 多个主机的集装箱](containers.md) 。

<a id="clients-cant-connect"></a>

## 客户无法连接

客户必须使用与 silos相同的服务标识、集群标识和组群供应商。 确认提供商返回活跃网关,客户网络可以到达每个广告的网关端点。

网络负载平衡器或 Kubernetes 服务不能使单个网关地址能够到达。测试以成员身份存储的确切地址 。

<a id="calls-fail-during-membership-changes"></a>

## 成员变更时电话不能接

<xref:Orleans.Runtime.SiloUnavailableException>、超时或连接故障可能发生在 silo 离开或无法到达之时。 grain 参考仍然可用,而且 Orleans 可以将以后的电话转到新的激活线上。

失败的调用有一个 **未知结果**:它可能已经运行,然后才丢失回复。只有在操作是无能或被复制的并且再试验策略是受约束的情况下才会重新尝试。 [处理失败](handling-failures.md).

经常会籍点指向主机重新启动、生命阈值、资源耗竭、网络损失、供应商的延迟或不兼容的推出行为。 用平台事件和处理遥测来校准会员活动。

<a id="high-latency-or-rejection-rate"></a>

## 高潜留率或拒绝率

检查 :

- CPU压力、排程延迟、垃圾收集、记忆压力和线性饥荒。
- 热 grain 键, 长跑转圈, 封锁电话和扇子出局 。
- 网关加载缓期、 投放或过期信件, 以及套接字数 。
- 依赖性潜伏、节流、连接池、断路器和回溯体积。
- 损失 silo 后,激活增长和重新启动。

只有在核实提供者和下游服务能够吸收后才能增加能力。 使用 [能力规划和规模](capacity-planning.md) 来避免调整再试风暴的缩放。

<a id="dependency-degradation"></a>

## 依赖性退化

远程依赖性下降时保持健康。 只有在应用程序无法安全地为任何新工作服务的情况下, 才能使用准备状态。 否则会暴露这种依赖性已经退化并保存有文件证明的降低的能力。

确认超时和断路器在起作用,且重新测试交通是受约束的。 检查证书过期、供应商配额、 DNS、 TLS验证以及地区性事件 。

<a id="shutdown-and-rollout-failures"></a>

## 关闭和推出失败

如果在计划更换期间电话失灵:

- 关闭开始后确认准备就绪就绪就是假的
- 比较 .NET 主机关闭超时, 并有平台终止宽限期 。
- 检查平台是否在杀死进程前发出优雅的终止信号 。
- 减少推出货币并保持快速增援能力。
- 核查新旧合同、序列器、储存计划以及配置是兼容的。

见 [优优优停机和升级](upgrades.md)。

<a id="missing-or-unsafe-telemetry"></a>

## 失踪或不安全遥测

Orleans 日志经过 `Microsoft.Extensions.Logging` 并公布使用 .NET APPs.见 [Orleans 易观测](../host/monitoring/index.md).

远程测量包括集束和部署身份,但不记录机密或高心智质 grain 键的度量尺寸。 如果遥测随着服务而消失,则将其发送到外部收藏家,并给出关闭时冲刷一个约束性最后期限。

<a id="kubernetes-checks"></a>

## Kubernetes 支票检查

使用 :

```bash
kubectl get pods --namespace <namespace> --show-labels
kubectl describe pod --namespace <namespace> <pod-name>
kubectl logs --namespace <namespace> <pod-name> --previous
kubectl auth can-i list pods --namespace <namespace> --as system:serviceaccount:<namespace>:<service-account>
```

检查舱位IP、身份标签、下至-API 环境变量、服务账户、角色约束、检测故障、退出理由和资源抽动。见 [东道主主 Orleans 日期 时 Kubernetes](kubernetes.md)。

<a id="escalation-evidence"></a>

## 升级证据

收集包含以下内容的绑定时间窗口:

- 应用程序、 Orleans、平台和提供商日志。
- 测量和跟踪 围绕第一个症状。
- 成员简介和广告的终点。
- 部署清单和有效配置,并删除秘密。
- 运行时间和应用版本。
- 最小的序列,如果有的话,会复制失败。

特别是,不要将暂停业务描述为绝对失败,除非申请已经对结果进行了调和。
