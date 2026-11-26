// 用于文件实例的 " 实绩 " 项目元数据分类。
// 在真正的 Aspire 应用程序中, 这些是由 Aspire SDK 自动生成的
// 由于我们的样本项目是图书馆,所以我们的项目属于 ProjectReference 项目。
// (非可运行应用程序), 我们定义了可以编译的根条 。

using Aspire.Hosting;

namespace Projects;

// Silo 项目 stub
public class Silo : IProjectMetadata
{
    public string ProjectPath => "../Silo/Silo.csproj";
}

// 客户项目坚固
public class Client : IProjectMetadata
{
    public string ProjectPath => "../Client/Client.csproj";
}
