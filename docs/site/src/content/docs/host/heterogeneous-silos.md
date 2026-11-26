---
title: "异异 Orleans silos"
description: "不同的主机主机 Orleans grain 不同类型上的 silos."
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="heterogeneous-orleans-silos"></a>

# 异异 Orleans silos

一组中的Silos 可以存放不同组 grain 类。 这样可以分离工作量、 使用专门硬件或独立部署 grain 执行, 同时保留一个 Orleans 组。

:::image type="content" source="media/heterogeneous.png" alt-text="其 silos 支持不同 grain 类型组的集。":::

全部 silos 客户应参考他们可以交换的接口和序列合同。 silo 仅应提及 grain 它可以主办执行大会。

<a id="prefer-assembly-boundaries"></a>

## 预装配的边界

最简单的模式是将专门 grain 类放在单独的项目中,并仅从运行该项目的 silo 主机中引用每个执行项目。 Orleans 发现由生成的元数据类型支持的 grain 类。

例如:

- 普通用途 silos 参考 `Orders.Grains`。
- GPU silos 参考 `Recommendations.Grains`。
- 客户参考 `Orders.Contracts` 和 `Recommendations.Contracts`,但都没有实施大会。

相同 grain 执行必须兼容于每个 silo 广告支持 grain 类型。

<a id="configuration"></a>

## 配置

当一个二进制可以运行多个角色时, 请配置 <xref:Orleans.Configuration.GrainTypeOptions.Classes>:

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="configure_grain_types":::

<xref:Orleans.Configuration.GrainTypeOptions.Classes> is a set of <xref:System.Type> values. Use it to include the exact grain classes the process can host or remove discovered classes that a role must not host:

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="exclude_grain_type":::

不要用过时的 grain- 早先的类别排除选项名称 Orleans 版本。

<a id="direct-placement-by-capability"></a>

## 按能力分列的直接安置

使用 [silo 元元数据](configuration-guide/silo-metadata.md) 和 [grain 放置过滤器](../grains/grain-placement-filtering.md) 多个当多 silos 相同负载 grain 实施时只有部分符合安置要求,如区域、硬件、租户或预订类型。

使用差异化 grain 当当 silo 无法将执行设置为全部。 当它可以将类型安装为主时使用位置过滤, 但放置应该更喜欢或需要元数据 。

<a id="deployment-rules"></a>

## 部署规则

- 保留 <xref:Orleans.Configuration.ClusterOptions.ServiceId>, <xref:Orleans.Configuration.ClusterOptions.ClusterId>、集群和协议配置,在所有角色之间保持一致。
- 部署至少一个健康 silo 每一援助的, grain 类型,然后客户援引。
- 独立维持每套专门 grain 成套部件的能力和冗余。
- 在执行之前提出合同修改,然后执行这些修改。
- 避免删除最后一个 silo a 的a grain 选择请求或持久工作时仍然针对它。

客户在连接后获得集束类型信息。 grain 支持前的 UR 类型 silos 已有。

<a id="limitations"></a>

## 限制

- 当没有活动 silo 支持目标 grain 类型时请求失败。
- 每一 silo 支持一个 grain 类型必须使用兼容的执行和合同。
- 无国籍工人 grains 应始终贯穿于该组群,而不是分成不兼容的多种不同组别。
- 暗流订阅要求兼容 grain 可用性;当部署情况不同,所有权不明确时使用 [明确订阅](../streaming/streams-programming-apis.md) 。

测试表层变化,包括生产类角色计数,特别是损失和替换支持某一类型的最后一个 silo 。
