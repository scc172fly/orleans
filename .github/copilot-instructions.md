<a id="implementation-and-code-review"></a>

## 实施和守则审查

遵循root `AGENTS.md` 和可适用的实施原则和存储库工作流程目录指示。

代码审查时使用 `.github/skills/code-review/SKILL.md`。

<a id="build-test-and-style-commands"></a>

## 构建、测试和样式命令

* 与 .NET SDK 调自 `global.json` (`11.0.100`,滚转 `major`编辑 `global.json` 除非明确要求。
* 以 `dotnet build Orleans.slnx -bl`构建解决方案。 CI 使用此窗体并上传了 binlog 。
* 在 Windows 上, `.\Build.cmd` 运行 `build.ps1`, 恢复、 建造和包装 `Orleans.slnx`, 撰写 `Artifacts\<Configuration>`下的修复/ 建设/ 包装箱和产出包。 设置 `BuildConfiguration=Release` 以构建释放的文物。
* 使用 `dotnet test --solution Orleans.slnx --framework net11.0 --minimum-expected-tests 1 --max-parallel-test-modules 1`运行测试。 CI 也运行 `net10.0`; 源项目默认为 `net10.0;net11.0`。
* 运行类别 `dotnet test --solution Orleans.slnx --framework net10.0 --filter-trait "Category=BVT" --minimum-expected-tests 1 --max-parallel-test-modules 1`共同类别 `BVT`, `SlowBVT`, `Functional`和提供商特定类别,如 `Redis`, `Cassandra`, `SqlServer`, `Azure`, `AWS`,和 `Streaming`.
* 使用 `dotnet test --project test\Orleans.Core.Tests\Orleans.Core.Tests.csproj --framework net10.0 --filter-class "*MyTestClass*" --filter-method "*MyTestMethod*" --minimum-expected-tests 1`运行一个测试。
* `.\Test.cmd` 使用默认的 xUnit 查询过滤器 `/[(Category=BVT)|(Category=SlowBVT)]`运行脚本 Windows 测试子集。 `.\TestAll.cmd` 添加了 `Functional` 类别。
* 供应商测试往往需要外部服务、连接字符串环境变量或秘密。 所需的设置匹配 `.github\workflows\ci.yml` 和 `test\TestInfrastructure\TestExtensions\TestDefaultConfiguration.cs` 。
* 样式由 `dotnet build` 强制执行,因为 `Directory.Build.props` 设置了 `EnforceCodeStyleInBuild=true` 和 `TreatWarningsAsErrors=true`。

<a id="high-level-architecture"></a>

## 高级别架构

* Orleans 是一个虚拟行为者框架。用户代码定义 grain 界面(`IGrainWithStringKey`, `IGrainWithGuidKey`等)和 grain 课级;运行时间启动、地点、停止使用、持续和通话路线 grains 横 silos.
* `src\Orleans.Core.Abstractions` 包含公共方案编制模式: grain 接口、 grain 参考文献、属性和客户和 silos所使用的共享抽象数据。
* `src\Orleans.Serialization.Abstractions` 和 `src\Orleans.Serialization` 提供耐版本的连续序列器、连序特性、运行时间连续序列服务以及源源生成器集成。 `src\Orleans.Serialization.*` 和 `src\Serializers`.
* `src\Orleans.Core` 包含共享客户/服务器运行时间基础设施,如信息、主机集成、配置和序列整合。
* `src\Orleans.Runtime` 是 silo 执行:激活生命周期、安置、集群成员、提醒、运行时服务以及 grain 执行。
* `src\Orleans.Sdk`、 `src\Orleans.Client`和 `src\Orleans.Server` 是包装/元包项目。SDK带来核心包件加上分析器/代码生成;客户/服务器包件组成SDK,与客户或 silo 依赖方组成。
* 供应商包在 `src\Azure`、 `src\AWS`、 `src\AdoNet`、 `src\Cassandra`、 `src\Redis`和相关的最高级供应商目录下,有意按后端和能力分割。通常暴露在 `Orleans.Hosting` 中的建筑商扩展和 `Orleans.Configuration`中的选项。
* 测试镜像运行时间区域在 `test\`下。共享的集束定着位于 `test\TestInfrastructure\TestExtensions`;共享特性位于精干 `test\TestInfrastructure\TestUtilities` 库中。测试 grains 和 grain 接口位于 `test\Grains`之下,而 `Orleans.TestingHost`/`TestClusterBuilder` 是主要的整合测试工具。
* `src\api` 包含生成的公开 API 表面。跟随根 `AGENTS.md`中的再生和包件兼容命令。

<a id="key-conventions"></a>

## 主要公约

* `Directory.Packages.props`中启用中央软件包管理;在那里添加或更新软件包版本,而不是在单个项目中安装版本。
* 使用项目参考符作为回波内部依赖性。 `CONTRIBUTING.md` 明确劝阻 DLL 引用和不必要的 `Private=True` 元数据。
* `src\Directory.Build.props` 使源项目默认可以包装; `test\Directory.Build.props` 使测试工程不可包装,并配置 MTP 测试应用程序。根 `Directory.Build.targets` 在每个测试组装名称下复制 `test\testconfig.json` 。
* 建筑时间代码生成由 `OrleansBuildTimeCodeGen=true`进口 Orleans 代码生成器和分析器作为框架项目的建筑分析器。
* 串列 Orleans 类型 grain 通常需要电话、存储或流 `[GenerateSerializer]` 加上稳定 `[Id(n)]` 成员。不重新编号现有的序列化身份证。
* 试验使用 xUnit 与习惯 `[TestCategory("...")]` 属性来源 `test\TestInfrastructure\TestUtilities\TestCategory.cs`; 将此绘图类别 xUnit 特性 `dotnet test --filter-trait "Category=..."`.
* 集束试验通常来自或组成 `test\TestInfrastructure\TestExtensions` 配置和配置 silos 中 `TestClusterBuilder`, `ISiloConfigurator`,或 `ISiloBuilder`默认集束固定值在记忆提醒、耐久性工作以及 grain 储存。
* 后续 `.editorconfig`C# 使用文件范围的名称空间, 系统指令首先, `var` 新的线条上的括号 `_camelCase` 私域字段和预览语言特性。源项目启用了可读性,测试项目禁用了此功能。
* 尽管 CS1591 目前被抑制,但为新的或已改变的公共 API添加 XML docs;软件包项目生成文档文件。
