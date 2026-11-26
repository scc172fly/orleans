# OrleansApp

此解决方案将应用程序分为五个项目:

- `OrleansApp.AppHost` 调控应用程序及其 Azurite 资源。
- `OrleansApp.Contracts` 定义了调用者和执行者共享的 grain 接口。
- `OrleansApp.Grains` 执行这些 grain 接口。
- `OrleansApp.Silo` 将启动 Orleans 运行时间和 grain 激活。
- `OrleansApp.Client` 连接到 silo ,并呼叫 grain。

构建解决方案 :

```dotnetcli
dotnet build
```

启动应用程序 :

```dotnetcli
dotnet run --project OrleansApp.AppHost
```

AppHost 启动 Azurite、 silo和外部客户端。打开“ 占卜仪表板”,使用终端中打印的 URL 来检查资源及其日志 。

客户端指纹 `Hello, friend! Call count: 1.`。 grain 将其调用计数储存在 Azure 布洛布存储处, silo 和客户端通过 Azure 表存储处发现该组群。 AppHost 通过Azurite提供两种服务,用于当地发展。

容器运行时间必须运行, 以便 Aspire 可以启动 Azurite。 当应用程序发布时, 请为目标环境配置 Azure 存储资源 。
