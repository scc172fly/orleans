---
title: "ORLEANS0026:无效的可引用基型绘图"
description: "理解和决心 ORLEANS0026 当自定义时 grain- 呼叫返回型号映射无法产生有效的可援引请求。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0026-invalid-invokable-base-type-mapping"></a>

# ORLEANS0026:无效的可引用基型绘图

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

适用的习惯 `InvokableBaseType` 注册无效, 对于 grain 常见原因包括登记冲突、通用性或限制无效、无法进入或密封请求基地、缺少支持的施工者或无效 `ReturnValueProxy` 初始化 。

<a id="impact"></a>

## 影响影响

Orleans 无法可靠地选择或生成RPC方法的可援引请求类型。代理和请求代码生成无法产生有效的自定义调用合同 。

<a id="how-to-fix"></a>

## 如何修补

确定诊断所指定的登记,然后:

1. 删除自相矛盾的映射, 避免通过组装登记替换内置代理默认默认值 。
2. 将开放的通用回报和请求类型与性能和限制匹配。
3. 使用一个可获取、非静态、无密封请求基础,并配有支持的建筑师。
4. 校正 `ReturnValueProxy` 初始化器名称和签名。
5. 校验 `GenerateMethodSerializers`所选的每个代理基数。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

禁止并不修复源生成合同,源产生器可以单独报告同一条件。 只有在独立验证了产生的输出后,仅对经证明的只显示为假正数的分析器进行抑制。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0026.severity = none
```

见 [自定义 Orleans 代码生成](../grains/code-generation-customization.md)。
