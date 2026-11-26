# OrleansWebApp

本 ASP.NET 核心应用程序共同主办一个 Orleans silo。HTTP终点通过依赖注射解决 `IGrainFactory` ,并在同一组中呼叫 grain 。

运行应用程序 :

```dotnetcli
dotnet run
```

调用生成的端点 :

```console
curl http://localhost:5000/hello/Ada
```

终点返回 `Hello, Ada!`。

生成的应用程序使用本地主机群集促进本地发展。 在部署多个实例之前配置共享集群提供商、耐久存储和生产端点。
