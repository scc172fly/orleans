<a id="orleans-transport-layer-security-snippets"></a>

# Orleans 传输层安全代码片段

此文件夹包含经过编译检查的代码片段项目，供
`docs/site/src/content/docs/host/transport-layer-security.md` 使用。

<a id="build-the-snippets"></a>

## 构建代码片段

对每个项目运行 `dotnet build`：

- `csharp/SiloExample/SiloExample.csproj`
- `csharp/ClientExample/ClientExample.csproj`

入口点有意不启动 Silo 或客户端。文章中的示例
需要特定于部署环境的证书、名称及信任存储，且其设计可以在
不嵌入仅供开发使用的证书验证绕过逻辑的情况下通过编译。

有关可在本地运行且会创建开发证书的 mTLS 示例，请参阅
[Orleans 传输层安全示例](https://learn.microsoft.com/samples/dotnet/samples/orleans-transport-layer-security-tls/)。
