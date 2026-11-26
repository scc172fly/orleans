<a id="orleans-transport-layer-security-snippets"></a>

# Orleans 运输层安全片段

此文件夹包含用于
`docs/site/src/content/docs/host/transport-layer-security.md`.

<a id="build-the-snippets"></a>

## 构建片段

每个项目运行 `dotnet build` :

- `csharp/SiloExample/SiloExample.csproj`
- `csharp/ClientExample/ClientExample.csproj`

入境点故意不启动 silo 或客户。
需要具体部署部署的证明、姓名和信托商店,
设计成编译时不嵌入开发专用证书绕行。

创建开发证书的本地可运行 mTLS 示例,请参见
排 [Orleans 运输层安全抽样](https://learn.microsoft.com/samples/dotnet/samples/orleans-transport-layer-security-tls/).
