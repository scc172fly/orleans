---
title: "机密和全权证书"
description: "保护 Orleans 供应商的全权证书、工作量身份、证书和敏感配置。"
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="secrets-and-credentials"></a>

# 机密和全权证书

Orleans 向特定供应商客户提供 .NET 配置的证书。主机平台的工作量身份和秘密管理设施发布、储存和轮换这些证书。对每个集束、存储、提醒、流和遥测提供者应用证书指南。

<a id="prefer-workload-identity"></a>

## 更可取的工作量

提供商支持使用工作量身份或其他短期的认证机制。例如, Azure 提供者通常接受 <xref:Azure.Core.TokenCredential>;生产工作量可以使用 <xref:Azure.Identity.ManagedIdentityCredential> 来获取身份。在其他平台上遵循供应商对等机制的文件。

赋予每项工作量,使其承担其作用所要求的数据-计划行动:

- 当选定的集群提供商需要网关发现数据时,客户需要获得这种数据。
- Silos 需要会员业务及其所使用的具体的存储、提醒、流和目录操作。
- 部署和计划管理身份可以与运行时的身份分开。
- 生产、中转和发展应使用不同的身份和供应商命名空间。

处理 <xref:Orleans.Configuration.ClusterOptions.ServiceId>, <xref:Orleans.Configuration.ClusterOptions.ClusterId>, grain 关键字、提供者名称和连接端点作为识别资料。 通过认证提供者的连接和网络政策保护准入。

<a id="handle-unavoidable-secrets"></a>

## 处理不可避免的秘密

当服务提供人需要连接字符串、密码、访问密钥或证书密码时:

- 从一个经批准的秘密供应商或受保护部署配置上载
- 限制阅读工作量身份和负责轮换的操作员。
- 通过保护配置渠道传递口令秘密,以限制过程检查和诊断接触。
- 日志、跟踪、计量、例外信息、健康反应和仪表板可见状态的校正证明。
- 保留 grain 关键内容和请求背景限于经核准披露的识别资料和应用元数据。

[.NET 配置系统](https://learn.microsoft.com/dotnet/core/extensions/configuration) 组合多个提供者。审查最后优先级,并给生产秘密来源预定优先级。通过程序提供方配置将SDK的证明对象传给客户。

<a id="protect-certificates-and-private-keys"></a>

## 保护证书和私人钥匙

技术和后勤系统证书是工作量证明,限制私人钥匙进入程序身份、使用姓名和适合工作量作用的扩大关键用途,并在部署支持工作时签发个人工作量证书。

证书加载、缓存、处置、更新和信托存储管理是应用程序和平台责任。计划一个发行者和叶片认证者旋转的重叠窗口,监控过期情况并测试连接行为。 见 [并旋转 Orleans TLS 证书](../host/transport-layer-security.md#load-the-local-certificate)。

<a id="design-rotation-and-failure-behavior"></a>

## 设计、轮作和故障行为

每一证书:

1. 记录其所有人、许可、来源、消费者、到期和轮换程序。
1. 确定供应商SDK是自动刷新它还是主机必须重新连接或重新启动。
1. 过期前的警告, 以及编辑证书时认证失败时的提醒 。
1. 运行流量时测试旋转,并核实旧的证书在重叠窗口后被撤销。
1. 决定认证提供者的失败是否应当拒绝新的工作,维护现有联系,还是使东道主无法做好准备。

定期审核对等证书的轮换。 核实当前供应商身份是否保留最少特权, 撤销客户、 silos和操作员是否被拒绝 。
关于全权证书和供应商连接的网络边界,见 [网络加固](networking.md)。
