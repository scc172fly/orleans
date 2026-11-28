---
title: Grain 接口兼容性策略
description: 为 Orleans Grain 接口版本配置基于数字版本号的兼容性规则。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-interface-compatibility-strategies"></a>

# Grain 接口兼容性策略

兼容性策略回答一个问题：

```text
Can an activation at currentVersion process a request at requestedVersion?
```

内置策略仅比较这些数字版本号。

| 策略 | 数值规则 | 默认策略 |
|---|---|---|
| <xref:Orleans.Versions.Compatibility.BackwardCompatible> | `requestedVersion <= currentVersion` | 是 |
| <xref:Orleans.Versions.Compatibility.AllVersionsCompatible> | 始终兼容 | 否 |
| <xref:Orleans.Versions.Compatibility.StrictVersionCompatible> | `requestedVersion == currentVersion` | 否 |

<a id="backward-compatible"></a>

## 向后兼容

使用 <xref:Orleans.Versions.Compatibility.BackwardCompatible> 时，版本 2 的激活实例可以处理版本 1 的请求，但版本 1 的激活实例无法处理版本 2 的请求。

当每个较新实现都保留旧调用方需要的全部契约时，使用此策略。新调用方可以使用要求较新激活实例的新增功能，而旧调用方可以在任一版本上运行。

<a id="all-versions-compatible"></a>

## 所有版本兼容

<xref:Orleans.Versions.Compatibility.AllVersionsCompatible> 允许任意请求版本使用任意激活实例版本。Orleans 仍不执行结构检查。

仅当部署的契约确实双向兼容时才使用此策略，例如仅变更实现并有意提高路由版本号。如果新调用方使用旧激活实例不支持的方法或有效载荷，此策略可能将调用路由到无法履行该契约的实现。

<a id="strict-version-compatible"></a>

## 严格版本兼容

<xref:Orleans.Versions.Compatibility.StrictVersionCompatible> 要求数字版本号完全一致。它可以隔离各版本，但会减少放置选择；当其他版本的请求指向同一 Grain 标识时，不兼容的现有激活实例将被停用。

当各版本不得共享激活实例时使用此策略。它通常不适合作为渐进式滚动升级的默认策略，因为不同版本的调用方可能反复替换彼此的激活实例。

<a id="fully-compatible-is-a-contract-not-a-strategy"></a>

## “完全兼容”是一项契约，而非策略

完全兼容描述的是可双向工作的应用程序契约，而不是内置策略的名称。如果契约确实双向兼容，<xref:Orleans.Versions.Compatibility.AllVersionsCompatible> 就是向运行时表达这一保证的策略。
