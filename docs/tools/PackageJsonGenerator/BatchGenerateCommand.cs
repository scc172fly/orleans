// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Collections.Concurrent;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace PackageJsonGenerator;

/// <summary>
/// 子命令, 从 JSON 列表平行处理多个包 。
/// </summary>
internal static class BatchGenerateCommand
{
    private static readonly Option<string> s_manifestOption = new("--manifest")
    {
        Required = true,
        Description = "Path to a JSON manifest file listing packages to process.",
    };

    private static readonly Option<int> s_parallelismOption = new("--parallelism")
    {
        Description = "Maximum degree of parallelism. Defaults to processor count.",
    };

    public static Command GetCommand()
    {
        var command = new Command("batch", "Process multiple packages from a JSON manifest in parallel.")
        {
            s_manifestOption,
            s_parallelismOption,
        };

        command.SetAction(static parseResult =>
        {
            var manifestPath = parseResult.GetValue(s_manifestOption)!;
            var parallelism = parseResult.GetValue(s_parallelismOption);

            if (parallelism <= 0)
            {
                parallelism = Environment.ProcessorCount;
            }

            return RunBatch(manifestPath, parallelism);
        });

        return command;
    }

    private static int RunBatch(string manifestPath, int parallelism)
    {
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Manifest not found: {manifestPath}");
            return 1;
        }

        var manifest = JsonSerializer.Deserialize<BatchManifest>(
            File.ReadAllText(manifestPath));

        if (manifest?.Packages is null || manifest.Packages.Count == 0)
        {
            Console.Error.WriteLine("Manifest contains no packages.");
            return 1;
        }

        Console.WriteLine($"Batch: {manifest.Packages.Count} packages, parallelism={parallelism}");

        var sw = Stopwatch.StartNew();

        // 共享引用缓存 - 避免重新装入 ~167 运行时间参考
        // 每个包件都装配组(和共同依赖性低剂量值)
        var referenceCache = new ConcurrentDictionary<string, PortableExecutableReference>(
            StringComparer.OrdinalIgnoreCase);

        int success = 0, failed = 0;

        Parallel.ForEach(
            manifest.Packages,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            pkg =>
            {
                try
                {
                    PackageJsonGenerator.GeneratePackageJson(
                        pkg.Input,
                        pkg.References,
                        pkg.Output,
                        pkg.PackageVersion,
                        pkg.PackageName,
                        pkg.SourceRepo,
                        pkg.SourceCommit,
                        pkg.SourceRoot,
                        pkg.SourceFileManifest,
                        pkg.TargetFramework,
                        referenceCache);

                    Interlocked.Increment(ref success);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"FAILED [{pkg.PackageName}]: {ex.Message}");
                    Interlocked.Increment(ref failed);
                }
            });

        sw.Stop();
        Console.WriteLine($"Batch complete in {sw.Elapsed.TotalSeconds:F1}s: {success} succeeded, {failed} failed");

        return failed > 0 ? 1 : 0;
    }
}
