<a id="microsoft-orleans-serialization"></a>

# Microsoft Orleans 序号

<a id="introduction"></a>

## 简介
Microsoft Orleans 序列化是 .NET的快速、灵活和耐版本的序列器,提供 Orleans的核心序列化能力,使整个网络的数据能够高效地序列化和脱序并用于储存。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Serialization
```

当您引用 Orleans SDK 或 Orleans 客户端/服务器元包时,此软件包将被自动包含。

软件包包括 Orleans 源生成器和分析器。
编译过程中生成了标明 `[GenerateSerializer]` 的类型的复印机。

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

## 支持您自己的类型

要在 Orleans中使您的类型具有序列性,请用 `[GenerateSerializer]` 属性标记类型,并标记每个字段/财产,这些字段/财产应与 `[Id(int)]` 属性具有序列性:

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

当启用了不关闭类型验证时,可以通过配置 `TypeManifestOptions.AddAllowedType` 或 `TypeManifestOptions.AddAllowedAssembly`允许其他类型。

C# 有效载荷使用 Ref-回射生成的序列器和复印机
`UnsafeAccessor` 读取和恢复私有字段的方法,只读字段,
和在支持目标上支持的自动财产支持字段。
在 .NET 8 和以后使用这种访问;通用C#有效载荷在 .NET 9和
之后,有效载荷的一般限制保留下来。
引用后通过 。 这样允许 NativeAOT- C# 的编译代码
有效载荷,以恢复只获取和仅自入的特性以及深镜值
保存在只读字段中。

生成 C# 代码和复印机, 每个字段释放一个返回存取器
阅读和撰写关于支助目标的著作。

发电机从SDK的目标框架中选择这些能力
和标识符号、版本以及核实 `UnsafeAccessorAttribute` 是否可用
汇编参考资料中。

C# # 有效载荷、遗留目标 .NET 产生8个通用有效载荷的使用
设置 `OrleansHotReload=true` 时不固定
实地初始化代表,因此现有连发机和复印机
成员可访问, 加上热重载。

易挥发字段在 .NET 10号及以后使用回溯存取器。
目标:通过代表产生的、保护
与受挥发性实地签名匹配影响的运行时间兼容。

F# 记录和联合田地恢复使用现有生成的发射
联合调查队启动的运行时间战略。

NativeAOT 应用程序还需要静态可用的编码器、复印机和
用于封闭有效载荷类型的活性体。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans中的序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [配置序列序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization-configuration/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证