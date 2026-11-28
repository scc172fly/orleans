// 文档示例的项目元数据桩类。
// 在真实的 Aspire 应用程序中，这些类由 Aspire SDK
// 根据 ProjectReference 项自动生成。由于示例项目是类库
// （而非可运行的应用程序），这里定义桩类以支持编译。

using Aspire.Hosting;

namespace Projects;

// Silo 项目的桩类。
public class Silo : IProjectMetadata
{
    public string ProjectPath => "../Silo/Silo.csproj";
}

// Client 项目的桩类。
public class Client : IProjectMetadata
{
    public string ProjectPath => "../Client/Client.csproj";
}
