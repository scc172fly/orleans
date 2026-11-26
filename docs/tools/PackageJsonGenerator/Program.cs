// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using PackageJsonGenerator;

#if LAUNCH_DEBUGGER
if (!System.Diagnostics.Debugger.IsAttached)
{
    System.Diagnostics.Debugger.Launch();
}
#endif

var rootCommand = RootGenerateCommand.GetCommand();
rootCommand.Add(BatchGenerateCommand.GetCommand());

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
