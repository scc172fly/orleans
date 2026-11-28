# PackageJsonGenerator

为各包生成确定性的 JSON，描述 Orleans 程序集中的公共 .NET API。其架构与 `microsoft/aspire.dev` Astro 站点使用的包 API 数据一致。

从仓库根目录使用 `docs/scripts/Generate-ApiData.ps1` 发现并构建可打包的 Orleans 项目，收集每个项目的完整传递引用集合，并生成完整的包数据集。

```powershell
pwsh ./docs/scripts/Generate-ApiData.ps1
```

默认输出目录为 `docs/site/src/data/pkgs`。传入 `-OutputDirectory` 可将输出生成到暂存目录，使用 `-TargetFramework` 选择其他受支持的 TFM，或在所有选定的包项目均已构建时使用 `-SkipBuild`。

通过求值后的 MSBuild 属性从 `src/**/*.csproj` 发现项目。同时具有 `IsPackable=true` 和 `IncludeBuildOutput=true` 的项目才会纳入，并根据选定框架的构建产物生成文档；仅面向 `net11.0` 的项目则使用其 `net11.0` 构建产物。这样无需维护包列表，即可排除测试项目、仅包含实现的项目、分析器、代码生成器及不含程序集的元包。

每个输出文件以 `{PackageId}.{PackageVersion}.json` 命名。仅输出该包程序集自身声明的公共类型和成员；项目、包和框架依赖项仅作为 Roslyn 的解析引用。可移植 PDB 路径依据经过验证的本地仓库目录树，以及所声明提交的受跟踪文件允许列表进行解析，从而避免依赖网络的输出及无效的生成或推断链接。脚本在删除由生成器管理的过时 JSON 文件之前，会验证 XML 文档、包元数据、源仓库及提交信息和重复类型标识。

API 筛选使用 `GeneratedCodeAttribute` 和 `CompilerGeneratedAttribute` 过滤实现类型及成员，包括嵌套类型与枚举成员。手工编写的分部类型在其生成成员单独标有这些特性时，仍保留自身的 API 条目。

默认情况下，源码链接指向最后修改 `src` 的提交。`src` 存在未提交的更改时，生成会失败。自动化流程可以传入 `-SourceCommit` 以明确指定完整的提交 SHA。

<a id="upstream-attribution"></a>

## 上游来源说明

本工具改编自提交 `9aa68083af47da79b63bc15b80f44c1927bf9c08` 中的 [`microsoft/aspire.dev` 的 `PackageJsonGenerator`](https://github.com/microsoft/aspire.dev/tree/9aa68083af47da79b63bc15b80f44c1927bf9c08/src/tools/PackageJsonGenerator)。

改编后的源码保留了上游 .NET Foundation 和 MIT 许可证文件头。针对 Orleans 的修改包括仓库 URL 规范化、相关 Orleans 特性、项目与包发现，以及本地构建编排。
