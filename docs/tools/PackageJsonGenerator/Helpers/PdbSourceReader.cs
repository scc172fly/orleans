// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PackageJsonGenerator.Helpers;

/// <summary>
/// 从嵌入的可移植 PDB 读取源文件路径和行号范围。
/// 构建从元数据令牌到（文件、起始行、结束行）的查找表，
/// 供 <see cref="CanonicalModelBuilder"/> 填充 SourceFile/SourceLines。
/// </summary>
internal sealed class PdbSourceReader : IDisposable
{
    private readonly PEReader _peReader;
    private readonly MetadataReaderProvider? _pdbProvider;
    private readonly MetadataReader? _pdbReader;
    private readonly MetadataReader _peMetadata;

    /// <summary>
    /// 以 MethodDef 行号为键的各方法源码信息。
    /// </summary>
    private readonly Dictionary<int, MethodSourceInfo> _methodSources = [];

    /// <summary>
    /// 以 TypeDef 行号为键的各类型汇总源码信息。
    /// </summary>
    private readonly Dictionary<int, MethodSourceInfo> _typeSources = [];

    /// <summary>
    /// 将规范化的类型完整名称（去除泛型表示）映射到其 TypeDef 句柄。
    /// 支持 O(1) 查找，并正确处理嵌套类型和泛型元数。
    /// </summary>
    private readonly Dictionary<string, TypeDefinitionHandle> _normalizedNameToHandle = [];

    /// <summary>
    /// 将命名空间映射到其源文件目录路径，依据具有 PDB 源码信息的类型推导。
    /// 用作回退方式，为没有方法体的类型推断源文件路径。
    /// </summary>
    private readonly Dictionary<string, string> _namespaceToDirs = [];

    /// <summary>
    /// 此程序集源文件的公共基础目录（例如 "src/Orleans.Core/"）。
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
            // 尝试独立的 PDB 文件。
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
    /// 构建从规范化类型完整名称到其 TypeDef 句柄的字典。
    /// 这样可实现 O(1) 查找，并正确处理嵌套类型及泛型元数。
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
    /// 根据已知源文件路径的类型构建命名空间到目录的映射。
    /// 同时确定程序集源文件的公共基础目录。
    /// </summary>
    private void BuildNamespaceDirMapping()
    {
        foreach (var (rowId, source) in _typeSources)
        {
            var handle = MetadataTokens.TypeDefinitionHandle(rowId);
            var typeDef = _peMetadata.GetTypeDefinition(handle);
            var ns = _peMetadata.GetString(typeDef.Namespace);

            // 对于嵌套类型，向外查找到所属类型的命名空间。
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

        // 确定公共基础目录（例如 "src/Orleans.Core/"）。
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
    /// 沿声明类型链向外查找，以构建类型的完整元数据名称，
    /// 从而正确处理嵌套类型。
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
    /// 将 Roslyn 泛型类型语法规范化为 PE 元数据的元数表示，
    /// 以便不同元数的名称仍能区分。
    /// <c>InteractionResult`1</c> 和 <c>InteractionResult&lt;T&gt;</c> 均规范化
    /// 为 <c>InteractionResult`1</c>。
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
    /// 汇总给定类型及其嵌套类型中所有方法的源码信息。
    /// 嵌套类型包含编译器为异步方法或迭代器方法生成的状态机，
    /// 其序列点映射回外层类型的源文件。
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
    /// 根据类型完整名称（由 Roslyn 的 ToDisplayString 生成）查找源码信息。
    /// 通过规范化名称匹配处理嵌套类型和泛型类型名称。
    /// 没有可用的 PDB 信息时返回 null。
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
    /// 对于没有 PDB 序列点的类型（接口、枚举、委托、仅含常量的类），
    /// 根据命名空间到目录的映射及类型名称推断可能的源文件路径。
    /// 只返回文件路径（不含行号）；无法推断时返回 null。
    /// </summary>
    public string? InferTypeSourceFile(string fullName, string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName)) return null;

        // 去除命名空间前缀以获得类型路径。
        var typePath = fullName.Length > namespaceName.Length + 1
            ? fullName[(namespaceName.Length + 1)..]
            : fullName;

        // 规范化名称，去除泛型参数。
        typePath = StripGenericArity(typePath);

        // 对于嵌套类型，使用最外层类型的名称作为文件名。
        var topLevelName = typePath.Contains('.')
            ? typePath[..typePath.IndexOf('.')]
            : typePath;
        var fileName = $"{topLevelName}.cs";

        // 优先直接查找命名空间到目录的映射。
        if (_namespaceToDirs.TryGetValue(namespaceName, out var dir))
        {
            return $"{dir}{fileName}";
        }

        // 回退：使用基础目录和相对命名空间构建路径。
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
    /// 通过匹配类型名称及成员名称，查找类型中某个成员的源码信息。
    /// 提供 <paramref name="parameterNames"/> 时，尝试匹配特定重载，
    /// 方法是比较 PE 元数据中的参数名称。
    /// <para>
    /// 仅当能够确信匹配到单个方法（唯一的
    /// 参数名称匹配，或只有一个同名方法）时，才返回准确的行号范围。多个方法匹配且无法区分时，
    /// 仅返回文件信息（<see cref="MethodSourceInfo.StartLine"/> == 0），
    /// 使调用方能够回退到文件级链接，而不使用误导性的行号范围。
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

        // 搜索方法：记录所有匹配项以评估匹配可信度。
        string? file = null;
        int minLine = int.MaxValue, maxLine = 0;
        int methodMatchCount = 0;
        MethodSourceInfo? paramExactMatch = null;
        int paramMatchCount = 0;

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var methodDef = _peMetadata.GetMethodDefinition(methodHandle);
            var mname = _peMetadata.GetString(methodDef.Name);

            // 匹配方法名称（构造函数：.ctor 对应的成员名为类型名称或 ".ctor"）。
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

            // 记录参数名称匹配项，以解析重载。
            if (parameterNames is not null && ParameterNamesMatch(methodDef, parameterNames))
            {
                paramExactMatch = info;
                paramMatchCount++;
            }

            // 累积用于合并回退范围的信息。
            file ??= info.File;
            if (info.StartLine < minLine) minLine = info.StartLine;
            if (info.EndLine > maxLine) maxLine = info.EndLine;
        }

        // 同时搜索字段（用于常量或静态字段）。
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var fieldDef = _peMetadata.GetFieldDefinition(fieldHandle);
            var fname = _peMetadata.GetString(fieldDef.Name);
            if (fname != memberName) continue;

            // 字段没有序列点，因此只返回文件信息（无行号范围）。
            if (_typeSources.TryGetValue(MetadataTokens.GetRowNumber(typeHandle), out var typeInfo))
            {
                return new MethodSourceInfo(typeInfo.File, 0, 0);
            }
        }

        // 检查编译器生成的嵌套类型（异步状态机、迭代器），
        // 它们的名称遵循 <MemberName>d__N 或类似模式。
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

        // 根据匹配可信度返回结果：
        // 1. 参数名称唯一匹配：可信。
        if (paramExactMatch is not null && paramMatchCount == 1)
        {
            return paramExactMatch;
        }

        // 2. 按名称仅匹配到一个方法（无重载）：可信。
        if (methodMatchCount == 1 && file is not null)
        {
            return new MethodSourceInfo(file, minLine, maxLine);
        }

        // 3. 多个方法或匹配不明确：只返回文件（无行号范围）。
        if (file is not null)
        {
            return new MethodSourceInfo(file, 0, 0);
        }

        return null;
    }

    /// <summary>
    /// 检查方法定义的参数名称是否与预期名称完全匹配。
    /// 跳过序列号 0（表示返回值的伪参数）。
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
    /// 从源码路径中去除确定性构建的路径前缀（例如 "/_/"）。
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
/// 源文件及行号范围信息。
/// 当 <see cref="StartLine"/> 为 0 时，行号范围未知，只有文件路径有意义。
/// </summary>
internal sealed record MethodSourceInfo(string File, int StartLine, int EndLine)
{
    /// <summary>返回适合 GitHub 的行号范围，如 "15-200"。</summary>
    public string ToLineRange() => $"{StartLine}-{EndLine}";
}
