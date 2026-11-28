---
title: 配置 Consul 集群
description: 配置 Orleans Silo 和客户端，使其使用 Consul 管理集群成员资格及发现网关。
ms.date: 10/03/2026
ms.topic: how-to
---

<a id="configure-consul-clustering"></a>

# 配置 Consul 集群

使用 [`Microsoft.Orleans.Clustering.Consul`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Consul) 包，将 Orleans 成员资格记录存储在 [Consul 键值存储](https://developer.hashicorp.com/consul/docs/dynamic-app-config/kv) 中。

请使用 Consul 1.22 或更高版本。Orleans Silo 地址键包含 `:` 和 `@`，Consul 1.21.5 的默认键筛选器会拒绝这些字符。升级现有 Consul 1.21.5 部署时，请暂时启用 [`disable_http_unprintable_char_filter`](https://developer.hashicorp.com/consul/docs/v1.21.x/reference/agent/configuration-file/general#disable_http_unprintable_char_filter)，直到部署升级至 Consul 1.22 或更高版本。

集群中的每个 Silo 和客户端都必须使用：

- 相同的 Consul 集群和键值存储根路径。
- 相同的 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。
- 相同的 <xref:Orleans.Configuration.ClusterOptions.ClusterId>。

<a id="configure-a-silo"></a>

## 配置 Silo

配置 <xref:Orleans.Configuration.ConsulClusteringOptions>，指定 Consul 地址；若启用了 Consul ACL，还需指定 ACL 令牌：

:::code language="csharp" source="../../../snippets/compiled/Host/HostSnippets.cs" id="configure_consul_silo":::

<a id="configure-a-client"></a>

## 配置客户端

使用相同的 Consul 设置和集群标识配置 Orleans 客户端：

:::code language="csharp" source="../../../snippets/compiled/Host/HostSnippets.cs" id="configure_consul_client":::

<a id="inspect-membership-data"></a>

## 检查成员资格数据

提供程序将每个集群的数据存储在一个 Consul 键值前缀之下：

- 已配置 <xref:Orleans.Configuration.ConsulClusteringOptions.KvRootFolder?displayProperty=nameWithType> 时，使用 `<KvRootFolder>/orleans/<ClusterId>`。
- 未配置 `KvRootFolder` 时，使用 `orleans/<ClusterId>`。

`ServiceId` 不属于此前缀。不要对不同服务重复使用相同的 `KvRootFolder` 和 `ClusterId`，否则其成员资格记录将共享同一个键空间。

在集群前缀之下，提供程序维护：

| 键 | 用途 |
| --- | --- |
| `version` | 整数形式的成员资格表版本。Consul 对此键的 `ModifyIndex` 是用于比较并设置的 ETag。 |
| `<silo-address>` | Silo 注册记录，包括主机名、网关端口、启动时间、状态、Silo 名称及故障检测器投票。 |
| `<silo-address>/iamalive` | Silo 定期更新的 `IAmAlive` 时间戳。 |

成员资格读取使用 Consul 的一致性模式，以返回注册记录及表版本的一致快照。单个 Silo 的读取操作查询其注册记录和时间戳前缀，并验证表版本的 ETag 在整个读取期间保持不变。版本发生变化时，重试读取之前等待 100 ms，并遵循取消请求。插入及状态更新使用 [Consul 事务](https://developer.hashicorp.com/consul/api-docs/txn)，以原子方式对注册记录和表版本执行比较并设置。行或表的 ETag 冲突时返回更新失败结果，以便调用方刷新其成员资格视图。其他事务故障以异常形式呈现，其中包含 Consul 的操作错误详细信息。

每个 Silo 在其整个注册生存期内负责自身的定期 `IAmAlive` 更新。每次更新对该 Silo 的 `iamalive` 键执行一次原生写入，保留注册字段、行 ETag 及表版本。写入遵循取消请求，并传播存储故障。成员资格读取包含单独发布的时间戳，用于存活性诊断；行和表的并发检查仅使用规范的成员资格令牌。

清理操作移除启动时间、`IAmAlive` 时间戳及故障检测器投票均早于截止时间的 Dead 状态注册记录。它以原子方式比较注册记录和时间戳键，并保留表版本，从而保护被并发写入者修改的条目。

`IAmAlive` 时间戳用于诊断和启动恢复。Silo 之间相互探测以检测故障，详见[集群成员资格](../../../implementation/cluster-management.md)。

Orleans 客户端列出集群前缀下的记录，并选择状态为活跃且网关端口非零的注册记录。若客户端未发现任何网关，请检查客户端和 Silo 实际使用的前缀，再比较注册状态、网关端口及通告地址的可达性。

使用 [Consul KV 命令](https://developer.hashicorp.com/consul/commands/kv/get) 列出或检查记录：

```bash
consul kv get -keys -recurse <cluster-prefix>
consul kv get -detailed -recurse <cluster-prefix>
```

详细输出包含每个键的值和 `ModifyIndex`。请将此布局视为用于诊断的实现细节。如果该集群中可能仍有 Silo 正在运行，不要手动编辑或删除成员资格键。

<a id="production-considerations"></a>

## 生产环境注意事项

- 使用高可用配置运行 Consul，并遵循 [Consul 生产部署指南](https://developer.hashicorp.com/consul/docs/deploy/server)。
- 启用 Consul ACL 和 TLS。通过工作负载标识、机密存储或平台受保护的配置机制提供令牌；不要将令牌嵌入源代码或镜像。
- 限制网络访问，只允许获得授权的 Silo 和客户端访问 Consul API。
- 仅向 Orleans 标识授予其键值前缀所需的权限。
- 对必须彼此隔离的环境使用不同的 `ClusterId` 和键值存储根路径。
- 监控 Consul 可用性、请求延迟、领导者变更、存储容量以及 ACL 或 TLS 故障。
- 测试 Consul 降级或不可用时的集群启动和恢复。避免同步发生且无上限的重试。

Consul 成员资格数据用于协调集群发现和存活性，不包含 Grain 应用状态，也不能替代 Grain 存储提供程序。有关提供程序选择标准，请参见[拓扑、网络与集群](../../../deployment/networking.md#choose-a-clustering-provider)。
