<a id="microsoft-orleans-project-templates"></a>

# Microsoft Orleans 项目模板

此软件包为通用 Orleans 应用程序布局提供模板。

安装模板 :

```dotnetcli
dotnet new install Microsoft.Orleans.Templates
```

创建独立使用的星号解 grain 合同, grain 执行, silo外部客户, AppHost 项目:

```dotnetcli
dotnet new orleans --name MyOrleansApp
```

创建一个 ASP.NET 核心应用程序,该程序将共同主办一个 Orleans silo ,并通过 HTTP 端点显示一个 grain :

```dotnetcli
dotnet new orleans-web --name MyOrleansWebApp
```

两个模板都默认地针对 .NET 10。通过 `--framework net11.0` 选择 .NET 11:

```dotnetcli
dotnet new orleans --name MyOrleansApp --framework net11.0
```

模板引用 Orleans 带有模板包的版本,包括预发版本。 `--orleans-version` 选择其它 Orleans 版本 :

```dotnetcli
dotnet new orleans-web --name MyOrleansWebApp --orleans-version 10.2.2
```

生成的项目包括 `.github/instructions/orleans.instructions.md`,它引导 GitHub 共同试办当前 Orleans 文档和 API 参考。

`orleans` 模板使用 Aspire Orleans 集成来为 silo、客户和 Azurite 支持的 Azure 表群组合和 Azure Blob grain 存储进行交配。运行生成的 AppHost 时,可有一个容器运行时间,以便 Aspire 可以启动 Azurite。 `orleans-web` 模板使用本地主机组合一个单点本地开发主机。请参见 [Orleans 文件主机](https://dotnet.github.io/orleans/docs/host/configuration-guide/) 的生产配置指导 。
