<a id="orleans-samples-guidance"></a>

# Orleans 样品指导

这些规则依次适用于 `samples/`下的一切。 存储处一级的指导意见也适用于所有事项。

<a id="orchestration"></a>

## 管弦

- 使用 [指定](https://aspire.dev) 应用程序主机新建或重新修改的多进程
  以当地资源作为模型依赖性
  模拟器或容器;使用 `aspire run` 而不是人工设置脚本。
- 电线 Orleans 中 `Aspire.Hosting.Orleans`: `builder.AddOrleans("default")`,然后 `WithClustering(...)`, `WithGrainStorage(...)`,等等,并提供服务项目a `WithReference(orleans)`。见 `JournaledTodoList` 和 `JournalingAzureBlobJson` 单次服务者。
- 命名应用程序主机项目 `<Sample>.AppHost`,设置 `IsAspireHost`,并引用 `Aspire.AppHost.Sdk`。当样本有不止一个服务时添加一个 `<Sample>.ServiceDefaults` 项目,并从每个服务处调用 `AddServiceDefaults()` 。
- 启动时使用 `WaitFor(...)` 资源来满足服务需求,在浏览器中打开用户端点使用 `WithExternalHttpEndpoints()` 。
- 自足样品,如单处理 `UseLocalhostClustering()` 控制台
  可在没有应用程序主机的情况下运行。
- 现有样本显示具体的部署目标,例如 `Deployment/`下的样本,保留了目标所需的基础设施资产。

<a id="projects"></a>

## 项目项目项目

- 每个抽样项目的目标 `net10.0` ,都是不可包装的。
- 保持每个样本都可复制和独立建构。 Orleans 中
  `Microsoft.Orleans.*` NuGet 包;项目参考书留在样本中。
- `Build-Samples.ps1` 包装 `Orleans.slnx` ,有独一的预释放版本并建造
  `Samples.slnx` 与这些包裹相抵。
- 每个样本都拥有一个 `Directory.Packages.props` ,每个依赖性都有版本。
  根样本文件声明没有; NuGet 只读取最近的文件。共享
  样本,如 `Streaming/Common` ,使用其共享母体作为副本出机单元。
- `PackageReference` 元素不得携带 `Version` 或 `VersionOverride`。在所有样本中保持一个套件固定在同一个版本上,使主机和组件版本保持一致,并明确自 `CentralPackageTransitivePinningEnabled` 开始使用以来就存在脆弱的过境依赖性。
- 之后 `.editorconfig`;保留专题教学守则和评论,着重
  正在演示Orleans 概念。

<a id="registration-and-documentation"></a>

## 登记和文件

- `samples/Samples.slnx` 必须包含在 `samples/`下的项目, 并按样本分组到文件夹中。 添加每个新项目, 包括应用程序主机和服务默认值 。
- 在现有的条目和其中的图案之后,将新样本添加到 `gallery.json`
  `Validate-Samples.ps1`. 标记的 `aspire`标注式标本样品。
- `samples/README.md` 运行 `pwsh ./samples/Update-Readme.ps1` 变化后 `gallery.json`;从不手工编辑它。
- 给每个样本都提供 `README.md` ,解释其展示的内容和如何操作。进口样品保留了现有的前端物品及其原始来源存放处和许可证。

<a id="validation"></a>

## 审定

- 运行 `pwsh ./samples/Validate-Samples.ps1` 以查看在 `samples/`下的任何更改。 它检查画廊清单、 README 新鲜度、 解决方案成员资格、 软件包版本声明、 样本自我封存, 并引用 `Build-Samples.ps1` 来根据本地包装的 Orleans 软件包构建所有样品。
- 用于只对指令进行编辑,请使用 `-NoBuild`。
- 验证新的或改变的样本仍然能独立地构建:在存储库之外复制文件夹并运行 `dotnet build`。使用一个 Orleans 软件包的样本,该软件包尚未发布,但在包装船之前是免责的;树内解决方案仍然从本地的软件包源中构建。
- 建模样本不得要求云层证书,只可运行样本。
- 在承诺前检查 `git diff --check` 。
