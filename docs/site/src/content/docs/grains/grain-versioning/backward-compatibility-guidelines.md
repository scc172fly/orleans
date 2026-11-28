---
title: Grain 契约兼容性指南
description: 在滚动部署期间安全地演进 Orleans Grain 接口。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-contract-compatibility-guidelines"></a>

# Grain 契约兼容性指南

Orleans 版本路由不会验证契约。只有较新的激活实例能够正确处理所有可能向其发出请求的旧调用方所生成的请求时，该实例才具备向后兼容性。

<a id="preserve-existing-methods"></a>

## 保留现有方法

只要使用某个方法的调用方仍在部署运行，就不要删除该方法或更改其签名。这包括参数顺序、参数与返回类型、泛型结构及方法语义。

添加新方法，而不是改变现有方法的用途：

:::code language="csharp" source="../../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="versioned_inventory_interface":::
将旧方法标记为 `[Obsolete]` 以避免新的使用，但在遥测和部署状态表明旧调用方均已退出前，仍应保留该方法。

<a id="preserve-payload-contracts"></a>

## 保留有效载荷契约

Grain 方法的参数和返回值属于序列化契约。遵循 Orleans 序列化器的版本容错规则：

- 保持现有 `[Id]` 值稳定。
- 使用新的 ID 和安全默认值添加新字段。
- 不要将 ID 复用于不同含义。
- 当旧代码仍可观察到这些值时，不要重命名或重新解释它们。
- 确保各版本的调用方均可使用异常类型及结果类型。

参数名称不是调用分派的一部分，但如果保留相同类型却更改参数的语义顺序，可能在不报错的情况下产生错误结果。

<a id="preserve-behavior"></a>

## 保留行为

仅签名兼容还不够。为旧调用方提供服务的新实现必须维持这些调用方预期的不变量、授权行为、幂等性及结果含义。

若行为必须发生不兼容变更，应添加新方法，或使用严格版本兼容策略并接受版本隔离带来的运维代价。

<a id="coordinate-with-persisted-state"></a>

## 与持久化状态协调

Grain 接口版本控制不会为存储进行版本控制。在滚动升级期间，对于路由策略所允许的 Grain 标识，任一版本的实现都可能被激活。只要仍可能发生回滚或混合版本放置，两个版本就必须能够读取持久化的数据表示，并容忍对方写入的数据。

分阶段变更架构：先部署兼容的读取方，再部署新的写入方，最后在旧版本退出后清理。
