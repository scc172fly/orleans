<a id="microsoft-orleans-serialization-for-newtonsoftjson"></a>

# Microsoft Orleans Newtonsoft.Json 序列化

<a id="introduction"></a>

## 简介
Microsoft Orleans Newtonsoft.Json 序列化使用广泛应用的 Newtonsoft.Json 库为 Microsoft Orleans 提供 JSON 序列化支持，使 Orleans 应用程序能够利用 Newtonsoft.Json 丰富的 JSON 功能。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization.NewtonsoftJson
```

<a id="example---configuring-newtonsoftjson-serialization"></a>

## 示例 - 配置 Newtonsoft.Json 序列化
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
            // Configure Newtonsoft.Json as a serializer
            .AddSerializer(serializerBuilder => serializerBuilder.AddNewtonsoftJsonSerializer(type => type.Namespace.StartsWith("ExampleGrains")));
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-newtonsoftjson-with-a-custom-type"></a>

## 示例 - 将 Newtonsoft.Json 用于自定义类型
```csharp
using Orleans;
using Newtonsoft.Json;
namespace ExampleGrains;

// Define a class with Newtonsoft.Json attributes
public class MyJsonClass
{
    [JsonProperty("full_name")]
    public string Name { get; set; }
    
    [JsonProperty("age")]
    public int Age { get; set; }
    
    [JsonProperty("tags")]
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
- [Newtonsoft.Json 文档](https://www.newtonsoft.com/json/help/html/Introduction.htm)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)