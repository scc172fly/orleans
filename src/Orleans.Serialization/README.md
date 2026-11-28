<a id="microsoft-orleans-serialization"></a>

# Microsoft Orleans 序列化

<a id="introduction"></a>

## 简介
Microsoft Orleans Serialization 是面向 .NET 的快速、灵活且支持版本容错的序列化器。它提供 Orleans 的核心序列化能力，支持在网络传输及存储场景中高效序列化与反序列化数据。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization
```

引用 Orleans SDK 或 Orleans 客户端/服务器元包时，会自动包含此包。

此包包含 Orleans 源代码生成器及分析器。标记了
`[GenerateSerializer]` 的类型的编解码器与复制器将在编译期间生成。

<a id="example"></a>

## 示例

```csharp
// Creating a serializer
var services = new ServiceCollection();
services.AddSerializer();
var serviceProvider = services.BuildServiceProvider();
var serializer = serviceProvider.GetRequiredService<Serializer>();

// Serializing an object
var bytes = serializer.SerializeToArray(myObject);

// Deserializing an object
var deserializedObject = serializer.Deserialize<MyType>(bytes);
```

<a id="supporting-your-own-types"></a>

## 支持自定义类型

要使类型可在 Orleans 中序列化，请用 `[GenerateSerializer]` 特性标记类型，并用 `[Id(int)]` 特性标记每个需要序列化的字段或属性：

```csharp
[GenerateSerializer]
public class MyClass
{
    [Id(0)]
    public string Name { get; set; }
    
    [Id(1)]
    public int Value { get; set; }
}
```

启用默认拒绝的类型验证（fail-closed）时，可以通过配置 `TypeManifestOptions.AddAllowedType` 或 `TypeManifestOptions.AddAllowedAssembly` 来允许其他类型。

为 C# 有效载荷生成的序列化器和复制器在受支持的目标框架上使用返回引用的
`UnsafeAccessor` 方法读取并恢复私有字段、只读字段
以及自动属性的后备字段。非泛型 C# 有效载荷
在 .NET 8 及更高版本中采用这种访问方式；泛型 C# 有效载荷则在 .NET 9 及
更高版本中采用这种方式，并保留有效载荷的泛型约束。结构体接收者
以引用方式传递。这使得针对 C# 有效载荷生成并经 NativeAOT 编译的代码
能够恢复仅包含 getter 的属性及仅允许初始化的属性，并深拷贝
存储在只读字段中的值。

在受支持的目标框架上，生成的 C# 编解码器及复制器会为每个字段生成一个返回引用的访问器，
同时用于读取与写入。

生成器根据 SDK 的目标框架标识符
及版本选择这些功能，并验证编译引用中是否包含
`UnsafeAccessorAttribute`。

对于 C# 有效载荷，旧版目标框架以及 .NET 8 上的泛型有效载荷使用生成的
字段访问委托。设置 `OrleansHotReload=true` 会保留延迟初始化的
字段访问委托，使已有的序列化器及复制器实例
能够访问通过热重载添加的成员。

volatile 字段在 .NET 10 及更高版本中使用返回引用的访问器。较早的
目标框架通过生成的委托恢复私有volatile 字段，从而保持与
受volatile 字段签名匹配问题影响的运行时兼容。

F# 记录类型和联合类型的字段恢复在启用 JIT 的运行时上
继续使用现有的生成委托策略。

NativeAOT 应用程序还需要为其封闭的有效载荷类型提供静态可用的编解码器、复制器
及激活器。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 中的序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [配置序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization-configuration/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)