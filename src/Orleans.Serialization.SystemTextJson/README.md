<a id="microsoft-orleans-serialization-for-systemtextjson"></a>

# Microsoft Orleans System.Text.Json 序列化

<a id="introduction"></a>

## 简介
Microsoft Orleans System.Text.Json 序列化使用现代的 System.Text.Json 库为 Microsoft Orleans 提供 JSON 序列化支持，使 Orleans 应用程序能够利用 .NET 运行时的高性能 JSON 序列化功能。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization.SystemTextJson
```

<a id="example---configuring-systemtextjson-serialization"></a>

## 示例 - 配置 System.Text.Json 序列化
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Serialization;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure System.Text.Json as a serializer
            .AddSerializer(serializerBuilder => serializerBuilder.AddSystemTextJsonSerializer(type => type.Namespace.StartsWith("ExampleGrains")));
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-systemtextjson-with-a-custom-type"></a>

## 示例 - 将 System.Text.Json 用于自定义类型
```csharp
using Orleans;
using System.Text.Json.Serialization;
namespace ExampleGrains;

// Define a class with System.Text.Json attributes
public class MyJsonClass
{
    [JsonPropertyName("full_name")]
    public string Name { get; set; }
    
    [JsonPropertyName("age")]
    public int Age { get; set; }
    
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; }
    
    [JsonIgnore]
    public string SecretData { get; set; }
}

// You can use it directly in your grain interfaces and implementation
public interface IMyGrain : IGrainWithStringKey
{
    Task<MyJsonClass> GetData();
    Task SetData(MyJsonClass data);
}

public class MyGrain : Grain, IMyGrain
{
    private MyJsonClass _data;

    public Task<MyJsonClass> GetData()
    {
        return Task.FromResult(_data);
    }

    public Task SetData(MyJsonClass data)
    {
        _data = data;
        return Task.CompletedTask;
    }
}
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [System.Text.Json 文档](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/overview)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)