---
title: "ORLEANS0026: 无效的可调用对象基类型映射"
description: 了解并解决自定义 Grain 调用返回类型映射无法生成有效可调用请求时出现的 ORLEANS0026。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0026-invalid-invokable-base-type-mapping"></a>

# ORLEANS0026: 无效的可调用对象基类型映射

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

适用于当前 Grain 方法返回类型和所选代理基类的自定义 `InvokableBaseType` 注册无效。常见原因包括注册冲突、泛型元数或约束无效、请求基类不可访问或已密封、缺少受支持的构造函数，以及 `ReturnValueProxy` 初始化器无效。

<a id="impact"></a>

## 影响

Orleans 无法可靠地为 RPC 方法选择或生成可调用请求类型。代理及请求代码生成无法产生有效的自定义调用契约。

<a id="how-to-fix"></a>

## 修复方法

定位诊断中指出的注册，然后执行以下操作：

1. 移除冲突的映射，避免通过程序集注册替换内置代理默认设置。
2. 按元数及约束匹配开放泛型返回类型与请求类型。
3. 使用可访问、非静态、非密封且具有受支持构造函数的请求基类。
4. 修正 `ReturnValueProxy` 初始化器的名称和签名。
5. 验证 `GenerateMethodSerializers` 选定的每个代理基类。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

抑制诊断并不能修复源生成契约，且源生成器可能单独报告相同问题。只有在独立验证生成输出之后，证实仅为分析器误报时才抑制。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0026.severity = none
```

参阅[自定义 Orleans 代码生成](../grains/code-generation-customization.md)。
