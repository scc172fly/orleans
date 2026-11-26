<a id="custom-grain-call-return-type"></a>

# 自定义 grain- 调用返回类型

此样本定义 `GrainCall<T>`中直接用于 grain 接口。 Orleans 生成请求类型 `GrainCallRequest<T>` 每个 grain 方法。

生成的代理服务器 :

1. 创建请求并复制方法参数。
2. 呼吁 `GrainCallRequest<T>.InitializeRequest`。
3. 通过 `IGrainReferenceRuntime`提交请求。
4. 返回应用程序 `GrainCall<T>` 。

在目标激活时,生成的请求要求要求 grain 执行。 `GrainCallRequest<T>.Invoke` 等待返回的包装并转换其价值或例外为 Orleans 回应。

`GrainCall<T>` 立即开始一次请求,通过支持 `Task<T>`支持多个等待者,并传播远程终端结果。当合同需要合作取消时,作为 grain 方法 `CancellationToken` 的参数提供取消。

<a id="run-the-sample"></a>

## 运行样本

抽样目标 .NET 10,使用当地主机群集,因此不需要外部服务。

```powershell
dotnet run --project CustomGrainCallReturnType.csproj
```

客户打印成功的结果,然后观察 grain退回的例外情况。

关于登记规则和兼容性指南,见 [自定义 Orleans 序列代码生成](../../docs/site/src/content/docs/grains/code-generation-customization.md)。生成的代理、请求、发送和响应路径见 [内部的序列化和代码生成](../../docs/site/src/content/docs/implementation/serialization.md)。
