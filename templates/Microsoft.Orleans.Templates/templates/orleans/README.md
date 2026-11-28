# OrleansApp

此解决方案将应用程序划分为五个项目：

- `OrleansApp.AppHost` 编排应用程序及其 Azurite 资源。
- `OrleansApp.Contracts` 定义调用方与实现共享的 Grain 接口。
- `OrleansApp.Grains` 实现这些 Grain 接口。
- `OrleansApp.Silo` 托管 Orleans 运行时及 Grain 激活实例。
- `OrleansApp.Client` 连接到 Silo 并调用 Grain。

构建解决方案：

```dotnetcli
dotnet build
```

启动应用程序：

```dotnetcli
dotnet run --project OrleansApp.AppHost
```

AppHost 启动 Azurite、Silo 及外部客户端。使用终端中输出的 URL 打开 Aspire Dashboard，查看资源及其日志。

客户端输出 `Hello, friend! Call count: 1.`。Grain 将调用次数存储在 Azure Blob Storage 中，Silo 和客户端均通过 Azure Table Storage 发现集群。AppHost 在本地开发环境中通过 Azurite 运行这两项服务。

容器运行时必须处于运行状态，以便 Aspire 启动 Azurite。发布应用程序时，请为目标环境配置 Azure Storage 资源。
