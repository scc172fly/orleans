---
title: "配置 Conser 群集"
description: "配置 Orleans silos 和客户使用领事作为分组成员和网关发现。"
ms.date: 10/03/2026
ms.topic: how-to
---

<a id="configure-consul-clustering"></a>

# 配置 Conser 群集

使用 [`Microsoft.Orleans.Clustering.Consul`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Consul) 软件包将 Orleans 会籍记录存储在 [领事密钥/价值商店](https://developer.hashicorp.com/consul/docs/dynamic-app-config/kv)中。

使用领事1.22或以后。 Orleans silo-地址键包含 `:` 和 `@`,但领事1.21.5以默认的密钥过滤器拒绝。当升级现有的领事1.21.5部署时,临时启用 [`disable_http_unprintable_char_filter`](https://developer.hashicorp.com/consul/docs/v1.21.x/reference/agent/configuration-file/general#disable_http_unprintable_char_filter) ,直到部署抵达领事1.22或以后。

组群中的每一 silo 和客户必须使用:

- 同一个领事集群和关键/价值根。
- 相同 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。
- 相同 <xref:Orleans.Configuration.ClusterOptions.ClusterId>。

<a id="configure-a-silo"></a>

## 配置 silo

配置 <xref:Orleans.Configuration.ConsulClusteringOptions> , 使用领事地址, 当启用领事ACLs时, 设置 ACL 符号 :

:::code language="csharp" source="../../../snippets/compiled/Host/HostSnippets.cs" id="configure_consul_silo":::

<a id="configure-a-client"></a>

## 配置客户端

配置具有相同领事设置和群集身份的 Orleans 客户 :

:::code language="csharp" source="../../../snippets/compiled/Host/HostSnippets.cs" id="configure_consul_client":::

<a id="inspect-membership-data"></a>

## 被检查成员数据

一家领事公司在关键/价值前缀下每个组群的供应商商店:

- `<KvRootFolder>/orleans/<ClusterId>` 何时 <xref:Orleans.Configuration.ConsulClusteringOptions.KvRootFolder?displayProperty=nameWithType> 被配置。
- `orleans/<ClusterId>` 时, `KvRootFolder` 不配置 。

`ServiceId` 不是这个前缀的一部分。 不要重新使用相同的 `KvRootFolder` 和 `ClusterId` , 用于不同的服务, 因为会员记录将共享一个密钥空间 。

在组群前缀下,供应商坚持:

| 键关键键键 | 目的目的宗旨 |
| --- | --- |
| `version` | 成员表格的整数版本。 此密钥的领事 `ModifyIndex` 是比较和设置ETag 。 |
| `<silo-address>` | silo 登记,包括其主机名称、网关端口、启动时间、状态、 silo 名称和故障检测票。 |
| `<silo-address>/iamalive` | silo的周期 `IAmAlive` 时间戳。 |

成员阅读了领事的一致模式, 以返回一致的登记快照和表格版本。 Single-silo 读了查询 silo的注册和时间戳前缀并验证了表格版本的 ETag 在整个读取中保持不变。 在版本更改和荣誉取消后重新检索了 100 ms 的文本。插入和状态更新使用 [领事往来](https://developer.hashicorp.com/consul/api-docs/txn) 原子比较和设置登记和表格版本。相矛盾的行或表ETag 返回一个失败的更新,以便调用者可以刷新其成员视图。其他交易失败作为例外出现,包含领事操作错误的细节 。

每个 silo 在其注册的一生中拥有定期更新 `IAmAlive` 。每更新都使用一个本地字母,写到 silo的 `iamalive` 键上,保留登记字段、行ETag和表格版本。写荣誉取消并宣传存储失败。会员包括独立公布的活性诊断时间戳;行和表同值检查只使用卡通会籍标记。

清理清除了在截断点之前所有开始时间、 `IAmAlive` 时间戳和失败检测票的死登记。 它比较了注册和按原程序对键进行时间戳,保留表格版本,保存同时作者更改的条目。

`IAmAlive` 时间戳支持诊断和启动恢复。 Silos 如 [分组成员](../../../implementation/cluster-management.md)所述,相互调查以检测失败。

Orleans 客户列出组群前缀,并在非零网关端选择主动注册。如果客户发现没有网关,检查客户和 silos所使用的确切的前缀,然后比较登记状况、网关和广告上的可到达性。

使用 [Concoper KV 命令](https://developer.hashicorp.com/consul/commands/kv/get) 来列出或检查记录:

```bash
consul kv get -keys -recurse <cluster-prefix>
consul kv get -detailed -recurse <cluster-prefix>
```

详细输出包含每个关键字的值和 `ModifyIndex`。 将布局作为诊断性实施细节处理。 在该组中的任何 silo 可能仍在运行时, 不要手动编辑或删除会籍密钥 。

<a id="production-considerations"></a>

## 生产方面的考虑

- 运行领事,配置高度可用并遵循 [顾问团生产部署指导](https://developer.hashicorp.com/consul/docs/deploy/server)。
- 启用 Consulter ACLS 和 TLS。 使用工作量身份、 秘密商店或平台受保护配置机制提供代号; 不要在源代码或图像中嵌入代号 。
- 限制网络接入,仅授权 silos 客户可以联系领事 API.
- 只给 Orleans 身份关键/值前缀所需的权限 。
- 对必须保持孤立的环境使用单独的 `ClusterId` 和密钥/价值根。
- 监测领事的可用性、请求的延缓期、领导层的变化、储存能力以及ACL或TLS故障。
- 在领事被降级或无法使用时测试集束启动和恢复。 避免同步、 未约束的重试 。

领事数据会籍数据协调群集发现和活性。 它不包含 grain 应用程序状态, 也不取代 grain 存储提供者。 关于供应商的选择标准, 见 [地形学、联网和集群](../../../deployment/networking.md#choose-a-clustering-provider)。
