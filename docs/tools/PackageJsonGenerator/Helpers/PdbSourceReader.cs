// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PackageJsonGenerator.Helpers;

/// <summary>
/// 读取源文件路径和行距, 范围来自嵌入式的便携式 PDB 。
/// 构建一个从元数据符号 `( 文件、 startLine、 endLine中创建的查找
/// <see cref="CanonicalModelBuilder"/> 用于填充 SourceFile/SourceLines。
/// </summary>
internal sealed class PdbSourceReader : IDisposable
{
    private readonly PEReader _peReader;
    private readonly MetadataReaderProvider? _pdbProvider;
    private readonly MetadataReader? _pdbReader;
    private readonly MetadataReader _peMetadata;

    /// <summary>
    /// 以 MethodDef 行号为键入的每条方法源信息。
    /// </summary>
    private readonly Dictionary<int, MethodSourceInfo> _methodSources = [];

    /// <summary>
    /// 以 TypeDef 行号为键入的每类集成源信息。
    /// </summary>
    private readonly Dictionary<int, MethodSourceInfo> _typeSources = [];

    /// <summary>
    /// 地图将全型名称(通用脱光)与 TypeDef 把手正常化。
    /// 启用 O(1) 查找, 正确处理嵌套类型和通用正数 。
    /// </summary>
    private readonly Dictionary<string, TypeDefinitionHandle> _normalizedNameToHandle = [];

    /// <summary>
    /// 映射源目录路径的命名空间,这些路径来自有 PDB 源信息的类型。
    /// 用作没有方法体类型源文件路径的回溯。
    /// </summary>
    private readonly Dictionary<string, string> _namespaceToDirs = [];

    /// <summary>
    /// 组件中源文件的共同基准目录 (e.g. "src/Orleans.Core/")。
    /// </summary>
    private string? _baseDir;

    public bool HasPdb => _pdbReader is not null;

    public PdbSourceReader(string assemblyPath)
    {
        _peReader = new PEReader(File.OpenRead(assemblyPath));
        _peMetadata = _peReader.GetMetadataReader();

        var debugEntries = _peReader.ReadDebugDirectory();
        var embeddedEntry = Array.Find(
            debugEntries.ToArray(),
            e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);

        if (embeddedEntry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
        {
            _pdbProvider = _peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedEntry);
            _pdbReader = _pdbProvider.GetMetadataReader();
            BuildMethodIndex();
            BuildTypeIndex();
        }
        else
        {
            // 尝试独立 PDB
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                var pdbStream = File.OpenRead(pdbPath);
                _pdbProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
                _pdbReader = _pdbProvider.GetMetadataReader();
                BuildMethodIndex();
                BuildTypeIndex();
            }
        }

        BuildNameIndex();
        BuildNamespaceDirMapping();
    }

    private void BuildMethodIndex()
    {
        if (_pdbReader is null) return;

        foreach (var handle in _pdbReader.MethodDebugInformation)
        {
            var mdi = _pdbReader.GetMethodDebugInformation(handle);
            if (mdi.Document.IsNil) continue;

            var doc = _pdbReader.GetDocument(mdi.Document);
            var docName = _pdbReader.GetString(doc.Name);

            int minLine = int.MaxValue, maxLine = 0;
            foreach (var sp in mdi.GetSequencePoints())
            {
                if (sp.IsHidden) continue;
                if (sp.StartLine < minLine) minLine = sp.StartLine;
                if (sp.EndLine > maxLine) maxLine = sp.EndLine;
            }

            if (minLine < int.MaxValue)
            {
                var rowId = MetadataTokens.GetRowNumber(handle);
                _methodSources[rowId] = new MethodSourceInfo(CleanPath(docName), minLine, maxLine);
            }
        }
    }

    private void BuildTypeIndex()
    {
        foreach (var typeDefHandle in _peMetadata.TypeDefinitions)
        {
            var typeDef = _peMetadata.GetTypeDefinition(typeDefHandle);
            string? file = null;
            int typeMinLine = int.MaxValue, typeMaxLine = 0;

            AggregateMethodSources(typeDef, ref file, ref typeMinLine, ref typeMaxLine);

            if (file is not null)
            {
                var typeRowId = MetadataTokens.GetRowNumber(typeDefHandle);
                _typeSources[typeRowId] = new MethodSourceInfo(file, typeMinLine, typeMaxLine);
            }
        }
    }

    /// <summary>
    /// 构建一个词典映射, 将全型号名称标准化到 TypeDef 控件上。
    /// 这样可以进行 O(1) 查找, 正确处理嵌套类型和通用正数 。
    /// </summary>
    private void BuildNameIndex()
    {
        foreach (var handle in _peMetadata.TypeDefinitions)
        {
            var fullName = GetFullMetadataName(handle);
            var normalized = NormalizeGenericName(fullName);
            _normalizedNameToHandle.TryAdd(normalized, handle);
        }
    }

    /// <summary>
    /// 根据源文件路径已知的类型建立命名空间到目录的映射。
    /// 确定集会源文件的共同基准目录。
    /// </summary>
    private void BuildNamespaceDirMapping()
    {
        foreach (var (rowId, source) in _typeSources)
        {
            var handle = MetadataTokens.TypeDefinitionHandle(rowId);
            var typeDef = _peMetadata.GetTypeDefinition(handle);
            var ns = _peMetadata.GetString(typeDef.Namespace);

            // 对于嵌套类型,步行到包含型号的命名空间
            if (string.IsNullOrEmpty(ns))
            {
                var parent = typeDef.GetDeclaringType();
                while (!parent.IsNil)
                {
                    var parentDef = _peMetadata.GetTypeDefinition(parent);
                    ns = _peMetadata.GetString(parentDef.Namespace);
                    if (!string.IsNullOrEmpty(ns)) break;
                    parent = parentDef.GetDeclaringType();
                }
            }

            if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(source.File)) continue;

            var lastSlash = source.File.LastIndexOf('/');
            if (lastSlash > 0)
            {
                var dir = source.File[..(lastSlash + 1)];
                _namespaceToDirs.TryAdd(ns, dir);
            }
        }

        // 确定共同基准目录 (e.g) 。 “src/Orleans.Core/”
        var baseDirs = _typeSources.Values
            .Select(s => s.File)
            .Where(f => f.StartsWith("src/", StringComparison.Ordinal))
            .Select(f =>
            {
                var secondSlash = f.IndexOf('/', 4);
                return secondSlash > 0 ? f[..(secondSlash + 1)] : null;
            })
            .Where(d => d is not null)
            .Distinct()
            .ToList();

        if (baseDirs.Count == 1)
        {
            _baseDir = baseDirs[0];
        }
    }

    /// <summary>
    /// 构建类型的全部元数据名称,正确处理嵌套类型
    /// 沿着申报型号的链条走。
    /// </summary>
    private string GetFullMetadataName(TypeDefinitionHandle handle)
    {
        var typeDef = _peMetadata.GetTypeDefinition(handle);
        var name = _peMetadata.GetString(typeDef.Name);

        if (!typeDef.GetDeclaringType().IsNil)
        {
            var parentName = GetFullMetadataName(typeDef.GetDeclaringType());
            return $"{parentName}.{name}";
        }

        var ns = _peMetadata.GetString(typeDef.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// 将Roslyn通用语法的普通化 化为PE元数据对等符号
    /// 具有不同异位名称的地名保持区别。
    /// <c>InteractionResult`1</c> 和 <c>InteractionResult&lt;T&gt;</c> 两者都实现正常化
    /// 至 <c>InteractionResult`1</c>.
    /// </summary>
    internal static string NormalizeGenericName(string name)
    {
        if (!name.Contains('<'))
            return name;

        var sb = new StringBuilder(name.Length);
        int i = 0;
        while (i < name.Length)
        {
            if (name[i] == '<')
            {
                int depth = 1;
                int arity = 1;
                i++;
                while (i < name.Length && depth > 0)
                {
                    if (name[i] == '<') depth++;
                    else if (name[i] == '>') depth--;
                    else if (name[i] == ',' && depth == 1) arity++;
                    i++;
                }
                sb.Append('`');
                sb.Append(arity);
            }
            else
            {
                sb.Append(name[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private static string StripGenericArity(string name)
    {
        var sb = new StringBuilder(name.Length);
        int i = 0;
        while (i < name.Length)
        {
            if (name[i] == '`')
            {
                i++;
                while (i < name.Length && char.IsDigit(name[i])) i++;
            }
            else if (name[i] == '<')
            {
                int depth = 1;
                i++;
                while (i < name.Length && depth > 0)
                {
                    if (name[i] == '<') depth++;
                    else if (name[i] == '>') depth--;
                    i++;
                }
            }
            else
            {
                sb.Append(name[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 以给定类型及其嵌套类型中的所有方法提供的源信息。
    /// 括号类型包括编译器生成的用于合成/编辑方法的国家机器
    /// 其序列点映射回附于附件类型源文件的源文件。
    /// </summary>
    private void AggregateMethodSources(TypeDefinition typeDef, ref string? file, ref int minLine, ref int maxLine)
    {
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var rowId = MetadataTokens.GetRowNumber(methodHandle);
            if (_methodSources.TryGetValue(rowId, out var info))
            {
                file ??= info.File;
                if (info.StartLine < minLine) minLine = info.StartLine;
                if (info.EndLine > maxLine) maxLine = info.EndLine;
            }
        }

        foreach (var nestedHandle in typeDef.GetNestedTypes())
        {
            var nestedType = _peMetadata.GetTypeDefinition(nestedHandle);
            AggregateMethodSources(nestedType, ref file, ref minLine, ref maxLine);
        }
    }

    /// <summary>
    /// 查找一种类型的源信息,其全名(由Roslyn的 ToDisplayString制作)
    /// 通过身份正常匹配处理嵌套类型和通用型名 。
    /// 如果没有可用的 PDB 信息,返回无效 。
    /// </summary>
    public MethodSourceInfo? GetTypeSource(string fullMetadataName)
    {
        if (_pdbReader is null) return null;

        var normalized = NormalizeGenericName(fullMetadataName);
        if (_normalizedNameToHandle.TryGetValue(normalized, out var handle))
        {
            var rowId = MetadataTokens.GetRowNumber(handle);
            return _typeSources.GetValueOrDefault(rowId);
        }

        return null;
    }

    /// <summary>
    /// 对于没有 PDB 序列点的类型(界面、 enums、 代表、 仅含Const- slears) ,
    /// 推导出从命名空间指令映射和类型名称中得出的可能的源文件路径。
    /// 返回文件路径( 无行号) , 如果无法推断则返回无效 。
    /// </summary>
    public string? InferTypeSourceFile(string fullName, string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName)) return null;

        // 获取类型路径的条形命名空间前缀
        var typePath = fullName.Length > namespaceName.Length + 1
            ? fullName[(namespaceName.Length + 1)..]
            : fullName;

        // 将通用参数正常化
        typePath = StripGenericArity(typePath);

        // 对于嵌套类型,使用最外的类型名称作为文件名
        var topLevelName = typePath.Contains('.')
            ? typePath[..typePath.IndexOf('.')]
            : typePath;
        var fileName = $"{topLevelName}.cs";

        // 先尝试按命名空间直接查找目录
        if (_namespaceToDirs.TryGetValue(namespaceName, out var dir))
        {
            return $"{dir}{fileName}";
        }

        // 后退: 从基准目录+相对命名空间构建路径
        if (_baseDir is not null)
        {
            var assemblyName = _baseDir.TrimEnd('/');
            var lastSlash = assemblyName.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                assemblyName = assemblyName[(lastSlash + 1)..];
            }

            var relativeNs = namespaceName.StartsWith(assemblyName + ".", StringComparison.Ordinal)
                ? namespaceName[(assemblyName.Length + 1)..]
                : namespaceName == assemblyName
                    ? ""
                    : null;

            if (relativeNs is not null)
            {
                var relativeDir = string.IsNullOrEmpty(relativeNs)
                    ? ""
                    : relativeNs.Replace('.', '/') + "/";
                return $"{_baseDir}{relativeDir}{fileName}";
            }
        }

        return null;
    }

    /// <summary>
    /// 查找类型内成员的类型名和成员名的源信息。
    /// 当提供 <paramref name="parameterNames"/> 时,试图匹配特定超载
    /// 比较PE元数据中的参数名称。
    /// <para>
    /// 只有在单一方法有自信匹配时才会返回精确的行范围(唯一
    /// 参数- name 匹配或使用该名称的单方法。当多个方法匹配
    /// 模糊地返回只使用文件的结果 (<see cref="MethodSourceInfo.StartLine"/> = 0)
    /// 以便呼叫者可以返回到文件级链接,而不是误导范围。
    /// </para>
    /// </summary>
    public MethodSourceInfo? GetMemberSource(string typeFullMetadataName, string memberName, IReadOnlyList<string>? parameterNames = null)
    {
        if (_pdbReader is null) return null;

        var normalized = NormalizeGenericName(typeFullMetadataName);
        if (!_normalizedNameToHandle.TryGetValue(normalized, out var typeHandle))
            return null;

        var typeDef = _peMetadata.GetTypeDefinition(typeHandle);
        var tname = _peMetadata.GetString(typeDef.Name);

        // 搜索方法——跟踪所有匹配的建立信任措施评估
        string? file = null;
        int minLine = int.MaxValue, maxLine = 0;
        int methodMatchCount = 0;
        MethodSourceInfo? paramExactMatch = null;
        int paramMatchCount = 0;

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var methodDef = _peMetadata.GetMethodDefinition(methodHandle);
            var mname = _peMetadata.GetString(methodDef.Name);

            // 匹配方法名称( 构件: .ctor {} 成员名称将是类型名称或“. ctor”)
            bool isMatch = mname == memberName
                || (memberName == ".ctor" && mname == ".ctor")
                || (mname == ".ctor" && memberName == tname)
                || (mname.StartsWith("get_", StringComparison.Ordinal) && mname[4..] == memberName)
                || (mname.StartsWith("set_", StringComparison.Ordinal) && mname[4..] == memberName)
                || (mname.StartsWith("add_", StringComparison.Ordinal) && mname[4..] == memberName)
                || (mname.StartsWith("remove_", StringComparison.Ordinal) && mname[7..] == memberName);

            if (!isMatch) continue;

            var rowId = MetadataTokens.GetRowNumber(methodHandle);
            if (!_methodSources.TryGetValue(rowId, out var info)) continue;

            methodMatchCount++;

            // 超载分辨率的音轨参数名匹配
            if (parameterNames is not null && ParameterNamesMatch(methodDef, parameterNames))
            {
                paramExactMatch = info;
                paramMatchCount++;
            }

            // 合并后退后范围累积
            file ??= info.File;
            if (info.StartLine < minLine) minLine = info.StartLine;
            if (info.EndLine > maxLine) maxLine = info.EndLine;
        }

        // 搜索字段( 对于粘结/静态字段)
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var fieldDef = _peMetadata.GetFieldDefinition(fieldHandle);
            var fname = _peMetadata.GetString(fieldDef.Name);
            if (fname != memberName) continue;

            // 字段没有序列点——只返回文件(无行范围)。
            if (_typeSources.TryGetValue(MetadataTokens.GetRowNumber(typeHandle), out var typeInfo))
            {
                return new MethodSourceInfo(typeInfo.File, 0, 0);
            }
        }

        // 检查编译器生成的嵌套类型( 同步状态机器、 循环器)
        // 其姓名遵循模式 <MemberName>d__N或类似的。
        if (file is null)
        {
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nestedType = _peMetadata.GetTypeDefinition(nestedHandle);
                var nestedName = _peMetadata.GetString(nestedType.Name);

                if (!nestedName.StartsWith($"<{memberName}>", StringComparison.Ordinal))
                    continue;

                foreach (var nestedMethod in nestedType.GetMethods())
                {
                    var rowId = MetadataTokens.GetRowNumber(nestedMethod);
                    if (_methodSources.TryGetValue(rowId, out var info))
                    {
                        methodMatchCount++;
                        file ??= info.File;
                        if (info.StartLine < minLine) minLine = info.StartLine;
                        if (info.EndLine > maxLine) maxLine = info.EndLine;
                    }
                }
            }
        }

        // 基于信任度的回报率:
        // 1. 唯一参数名匹配
        if (paramExactMatch is not null && paramMatchCount == 1)
        {
            return paramExactMatch;
        }

        // 2. 单一方法,名称匹配(无超载)
        if (methodMatchCount == 1 && file is not null)
        {
            return new MethodSourceInfo(file, minLine, maxLine);
        }

        // 3. 多方法或模糊匹配 `回程文件'(无行范围)
        if (file is not null)
        {
            return new MethodSourceInfo(file, 0, 0);
        }

        return null;
    }

    /// <summary>
    /// 检查方法定义的参数名称是否与预期的名称完全匹配。
    /// 跳过序列编号 0 (返回值伪参数) 。
    /// </summary>
    private bool ParameterNamesMatch(MethodDefinition methodDef, IReadOnlyList<string> expectedNames)
    {
        var actualNames = new List<string>();
        foreach (var paramHandle in methodDef.GetParameters())
        {
            var param = _peMetadata.GetParameter(paramHandle);
            if (param.SequenceNumber > 0)
            {
                actualNames.Add(_peMetadata.GetString(param.Name));
            }
        }

        if (actualNames.Count != expectedNames.Count) return false;
        for (int i = 0; i < actualNames.Count; i++)
        {
            if (actualNames[i] != expectedNames[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// 从源路径中切除确定性构建路径前缀(e.g. "/_/") 。
    /// </summary>
    internal static string CleanPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (normalizedPath.StartsWith("/_/", StringComparison.Ordinal))
        {
            return normalizedPath[3..];
        }

        var sourceIndex = normalizedPath.LastIndexOf("/src/", StringComparison.OrdinalIgnoreCase);
        if (sourceIndex >= 0)
        {
            return normalizedPath[(sourceIndex + 1)..];
        }

        if (Path.IsPathRooted(path) ||
            (normalizedPath.Length > 2 && char.IsLetter(normalizedPath[0]) && normalizedPath[1] == ':'))
        {
            return Path.GetFileName(normalizedPath);
        }

        return normalizedPath;
    }

    public void Dispose()
    {
        _pdbProvider?.Dispose();
        _peReader.Dispose();
    }
}

/// <summary>
/// 源文件和行范围信息。
/// <see cref="StartLine"/> 是 0时, 范围未知, 只有文件路径才有意义 。
/// </summary>
internal sealed record MethodSourceInfo(string File, int StartLine, int EndLine)
{
    /// <summary>返回 GitHub- 友好线条范围,如“15-200”。</summary>
    public string ToLineRange() => $"{StartLine}-{EndLine}";
}
