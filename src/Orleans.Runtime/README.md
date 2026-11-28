<a id="microsoft-orleans-runtime"></a>

# Microsoft Orleans 运行时

<a id="introduction"></a>

## 简介
Microsoft Orleans Runtime 是 Orleans 的核心服务器端组件。它托管并执行 Grain，管理 Grain 生命周期，并为正常运行的 Orleans 服务器（Silo）提供所有必要的运行时服务。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Runtime
```

引用 Orleans Server 元包时，会自动包含此包。

<a id="grain-construction"></a>

## Grain 构造

生成的 Grain 注册保留公共构造函数元数据，以支持裁剪及 NativeAOT 应用程序中的激活。默认 Grain 激活器通过依赖注入选择公共构造函数，遵循 `ActivatorUtilitiesConstructorAttribute`，并从每个激活实例的服务作用域解析依赖项。手动 Grain 配置使用 `GrainTypeOptions.AddClass(typeof(MyGrain))`；手动清单配置使用 `TypeManifestOptions.AddInterfaceImplementation(typeof(MyGrain))`。二者均保留相同的构造函数元数据。直接访问 `Classes` 集合以及根据任意字典构造 `GrainClassMap` 时，调用方需要单独保留构造函数；这两种操作会产生裁剪警告。

<a id="file-grain-storage"></a>

## 文件 Grain 存储

运行时包含基于文件的 Grain 存储提供程序，用于本地单 Silo 开发与测试。它为每条 Grain 状态记录存储一个二进制文件，并使用持久化 ETag 实现乐观并发控制。

```csharp
using Orleans.Persistence.FileStorage;

siloBuilder.AddFileGrainStorage(
    options: options => options.RootDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Orleans",
        "GrainState"));
```

<a id="example---configuring-a-silo"></a>

## 示例 - 配置 Silo

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering();
    });

await builder.Build().RunAsync();
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [服务器配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/server-configuration/)
- [Silo 生命周期](https://dotnet.github.io/orleans/docs/host/silo-lifecycle/)
- [集群](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)