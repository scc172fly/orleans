<a id="microsoft-orleans-serialization-abstractions"></a>

# Microsoft Orleans 序列化抽象库

<a id="introduction"></a>

## 简介
Orleans Serialization Abstractions 包提供 Orleans 序列化所需的核心接口与特性。此包包含用于序列化的定义，但不包含序列化实现本身。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization.Abstractions
```

引用 Orleans Serialization 包或 Orleans SDK 时，会自动包含此包。

<a id="example"></a>

## 示例

```csharp
using Orleans.Serialization;

// Define a serializable class
[GenerateSerializer]
public class MyData
{
    [Id(0)]
    public string Name { get; set; }
    
    [Id(1)]
    public int Age { get; set; }
    
    [Id(2)]
    public List<string> Tags { get; set; }
}
```

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