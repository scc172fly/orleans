---
title: "ORLEANS0010:添加一个稳定的别名"
description: "理解和决心 ORLEANS0010 时 Orleans 类型或RPC方法使用名称衍生身份。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0010-add-a-stable-alias"></a>

# ORLEANS0010:添加一个稳定的别名

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 信息信息 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

grain 接口、 grain- 界面方法,或生成的序列数据类型没有 `[Alias]`。

<a id="impact"></a>

## 影响影响

Orleans 可追溯到姓名衍生身份,重命名CLR类型或方法可打破序列数据、RPC兼容性或滚动升级。

<a id="how-to-fix"></a>

## 如何修补

添加一个稳定、 有意选择的别名, 并保存在 CLR 重命名中。 代码修正会从名称空间和嵌套中生成一种别名, 或者用当前方法名称生成一种别名 。

审查为超载方法生成了别名,因为每次超载都需要独特的方法识别。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有在重新命名、部署和持续数据之间相互兼容显然无关紧要时才禁止使用。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0010.severity = none
```
