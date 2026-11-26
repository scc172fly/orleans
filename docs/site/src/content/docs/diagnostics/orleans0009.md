---
title: "ORLEANS0009:使用已登记的 grain-呼叫返回类型"
description: "理解和决心 ORLEANS0009 何时 Orleans 无法映射 grain 界面方法返回类型 。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0009-use-a-registered-grain-call-return-type"></a>

# ORLEANS0009:使用已登记的 grain-呼叫返回类型

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

Orleans 远程界面方法对于其返回类型和选定的代理基础没有有效的可引用基型绘图。

<a id="impact"></a>

## 影响影响

Orleans 无法生成和约束远程调用的调用对象和代用方法。

<a id="how-to-fix"></a>

## 如何修补

使用登记过的返回类型,如 `Task`、 `Task<T>`、 `ValueTask`或 `ValueTask<T>`。对于调用抽象的自定义,登记每个选定的代理基点所需的兼容 `InvokableBaseType` 和任何需要的 `ReturnValueProxy` 初始化器。

格式错误的自定义映射报告 `ORLEANS0026`。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有在有效的自定义运行时间注册被分析器故意忽略时才会禁止。 独立验证生成的代理和可引用代码 。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0009.severity = none
```

见 [自定义 Orleans 代码生成](../grains/code-generation-customization.md)。
