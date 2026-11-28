---
title: 无状态工作者 Grain
description: 使用 Orleans 扩展位置透明的工作者池。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="stateless-worker-grains"></a>
# 无状态工作者 Grain

普通 Grain 标识在集群中最多拥有一个激活实例。无状态工作者标识则可以拥有多个激活实例，使 Orleans 能够在兼容的 Silo 之间扩展彼此独立的工作负载。

在实现类上应用 <xref:Orleans.Concurrency.StatelessWorkerAttribute>：

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="image_worker":::
像调用其他 Grain 一样调用它：

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="call_image_worker":::
键标识的是工作者池，而不是单个激活实例。针对同一个引用的连续调用可以在不同的激活实例上执行。

<a id="scaling-behavior"></a>
## 扩缩容行为

Orleans 优先选择本地兼容的激活实例。如果本地所有激活实例均繁忙，且尚未达到每个 Silo 的数量上限，则 Orleans 可以创建另一个激活实例。默认上限为每个 Silo `Environment.ProcessorCount` 个激活实例。

显式设置数量上限：

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="limited_image_worker":::
默认情况下，空闲的工作者会被移除。对于特殊工作负载，可以使用带两个参数的特性构造函数禁用空闲工作者回收。

<a id="state-and-concurrency"></a>
## 状态与并发

“无状态”是指各激活实例不能被单独寻址，也不存在某个激活实例拥有该键的权威状态。工作者可以保留缓存或其他本地状态，但这些状态不与其他激活实例协调，并且随时可能消失。

无状态工作者默认不可重入。仅在实现可以安全地交错处理请求时，才添加 <xref:Orleans.Concurrency.ReentrantAttribute>。

合适的用途包括 CPU 密集型转换、本地预聚合、协议适配以及复制的读缓存。不要将无状态工作者用于需要单写入者一致性的实体状态。

无状态工作者的激活实例不参与 Grain 迁移。
