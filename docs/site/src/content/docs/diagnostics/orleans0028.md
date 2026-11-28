---
title: "ORLEANS0028: 无效的 Orleans 标识"
description: 解决 Orleans 别名、Grain 类型和 Grain 接口类型中的无效字符问题。
ms.date: 10/26/2026
ms.topic: reference
---

<a id="orleans0028-invalid-orleans-identity"></a>

# ORLEANS0028: 无效的 Orleans 标识

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

源码中的 <xref:Orleans.AliasAttribute>、<xref:Orleans.GrainTypeAttribute> 或 <xref:Orleans.Runtime.GrainInterfaceTypeAttribute> 指定了 null 或空标识、含有引号、空白字符、控制字符或保留类型名分隔符（`[](),*&`）的值，或者无效的泛型元数后缀。

<a id="how-to-fix"></a>

## 修复方法

选择不含引号、空白字符、控制字符和保留类型名分隔符的非空标识。限定名称、嵌套类型名称、泛型元数后缀和字面量反斜杠仍保留原有含义。Orleans 将有效标识原样记录在[契约清单](../grains/grain-versioning/contract-compatibility-analyzer.md)中。

在每个嵌套类型名称末尾用反引号和随后的 ASCII 十进制数字表示泛型元数，元数总计不得超过 64。例如，``counter`1+Nested`2`` 有效；``counter`1v2`` 在元数后缀之后还有其他文本，必须重命名。

部署前检查标识变更的线协议兼容性。特性构造函数和配置的类型别名注册在运行时执行相同的有效性规则。对于现有清单及持久化别名，请遵循[标识迁移指南](../migration/contract-identities.md)。
