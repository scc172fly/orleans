<a id="custom-grain-call-return-type"></a>

# 自定义 Grain 调用返回类型

本示例定义 `GrainCall<T>`，这是由任务支持的可等待类型，可直接用于 Grain 接口。Orleans 为每个 Grain 方法生成一个派生自 `GrainCallRequest<T>` 的请求类型。

生成的代理会：

1. 创建请求并将方法参数复制到其中。
2. 调用 `GrainCallRequest<T>.InitializeRequest`。
3. 通过 `IGrainReferenceRuntime` 提交请求。
4. 向应用返回 `GrainCall<T>`。

在目标激活实例上，生成的请求会调用 Grain 实现。`GrainCallRequest<T>.Invoke` 等待返回的包装对象，并将其值或异常转换为 Orleans 响应。

`GrainCall<T>` 立即启动一次调用，通过其底层 `Task<T>` 支持多个等待者，并传播远程调用的最终结果。当契约需要协作式取消时，通过 Grain 方法的 `CancellationToken` 参数传递取消令牌。

<a id="run-the-sample"></a>

## 运行示例

示例面向 .NET 10，并使用本地主机集群，因此不需要外部服务。

```powershell
dotnet run --project CustomGrainCallReturnType.csproj
```

客户端输出成功结果，随后观察到 Grain 返回的异常。

有关注册规则及兼容性指南，请参阅[自定义 Orleans 序列化代码生成](../../docs/site/src/content/docs/grains/code-generation-customization.md)。有关生成的代理、请求、分派和响应路径，请参阅[序列化与代码生成内部机制](../../docs/site/src/content/docs/implementation/serialization.md)。
