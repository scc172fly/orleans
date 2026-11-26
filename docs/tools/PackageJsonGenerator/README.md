# PackageJsonGenerator

生成确定型每包 JSON 描述公众 .NET API 中 Orleans 组合体。公式与包件相吻合 API 数据所消耗的数据 `microsoft/aspire.dev` 星体定位

使用存储库根的 `docs/scripts/Generate-ApiData.ps1` 来发现和构建可包装 Orleans 项目,汇集每个项目的参考关闭,并生成完整的软件包数据集。

```powershell
pwsh ./docs/scripts/Generate-ApiData.ps1
```

默认输出为 `docs/site/src/data/pkgs`出入证 `-OutputDirectory` 生成到一个中继目录, `-TargetFramework` 选择另一个支持的 TFM,或 `-SkipBuild` 已经建立所有选中的软件包项目。

项目从 `src/**/*.csproj` 使用经过评估的 MSBulild 属性。项目既包括了 `IsPackable=true` 和 `IncludeBuildOutput=true`,并记录在选定框架的构建中或从其 `net11.0` 仅当它只针对目标时构建 `net11.0`不包括测试项目、只实施的项目、分析器、代码生成器和无组装元包装,而没有包件清单。

每个输出文件都命名 `{PackageId}.{PackageVersion}.json`。只有公共类型和该软件包组装的用户才发布;项目、软件包和框架依赖性是分辨率仅为Roslyn参考的。可移动 PDB路径在已核实本地仓库树上解决,并用所公布承诺的跟踪文档允许列表解决,避免网络独立输出和断开生成/创建链接。脚本验证 XML 文件、软件包元数据、源存储/承诺信息以及重复型号身份,然后删除已失效的发电机所属 JSON 文件。

API 选择使用 `GeneratedCodeAttribute` 和 `CompilerGeneratedAttribute` 来筛选执行类型和成员,包括嵌套类型和大成员。如果生成的成员单独携带这些属性,则部分类别保留其 API 条目。

默认情况下,源链接针对的是已更改 `src`的最新承诺。当 `src` 有未承诺的修改时,生成失败。自动可以通过 `-SourceCommit` 选择一个明确的完整承诺 SHA。

<a id="upstream-attribution"></a>

## 上游归属

此工具是自 [`microsoft/aspire.dev`'s `PackageJsonGenerator`](https://github.com/microsoft/aspire.dev/tree/9aa68083af47da79b63bc15b80f44c1927bf9c08/src/tools/PackageJsonGenerator) 承诺 `9aa68083af47da79b63bc15b80f44c1927bf9c08`.

调整后的源保留了上游 .NET 基金会和麻省理工学院许可证头目。 Orleans-- -- 具体变化包括储存库URL正常化、相关 Orleans 属性、项目/包装发现以及当地建筑管弦化。
