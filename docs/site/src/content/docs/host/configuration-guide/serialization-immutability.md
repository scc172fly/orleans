---
title: "Orleans中不可变型的序列化"
description: "学习 .NET Orleans 在序列化背景下如何处理不可移动性的类型。"
ms.date: 05/23/2025
ms.topic: concept-article
---

<a id="serialization-of-immutable-types-in-orleans"></a>

# Orleans中不可变型的序列化

Orleans 具有一个特性, 您可以用来避免与含有不可更改类型的信件序列相关的一些间接费用。 本节描述该特性及其应用, 从相关背景开始 。

<a id="serialization-in-orleans"></a>

## Orleans中的序列化

当您使用 grain 方法时, Orleans 运行时间将生成一份精密的方法参数副本并从这些副本中提出请求。这防止在数据传送到所谓的 grain之前调用代码修改参数对象。

如果调用 grain 的是在不同的 silo上,则最终将副本序列成字节流,并传送到网络目标 silo,然后将其解密后再送回对象。如果调用的 grain 在同一 silo上,则直接交给称为方法。

返回值处理方式相同:首先复制,然后可能按顺序排列和取消。

请注意,所有三个过程——复制、序列化和消散——都以尊重对象身份为单位。 换句话说,如果通过一份包含同一物体的清单两次,接收方得到一份带有相同物体的清单两次,而不是两个具有相同价值的物体两次。

<a id="optimize-copying"></a>

## 优化复制

在许多情况下, 深复制是不必要的。 例如, 考虑一个假设方案, 即网络前端从客户端接收字节阵列, 并将该请求( 包括字节阵列) 传递到 grain 处理中。 前端进程在将数组传送至 grain后, 没有使用此阵列; 具体地说, 它不会再为未来请求重新使用该阵列。 在 grain内, 字节阵列是用来获取输入数据的, 但并没有修改。 grain 将它创建的另一个字节阵列返回网络客户, 然后在返回后立即丢弃该阵列。 网前端将结果逐项不加修改地传回客户端 。

在这样的情况下, 不需要复制请求或响应字节数组。 不幸的是, Orleans 运行时间无法自动解答这一点, 因为无法确定网络前端还是 grain 稍后修改阵列。 理想的情况是, 一个 .NET 机制将表明一个数值不再更改。 没有这个机制, 我们添加了 Orleans- 特定机制: <xref:Orleans.Concurrency.Immutable`1> 包装类和 <xref:Orleans.ImmutableAttribute>。

<a id="use-the--attribute-to-mark-a-type-parameter-property-or-field-as-immutable"></a>

### 使用 <xref:Orleans.ImmutableAttribute> 属性来标记不可更改的类型、参数、属性或字段

对于用户定义的类型,您可以将 <xref:Orleans.ImmutableAttribute> 添加到类型中。这指示 Orleans 序列器避免复制这类类型的实例。以下代码片段显示使用 <xref:Orleans.ImmutableAttribute> 表示不可更改的类型。在传输过程中不会复制这种类型。

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_type":::

有时,你可能无法控制对象;例如,它可能是 `List<int>` 发送到 grains。其他时候,你物品的部分可能无法移动,而其他的则不是。 Orleans 支持其他选项。

1. 方法的特征可包括每参数 <xref:Orleans.ImmutableAttribute> :

    :::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_parameter":::

1. 将单个属性和字段标为 <xref:Orleans.ImmutableAttribute> ,以防止复制内容类型被复制。

    :::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_members":::

<a id="use-"></a>

### 使用 <xref:Orleans.Concurrency.Immutable`1>

使用 <xref:Orleans.Concurrency.Immutable`1> 包装器类来表示一个值可以被视为不可改变的; 也就是说, 基本值不会被修改, 所以安全共享不需要复制。 请注意, 使用 <xref:Orleans.Concurrency.Immutable`1> 既不意味着该值的提供者或接受者今后也不会更改它。 这是一个相互、 双向的承诺, 不是单方面的承诺 。

使用 <xref:Orleans.Concurrency.Immutable`1> 插入 grain 接口、过 <xref:Orleans.Concurrency.Immutable`1> 代替 `T`例如,在上述假设情况中, grain 方法为:

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="mutable_request":::

其结果将是:

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_request":::

创建 <xref:Orleans.Concurrency.Immutable`1>,只需使用其构建器:

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="create_immutable":::

要获取不可更改的包装内值, 请使用 `.Value` 属性:

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="read_immutable":::

<a id="immutability-in-orleans"></a>

## Orleans中的不易变性

对于 Orleans目的,不可变性是一个严格的说明:数据项的内容不会以任何方式修改,从而改变其语义含义或干扰同时访问它的另一行。最安全的方法就是确保这根本不修改项目:使用不易变的比特逻辑而非逻辑不更动。

在某些情况下,将这种变化放松到逻辑的不易变性是安全的,但你必须小心确保突变代码适当安全。 因为多读化处理在 Orleans 背景下复杂且罕见,我们强烈建议反对这种做法,并建议坚持比特不易变。
