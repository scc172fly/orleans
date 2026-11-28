---
title: "ORLEANS0009: 使用已注册的 Grain 调用返回类型"
description: 了解并解决 Orleans 无法映射 Grain 接口方法返回类型时出现的 ORLEANS0009。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0009-use-a-registered-grain-call-return-type"></a>

# ORLEANS0009: 使用已注册的 Grain 调用返回类型

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

Orleans 远程接口方法的返回类型与所选代理基类之间没有有效的可调用对象基类型映射。

<a id="impact"></a>

## 影响

Orleans 无法为远程调用生成并绑定调用对象和代理方法。

<a id="how-to-fix"></a>

## 修复方法

使用已注册的返回类型，例如 `Task`、`Task<T>`、`ValueTask` 或 `ValueTask<T>`。对于自定义调用抽象，为每个选定的代理基类注册兼容的 `InvokableBaseType` 以及任何必需的 `ReturnValueProxy` 初始化器。

格式无效的自定义映射会报告 `ORLEANS0026`。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅当某个有效的自定义运行时注册按设计对分析器不可见时抑制诊断。独立验证生成的代理和可调用对象代码。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0009.severity = none
```

参阅[自定义 Orleans 代码生成](../grains/code-generation-customization.md)。
