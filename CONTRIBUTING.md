<a id="contributing-to-orleans"></a>

# 参与 Orleans 贡献

面向希望为 Orleans 作出贡献的开发者的一些说明与指南。

<a id="getting-started"></a>

## 入门

<a id="prerequisites"></a>

### 先决条件

安装 [Git](https://git-scm.com/downloads) 和 [.NET SDK](https://dotnet.microsoft.com/download/dotnet)。仓库中的 `global.json` 选定构建所使用的 SDK。

<a id="clone-and-build"></a>

### 克隆和构建

克隆仓库并在其根目录构建解决方案：

```console
git clone https://github.com/dotnet/orleans.git
cd orleans
dotnet build Orleans.slnx -bl
```

`Orleans.slnx` 解决方案包含 `src` 下的源码项目及 `test` 下的测试项目。在 Windows 上，`Build.cmd` 还会还原、构建并打包解决方案，将包放入 `Artifacts/<Configuration>`。

<a id="verify-formatting"></a>

### 校验格式

构建解决方案后，通过以下命令检查整个仓库的格式化基准：

```console
dotnet format whitespace Orleans.slnx --verify-no-changes --no-restore
```

此命令检查与 CI 相同范围的空白字符格式，并复用上一次构建的还原结果。代码风格与分析器诊断由仓库构建流程强制执行。

<a id="package-compatibility"></a>

### 包兼容性

打包时会对照最新已发布包的基准验证包含 API 的 Orleans 包。这可以检测二进制破坏性变更、被移除的目标框架，以及生成的 NuGet 包在各目标框架之间不一致的资产。生成的 API 差异工作流仍然是可供审查的公共 API 源代码表示；包验证则针对实际生成的包强制执行兼容性要求。

如果有意引入二进制破坏性变更：

1. 确认变更及其迁移影响符合计划发行版的要求。
1. 生成针对特定包的诊断抑制：

   ```console
   dotnet pack <project> --configuration Release /p:GenerateCompatibilitySuppressionFile=true
   ```

1. 审查项目旁生成的 `CompatibilitySuppressions.xml`，只保留获准的破坏性变更条目。将该文件与实现一并提交，并在拉取请求中说明兼容性影响。
1. 包含该破坏性变更的发行版成为仓库基准后，删除因新基准而过时的抑制条目。

有意移除目标框架时，在包项目中使用 `PackageValidationBaselineFrameworkToIgnore`。尚未首次发布的包可以将 `EnablePackageValidation` 设为 `false`，并通过注释说明尚无已发布的基准；首次发布后启用验证。例外应限定于具体包，而非将兼容性诊断添加到共享的 `NoWarn` 中。

<a id="run-tests"></a>

### 运行测试

针对当前 .NET 目标运行完整测试套件：

```console
dotnet test --solution Orleans.slnx --framework net10.0 --minimum-expected-tests 1 --max-parallel-test-modules 1
```

为了更快获得反馈，可以运行某一类别或单项测试。例如：

```console
dotnet test --solution Orleans.slnx --framework net10.0 --filter-trait "Category=BVT" --minimum-expected-tests 1 --max-parallel-test-modules 1
dotnet test --project test/Orleans.Core.Tests/Orleans.Core.Tests.csproj --framework net10.0 --filter-class "*MyTestClass*" --filter-method "*MyTestMethod*" --minimum-expected-tests 1
```

<a id="nativeaot-smoke-tests"></a>

### NativeAOT 冒烟测试

可复用的 `test\Orleans.NativeAotSmoke` 项目将选定场景发布为原生可执行程序。其默认依赖注入场景与已有的裁剪冒烟测试共享源码，检验放置策略和放置决策器注册、键控服务解析、构造函数依赖项的标识一致性，以及由生命周期驱动的启动任务激活。静态分析工作流分别以裁剪和 NativeAOT 方式发布并执行这些流程。

若要在 Windows 上执行原生冒烟测试，请安装 [NativeAOT C++ 工具链先决条件](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)，然后运行：

```console
dotnet publish test\Orleans.NativeAotSmoke\Orleans.NativeAotSmoke.csproj --configuration Release --runtime win-x64 --output Artifacts\NativeAotSmoke
Artifacts\NativeAotSmoke\Orleans.NativeAotSmoke.exe
```

原生冒烟测试项目内部启用了 `PublishAot`，使构建时分析器和源代码生成器继续使用其托管目标框架。可执行程序在执行选定流程前确认运行时代码生成已禁用。其他场景通过 `-p:NativeAotSmokeScenario=<name>` 指定，并共享同一项目；每次构建编译该场景的 `<name>*.cs` 文件。已有的 `Orleans.DependencyInjection.TrimmedSmoke` 项目在经过裁剪的独立部署应用中验证相同的依赖注入流程。

为每个原生场景在其源码旁注册一个 `<name>.smoke.json` 清单。已有的静态分析工作流发现这些清单，并在 Windows 和 Linux 上运行统一的矩阵任务。共享运行脚本发布场景、检查链接器/AOT 诊断、执行原生二进制文件，并保留诊断日志与二进制构建日志：

```powershell
.\.github\scripts\run-native-aot-smoke.ps1 -List
.\.github\scripts\run-native-aot-smoke.ps1 -Scenario DependencyInjection -RuntimeIdentifier win-x64
```

每个清单声明 `scenario`、`diagnostics` 和 `rejectDiagnostics`。对于无警告的原生执行路径，使用 `strict`。在范围有限的兼容性逐步引入过程中，可以使用 `legacy-visible`，并为受支持的路径提供非空的诊断拒绝模式；运行脚本会保留可见的历史待处理诊断，并拒绝与模式匹配的新路径警告和错误。功能拉取请求应将场景源码及清单加入这套共享测试设施。

<a id="work-on-an-application-with-local-source"></a>

### 在应用中使用本地源码

如果应用需要验证尚未发布的变更，请引用 `src` 下的相关项目，而不是已发布的 NuGet 包。例如，从应用项目引用 `src/Orleans.Core/Orleans.Core.csproj`，并将应用与 Orleans 解决方案一起构建。这使调试器能够单步进入本地 Orleans 源码。

大多数变更应从 `src` 下的项目开始，并在 `test` 下对应的项目中编写针对性测试。仓库还包含 `docs` 下的文档及 `samples` 下的示例。

<a id="contributing-to-this-project"></a>

## 为本项目作贡献

以下是一些建议，供希望寻找可以为 Orleans 带来积极贡献的小型功能和工作项的人参考。

这些只是若干想法。如果你想到其他有价值的事项，可以在 GitHub 上发起[讨论](https://github.com/dotnet/orleans/issues)，讨论提案后着手实施。

* **[Orleans GitHub 仓库](https://github.com/dotnet/orleans)**

始终欢迎拉取请求。

* **[实习生与学生项目](https://docs.microsoft.com/dotnet/orleans/resources/student-projects)**

一些适合实习生或学生的项目建议。

* **[文档指南](https://docs.microsoft.com/contribute/dotnet/dotnet-contribute)**

为此站点编写文档的风格指南。

<a id="code-contributions"></a>

## 代码贡献

本项目采用与 GitHub 上其他 **[.NET 项目](https://github.com/dotnet)** 相同的贡献流程。

* **[.NET 项目贡献指南](https://github.com/dotnet/runtime/blob/main/CONTRIBUTING.md)**

为 GitHub 上的 .NET 项目作贡献的指南与工作流程。

* **[.NET CLA](https://cla.dotnetfoundation.org/)**

GitHub 上 .NET 项目的贡献者许可协议。

* **[.NET Framework 设计指南](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/framework-design-guidelines-digest.md)**

.NET Framework API 的一些基本 API 设计规则、编码标准与风格指南。

<a id="coding-standards-and-conventions"></a>

## 编码标准与约定

我们尽量不过分纠结编码风格之争，但发生分歧时，会依据 GitHub 上其他 .NET 开源项目所采用的两份“.NET 编码标准”中的核心原则：

* [C# 编码风格指南](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)

* [.NET Framework 设计指南](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/framework-design-guidelines-digest.md)

[.NET](https://github.com/dotnet/runtime/tree/main/docs#coding-guidelines) 文档站点还有许多值得阅读的实用文档；不过有经验的 C# 开发者可能已经在日常实践中逐渐掌握了其中许多最佳实践，尤其是性能和内存管理方面的实践。

<a id="source-code-organization"></a>

## 源码组织

Orleans 并未严格遵循“每个文件一个类”的规则，而是力求根据实际情况作出判断，使团队开发者尽可能容易理解代码。如果许多小型类具有共同主题，或者总是需要一起处理，那么大多数情况下可以将它们放入同一源文件。例如，各种“日志消费者”类最初就放在同一个源文件中，因为它们构成了一个可以整体理解的代码单元。

另一方面，如果类与所在文件同名（类似 Java 的文件命名规则），就更容易找到类的源码，因此需要在代码可查找性与限制解决方案中的项目数及项目中的文件数之间作出权衡；后二者都会直接影响 Visual Studio 打开和构建大型项目所需的时间。

VS 和 ReSharper 的代码搜索工具在此非常有帮助。

<a id="dependencies-and-inter-project-references"></a>

## 依赖项与项目间引用

对于组件和子系统之间的依赖引用，我们有非常严格的要求。

<a id="component--project-references"></a>

### 组件及项目引用

解决方案中项目之间的引用必须始终使用“**项目引用**”，而非“_DLL 引用_”，以确保构建工具了解组件之间的构建依赖关系。

**正确**：

```xml
<ProjectReference Include="..\Orleans\Orleans.csproj">
    <Project>{BC1BD60C-E7D8-4452-A21C-290AEC8E2E74}</Project>
    <Name>Orleans</Name>
</ProjectReference>
```

_错误_：

```xml
<Reference Include="Orleans" >
    <HintPath>..\Orleans\bin\Debug\Orleans.dll</HintPath>
</Reference>
```

为保持项目间引用的整洁，在构建服务器（以及本地 `Build.cmd` 脚本）中，我们特意采用并列的输入目录 `.\src` 与输出目录 `.\Binaries`，而不是 VS 在本地开发机上通常使用的原地构建目录结构（如 `[PROJ]\bin\Release`）。

<a id="unified-component-versions"></a>

### 统一组件版本

整个 Orleans 代码库统一使用相同版本的外部组件，因此不应需要在 `App.config` 文件中添加 `bindingRedirect` 条目。

此外，通常几乎不需要在 Orleans 项目文件中添加 `Private=True` 元素，除非为了覆盖与 Windows 或 VS“系统”组件的冲突。某些包管理工具在更改版本时偶尔会出现混淆，误以为解决方案中使用了同一程序集的多个版本，而我们当然不会这样做。

我们期待 .NET 包管理工具能够以事务方式进行版本变更。在此之前，偶尔需要手动编辑 .csproj 文件（它们只是 XML 文本文件），或者使用大多数优秀 Git 工具（如 [Atlassian SourceTree](https://www.sourcetreeapp.com/)）提供的“Discard Edited Line”（丢弃已编辑行）功能来纠正部分 .NET 包管理工具的不当操作。

采用原文所称的“sort”引用和统一的组件版本，可以避免 Orleans 运行时组件与外部组件之间形成脆弱的链接。过去几年中，这种做法在重要部署里程碑期间有效减轻了 Orleans 团队的压力。:)
