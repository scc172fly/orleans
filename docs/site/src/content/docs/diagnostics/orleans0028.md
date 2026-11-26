---
title: "ORLEANS0028:无效的 Orleans 身份"
description: "解析无效字符的 Orleans 别名、 grain 类型和 grain 接口类型。"
ms.date: 10/26/2026
ms.topic: reference
---

<a id="orleans0028-invalid-orleans-identity"></a>

# ORLEANS0028:无效的 Orleans 身份

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

源 <xref:Orleans.AliasAttribute>、 <xref:Orleans.GrainTypeAttribute>或 <xref:Orleans.Runtime.GrainInterfaceTypeAttribute> 指定了无效或空身份、含有引号的数值、白空间、控制字符、或保留类型名称分隔符(`[](),*&`),或无效的通用异性后缀。

<a id="how-to-fix"></a>

## 如何修补

选择一个非空身份, 不带引号、 空白空间、 控制字符或保留类型名称分隔符。 限定名称、 嵌套型名、 通用等同后缀和字面反线保留其意义 。 Orleans 在 [合同货单](../grains/grain-versioning/contract-compatibility-analyzer.md)中逐字记录有效身份 。

将通用性写为反向, 然后在每个嵌套型名称结尾处的 ASCII 小数位数, 总计最多64。 例如, ``counter`1+Nested`2`` 是有效的; ``counter`1v2] 在其正数后有文字, 必须重新命名 。

属性构造器和配置的型名登记在运行时执行同样的有效规则。对现有的货单和固定的别名遵循 [身份移徙指南](../migration/contract-identities.md) 标准。
