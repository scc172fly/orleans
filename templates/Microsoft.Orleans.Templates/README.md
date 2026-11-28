<a id="microsoft-orleans-project-templates"></a>

# Microsoft Orleans 项目模板

此包提供常见 Orleans 应用布局的模板。

安装模板：

```dotnetcli
dotnet new install Microsoft.Orleans.Templates
```

创建 Aspire 解决方案，其中包含独立的 Grain 契约、Grain 实现、Silo、外部客户端及 AppHost 项目：

```dotnetcli
dotnet new orleans --name MyOrleansApp
```

创建共同托管 Orleans Silo 并通过 HTTP 终结点公开 Grain 的 ASP.NET Core 应用：

```dotnetcli
dotnet new orleans-web --name MyOrleansWebApp
```

两个模板均默认面向 .NET 10。传入 `--framework net11.0` 以选择 .NET 11：

```dotnetcli
dotnet new orleans --name MyOrleansApp --framework net11.0
```

模板引用随模板包发布的 Orleans 版本，包括预发布版本。传入 `--orleans-version` 以选择其他 Orleans 版本：

```dotnetcli
dotnet new orleans-web --name MyOrleansWebApp --orleans-version 10.2.2
```

生成的项目包含 `.github/instructions/orleans.instructions.md`，该文件引导 GitHub Copilot 使用当前 Orleans 文档及 API 参考。

`orleans` 模板使用 Aspire Orleans 集成来编排 Silo、客户端，以及由 Azurite 支持的 Azure Table 集群存储和 Azure Blob Grain 存储。运行生成的 AppHost 时，请确保容器运行时可用，使 Aspire 能够启动 Azurite。`orleans-web` 模板使用 localhost 集群配置，以提供单节点本地开发主机。有关生产配置的指导，请参阅 [Orleans 托管文档](https://dotnet.github.io/orleans/docs/host/configuration-guide/)。
