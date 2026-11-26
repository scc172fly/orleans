<a id="contributing-to-orleans"></a>

# 向 Orleans缴款

为希望为 Orleans作出贡献的开发商编写的一些说明和指南。

<a id="getting-started"></a>

## 入门

<a id="prerequisites"></a>

### 先决条件

安装 [基特](https://git-scm.com/downloads) 和 [.NET SDK 斯丁克](https://dotnet.microsoft.com/download/dotnet)。存储库的 `global.json` 选择了构建中使用的 SDK 。

<a id="clone-and-build"></a>

### 克隆和构建

克隆存储库并从根目录中构建解决方案 :

```console
git clone https://github.com/dotnet/orleans.git
cd orleans
dotnet build Orleans.slnx -bl
```

`Orleans.slnx` 解决办法包括 `src` 下的源项目和 `test`下的测试项目。在Windows上, `Build.cmd` 还恢复、建造和包装了解决方案,将软件包放在 `Artifacts/<Configuration>`中。

<a id="verify-formatting"></a>

### 校验格式

在建立解决方案后,核查全库格式化基准:

```console
dotnet format whitespace Orleans.slnx --verify-no-changes --no-restore
```

此命令检查与 CI 相同的白空格式范围, 并重新使用前一建筑的恢复功能。 代码样式和分析器诊断由存储库构建实施 。

<a id="package-compatibility"></a>

### 套包兼容性

包装验证 API- 与最新释放的包件基准相比,以 Orleans 包件进行计值。这包含二进制断裂变化、丢弃的目标框架以及所生产的 NuGet 包件中目标框架中各框架资产不一致的资产。生成的 API- diff工作流程仍然是公众可审查的来源代表 API;软件包验证强制执行实际包件的兼容性。

当二进制突破发生故意变化时:

1. 确认这一变化及其移徙影响适合于计划释放。
1. 生成特定包件的抑制 :

   ```console
   dotnet pack <project> --configuration Release /p:GenerateCompatibilitySuppressionFile=true
   ```

1. 在项目旁边审查生成的 `CompatibilitySuppressions.xml` ,并只保留已核准的分解条目。 提交此文件与执行文件, 并解释拉动请求中的兼容性影响 。
1. 在含有休息时间的释放成为存储库基线后,去除该基准使该基准过时的抑制。

在包件项目中使用 `PackageValidationBaselineFrameworkToIgnore` ,用于有意的目标框架清除。等待第一次释放的包件可以将 `EnablePackageValidation` 设为 `false` ,并附有说明,说明它没有释放基准;在第一次释放后允许验证。保留例外包件的特定性,而不是在共享的 `NoWarn`中添加兼容性诊断。

<a id="run-tests"></a>

### 运行测试

运行当前 .NET 目标的全部测试套件,使用:

```console
dotnet test --solution Orleans.slnx --framework net10.0 --minimum-expected-tests 1 --max-parallel-test-modules 1
```

对于更快的反馈环,运行一个类别或单一测试。例如:

```console
dotnet test --solution Orleans.slnx --framework net10.0 --filter-trait "Category=BVT" --minimum-expected-tests 1 --max-parallel-test-modules 1
dotnet test --project test/Orleans.Core.Tests/Orleans.Core.Tests.csproj --framework net10.0 --filter-class "*MyTestClass*" --filter-method "*MyTestMethod*" --minimum-expected-tests 1
```

<a id="nativeaot-smoke-tests"></a>

### NativeAOT 烟雾测试

重用 `test\Orleans.NativeAotSmoke` 项目将选定的假设作为本地可执行软件公布。 它的默认依赖注射设想方案共享了现有的剪裁烟雾源和练习定位策略以及董事注册、关键服务解析、构建者依赖身份和生命周期驱动启动任务启动。 静态分析工作流程同时发布并实施了这些流量,包括裁剪和 NativeAOT。

要在 Windows 上执行本地烟雾, 请安装 [NativeAOT C+++ 工具链先决条件](https://learn.microsoft.com/dotnet/core/deploying/native-aot/), 然后运行 :

```console
dotnet publish test\Orleans.NativeAotSmoke\Orleans.NativeAotSmoke.csproj --configuration Release --runtime win-x64 --output Artifacts\NativeAotSmoke
Artifacts\NativeAotSmoke\Orleans.NativeAotSmoke.exe
```

`PublishAot` 是在本地烟雾项目中启用的,将建设时间分析器和源生成器保留在其管理的目标框架中。运行时间代码生成的可执行核查在使用选定流程之前就已禁用。其他设想方案使用 `-p:NativeAotSmokeScenario=<name>` 并共享同一项目;每个设想方案都构建该假设方案的 `<name>*.cs` 文件。现有的 `Orleans.DependencyInjection.TrimmedSmoke` 项目在一个自成一体的剪裁应用程序中进行相同的DI流。

登记每个本地情况,并 `<name>.smoke.json` 现有静态分析工作流程发现这些清单,并在Windows和Linux上运行一个集中的矩阵。共享的选手公布情景、检查链接/AOT诊断、执行原生二进制,并保留诊断日志和一卷:

```powershell
.\.github\scripts\run-native-aot-smoke.ps1 -List
.\.github\scripts\run-native-aot-smoke.ps1 -Scenario DependencyInjection -RuntimeIdentifier win-x64
```

每个单项声明 `scenario`、 `diagnostics`和 `rejectDiagnostics`。使用 `strict` 进行无警示本地路径。连接兼容的推出可以使用非空诊断拒绝模式的辅助路径的 `legacy-visible` ;运行者保留现有积存明显,拒绝匹配新路径警告和错误。功能性 公关增加假想源并显示此共享控管的特性。

<a id="work-on-an-application-with-local-source"></a>

### 与本地来源合作申请

需要对应用程序进行未释放的更改,请参考 `src` 下的相关项目,而不是已公布的 NuGet 软件包。例如,应用项目中的参考 `src/Orleans.Core/Orleans.Core.csproj` ,并结合 Orleans 解决方案构建应用程序。这样可以让调试器进入本地 Orleans 源。

大多数改动应始于 `src` 下的项目,并在 `test`下的相应项目中进行重点测试。存储处还载有 `docs` 下的文件和 `samples`下的例子。

<a id="contributing-to-this-project"></a>

## 向该项目捐款

本文为任何寻找小型地物和工作项目的人提出一些要点,这些要点将对 Orleans.

这些只是几个想法, 所以如果你想到其他有用的东西, 然后把一个 [讨论线索](https://github.com/dotnet/orleans/issues) 日期 时 GitHub 讨论提案,并努力争取。

* **[Orleans GitHub 仓库](https://github.com/dotnet/orleans)**

总是欢迎提出拉动请求。

* **[实习和学生项目](https://docs.microsoft.com/dotnet/orleans/resources/student-projects)**

关于可能开展的实习生/学生项目的一些建议。

* **[文件编制准则文件编写准则](https://docs.microsoft.com/contribute/dotnet/dotnet-contribute)**

此网站的书写文档样式指南 。

<a id="code-contributions"></a>

## 代码捐款

该项目使用与另一个相同的捐款程序 **[.NET 项目](https://github.com/dotnet)** 日期 时 GitHub.

* **[.NET 项目捐款准则](https://github.com/dotnet/runtime/blob/main/CONTRIBUTING.md)**

协助促进《二十一世纪 .NET 项目项目执行 GitHub.

* **[.NET 法律援助中心](https://cla.dotnetfoundation.org/)**

缴款许可证协定 .NET 项目项目执行 GitHub.

* **[.NET 框架设计指南](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/framework-design-guidelines-digest.md)**

一些基本 API 规则设计、编码标准和 .NET 框架API。

<a id="coding-standards-and-conventions"></a>

## 编码标准和公约

我们尽量不要太过偏重于编译风格战争的编码,但是在出现争端时,我们确实要回到两个“.NET 编码标准”书中的核心原则,而另一个 .NET 开放源码软件项目则使用这两个“ GitHub码标准”书:

* [C# 编码样式指南](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)

* [.NET 框架设计指南](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/framework-design-guidelines-digest.md)

有关 [.NET](https://github.com/dotnet/runtime/tree/main/docs#coding-guidelines) 文件站点的许多其他有用文件值得阅读,尽管大多数有经验的C#开发者很可能会从宇宙号中汲取许多最佳做法,特别是绩效和记忆管理方面的最佳做法。

<a id="source-code-organization"></a>

## 源代码组织

Orleans 没有在宗教上遵循“一等文件”规则,而是试图利用务实的判断来最大限度地改变团队开发者的“代码可理解性 ” 。 如果很多小类共享一个“共同主题 ” 和/或总是一起处理, 那么在大多数情况下将这些分类放入一个源代码文档是正常的。 例如,不同的“ 用户” 类别最初被放在单一源文件中, 因为它们代表了单个代码理解单位 。

由此推论,如果一个类别在与该类同名的文件中找到源代码(类似于爪哇文件命名规则),则更容易找到该类别的源代码,因此,在代码可查找性和尽量减少/限制项目内解决方案和文件中的项目数量[对大型项目的视觉工作室“打开”和“建设”时间都产生直接影响]之间存在着一种紧张和价值判断。

VS 和 ReSharper 的代码搜索工具绝对可以帮助这里 。

<a id="dependencies-and-inter-project-references"></a>

## 依赖和项目间参考

我们非常严格处理的一个议题是围绕各组成部分和分系统之间的依赖性参照。

<a id="component--project-references"></a>

### 构成部分/项目参考

解决方案中项目之间的参考必须始终使用 "**项目参考**" ,而不是“_DLL参考_”,以确保构建工具了解构件建立关系的组成部分。

**右**:

```xml
<ProjectReference Include="..\Orleans\Orleans.csproj">
    <Project>{BC1BD60C-E7D8-4452-A21C-290AEC8E2E74}</Project>
    <Name>Orleans</Name>
</ProjectReference>
```

_Wrong_:

```xml
<Reference Include="Orleans" >
    <HintPath>..\Orleans\bin\Debug\Orleans.dll</HintPath>
</Reference>
```

为了确保我们保持项目间参考文件的清洁,然后在建设服务器[和本地 `Build.cmd` 脚本]上,我们有意使用边对边输入 `.\src` 和输出 `.\Binaries` 目录,而不是VS在当地德瓦机器上使用的更正常的现场构建目录结构(例如 `[PROJ]\bin\Release`)。

<a id="unified-component-versions"></a>

### 统一组成部分版本

我们在整个联合国整个联合国内部使用同样的外部组成部分统一版本。 Orleans 代码基底,因此不必添加 `bindingRedirect` 条目 `App.config` 文件。

而且,一般而言几乎永远没有必要 `Private=True` 元素中的 Orleans 工程文件, 但与 Windows/ VS “ system” 组件的冲突要取消 。
有些一揽子管理工具在修改版本时有时会被混淆,有时会认为我们在一个解决方案中使用同一个组合的多种版本,而我们当然从不这样做。

我们渴望有一天,我们期待有一套管理工具用于 .NET 能够对版本进行交易更改。 在那时之前, 有时有必要“ 固定” 一些用户错误的动作 .NET 手动编辑.csproj 文件(它们只是 XML 文本文件), 恢复到理智和/或使用“ 丢弃编辑线条” 函数, 而这些功能是最好的 Git 工具, 例如 [阿特拉斯语 SourceTree](https://www.sourcetreeapp.com/) 提供。

使用“分类”参考资料和统一的组成部分版本避免在 Orleans 运行时段和/或外部组成部分之间建立不便的联系,过去几年证明非常有效,在重要的部署里程碑期间,能够减少 Orleans 小组的压力。
