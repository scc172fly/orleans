<a id="microsoft-orleans-runtime"></a>

# Microsoft Orleans 运行时间

<a id="introduction"></a>

## 简介
Microsoft Orleans 运行时间是 Orleans的核心服务器侧部。它主机和执行 grains,管理 grain 生命周期,并为一个功能化的 Orleans 服务器提供所有运行时所需的服务(silo)。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Runtime
```

当您引用 Orleans 服务器元包时, 此软件包会自动包含 。

<a id="grain-construction"></a>

## Grain 建筑工程

生成的 grain 登记保存了用于启动剪裁和 NativeAOT 应用程序的公共构建元数据。默认 grain 激活器使用依赖性注射来选择公共构建人、荣誉 `ActivatorUtilitiesConstructorAttribute`,并解决每个启动服务范围的依赖关系。手册 grain 配置使用 `GrainTypeOptions.AddClass(typeof(MyGrain))`;手动标记配置使用 `TypeManifestOptions.AddInterfaceImplementation(typeof(MyGrain))`。两者都保留相同的构建元数据。直接 `Classes` 收集访问和从任意字典中建造一个 `GrainClassMap` 要求呼叫器单独保存建构人并发出三联警告。

<a id="file-grain-storage"></a>

## 文件 grain 存储

运行时包括一个基于文件的 grain 存储供应商,用于本地、单个silo 开发和测试。它每个 grain 国家记录存有一个二进制文件,并使用长期存在的ETags来进行乐观的同价计算。

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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [服务器配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/server-configuration/)
- [Silo 生命周期](https://dotnet.github.io/orleans/docs/host/silo-lifecycle/)
- [集群组合](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证