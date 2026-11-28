<a id="orleans-documentation-site"></a>

# Orleans 文档站点

此 Astro + Starlight 站点托管 Orleans 文档。概念性 Markdown 源文件位于 `src/content/docs` 下；
`npm run prepare:docs` 在 `/docs/` 下生成被 Git 忽略的对应 `.mdx` 文件，以便 Starlight 渲染导入的 Microsoft Learn 语法。

使用 Node.js 24 或更高版本，以及由仓库的 `global.json` 选定的 .NET SDK。

```powershell
npm install
npm run api:generate
npm run dev
```

API 生成过程会构建公开发布的 Orleans 包，可能需要数分钟。生成的包 JSON 文件被 Git 忽略，不应提交。
如果本地预览不需要 API 参考页，可以跳过此步骤。

开发服务器不包含 Pagefind 搜索功能，因为 Astro 按需渲染页面。若要预览准确的静态构建产物（包括生产环境的搜索索引），请运行：

```powershell
npm run build
npm run preview
```

生产构建发布 `/llms.txt`、`/llms-small.txt` 和 `/llms-full.txt`。每个文档页面还提供一个经过渲染的 Markdown 配套页面，
其 URL 为原页面 URL 加 `.md` 后缀。这些配套页面与 Starlight 使用同一批预处理后的内容集合条目，
因此已处理包含文件、代码指令、xref 以及 DocFX 转换。

运行专项转换测试、Astro 类型检查、严格的代码片段展开、源文件质量及综合项目策略审计、Starlight 链接验证、Pagefind 索引生成和生产构建：

```powershell
npm run validate
```

为避免 [#10672](https://github.com/dotnet/orleans/issues/10672) 跟踪的并发预渲染输出竞争问题，Astro 在 Windows 上默认每次生成一个路由。
其他平台同时生成四个路由。将 `ORLEANS_DOCS_BUILD_CONCURRENCY` 设置为正整数，可选择其他工作进程数量。
启用并发生成的 Windows 用户应使用 Node.js 24.20 或更高版本，其中包含针对第二个 libuv 关闭阶段断言问题的上游修复。

源文件审计会报告规则 ID、文件、行号和修复建议，并强制执行以下要求：

- 除迁移页面和明确标注版本的兼容性区域外，提供 Orleans 10 的指导内容。
- 每个概念性页面在 `toc.yml` 中恰有一个条目；排除 `includes`、代码片段支持文件及标记为 `navigation: hidden` 的兼容性路由；架构与内部实现、事件溯源仍作为独立的导航章节。
- 对照源项目元数据核查包与流提供程序清单，对照源码/API 常量核查活动源及生命周期阶段，对照 `InstrumentNames.cs` 核查文档中的指标名称，对照 `samples/gallery.json` 核查示例路径。
- 通过 `:::code` 指令引用命名区域中的、经编译器验证的 C# 示例。内联 C# 代码围栏无法通过源文件审计；请将不完整的示例放在共享源文件中，并在显示区域之外补充隐藏的辅助代码。
- 基于源文件及渲染结果的链接验证。迁移自 Microsoft Learn 的相对链接必须使用规范的 `https://learn.microsoft.com/...` URL；渲染后的路由、编码路径、重定向和锚点必须能在 `/orleans/` 下解析。源文件诊断包含文件及行号来源。
- 生成的本仓库链接依据本地检出内容进行验证。其余外部 HTTP(S) 验证采用有界并发、重定向、超时、重试以及按主机选择的 HEAD 到 GET 回退。确认失效的目标会导致验证失败。明确报告速率限制及暂时性网络故障，同时避免使 CI 产生不确定结果。拉取请求内容不可信：探测器拒绝凭据、非默认端口以及任何能访问私有网络、环回网络、链路本地网络、元数据服务或其他非公共网络的主机名/IP。在每次重定向时都会验证 DNS 响应，并将其绑定到实际 TLS 连接，以防贡献者利用重定向、DNS 重绑定或代理环境变量将链接验证变成内部网络请求。

`npm run build` 和 `npm run validate` 会发现 `docs/` 与 `samples/` 下的全部项目，要求它们与 `docs/Docs.slnx` 和 `samples/Samples.slnx` 的成员完全一致，
求值后必须恰好以 `net10.0` 为目标，并要求每个 Orleans 包引用解析为 `10.2.2`。
如果项目所介绍的包仅支持 `net11.0`，项目必须恰好以 `net11.0` 为目标，并在含有明确理由的 `OrleansDocumentationTargetFrameworkException` 属性中说明原因。
只有在迁移项目中实际使用了含有明确理由的 `OrleansDocumentationVersionException` 属性时，才允许历史包版本。
尚待发布的示例可以通过同一属性注明具体原因，并使用 `10.2.2` 的预发布版本。若要在本地构建和测试文档项目，请运行：

```powershell
dotnet build ../Docs.slnx --configuration Release
dotnet test --solution ../Docs.slnx --configuration Release --framework net10.0 --no-build --minimum-expected-tests 1 --max-parallel-test-modules 1
```

通过 `samples/Build-Samples.ps1` 构建示例；该脚本先将当前 Orleans 源码打包到本地包源，再构建 `samples/Samples.slnx`。

若只需按顺序编译已纳入版本控制的文档代码片段项目，请运行：

```powershell
pwsh src/content/docs/validate-snippets.ps1
```

修复 `DOCS001` 时，应使当前发行版文档不绑定具体版本，将特定版本的指导移至 `migration/` 或升级页面，或者链接到这些页面。
在 `toc.yml` 中修复 `DOCS002`/`DOCS003`。对于 `DOCS004`，将内联 C# 示例移入参与编译的代码片段项目中的命名区域，并在区域外添加隐藏上下文。
对于 `DOCS005`，将编写的参考内容与指定源文件清单同步。
每个可打包的 Orleans 源码包都必须具有文档，或在 `src/data/package-inventory-exclusions.json` 中添加附有理由的条目。
通过在 `docs/Docs.slnx` 中增删文档项目、在 `samples/Samples.slnx` 中增删示例项目来修复 `PROJECT001`-`PROJECT003`，直到与文件系统发现的项目完全匹配。
修复 `PROJECT004` 时，仅以 `net10.0` 为目标，或者仅以 `net11.0` 为目标并使用含有明确理由的 `OrleansDocumentationTargetFrameworkException` 属性；
修复 `PROJECT005` 时，将实际使用的 Orleans 包版本更新至 `10.2.2`。
`PROJECT006` 表示无效、含糊或过时的版本例外。重新构建前，请在报告所指的代码片段项目中修复 `SNIPPET001`/`SNIPPET002`。
添加项目时，传入 `--include-references false`，并使用与项目在 `docs/` 或 `samples/` 下的路径一致的解决方案文件夹；
仓库源项目依赖项会通过传递性依赖进行构建，无须成为 `Docs.slnx` 的直接条目。
使用修复建议中的规范 URL 修复 `LINK001`。渲染链接的验证失败时，如果可映射到原始链接，则报告源文件及行号，否则报告渲染后的路由。
`src/data/external-link-allowlist.json` 只接受精确的 URL；每个条目记录范围明确的探测可靠性原因，并继续受到主动探测。
审计会报告各响应的状态与原因，包括偶尔可访问目标的成功响应。确认目标能够可靠接受有界自动探测后，移除对应条目。
未被引用的条目会因过时而导致验证失败；每个条目和重定向都必须通过目标地址安全检查。
对于已生成 API 但尚待首次 NuGet 发布的包，还需在 `src/data/unpublished-api-packages.json` 中添加临时条目；发布后删除这两个条目。

选项说明仍是有意筛选的简要清单，而不是自动生成的目录；审计要求该页面明确指出，生成的 API 参考才是完整信息的来源。
指标正文也采用类似的选择性说明，但其提到的每个指标标识符都必须存在于运行时源码中。

示例页面从仓库根目录读取 `samples/gallery.json`。当示例清单缺失时，开发构建会渲染带说明的空状态。

<a id="native-api-reference"></a>

## 原生 API 参考

`/docs/api/csharp/` 路由树使用与 [`microsoft/aspire.dev`](https://github.com/microsoft/aspire.dev) 相同的原生渲染架构：
Astro 的 `packages` 内容集合读取 `src/data/pkgs/*.json`，Starlight 页面渲染包、类型、成员种类以及各成员的路由，并提供对应的 `.md` 配套页面。
预期的 JSON 结构来自 Aspire 基于 Roslyn/XML/PDB 的 `PackageJsonGenerator` 的输出，并由 Orleans API 生成器开发工作进行适配。

预览 API 页面或执行完整的本地构建前，先生成 API 数据：

```powershell
npm run api:generate
```

GitHub Actions 按需生成 API 数据，针对各项目的目标框架构建 `docs/Docs.slnx` 并生成诊断二进制日志，运行其中的测试项目，构建完整站点，并从 `main` 将 `dist` 部署到 GitHub Pages。
拉取请求可以获得可下载的站点构建产物，但绝不会获得部署权限。

首次部署前，将 **Settings → Pages → Source** 设置为 **GitHub Actions**。工作流会在推送到 `main` 后、每天 UTC 09:00 以及手动触发时发布站点。

生产构建还为两个旧版 `gh-pages` 站点地图生成兼容性重定向。
`src/data/legacy-jekyll-pages.json` 将 DocFX 之前的每个文档页面映射到当前最合适的替代页面。
GitHub Pages 也会通过历史上不带扩展名的别名提供生成的 `.html` 重定向。
`src/data/legacy-pages.json` 记录 DocFX 时代最后的站点。新站点仍提供的页面保留原有 URL；已退役的文档和博客 URL 则重定向到当前最接近的文档入口。
`src/data/redirects.json` 中显式指定的替代地址会保留传入链接的锚点，并覆盖自动的 DocFX 时代路径匹配。

`npm run audit:links` 验证源文档中编写的外部链接和每个渲染后的内部路由与锚点。
`npm run build` 和 `npm run validate` 都会自动运行此命令。

`npm run audit:output` 扫描整个渲染后站点，检查重复或缺失的页面标题、意外泄露的 Microsoft Learn 指令、格式错误的 API 签名及运算符名称、过大的导航、缺失的旧版重定向，以及违反 GitHub Pages 大小限制的回归问题。
