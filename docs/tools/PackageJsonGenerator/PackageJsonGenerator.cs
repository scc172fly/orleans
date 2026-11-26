// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PackageJsonGenerator.Helpers;

namespace PackageJsonGenerator;

public static class PackageJsonGenerator
{
    private const string OrleansRepositoryUrl = "https://github.com/dotnet/orleans";

    public static void GeneratePackageJson(string? inputAssembly, string[]? references, string? outputFile, string? versionOverride = null, string? packageNameOverride = null, string? sourceRepoOverride = null, string? sourceCommitOverride = null, string? sourceRootOverride = null, string? sourceFileManifestOverride = null, string? targetFrameworkOverride = null, ConcurrentDictionary<string, PortableExecutableReference>? referenceCache = null)
    {
        if (string.IsNullOrEmpty(inputAssembly))
        {
            throw new ArgumentException("Input assembly path is required.", nameof(inputAssembly));
        }

        if (references is null || references.Length is 0)
        {
            throw new ArgumentException("At least one reference assembly is required.", nameof(references));
        }

        if (string.IsNullOrEmpty(outputFile))
        {
            throw new ArgumentException("Output file path is required.", nameof(outputFile));
        }

        var inputReference = CreateMetadataReference(inputAssembly);
        var resolvedRefs = referenceCache is not null
            ? references.Select(r => referenceCache.GetOrAdd(r, CreateMetadataReference))
            : references.Select(CreateMetadataReference);
        var compilation = CSharpCompilation.Create(
            "PackageJsonGen",
            references: resolvedRefs.Concat([inputReference]));

        var assemblySymbol = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(inputReference)!;

        // 从集会中收集所有公开类型的集合
        var types = new List<INamedTypeSymbol>();
        CollectTypes(assemblySymbol.GlobalNamespace, types, assemblySymbol);

        if (types.Count == 0)
        {
            Console.WriteLine($"No public types found in assembly: {assemblySymbol.Name}");
        }

        var assemblyName = !string.IsNullOrEmpty(packageNameOverride)
            ? packageNameOverride
            : assemblySymbol.Name;
        var assemblyVersion = !string.IsNullOrEmpty(versionOverride)
            ? versionOverride
            : assemblySymbol.Identity.Version.ToString();
        var targetFramework = !string.IsNullOrEmpty(targetFrameworkOverride)
            ? targetFrameworkOverride
            : "net10.0";

        // 解析源链接信息来自组装元数据或 CLI 覆盖
        var sourceRepo = sourceRepoOverride;
        var sourceCommit = sourceCommitOverride;

        if (string.IsNullOrEmpty(sourceRepo) || string.IsNullOrEmpty(sourceCommit))
        {
            foreach (var attr in assemblySymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"
                    && attr.ConstructorArguments.Length == 2)
                {
                    var key = attr.ConstructorArguments[0].Value as string;
                    var value = attr.ConstructorArguments[1].Value as string;

                    if (string.IsNullOrEmpty(sourceRepo) && key == "RepositoryUrl")
                        sourceRepo = value;
                    if (string.IsNullOrEmpty(sourceCommit) && key == "RepositoryCommit")
                        sourceCommit = value;
                }

                // 后退: 从 InformationalVersion (e.g. "1.2.3+abc123def") 提取承诺
                if (string.IsNullOrEmpty(sourceCommit)
                    && attr.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyInformationalVersionAttribute"
                    && attr.ConstructorArguments.Length == 1)
                {
                    var infoVersion = attr.ConstructorArguments[0].Value as string;
                    if (infoVersion is not null)
                    {
                        var plusIdx = infoVersion.IndexOf('+');
                        if (plusIdx >= 0 && plusIdx + 1 < infoVersion.Length)
                        {
                            sourceCommit = infoVersion[(plusIdx + 1)..];
                        }
                    }
                }
            }
        }

        sourceRepo = NormalizeSourceRepository(sourceRepo);
        var trackedSourceFiles = LoadSourceFileManifest(sourceFileManifestOverride);

        // 构建金库模型
        var modelBuilder = new CanonicalModelBuilder(compilation);
        var typeModels = modelBuilder.BuildTypes(types.ToImmutableArray());

        // 以 PDB 源信息( 文件路径+行区域) 加入 pDB 来源信息丰富
        using (var pdbReader = new PdbSourceReader(inputAssembly))
        {
            if (pdbReader.HasPdb)
            {
                foreach (var typeModel in typeModels)
                {
                    var typeSource = pdbReader.GetTypeSource(typeModel.FullName);
                    if (typeSource is not null &&
                        IsSourceFileAvailable(typeSource.File, sourceRootOverride, trackedSourceFiles))
                    {
                        typeModel.SourceFile = typeSource.File;
                        // 仅使用起始行作为单线锁定。 PDB
                        // 方法的实施,而不是类型
                        // 声明。启动线锚锁定将定位类(特别是
                        // ),但并未暗示误导范围。
                        typeModel.SourceLines = $"{typeSource.StartLine}-{typeSource.StartLine}";
                    }

                    foreach (var member in typeModel.Members)
                    {
                        var paramNames = member.Parameters?.Select(p => p.Name).ToList();
                        var memberSource = pdbReader.GetMemberSource(typeModel.FullName, member.Name, paramNames);
                        if (memberSource is not null &&
                            IsSourceFileAvailable(memberSource.File, sourceRootOverride, trackedSourceFiles))
                        {
                            member.SourceFile = memberSource.File;
                            if (memberSource.StartLine > 0)
                            {
                                member.SourceLines = memberSource.ToLineRange();
                            }
                        }
                    }
                }
            }

            // 后背: 没有 PDB 序列点的类型的源文件路径推推
            // (面部无默认方法、 设置、 代表、 仅含Const类)
            foreach (var typeModel in typeModels)
            {
                if (typeModel.SourceFile is null)
                {
                    var inferredSourceFile = pdbReader.InferTypeSourceFile(typeModel.FullName, typeModel.Namespace);
                    if (inferredSourceFile is not null &&
                        IsSourceFileAvailable(inferredSourceFile, sourceRootOverride, trackedSourceFiles))
                    {
                        typeModel.SourceFile = inferredSourceFile;
                    }
                }
            }
        }

        // 基于PDB的型号信息在第一个方法体中往往指向基于 PDB 的类型线信息,而不是
        // 类型声明。获取源文件并解决声明线
        // 具备源信息的所有类型。
        AdjustSourceLinesForTypeDeclarations(typeModels, sourceRootOverride);

        // 以 EMit JSON 计划
        var schemaJson = SchemaEmitter.EmitAssemblySchema(
            assemblyName,
            assemblyVersion,
            targetFramework,
            typeModels,
            sourceRepo,
            sourceCommit);

        // 确保输出目录存在
        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var wroteFile = StableFileWriter.WriteIfChanged(outputFile, schemaJson);
        Console.WriteLine($"{(wroteFile ? "Generated" : "Unchanged")}: {outputFile}");
    }

    private static HashSet<string>? LoadSourceFileManifest(string? sourceFileManifest)
    {
        if (string.IsNullOrEmpty(sourceFileManifest))
        {
            return null;
        }

        if (!File.Exists(sourceFileManifest))
        {
            throw new FileNotFoundException("Source file manifest not found.", sourceFileManifest);
        }

        return File.ReadLines(sourceFileManifest)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsSourceFileAvailable(
        string sourceFile,
        string? sourceRoot,
        HashSet<string>? trackedSourceFiles)
    {
        sourceFile = sourceFile.Replace('\\', '/');
        if (sourceFile.Split('/').Any(static segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (trackedSourceFiles is not null && !trackedSourceFiles.Contains(sourceFile))
        {
            return false;
        }

        if (string.IsNullOrEmpty(sourceRoot))
        {
            return true;
        }

        sourceRoot = Path.GetFullPath(sourceRoot);
        var candidatePath = Path.GetFullPath(Path.Combine(sourceRoot, sourceFile));
        var sourceRootPrefix = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidatePath.StartsWith(sourceRootPrefix, pathComparison) && File.Exists(candidatePath);
    }

    internal static PortableExecutableReference CreateMetadataReference(string path)
    {
        var docPath = Path.ChangeExtension(path, "xml");
        var documentationProvider = File.Exists(docPath)
            ? XmlDocumentationProvider.CreateFromFile(docPath)
            : null;

        return MetadataReference.CreateFromFile(path, documentation: documentationProvider);
    }

    private static void CollectTypes(
        INamespaceSymbol ns,
        List<INamedTypeSymbol> types,
        IAssemblySymbol targetAssembly)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            CollectTypeAndNested(type, types);
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            if (SymbolEqualityComparer.Default.Equals(childNs.ContainingAssembly, targetAssembly))
            {
                CollectTypes(childNs, types, targetAssembly);
            }
        }
    }

    private static void CollectTypeAndNested(INamedTypeSymbol type, List<INamedTypeSymbol> types)
    {
        if (type.DeclaredAccessibility == Accessibility.Public && !CanonicalModelBuilder.IsGenerated(type)
            && !string.IsNullOrEmpty(type.Name))
        {
            types.Add(type);

            // 收集公共巢式
            foreach (var nestedType in type.GetTypeMembers())
            {
                CollectTypeAndNested(nestedType, types);
            }
        }
    }

    /// <summary>
    /// 对于含有多种类型源文件的源文件,获取实际源文本
    /// 从存储器中找到确切类型声明行。 PDB 序列
    /// 点只覆盖方法体,所以合计 StartLine 能够超击
    /// 实际 <c>class</c>/<c>interface</c>/<c>struct</c>/etc.关键字,用几行表示。
    /// </summary>
    private static void AdjustSourceLinesForTypeDeclarations(
        List<CanonicalType> typeModels,
        string? sourceRoot)
    {
        if (string.IsNullOrEmpty(sourceRoot))
            return;

        sourceRoot = Path.GetFullPath(sourceRoot);
        var sourceFiles = typeModels
            .Where(t => t.SourceFile is not null)
            .GroupBy(t => t.SourceFile!)
            .ToList();

        if (sourceFiles.Count == 0) return;

        // 缓存源文件, 所以每个文件最多一次读取 。
        var sourceCache = new Dictionary<string, string[]?>();

        foreach (var group in sourceFiles)
        {
            var lines = GetSourceLines(sourceCache, sourceRoot, group.Key);
            if (lines is null)
            {
                // 来源不可用——保留任何现有的基于PDB的数值,而不是
                // 发明一个新的线锚
                foreach (var typeModel in group)
                {
                    if (typeModel.SourceLines is null)
                    {
                        typeModel.SourceLines = null;
                    }
                }
                continue;
            }

            foreach (var typeModel in group)
            {
                var simpleName = PdbSourceReader.NormalizeGenericName(typeModel.Name);

                // 对于 "Outer.Inner" 等嵌套类型,只使用内部名称
                var dotIdx = simpleName.LastIndexOf('.');
                if (dotIdx >= 0) simpleName = simpleName[(dotIdx + 1)..];

                var pdbHintLine = ParseStartLine(typeModel.SourceLines);
                var declarationLine = FindTypeDeclarationLine(lines, simpleName, pdbHintLine);
                if (declarationLine > 0)
                {
                    typeModel.SourceLines = $"{declarationLine}-{declarationLine}";
                }
                else if (typeModel.SourceLines is not null)
                {
                    // Regex 找不到匹配的 Regex—— 放下基于 PDB 的行, 链接
                    // 点到源文件本身,而不是错误的行。
                    typeModel.SourceLines = null;
                }
            }
        }
    }

    /// <summary>
    /// 返回给定文件的源行,从第一个存储库读取
    /// 访问权限和随后的检查缓存。
    /// </summary>
    private static string[]? GetSourceLines(
        Dictionary<string, string[]?> cache,
        string sourceRoot,
        string filePath)
    {
        if (cache.TryGetValue(filePath, out var cached))
            return cached;

        string[]? lines = null;
        var candidatePath = Path.GetFullPath(Path.Combine(sourceRoot, filePath));
        var sourceRootPrefix = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (candidatePath.StartsWith(sourceRootPrefix, pathComparison) &&
            File.Exists(candidatePath))
        {
            lines = File.ReadAllLines(candidatePath);
        }

        cache[filePath] = lines;
        return lines;
    }

    internal static string? BuildRawGitHubUrl(string sourceRepo, string sourceCommit, string filePath)
    {
        sourceRepo = NormalizeSourceRepository(sourceRepo) ?? sourceRepo;

        if (!Uri.TryCreate(sourceRepo, UriKind.Absolute, out var repoUri))
            return null;

        if (!repoUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var repoPath = repoUri.AbsolutePath.TrimEnd('/');
        if (repoPath.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repoPath = repoPath[..^4];

        return $"https://raw.githubusercontent.com{repoPath}/{sourceCommit}/{filePath}";
    }

    internal static string? NormalizeSourceRepository(string? sourceRepo)
    {
        if (string.IsNullOrWhiteSpace(sourceRepo))
        {
            return sourceRepo;
        }

        var trimmed = sourceRepo.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var repoUri) &&
            repoUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = repoUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 2 &&
                segments[0].Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
                (segments[1].Equals("orleans", StringComparison.OrdinalIgnoreCase) ||
                 segments[1].Equals("orleans.git", StringComparison.OrdinalIgnoreCase)))
            {
                return OrleansRepositoryUrl;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// 扫描匹配 <paramref name="typeName"/>类型声明的源线。
    /// 当多重匹配存在时(e.g. 使用共同名称的嵌套类型),
    /// 最接近 <paramref name="pdbHintLine"/> 的胜算。
    /// </summary>
    internal static int FindTypeDeclarationLine(string[] lines, string typeName, int pdbHintLine)
    {
        var escapedName = Regex.Escape(typeName);
        var pattern = new Regex(
            $@"\b(?:class|interface|struct|enum|record|extension)\s+{escapedName}\b",
            RegexOptions.Compiled);

        int bestLine = -1;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            // 跳过注释行
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal))
                continue;

            if (pattern.IsMatch(lines[i]))
            {
                int lineNum = i + 1; // 1-indexed
                int distance = pdbHintLine > 0 ? Math.Abs(lineNum - pdbHintLine) : 0;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLine = lineNum;
                }
            }
        }

        return bestLine;
    }

    internal static int ParseStartLine(string? sourceLines)
    {
        if (sourceLines is null) return 0;
        var dash = sourceLines.IndexOf('-');
        return dash > 0 && int.TryParse(sourceLines[..dash], out var line) ? line : 0;
    }
}
