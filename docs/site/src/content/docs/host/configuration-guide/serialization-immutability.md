---
title: Orleans 中不可变类型的序列化
description: 了解 .NET Orleans 在序列化过程中如何处理类型的不可变性。
ms.date: 05/23/2025
ms.topic: concept-article
---

<a id="serialization-of-immutable-types-in-orleans"></a>

# Orleans 中不可变类型的序列化

Orleans 提供一项功能，可避免序列化包含不可变类型的消息时产生的一部分开销。本节从适用的场景开始，介绍这一功能及其使用方式。

<a id="serialization-in-orleans"></a>

## Orleans 中的序列化

调用 Grain 方法时，Orleans 运行时会对方法实参进行深拷贝，并使用这些副本构造请求。这可避免调用方代码在数据传递给被调用 Grain 之前修改实参对象而影响请求数据。

如果被调用的 Grain 位于另一个 Silo，副本最终会被序列化为字节流，并通过网络发送到目标 Silo，在目标 Silo 上反序列化为对象。如果被调用 Grain 位于同一个 Silo，则直接将副本传给被调用方法。

返回值以相同的方式处理：先复制，再视情况进行序列化和反序列化。

注意，复制、序列化及反序列化这三个过程都保留对象标识。也就是说，如果传递的列表两次包含同一个对象，接收方得到的列表也两次包含同一个对象，而不是包含两个值相同的对象。

<a id="optimize-copying"></a>

## 优化复制操作

许多情况下不需要深拷贝。例如，某 Web 前端从客户端收到字节数组，并将包含该数组的请求传给 Grain 处理。前端进程在将数组传给 Grain 之后不再使用该数组，特别是不会将其复用于后续请求。在 Grain 中，解析字节数组以获取输入数据，但不修改它。Grain 将自己创建的另一个字节数组返回给 Web 客户端，并在返回后立即放弃对该数组的使用。Web 前端也不修改结果字节数组，直接将其传回客户端。

这种情况下，请求和响应的字节数组都不需要复制。遗憾的是，Orleans 运行时无法自动确定这一点，因为它无法判断 Web 前端或 Grain 之后是否会修改这些数组。理想情况下，应有一种 .NET 机制可以指示某个值不再被修改。在缺少这种机制的情况下，我们添加了 Orleans 专用的机制：<xref:Orleans.Concurrency.Immutable`1> 包装类以及 <xref:Orleans.ImmutableAttribute>。

<a id="use-the--attribute-to-mark-a-type-parameter-property-or-field-as-immutable"></a>

### 使用 <xref:Orleans.ImmutableAttribute> 特性将类型、参数、属性或字段标记为不可变

对于用户定义的类型，可以向类型添加 <xref:Orleans.ImmutableAttribute>。这会指示 Orleans 序列化器避免复制该类型的实例。以下代码片段演示使用 <xref:Orleans.ImmutableAttribute> 表示不可变类型。该类型在传输期间不会被复制。

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_type":::

有时你无法控制对象本身，例如在 Grain 之间发送 `List<int>`；有时对象的一些部分不可变，而其他部分可变。针对这些情形，Orleans 支持其他选项。

1. 方法签名可以在单个参数上应用 <xref:Orleans.ImmutableAttribute>：

    :::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_parameter":::

1. 将单个属性和字段标记为 <xref:Orleans.ImmutableAttribute>，以便在复制其所属类型的实例时跳过这些成员的复制。

    :::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_members":::

<a id="use-"></a>

### 使用 <xref:Orleans.Concurrency.Immutable`1>

使用 <xref:Orleans.Concurrency.Immutable`1> 包装类表示某个值可被视为不可变，即底层值不会被修改，因此无需复制即可安全共享。注意，使用 <xref:Orleans.Concurrency.Immutable`1> 意味着值的提供方和接收方都不会在将来修改该值。这是双方共同作出的承诺，而不是单方面的承诺。

若要在 Grain 接口中使用 <xref:Orleans.Concurrency.Immutable`1>，请传递 <xref:Orleans.Concurrency.Immutable`1> 而非 `T`。例如，在上文介绍的场景中，原来的 Grain 方法为：

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="mutable_request":::

随后改为：

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="immutable_request":::

若要创建 <xref:Orleans.Concurrency.Immutable`1>，只需使用其构造函数：

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="create_immutable":::

若要获取不可变包装对象中的值，请使用 `.Value` 属性：

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="read_immutable":::

<a id="immutability-in-orleans"></a>

## Orleans 中的不可变性

对于 Orleans 而言，不可变性是一项严格的承诺：数据项的内容不得以任何可能改变其语义或干扰另一线程同时访问的方式进行修改。保证这一点最安全的方法就是完全不修改该数据项，即使用逐位不可变性，而不是逻辑不可变性。

在某些情况下，可以安全地放宽为逻辑不可变性，但必须注意确保执行修改的代码具备正确的线程安全性。由于多线程处理较复杂，而且在 Orleans 场景中并不常见，我们强烈不建议采用这种方式，建议坚持使用逐位不可变性。
