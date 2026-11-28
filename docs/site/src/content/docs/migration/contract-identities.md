---
title: 迁移 Orleans 契约标识
description: 采用与运行时一致的标识分析时，迁移现有契约清单和持久化别名。
ms.date: 10/27/2026
ms.topic: how-to
---

<a id="migrate-orleans-contract-identities"></a>

# 迁移 Orleans 契约标识

[契约兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md) 记录 Orleans 实际使用的标识：优先采用显式的 <xref:Orleans.Runtime.GrainInterfaceTypeAttribute>，否则采用接口的 <xref:Orleans.AliasAttribute>，再否则采用其 CLR 名称（其中外围类型的名称以别名替换）。

Orleans 10.4.0 即使对具有别名的接口，也会生成基于 CLR 的接口标识。升级到识别别名的分析方式时，需一次性审查已签入版本控制的 `OrleansContracts.txt` 条目。对于别名包含新规则所拒绝字符的应用程序，标识验证还引入了独立的运行时兼容性要求。

<a id="migrate-a-1040-contract-manifest"></a>

## 迁移 10.4.0 契约清单

在更改应用程序契约之前，针对最近一次部署的源码及配置执行基线迁移：

1. 保留已部署源码、契约清单及标识配置的副本。
2. 使用 <xref:Orleans.Metadata.GrainInterfaceTypeResolver.GetGrainInterfaceType*> 解析每个受影响接口的已部署标识。先检查显式接口标识，再检查接口及外围类型的别名。结合应用程序运行时配置，审查自定义标识提供程序和配置的类型别名。
3. 对错误记录 CLR 名称的条目，将记录的 `GrainInterfaceType` 值替换为已验证的部署标识。保留其 CLR 名称、版本、方法标识、签名、注释及退役状态。
4. 使用更新后的分析器构建项目，并审查所有剩余诊断信息。基线与已部署标识一致后，使用[项目级重新生成命令](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest) 重新生成基线并审查差异。
5. 将基线修正与应用程序契约变更分开提交。确认随后更改别名时会产生契约诊断信息。

例如，如果已部署的接口 `Contoso.ICounterGrain` 的别名为 `counter`，将以下条目：

```text
interface [GrainInterfaceType("Contoso.ICounterGrain")] Contoso.ICounterGrain [Version(2)]
  ping: Ping() -> Task
```

修正为：

```text
interface [GrainInterfaceType("counter")] Contoso.ICounterGrain [Version(2)]
  ping: Ping() -> Task
```

这样可使记录的基线与已部署的传输标识一致。运行时特性及持久化数据保持不变。

重新生成未经修正的 10.4.0 基线时，会发出基于别名的有效条目，并将旧的基于 CLR 的条目保留为 `*RETIRED*`。这种保守方式保留了记录历史。结合已部署源码审查这些成对条目，以区分基线修正和实际标识替换。仅凭当前别名无法确定早期部署所使用的别名。

对于只包含 CLR 名称的旧声明，在原始源类型仍然可用时重新生成清单。这样分析器就可以解析嵌套类型，并在退役声明中记录其标识。需要确定退役声明的原始嵌套关系或别名历史时，请恢复当时已部署的源码。

<a id="migrate-aliases-rejected-by-identity-validation"></a>

## 迁移被标识验证拒绝的别名

[`ORLEANS0028`](../diagnostics/orleans0028.md) 验证 <xref:Orleans.AliasAttribute>、<xref:Orleans.GrainTypeAttribute> 和 <xref:Orleans.Runtime.GrainInterfaceTypeAttribute> 声明。构造这些特性及注册配置的类型别名时，同样适用以下规则：

- 使用非空值，不得包含引号、空白字符、控制字符或保留的类型名称分隔符（`[](),*&`）。
- 每个嵌套类型名称的末尾使用反引号后跟 ASCII 十进制数字表示泛型参数个数，总个数不超过 64。例如，``Contoso.Outer`1+Inner`2`` 的泛型参数总个数为 3。

对于具有受影响的持久化别名的应用程序，在采用验证规则变更之前，请使用兼容版本的 Orleans 完成数据迁移：

1. 盘点源代码标识和配置的别名，包括已存储 Grain 状态、流有效载荷及持久化 Grain 引用使用的名称。备份受影响的数据。
2. 选择有效的替代标识。使用在较早 Orleans 版本上运行的应用程序专用迁移程序读取旧有效载荷，并以替代标识重写。保留字段 ID 和应用程序状态；更改 Grain 类型时，考虑 Grain 键、引用及存储寻址的影响。
3. 验证升级后的应用程序可以读取每个迁移后的有效载荷，并能构造其序列化器及运行时服务。准备能够读取已重写数据的回滚构建版本。
4. 使用[部署与回滚指南](deployment-and-rollback.md)，协调 RPC 或 Grain 标识变更的部署和流量切换。

重命名源码中的特性将更改未来使用的标识；重写现有有效载荷则使它们与新标识兼容。在 `WellKnownTypeAliases` 中注册被拒绝的名称同样会触发运行时验证，因此应使用较早运行时完成旧格式数据的读取。
