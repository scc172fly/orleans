# OrleansWebApp

此 ASP.NET Core 应用共同托管 Orleans Silo。HTTP 终结点从依赖注入容器解析 `IGrainFactory`，并调用同一集群中的 Grain。

运行应用程序：

```dotnetcli
dotnet run
```

调用生成的终结点：

```console
curl http://localhost:5000/hello/Ada
```

终结点返回 `Hello, Ada!`。

生成的应用使用 localhost 集群配置进行本地开发。在部署多个实例之前，请配置共享的集群提供程序、持久化存储及生产环境终结点。
