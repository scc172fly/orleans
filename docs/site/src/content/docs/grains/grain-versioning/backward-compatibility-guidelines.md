---
title: "Grain 合同兼容性准则"
description: "在整个滚动部署中安全地推进 Orleans grain 接口。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-contract-compatibility-guidelines"></a>

# Grain 合同兼容性准则

Orleans 版本路由不验证合同。 新的激活只有在能够正确处理每一个可能接到的老调时, 才能进行后向兼容。

<a id="preserve-existing-methods"></a>

## 保留现有方法

在使用该方法的调用器仍然被部署时, 不要删除或更改方法的签名。 这包括参数顺序、 参数和返回类型、 通用形状以及方法语义 。

添加新方法,而不是重新定位现有方法:

:::code language="csharp" source="../../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="versioned_inventory_interface":::
标记旧的方法 `[Obsolete]` ,以停止新的使用,但保留到遥测和部署状态显示没有老的呼叫器留下。

<a id="preserve-payload-contracts"></a>

## 保留有效有效有效有效载荷合同

Grain 方法参数和返回值是按序列排列的合同。 Orleans 序列号版本容忍规则:

- 保持现有 `[Id]` 值的稳定。
- 添加带有新ID和安全默认值的新字段。
- 身份证不能用其他的
- 当旧代码仍然可以观察到时, 不要重命名或重新解释价值 。
- 保留所有调用版本的例外和结果类型。

参数名称不是发送的一部分,但改变语义顺序,保留同样的类型,可以无声地产生错误的结果。

<a id="preserve-behavior"></a>

## 保护行为

签名兼容性还不够。 更新的实施为老的来电者服务必须保存变异、授权行为、权力和结果,这些来电者预期会达到的结果意义。

当行为必须发生不兼容的变化时,应增加新方法或使用严格版本兼容性,并接受版本隔离的操作成本。

<a id="coordinate-with-persisted-state"></a>

## 与持续保持的州协调

Grain 接口版本不版本存储。在滚动升级过程中,两个版本都可以启动路径政策允许的 grain 身份。两种版本必须读取持续表达式,并只要可以进行回放或混合定位,则两个版本都必须从另一个版本中能够保持写字。

使用预设的变换:兼容的读者,然后是新作者,然后在旧版本消失后清理。
