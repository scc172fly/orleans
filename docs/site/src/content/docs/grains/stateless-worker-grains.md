---
title: "无国籍工人 grains"
description: "规模化地点-具有 Orleans的透明工人人才库。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="stateless-worker-grains"></a>

# 无国籍工人 grains

正常的 grain 身份最多有一个在组群中的激活。无国籍工人的身份可以有多个启动,允许 Orleans 将独立工作的规模扩大到兼容的 silos。

将 <xref:Orleans.Concurrency.StatelessWorkerAttribute> 应用到执行:

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="image_worker":::
和别人一样叫它 grain:

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="call_image_worker":::
关键点指工人集合,而不是个体激活。 同一引用的连续呼叫可以使用不同的激活方式。

<a id="scaling-behavior"></a>

## 缩动行为

Orleans 选择本地兼容激活。 如果所有本地激活都忙碌且每-silo 极限还没达到 Orleans 默认最大值为 `Environment.ProcessorCount` 启动次数 silo.

明确设定限制:

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="limited_image_worker":::
空闲工人默认会被清除。 双引力属性构造器可以禁用因特殊工作量而拆迁闲置工人。

<a id="state-and-concurrency"></a>

## 国家货币和货币

“无国家”是指激活不是单独可处理的,也没有一个启动拥有钥匙的权威状态。 工人可以保留缓存或其他地方状态,但与其他激活没有协调,随时可以消失。

无国籍工人默认是无法重新加入的。 只有在申请中断时,执行这些工人是安全的,才添加 <xref:Orleans.Concurrency.ReentrantAttribute> 。

良好的用途包括CPU约束式转换、本地预汇总、协议适应和复制读取缓存。 不要在需要单体作者一致性的实体国使用无国籍工人。

无国籍工人的活化活动不参与 grain 移徙。
