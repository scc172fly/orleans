---
title: "迁移 Orleans 合同身份"
description: "在采用与时间不相符的身份分析时,将现有合同表和别名进行缩减并保持原用。"
ms.date: 10/27/2026
ms.topic: how-to
---

<a id="migrate-orleans-contract-identities"></a>

# 迁移 Orleans 合同身份

[合同兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md) 记录 Orleans所使用的身份:一个明确的 <xref:Orleans.Runtime.GrainInterfaceTypeAttribute>,否则是接口的 <xref:Orleans.AliasAttribute>,或者它的CLR名称用替代的别名代替含有型号的。

Orleans 10.4.0 生成的CLR(甚甚至对化名接口也生成了CLR)界面身份。 `OrleansContracts.txt` 身份验证还单独对使用含有新拒绝字符的别名的申请提出了运行时间兼容性要求。

<a id="migrate-a-1040-contract-manifest"></a>

## 10.4.0合同清单

在更改申请合同之前,对照最后部署的来源和配置进行基线迁移:

1. 保存部署来源、合同清单及其身份配置的副本。
2. 使用 <xref:Orleans.Metadata.GrainInterfaceTypeResolver.GetGrainInterfaceType*>解决每个受影响的接口部署的身份。 先检查明确的界面身份, 然后检查接口和包含型号别名。 审查自定义的身份提供者, 并配置了与应用程序运行时配置的别名 。
3. 对于错误记录 CLR 名称的条目, 请用已核实部署的身份替换其记录的 `GrainInterfaceType` 值。 保存其 CLR 名称、 版本、 方法身份、 签名、 注释和退休状态 。
4. 与更新的分析器一起构建, 并审查所有剩余的诊断。 在基准匹配了部署的身份后, 使用 [项目级再恢复命令](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest) 重新生成它, 并检查 diff 。
5. 将基准更正与应用合同的更改分开提交。确认随后的别名更改会产生合同诊断结果。

例如,如果部署的接口 `Contoso.ICounterGrain` 有别名 `counter`,请更正此条目:

```text
interface [GrainInterfaceType("Contoso.ICounterGrain")] Contoso.ICounterGrain [Version(2)]
  ping: Ping() -> Task
```

至:

```text
interface [GrainInterfaceType("counter")] Contoso.ICounterGrain [Version(2)]
  ping: Ping() -> Task
```

这改变了记录的基准,以与部署的线性身份匹配。运行时间属性和数据保持不变。

重新生成未校正的 10.4.0 基线,可释放一个主动化名输入,并保留旧的以CLR为基础的输入为 `*RETIRED*`。这保守了记录的历史。对照部署的来源审查这些对子,以区分基准更正和实际身份替换。仅用目前的别名无法确定使用哪个别名来取代先前部署。

对于仅包含 CLR 名称的遗留声明, 原始源类型可用时再生。 分析器可以解决嵌套类型并将身份记录在退休声明中。 当需要确定已退出声明的原嵌套或别名历史时, 恢复已部署的来源 。

<a id="migrate-aliases-rejected-by-identity-validation"></a>

## 身份验证拒绝使用的除役别名

[`ORLEANS0028`](../diagnostics/orleans0028.md) 验证 <xref:Orleans.AliasAttribute>、 <xref:Orleans.GrainTypeAttribute>和 <xref:Orleans.Runtime.GrainInterfaceTypeAttribute> 声明。在构建这些属性并登记配置的型号别名时,适用同样的规则:

- 使用含有无引号、空白空间、控制字符或保留类型名称分隔符的非空值(`[](),*&`).
- 以回溯方式写通用性, 之后是 ASCII 在每个嵌套型名称结尾的十进制位数, 组合数最多为 64. 例如, ``Contoso.Outer`1+Inner`2`` 组合数为3。

对于使用受影响长期存在别名的申请,在通过验证修改之前,使用兼容的 Orleans 版本完成数据迁移:

1. 清单源身份和配置的别名,包括储存 grain 状态、流有效载荷和持续 grain 参考文献所用的名称。
2. 选择有效的替换身份。使用在早期运行的应用程序特定迁移 Orleans 保存字段ID和应用状态,并记 grain 修改 grain 类型。
3. 测试升级后的应用是否读取了每一个迁移的有效载荷,并能够构建其序列器和运行时间服务。 建立一个可读取重写数据的回滚结构 。
4. 协调部署和交通的削减,以适应或协调已改变的区域通信委员会或 grain 使用 [部署和回滚指导](deployment-and-rollback.md).

重命名源属性会改变未来身份; 重写现有有效载荷可以确定它们与新身份的兼容性。 在 `WellKnownTypeAliases` 中注册一个被拒绝的名称也会触发运行时间验证, 所以完成旧格式读取和早期运行时间 。
