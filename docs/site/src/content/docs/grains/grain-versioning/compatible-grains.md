---
title: "Grain 接口兼容战略"
description: "配置 Orleans grain 接口版本的数值兼容规则 。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-interface-compatibility-strategies"></a>

# Grain 接口兼容战略

兼容性战略回答一个问题:

```text
Can an activation at currentVersion process a request at requestedVersion?
```

内置战略只比较这些数值。

| 战略战略战略战略 战略 | 数值规则 | 默认默认 |
|---|---|---|
| <xref:Orleans.Versions.Compatibility.BackwardCompatible> | `requestedVersion <= currentVersion` | 是 是 |
| <xref:Orleans.Versions.Compatibility.AllVersionsCompatible> | 始终兼容 | 无 |
| <xref:Orleans.Versions.Compatibility.StrictVersionCompatible> | `requestedVersion == currentVersion` | 无 |

<a id="backward-compatible"></a>

## 后向兼容

使用 <xref:Orleans.Versions.Compatibility.BackwardCompatible>,版本2激活可以处理版本1请求,但版本1启动不能处理版本2请求。

使用它,当每个更新的实施都保留了老来电员所需的每份合同。 新来电员可以使用需要新激活的附加设备,而旧来电员可以在任一版本运行。

<a id="all-versions-compatible"></a>

## 所有版本兼容

<xref:Orleans.Versions.Compatibility.AllVersionsCompatible> allows any request version to use any activation version. Orleans still performs no structural check.

只有在部署合同在两个方向上真正兼容时才能使用这一功能,比如只执行一次修改,并故意增加路径版本。 如果新来电者使用一种方法或有效载荷,而该方法没有旧的激活支持,则此策略可以将号召引向无法满足它的执行。

<a id="strict-version-compatible"></a>

## 严格版本兼容

<xref:Orleans.Versions.Compatibility.StrictVersionCompatible> requires an exact numeric match. It isolates versions but reduces placement choices and causes an incompatible existing activation to be deactivated when another version addresses the same grain identity.

当版本不能共享激活时使用它。 渐进滚动升级通常默认值不高, 因为不同版本的调用者可以反复替换对方的激活。

<a id="fully-compatible-is-a-contract-not-a-strategy"></a>

## "完全兼容"是合同,不是策略

完全兼容性描述的应用程序合同双向工作。 它不是内置策略的名称。 如果该合同是真正的双向, <xref:Orleans.Versions.Compatibility.AllVersionsCompatible> 代表着运行时的这一说法 。
