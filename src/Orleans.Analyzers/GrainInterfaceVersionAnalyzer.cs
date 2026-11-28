#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Orleans.CodeGenerator;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Orleans.Analyzers;

/// <summary>
/// 跟踪 Grain 接口定义及其版本的分析器。
/// 确保接口变更伴随版本递增。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class GrainInterfaceVersionAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticCategory = "Versioning";

    /// <summary>
    /// 启用 Orleans 契约兼容性分析的 MSBuild 属性。
    /// </summary>
    public const string EnableAnalyzerPropertyName = "EnableOrleansContractsAnalyzer";

    /// <summary>
    /// Grain 接口在 OrleansContracts.txt 中没有有效声明时的诊断标识符。
    /// </summary>
    public const string RuleId0016 = "ORLEANS0016";

    /// <summary>
    /// Grain 接口版本与 OrleansContracts.txt 不一致时的诊断标识符。
    /// </summary>
    public const string RuleId0017 = "ORLEANS0017";

    /// <summary>
    /// Grain 接口成员未在 OrleansContracts.txt 中声明时的诊断标识符。
    /// </summary>
    public const string RuleId0018 = "ORLEANS0018";

    /// <summary>
    /// 已删除的 Grain 接口未在 OrleansContracts.txt 中标记为退役时的诊断标识符。
    /// </summary>
    public const string RuleId0019 = "ORLEANS0019";

    /// <summary>
    /// 缺少 OrleansContracts.txt 文件时的诊断标识符。
    /// </summary>
    public const string RuleId0020 = "ORLEANS0020";

    /// <summary>
    /// OrleansContracts.txt 中 Grain 接口声明重复时的诊断标识符。
    /// </summary>
    public const string RuleId0021 = "ORLEANS0021";

    /// <summary>
    /// Grain 类在 OrleansContracts.txt 中没有有效声明时的诊断标识符。
    /// </summary>
    public const string RuleId0022 = "ORLEANS0022";

    /// <summary>
    /// Grain 类标识与 OrleansContracts.txt 不一致时的诊断标识符。
    /// </summary>
    public const string RuleId0023 = "ORLEANS0023";

    /// <summary>
    /// 已删除的 Grain 类未在 OrleansContracts.txt 中标记为退役时的诊断标识符。
    /// </summary>
    public const string RuleId0024 = "ORLEANS0024";

    /// <summary>
    /// OrleansContracts.txt 中 Grain 类声明重复时的诊断标识符。
    /// </summary>
    public const string RuleId0025 = "ORLEANS0025";

    /// <summary>
    /// 从源码移除后仍保留在 OrleansContracts.txt 中的 Grain 接口成员的诊断标识符。
    /// </summary>
    public const string RuleId0027 = "ORLEANS0027";

    // 供代码修复使用的属性集合键。
    internal const string InterfaceNamePropertyKey = "InterfaceName";
    internal const string MemberNamePropertyKey = "MemberName";
    internal const string MemberWireIdentityPropertyKey = "MemberWireIdentity";
    internal const string ExpectedVersionPropertyKey = "ExpectedVersion";
    internal const string ActualVersionPropertyKey = "ActualVersion";
    internal const string ExpectedSignaturePropertyKey = "ExpectedSignature";
    internal const string ActualSignaturePropertyKey = "ActualSignature";
    internal const string ClassNamePropertyKey = "ClassName";
    internal const string ActualAliasPropertyKey = "ActualAlias";
    internal const string GrainInterfaceTypePropertyKey = "GrainInterfaceType";

    internal const string RetiredPrefix = "*RETIRED*";

    private static readonly DiagnosticDescriptor InterfaceNotDeclaredRule = new(
        id: RuleId0016,
        title: new LocalizableResourceString(nameof(Resources.GrainInterfaceNotDeclaredTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainInterfaceNotDeclaredMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainInterfaceNotDeclaredDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0016));

    private static readonly DiagnosticDescriptor InterfaceVersionMismatchRule = new(
        id: RuleId0017,
        title: new LocalizableResourceString(nameof(Resources.GrainInterfaceVersionMismatchTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainInterfaceVersionMismatchMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainInterfaceVersionMismatchDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0017));

    private static readonly DiagnosticDescriptor MemberNotDeclaredRule = new(
        id: RuleId0018,
        title: new LocalizableResourceString(nameof(Resources.GrainInterfaceMemberNotDeclaredTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainInterfaceMemberNotDeclaredMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainInterfaceMemberNotDeclaredDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0018));

    private static readonly DiagnosticDescriptor RemovedMemberRule = new(
        id: RuleId0027,
        title: new LocalizableResourceString(nameof(Resources.GrainInterfaceMemberRemovedTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainInterfaceMemberRemovedMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainInterfaceMemberRemovedDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0027),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static readonly DiagnosticDescriptor RemovedInterfaceNotRetiredRule = new(
        id: RuleId0019,
        title: new LocalizableResourceString(nameof(Resources.GrainInterfaceRemovedNotRetiredTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainInterfaceRemovedNotRetiredMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainInterfaceRemovedNotRetiredDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0019),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static readonly DiagnosticDescriptor OrleansContractsFileMissingRule = new(
        id: RuleId0020,
        title: new LocalizableResourceString(nameof(Resources.OrleansContractsFileMissingTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.OrleansContractsFileMissingMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.OrleansContractsFileMissingDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0020),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor DuplicateInterfaceDeclarationRule = new(
        id: RuleId0021,
        title: new LocalizableResourceString(nameof(Resources.GrainInterfaceDuplicateDeclarationTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainInterfaceDuplicateDeclarationMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainInterfaceDuplicateDeclarationDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0021));

    private static readonly DiagnosticDescriptor GrainClassNotDeclaredRule = new(
        id: RuleId0022,
        title: new LocalizableResourceString(nameof(Resources.GrainClassNotDeclaredTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainClassNotDeclaredMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainClassNotDeclaredDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0022));

    private static readonly DiagnosticDescriptor GrainClassAliasMismatchRule = new(
        id: RuleId0023,
        title: new LocalizableResourceString(nameof(Resources.GrainClassAliasMismatchTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainClassAliasMismatchMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainClassAliasMismatchDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0023));

    private static readonly DiagnosticDescriptor RemovedGrainClassNotRetiredRule = new(
        id: RuleId0024,
        title: new LocalizableResourceString(nameof(Resources.GrainClassRemovedNotRetiredTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainClassRemovedNotRetiredMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainClassRemovedNotRetiredDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0024),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor DuplicateGrainClassDeclarationRule = new(
        id: RuleId0025,
        title: new LocalizableResourceString(nameof(Resources.GrainClassDuplicateDeclarationTitle), Resources.ResourceManager, typeof(Resources)),
        messageFormat: new LocalizableResourceString(nameof(Resources.GrainClassDuplicateDeclarationMessageFormat), Resources.ResourceManager, typeof(Resources)),
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(nameof(Resources.GrainClassDuplicateDeclarationDescription), Resources.ResourceManager, typeof(Resources)),
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId0025));

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            InterfaceNotDeclaredRule,
            InterfaceVersionMismatchRule,
            MemberNotDeclaredRule,
            RemovedInterfaceNotRetiredRule,
            OrleansContractsFileMissingRule,
            DuplicateInterfaceDeclarationRule,
            GrainClassNotDeclaredRule,
            GrainClassAliasMismatchRule,
            RemovedGrainClassNotRetiredRule,
            DuplicateGrainClassDeclarationRule,
            RemovedMemberRule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        if (!context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
                $"build_property.{EnableAnalyzerPropertyName}",
                out var enabled)
            || !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 尝试查找 OrleansContracts.txt 文件。
        var grainInterfacesFile = context.Options.AdditionalFiles
            .FirstOrDefault(file =>
                Path.GetFileName(file.Path).Equals(Constants.OrleansContractsFileName, StringComparison.OrdinalIgnoreCase)
                || context.Options.AnalyzerConfigOptionsProvider.GetOptions(file)
                    .TryGetValue("build_metadata.AdditionalFiles.OrleansContractsFile", out var value)
                    && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        GrainInterfaceData? data = null;
        SourceText? sourceText = null;
        List<Diagnostic>? fileParseErrors = null;

        if (grainInterfacesFile is not null)
        {
            sourceText = grainInterfacesFile.GetText(context.CancellationToken);
            if (sourceText is not null)
            {
                (data, fileParseErrors) = GrainInterfaceFileParser.Parse(sourceText, grainInterfacesFile.Path);
            }
        }

        var impl = new Impl(context.Compilation, data, grainInterfacesFile, sourceText, fileParseErrors);

        context.RegisterSymbolAction(impl.AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterCompilationEndAction(impl.OnCompilationEnd);
    }

    private sealed class Impl
    {
        private readonly GrainInterfaceData? _data;
        private readonly AdditionalText? _grainInterfacesFile;
        private readonly SourceText? _grainInterfacesFileText;
        private readonly List<Diagnostic>? _fileParseErrors;
        private readonly ConcurrentDictionary<string, bool> _visitedInterfaces = new(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<DeclaredGrainInterface, bool> _matchedInterfaces = new();
        private readonly LegacyGrainInterfaceTypeResolver _legacyGrainInterfaceTypes;
        private readonly ConcurrentDictionary<string, bool> _visitedClasses = new(StringComparer.Ordinal);
        private readonly ConcurrentBag<Diagnostic> _removedMemberDiagnostics = new();
        private readonly INamedTypeSymbol? _iAddressableType;
        private readonly INamedTypeSymbol? _aliasAttributeType;
        private readonly INamedTypeSymbol? _versionAttributeType;
        private Location? _firstContractLocation;

        public Impl(
            Compilation compilation,
            GrainInterfaceData? data,
            AdditionalText? grainInterfacesFile,
            SourceText? grainInterfacesFileText,
            List<Diagnostic>? fileParseErrors)
        {
            _data = data;
            _grainInterfacesFile = grainInterfacesFile;
            _grainInterfacesFileText = grainInterfacesFileText;
            _legacyGrainInterfaceTypes = new LegacyGrainInterfaceTypeResolver(compilation);
            _fileParseErrors = AddLegacyDuplicateDeclarations(fileParseErrors);

            _iAddressableType = compilation.GetTypeByMetadataName(Constants.IAddressibleFullyQualifiedName);
            _aliasAttributeType = compilation.GetTypeByMetadataName(Constants.AliasAttributeFullyQualifiedName);
            _versionAttributeType = compilation.GetTypeByMetadataName(Constants.VersionAttributeFullyQualifiedName);
        }

        public void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var namedType = (INamedTypeSymbol)context.Symbol;

            if (namedType.TypeKind == TypeKind.Class && !namedType.IsAbstract && namedType.IsGrainClass())
            {
                AnalyzeGrainClass(context, namedType);
                return;
            }

            if (namedType.TypeKind != TypeKind.Interface)
            {
                return;
            }

            // 检查是否为 RPC 契约（继承 IAddressable）。
            if (!IsRpcContract(namedType))
            {
                return;
            }

            var interfaceName = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "");

            // 如果文件不存在，则报告接口未被跟踪。
            if (_data is null)
            {
                _visitedInterfaces.TryAdd(interfaceName, true);
                RecordContractLocation(namedType);
                // 将在编译结束时报告文件缺失。
                return;
            }

            // 检查接口是否已在文件中声明。
            var grainInterfaceType = GetGrainInterfaceType(namedType);
            var interfaceProperties = ImmutableDictionary<string, string?>.Empty
                .Add(InterfaceNamePropertyKey, interfaceName)
                .Add(GrainInterfaceTypePropertyKey, grainInterfaceType);

            var declaredInterface = FindDeclaredInterface(grainInterfaceType);
            if (declaredInterface is null)
            {
                // 文件中未找到此接口。
                var properties = interfaceProperties;

                foreach (var location in namedType.Locations.Where(l => l.IsInSource))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InterfaceNotDeclaredRule,
                        location,
                        properties,
                        interfaceName));
                }
                return;
            }

            _matchedInterfaces.TryAdd(declaredInterface, true);

            // 检查是否已退役。
            if (declaredInterface.IsRetired)
            {
                // 接口仍存在于代码中，但在文件中标记为退役。
                // 这可以作为独立诊断，但目前将其视为不匹配。
                var properties = interfaceProperties;

                foreach (var location in namedType.Locations.Where(l => l.IsInSource))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InterfaceNotDeclaredRule,
                        location,
                        properties,
                        interfaceName));
                }
                return;
            }

            // 检查版本特性是否匹配。
            var codeVersion = GetVersionFromAttribute(namedType);
            if (codeVersion != declaredInterface.Version)
            {
                var properties = interfaceProperties
                    .Add(ExpectedVersionPropertyKey, declaredInterface.Version.ToString())
                    .Add(ActualVersionPropertyKey, codeVersion.ToString());

                foreach (var location in namedType.Locations.Where(l => l.IsInSource))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InterfaceVersionMismatchRule,
                        location,
                        properties,
                        interfaceName,
                        declaredInterface.Version,
                        codeVersion));
                }
            }

            // 检查别名是否匹配。
            var codeAlias = GetAliasFromAttribute(namedType);
            if (!string.Equals(codeAlias, declaredInterface.Alias, StringComparison.Ordinal))
            {
                // 别名不匹配，可为此添加独立诊断。
            }

            var sourceMembers = namedType.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(member => member.MethodKind == MethodKind.Ordinary && !member.IsStatic)
                .ToArray();

            // 检查成员。
            foreach (var member in sourceMembers)
            {
                var declaredMember = declaredInterface.Members.Values
                    .Where(candidate => GrainInterfaceVersionAnalyzer.IsMatchingMember(
                        declaredInterface.Name,
                        candidate.Signature,
                        candidate.WireIdentity,
                        member))
                    .FirstOrDefault();

                if (declaredMember is null)
                {
                    // 未找到成员，接口已发生变更。
                    var properties = interfaceProperties
                        .Add(MemberNamePropertyKey, GetManifestMethodSignature(member))
                        .Add(MemberWireIdentityPropertyKey, GetMethodWireIdentity(member).ToManifestMetadata());

                    foreach (var location in member.Locations.Where(l => l.IsInSource))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MemberNotDeclaredRule,
                            location,
                            properties,
                            GetClrMethodSignature(member),
                            GetMethodWireIdentity(member).Value,
                            interfaceName));
                    }
                }
            }

            if (_grainInterfacesFile is not null && _grainInterfacesFileText is { } sourceText)
            {
                foreach (var declaredMember in declaredInterface.Members.Values)
                {
                    if (sourceMembers.Any(member =>
                        GrainInterfaceVersionAnalyzer.IsMatchingHistoricalMember(
                            declaredInterface.Name,
                            declaredMember.Signature,
                            declaredMember.WireIdentity,
                            member)))
                    {
                        continue;
                    }

                    var properties = interfaceProperties
                        .Add(MemberNamePropertyKey, declaredMember.Signature);
                    var declaredMemberLine = GrainInterfaceFileParser.FormatMemberDeclaration(
                        declaredMember.Signature,
                        declaredMember.WireIdentity);
                    _removedMemberDiagnostics.Add(Diagnostic.Create(
                        RemovedMemberRule,
                        declaredMember.GetLocation(sourceText, _grainInterfacesFile.Path),
                        properties,
                        declaredMemberLine,
                        interfaceName));
                }
            }
        }

        public void OnCompilationEnd(CompilationAnalysisContext context)
        {
            // 报告文件解析错误。
            if (_fileParseErrors is not null)
            {
                foreach (var error in _fileParseErrors)
                {
                    context.ReportDiagnostic(error);
                }
            }

            foreach (var diagnostic in _removedMemberDiagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            // 如果发现任何 Grain 接口但文件不存在，则报告文件缺失。
            if (_data is null && (_visitedInterfaces.Count > 0 || _visitedClasses.Count > 0))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    OrleansContractsFileMissingRule,
                    _firstContractLocation ?? Location.None,
                    Constants.OrleansContractsFileName));
            }

            // 检查已删除的接口（文件中存在但代码中不存在）。
            if (_data is not null && _grainInterfacesFile is not null)
            {
                if (_grainInterfacesFileText is { } sourceText)
                {
                    foreach (var declaredInterface in _data.Interfaces)
                    {
                        if (declaredInterface.IsRetired)
                        {
                            continue;
                        }

                        if (!_matchedInterfaces.ContainsKey(declaredInterface))
                        {
                            // 接口在文件中存在但在代码中不存在，需要标记为退役。
                            var location = declaredInterface.GetLocation(sourceText, _grainInterfacesFile.Path);

                            var properties = ImmutableDictionary<string, string?>.Empty
                                .Add(InterfaceNamePropertyKey, declaredInterface.Name)
                                .Add(GrainInterfaceTypePropertyKey, declaredInterface.GrainInterfaceType);

                            context.ReportDiagnostic(Diagnostic.Create(
                                RemovedInterfaceNotRetiredRule,
                                location,
                                properties,
                                declaredInterface.Name));
                        }
                    }

                    foreach (var declaredClass in _data.Classes)
                    {
                        if (declaredClass.IsRetired || _visitedClasses.ContainsKey(GetDeclarationKey(declaredClass)))
                        {
                            continue;
                        }

                        var location = declaredClass.GetLocation(sourceText, _grainInterfacesFile.Path);
                        var properties = ImmutableDictionary<string, string?>.Empty
                            .Add(ClassNamePropertyKey, declaredClass.Name)
                            .Add(ActualAliasPropertyKey, declaredClass.Alias);

                        context.ReportDiagnostic(Diagnostic.Create(
                            RemovedGrainClassNotRetiredRule,
                            location,
                            properties,
                            declaredClass.Name));
                    }
                }
            }
        }

        private void AnalyzeGrainClass(SymbolAnalysisContext context, INamedTypeSymbol namedType)
        {
            var className = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");

            if (_data is null)
            {
                _visitedClasses.TryAdd(className, true);
                RecordContractLocation(namedType);
                return;
            }

            var codeAlias = GetGrainType(namedType);
            var declaredClass = FindDeclaredClass(className, codeAlias);
            if (declaredClass is null || declaredClass.IsRetired)
            {
                _visitedClasses.TryAdd(className, true);
                var properties = ImmutableDictionary<string, string?>.Empty.Add(ClassNamePropertyKey, className);
                foreach (var location in namedType.Locations.Where(location => location.IsInSource))
                {
                    context.ReportDiagnostic(Diagnostic.Create(GrainClassNotDeclaredRule, location, properties, className));
                }

                return;
            }

            _visitedClasses.TryAdd(GetDeclarationKey(declaredClass), true);
            if (declaredClass.Alias is null
                || string.Equals(codeAlias, declaredClass.Alias, StringComparison.Ordinal))
            {
                return;
            }

            var aliasProperties = ImmutableDictionary<string, string?>.Empty
                .Add(ClassNamePropertyKey, className)
                .Add(ActualAliasPropertyKey, codeAlias);
            foreach (var location in namedType.Locations.Where(location => location.IsInSource))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GrainClassAliasMismatchRule,
                    location,
                    aliasProperties,
                    className,
                    declaredClass.Alias ?? "<none>",
                    codeAlias ?? "<none>"));
            }
        }

        private bool IsRpcContract(INamedTypeSymbol type)
        {
            if (_iAddressableType is null)
            {
                return false;
            }

            return !SymbolEqualityComparer.Default.Equals(type, _iAddressableType)
                && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _iAddressableType));
        }

        private void RecordContractLocation(INamedTypeSymbol type)
        {
            var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
            if (location is not null)
            {
                Interlocked.CompareExchange(ref _firstContractLocation, location, null);
            }
        }

        private DeclaredGrainInterface? FindDeclaredInterface(string grainInterfaceType)
        {
            return _data!.Interfaces.FirstOrDefault(candidate =>
                    string.Equals(candidate.GrainInterfaceType, grainInterfaceType, StringComparison.Ordinal))
                ?? _data.Interfaces.FirstOrDefault(candidate =>
                    candidate.GrainInterfaceType is null
                    && string.Equals(
                        _legacyGrainInterfaceTypes.Resolve(candidate.Name),
                        grainInterfaceType,
                        StringComparison.Ordinal));
        }

        // 解析时仅在旧式声明之间相互比较，因为它们的标识依赖源码类型。
        private List<Diagnostic>? AddLegacyDuplicateDeclarations(List<Diagnostic>? errors)
        {
            if (_data is null || _grainInterfacesFile is null || _grainInterfacesFileText is null)
            {
                return errors;
            }

            foreach (var legacy in _data.Interfaces.Where(declaration => declaration.GrainInterfaceType is null))
            {
                var grainInterfaceType = _legacyGrainInterfaceTypes.Resolve(legacy.Name);
                foreach (var recorded in _data.Interfaces.Where(declaration =>
                    string.Equals(declaration.GrainInterfaceType, grainInterfaceType, StringComparison.Ordinal)))
                {
                    // 与解析阶段一样，报告后出现的声明。
                    var duplicate = legacy.Span.Start > recorded.Span.Start ? legacy : recorded;
                    errors ??= new List<Diagnostic>();
                    errors.Add(Diagnostic.Create(
                        DuplicateInterfaceDeclarationRule,
                        duplicate.GetLocation(_grainInterfacesFileText, _grainInterfacesFile.Path),
                        duplicate.Name));
                }
            }

            return errors;
        }

        private DeclaredGrainClass? FindDeclaredClass(string className, string? alias)
        {
            if (alias is not null)
            {
                var stableMatch = _data!.Classes.FirstOrDefault(candidate =>
                    string.Equals(GetDeclarationKey(candidate), alias, StringComparison.Ordinal));
                if (stableMatch is not null)
                {
                    return stableMatch;
                }

                return _data.Classes.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, className, StringComparison.Ordinal));
            }

            return _data!.Classes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, className, StringComparison.Ordinal));
        }

        private static string GetDeclarationKey(DeclaredGrainClass declaration)
            => declaration.Alias ?? GetDefaultGrainType(declaration.Name);

        private ushort GetVersionFromAttribute(INamedTypeSymbol type)
        {
            if (_versionAttributeType is null)
            {
                return 0;
            }

            foreach (var attribute in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _versionAttributeType))
                {
                    if (attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is ushort version)
                    {
                        return version;
                    }
                }
            }

            return 0;
        }

        private string? GetAliasFromAttribute(ISymbol symbol)
        {
            if (_aliasAttributeType is null)
            {
                return null;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _aliasAttributeType))
                {
                    if (attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is string alias)
                    {
                        return alias;
                    }
                }
            }

            return null;
        }

    }

    internal static string GetGrainType(INamedTypeSymbol type)
    {
        if (GetStringAttributeValue(type, Constants.GrainTypeAttributeFullyQualifiedName) is { } grainType)
        {
            return grainType;
        }

        return GetDefaultGrainType(type);
    }

    internal static string GetDefaultGrainType(INamedTypeSymbol type)
    {
        var name = type.Name.ToLowerInvariant();

        const string GrainSuffix = "grain";
        if (name.EndsWith(GrainSuffix, StringComparison.Ordinal) && name.Length > GrainSuffix.Length)
        {
            name = name.Substring(0, name.Length - GrainSuffix.Length);
        }

        var arity = 0;
        for (var current = type; current is not null; current = current.ContainingType)
        {
            arity += current.Arity;
        }

        return arity > 0 ? $"{name}`{arity}" : name;
    }

    internal static string GetDefaultGrainType(string typeName)
    {
        var simpleNameStart = typeName.LastIndexOf('.') + 1;
        var simpleName = typeName.Substring(simpleNameStart);
        var genericStart = simpleName.IndexOf('<');
        if (genericStart >= 0)
        {
            simpleName = simpleName.Substring(0, genericStart);
        }

        var name = simpleName.ToLowerInvariant();
        const string GrainSuffix = "grain";
        if (name.EndsWith(GrainSuffix, StringComparison.Ordinal) && name.Length > GrainSuffix.Length)
        {
            name = name.Substring(0, name.Length - GrainSuffix.Length);
        }

        var arity = 0;
        var searchIndex = 0;
        while ((genericStart = typeName.IndexOf('<', searchIndex)) >= 0)
        {
            var genericEnd = typeName.IndexOf('>', genericStart + 1);
            if (genericEnd < 0)
            {
                break;
            }

            arity++;
            for (var index = genericStart + 1; index < genericEnd; index++)
            {
                if (typeName[index] == ',')
                {
                    arity++;
                }
            }

            searchIndex = genericEnd + 1;
        }

        return arity > 0 ? $"{name}`{arity}" : name;
    }

    internal static string GetGrainInterfaceType(INamedTypeSymbol type)
    {
        if (GetStringAttributeValue(type, Constants.GrainInterfaceTypeAttributeFullyQualifiedName) is { } grainInterfaceType)
        {
            return grainInterfaceType;
        }

        return GetGrainInterfaceTypeByConvention(type);
    }

    /// <summary>
    /// 与 <c>GrainInterfaceTypeResolver.GetGrainInterfaceTypeByConvention</c> 的行为一致，其类型名称格式化使用
    /// 接口或其外层类型的 <c>[Alias]</c> 替代其 CLR 名称。
    /// </summary>
    private static string GetGrainInterfaceTypeByConvention(INamedTypeSymbol type)
    {
        if (GetStringAttributeValue(type, Constants.AliasAttributeFullyQualifiedName) is { } alias)
        {
            return alias;
        }

        if (type.ContainingType is { } containingType)
        {
            return $"{GetGrainInterfaceTypeByConvention(containingType)}+{type.MetadataName}";
        }

        return GetDefaultGrainInterfaceType(type);
    }

    /// <summary>
    /// 当 Grain 接口类型不同于 CLR 命名约定时返回该接口类型，否则返回 <see langword="null"/>。
    /// </summary>
    internal static string? GetNonDefaultGrainInterfaceType(INamedTypeSymbol type)
    {
        var grainInterfaceType = GetGrainInterfaceType(type);
        return GetStringAttributeValue(type, Constants.GrainInterfaceTypeAttributeFullyQualifiedName) is not null
            || !string.Equals(grainInterfaceType, GetDefaultGrainInterfaceType(type), StringComparison.Ordinal)
                ? grainInterfaceType
                : null;
    }

    internal static string GetDefaultGrainInterfaceType(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } containingType)
        {
            return $"{GetDefaultGrainInterfaceType(containingType)}+{type.MetadataName}";
        }

        return type.ContainingNamespace.IsGlobalNamespace
            ? type.MetadataName
            : $"{type.ContainingNamespace.ToDisplayString()}.{type.MetadataName}";
    }

    internal static string GetDefaultGrainInterfaceType(string typeName)
    {
        var segments = typeName.Split('.');
        var firstGenericSegment = Array.FindIndex(segments, segment => segment.IndexOf('<') >= 0);
        if (firstGenericSegment < 0)
        {
            return typeName;
        }

        for (var index = firstGenericSegment; index < segments.Length; index++)
        {
            var genericStart = segments[index].IndexOf('<');
            if (genericStart < 0)
            {
                continue;
            }

            var genericEnd = segments[index].LastIndexOf('>');
            var arity = 1;
            for (var characterIndex = genericStart + 1; characterIndex < genericEnd; characterIndex++)
            {
                if (segments[index][characterIndex] == ',')
                {
                    arity++;
                }
            }

            segments[index] = $"{segments[index].Substring(0, genericStart)}`{arity}";
        }

        return string.Join(".", segments.Take(firstGenericSegment))
            + (firstGenericSegment > 0 ? "." : string.Empty)
            + string.Join("+", segments.Skip(firstGenericSegment));
    }

    internal static string GetMethodSignature(IMethodSymbol method)
    {
        var identity = GetMethodWireIdentity(method);
        return GetMethodSignature(method, identity.Value);
    }

    private static string GetMethodSignature(IMethodSymbol method, object methodId)
    {
        var sb = new StringBuilder();
        sb.Append(methodId);
        if (method.Arity > 0)
        {
            sb.Append('`');
            sb.Append(method.Arity);
        }
        sb.Append('(');

        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(GetContractTypeName(method.Parameters[i].Type));
        }

        sb.Append(')');
        sb.Append(" -> ");
        sb.Append(GetContractTypeName(method.ReturnType));

        return sb.ToString();
    }

    internal static string GetManifestMethodSignature(IMethodSymbol method)
        => GetMethodSignature(method, method.Name);

    internal static MethodWireIdentity GetMethodWireIdentity(IMethodSymbol method)
    {
        if (GetAttributeValue(method, Constants.IdAttributeFullyQualifiedName) is { } methodId)
        {
            return new MethodWireIdentity(Convert.ToString(methodId, CultureInfo.InvariantCulture)!);
        }

        if (GetStringAttributeValue(method, Constants.AliasAttributeFullyQualifiedName) is { } methodAlias)
        {
            return new MethodWireIdentity(methodAlias);
        }

        return new MethodWireIdentity(MethodIdProvider.Create(method));
    }

    internal static string GetClrMethodSignature(IMethodSymbol method)
    {
        var sb = new StringBuilder();
        sb.Append(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""));
        sb.Append('.');
        sb.Append(method.Name);
        if (method.Arity > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", method.TypeParameters.Select(parameter => parameter.Name)));
            sb.Append('>');
        }
        sb.Append('(');

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(method.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            sb.Append(' ');
            sb.Append(method.Parameters[i].Name);
        }

        sb.Append(") -> ");
        sb.Append(method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        return sb.ToString();
    }

    internal static bool IdentityDiffersFromClrName(string? identity, INamedTypeSymbol type)
    {
        if (identity is null)
        {
            return false;
        }

        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
        return !string.Equals(identity, type.Name, StringComparison.Ordinal)
            && !string.Equals(identity, type.MetadataName, StringComparison.Ordinal)
            && !string.Equals(identity, fullName, StringComparison.Ordinal);
    }

    internal static string NormalizeStoredMemberSignature(string signature, string interfaceName)
    {
        var result = signature;
        if (result.StartsWith($"{interfaceName}.", StringComparison.Ordinal))
        {
            result = result.Substring(interfaceName.Length + 1);
        }

        result = Regex.Replace(result, @"^grain-interface\(""[^""]+""\)\.", "");
        return Regex.Replace(result, @"alias\(""([^""]+)""\)", "$1");
    }

    internal static bool IsMatchingMember(
        string declaredInterfaceName,
        string storedSignature,
        MethodWireIdentity storedWireIdentity,
        IMethodSymbol member)
    {
        var sourceWireIdentity = GetMethodWireIdentity(member);
        return string.Equals(
                storedWireIdentity.Value,
                sourceWireIdentity.Value,
                StringComparison.Ordinal)
            && string.Equals(
                GrainInterfaceFileParser.GetMethodAritySuffix(storedSignature),
                GrainInterfaceFileParser.GetMethodAritySuffix(GetManifestMethodSignature(member)),
                StringComparison.Ordinal)
            && string.Equals(
                GetSignatureSuffix(storedSignature),
                GetSignatureSuffix(GetManifestMethodSignature(member)),
                StringComparison.Ordinal);
    }

    internal static bool IsMatchingHistoricalMember(
        string declaredInterfaceName,
        string storedSignature,
        MethodWireIdentity storedWireIdentity,
        IMethodSymbol member)
    {
        return IsMatchingMember(declaredInterfaceName, storedSignature, storedWireIdentity, member);
    }

    private static string GetSignatureSuffix(string signature)
    {
        var parameterListStart = signature.IndexOf('(');
        return parameterListStart < 0 ? signature : signature.Substring(parameterListStart);
    }

    private static string GetContractTypeName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return $"{GetContractTypeName(array.ElementType)}[{new string(',', array.Rank - 1)}]";
        }

        if (type is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.Name;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        if (namedType.IsTupleType)
        {
            return $"({string.Join(", ", namedType.TupleElements.Select(element => GetContractTypeName(element.Type)))})";
        }

        var alias = GetStringAttributeValue(namedType.OriginalDefinition, Constants.AliasAttributeFullyQualifiedName);
        if (alias is not null)
        {
            return namedType.TypeArguments.IsEmpty
                ? alias
                : $"{alias}<{string.Join(", ", namedType.TypeArguments.Select(GetContractTypeName))}>";
        }

        if (namedType is
            {
                Name: "Task" or "ValueTask",
                ContainingNamespace: { } containingNamespace
            }
            && containingNamespace.ToDisplayString() == "System.Threading.Tasks")
        {
            if (namedType.TypeArguments.IsEmpty)
            {
                return namedType.Name;
            }

            return $"{namedType.Name}<{string.Join(", ", namedType.TypeArguments.Select(GetContractTypeName))}>";
        }

        if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return $"{GetContractTypeName(namedType.TypeArguments[0])}?";
        }

        if (namedType.TypeArguments.IsEmpty)
        {
            return namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        var genericName = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var typeArgumentStart = genericName.IndexOf('<');
        if (typeArgumentStart >= 0)
        {
            genericName = genericName.Substring(0, typeArgumentStart);
        }

        return $"{genericName}<{string.Join(", ", namedType.TypeArguments.Select(GetContractTypeName))}>";
    }

    private static object? GetAttributeValue(ISymbol symbol, string attributeName)
        => symbol.GetAttributes()
            .FirstOrDefault(attribute => string.Equals(attribute.AttributeClass?.ToDisplayString(), attributeName, StringComparison.Ordinal))
            ?.ConstructorArguments.FirstOrDefault().Value;

    private static string? GetStringAttributeValue(ISymbol symbol, string attributeName)
        => GetAttributeValue(symbol, attributeName) as string;
}

/// <summary>
/// 解析旧式声明所描述的 Grain 接口类型，该声明仅记录 CLR 名称。
/// </summary>
/// <remarks>
/// CLR 名称无法区分嵌套类型和命名空间，因此根据名称所指的源码接口
/// 进行解析，只有找不到此接口时才仅根据名称推导标识。
/// </remarks>
internal sealed class LegacyGrainInterfaceTypeResolver
{
    private readonly IAssemblySymbol _assembly;
    private readonly ConcurrentDictionary<string, string> _grainInterfaceTypes = new(StringComparer.Ordinal);

    public LegacyGrainInterfaceTypeResolver(Compilation compilation)
    {
        _assembly = compilation.Assembly;
    }

    public string Resolve(string declaredName)
        => _grainInterfaceTypes.GetOrAdd(
            declaredName,
            name => FindSourceInterface(name) is { } type
                ? GrainInterfaceVersionAnalyzer.GetDefaultGrainInterfaceType(type)
                : GrainInterfaceVersionAnalyzer.GetDefaultGrainInterfaceType(name));

    private INamedTypeSymbol? FindSourceInterface(string declaredName)
    {
        var segments = declaredName.Split('.');

        // 尝试命名空间和可能嵌套的类型名称之间的每一种划分方式，最长的命名空间优先。
        for (var namespaceLength = segments.Length - 1; namespaceLength >= 0; namespaceLength--)
        {
            if (segments.Take(namespaceLength).Any(segment => segment.IndexOf('<') >= 0))
            {
                continue;
            }

            var typeName = string.Join("+", segments.Skip(namespaceLength).Select(GetMetadataName));
            var metadataName = namespaceLength == 0
                ? typeName
                : $"{string.Join(".", segments.Take(namespaceLength))}.{typeName}";
            if (_assembly.GetTypeByMetadataName(metadataName) is { TypeKind: TypeKind.Interface } type)
            {
                return type;
            }
        }

        return null;
    }

    private static string GetMetadataName(string segment)
    {
        var genericStart = segment.IndexOf('<');
        return genericStart < 0
            ? segment
            : $"{segment.Substring(0, genericStart)}`{segment.Count(character => character == ',') + 1}";
    }
}

/// <summary>
/// 表示从 OrleansContracts.txt 文件解析的数据。
/// </summary>
internal sealed class GrainInterfaceData
{
    public List<DeclaredGrainInterface> Interfaces { get; } = new();

    public List<DeclaredGrainClass> Classes { get; } = new();

}

/// <summary>
/// 表示 OrleansContracts.txt 文件中声明的 Grain 接口。
/// </summary>
internal sealed class DeclaredGrainInterface
{
    public DeclaredGrainInterface(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public string? Alias { get; set; }

    public string? GrainInterfaceType { get; set; }
    public ushort Version { get; set; }
    public bool IsRetired { get; set; }
    public TextSpan Span { get; set; }
    public Dictionary<string, DeclaredGrainMember> Members { get; } = new Dictionary<string, DeclaredGrainMember>(StringComparer.Ordinal);

    public Location GetLocation(SourceText sourceText, string filePath)
    {
        var lineSpan = sourceText.Lines.GetLinePositionSpan(Span);
        return Location.Create(filePath, Span, lineSpan);
    }
}

/// <summary>
/// 表示 OrleansContracts.txt 文件中声明的 Grain 接口成员。
/// </summary>
internal sealed class DeclaredGrainMember
{
    public DeclaredGrainMember(string signature, MethodWireIdentity wireIdentity)
    {
        Signature = signature;
        WireIdentity = wireIdentity;
    }

    public string Signature { get; }
    public MethodWireIdentity WireIdentity { get; }
    public TextSpan Span { get; set; }

    public Location GetLocation(SourceText sourceText, string filePath)
    {
        var lineSpan = sourceText.Lines.GetLinePositionSpan(Span);
        return Location.Create(filePath, Span, lineSpan);
    }
}

internal readonly struct MethodWireIdentity
{
    public MethodWireIdentity(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public string ToManifestMetadata()
    {
        var result = new StringBuilder(Value.Length);
        foreach (var character in Value)
        {
            result.Append(character switch
            {
                '\\' => @"\\",
                ':' => @"\:",
                '#' => @"\#",
                ' ' => @"\s",
                '\r' => @"\r",
                '\n' => @"\n",
                '\t' => @"\t",
                _ when char.IsControl(character) || char.IsWhiteSpace(character)
                    => $"\\u{((int)character).ToString("X4", CultureInfo.InvariantCulture)}",
                _ => character.ToString()
            });
        }

        return result.ToString();
    }

}

/// <summary>
/// 表示 OrleansContracts.txt 文件中声明的 Grain 类。
/// </summary>
internal sealed class DeclaredGrainClass
{
    public DeclaredGrainClass(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string? Alias { get; set; }

    public bool IsRetired { get; set; }

    public TextSpan Span { get; set; }

    public Location GetLocation(SourceText sourceText, string filePath)
    {
        var lineSpan = sourceText.Lines.GetLinePositionSpan(Span);
        return Location.Create(filePath, Span, lineSpan);
    }
}

/// <summary>
/// 解析 OrleansContracts.txt 文件。
/// </summary>
internal static class GrainInterfaceFileParser
{
    // 用于解析的正则表达式模式。
    // 接口行：interface [GrainInterfaceType("x")] Namespace.IInterface<T> [Version(N)]
    // 或退役形式：*RETIRED* interface [GrainInterfaceType("x")] Namespace.IInterface<T> [Version(N)]
    // 名称可以包含泛型类型参数，例如 IMyGrain<T> 或 IMyGrain<TKey, TValue>。
    private static readonly Regex InterfacePattern = new(
        @"^(?<retired>\*RETIRED\*\s*)?(?:interface\s+)?(\[GrainInterfaceType\(""(?<grainInterfaceType>[^""]+)""\)\]\s*)?(\[Alias\(""(?<alias>[^""]+)""\)\]\s*)?(?<name>[\w]+(?:<[\w,\s]+>)?(?:\.[\w]+(?:<[\w,\s]+>)?)*)\s*\[Version\((?<version>\d+)\)\]$",
        RegexOptions.Compiled);

    // Grain 类行：class [GrainType("x")] Namespace.GrainClass
    // 或退役形式：*RETIRED* class [GrainType("x")] Namespace.GrainClass
    private static readonly Regex GrainClassPattern = new(
        @"^(?<retired>\*RETIRED\*\s*)?class\s+(\[(?:GrainType|Alias)\(""(?<alias>[^""]+)""\)\]\s*)?(?<name>[\w]+(?:<[\w,\s]+>)?(?:\.[\w]+(?:<[\w,\s]+>)?)*)$",
        RegexOptions.Compiled);

    // 成员行：0123ABCD: Method(params) -> ReturnType
    // 此值为实际传输标识，与源码声明方式无关。
    private static readonly Regex MemberPattern = new(
        @"^(?<signature>.+\(.*\)\s*->\s*.+)$",
        RegexOptions.Compiled);

    internal static bool TryGetInterfaceName(string line, out string name)
    {
        var match = InterfacePattern.Match(StripClrComment(line));
        if (match.Success)
        {
            name = match.Groups["name"].Value;
            return true;
        }

        name = string.Empty;
        return false;
    }

    internal static bool TryGetGrainInterfaceType(string line, out string grainInterfaceType)
    {
        var match = InterfacePattern.Match(StripClrComment(line));
        if (match.Success && GetRecordedGrainInterfaceType(match) is { } recorded)
        {
            grainInterfaceType = recorded;
            return true;
        }

        grainInterfaceType = string.Empty;
        return false;
    }

    // 旧式声明记录接口的 [Alias]，当没有 [GrainInterfaceType] 时，该别名即运行时标识。
    private static string? GetRecordedGrainInterfaceType(Match match)
    {
        if (match.Groups["grainInterfaceType"].Success)
        {
            return match.Groups["grainInterfaceType"].Value;
        }

        return match.Groups["alias"].Success ? match.Groups["alias"].Value : null;
    }

    /// <summary>
    /// 将有效接口声明格式化为退役声明，并记录旧式声明解析得到的标识。
    /// </summary>
    internal static string FormatRetiredInterfaceDeclaration(
        string declaration,
        LegacyGrainInterfaceTypeResolver? legacyGrainInterfaceTypes)
        => $"{GrainInterfaceVersionAnalyzer.RetiredPrefix} "
            + (TryRecordLegacyGrainInterfaceType(declaration, legacyGrainInterfaceTypes, out var recorded)
                ? recorded
                : declaration);

    /// <summary>
    /// 对于名称实际解析出的标识与名称本身隐含标识不同的旧式接口声明，重写该声明，
    /// 使其记录解析得到的标识。这样即使重命名该名称指向的源码接口，也不会改变
    /// 声明的含义。已退役的声明保持退役状态。
    /// </summary>
    internal static bool TryRecordLegacyGrainInterfaceType(
        string declaration,
        LegacyGrainInterfaceTypeResolver? legacyGrainInterfaceTypes,
        out string recordedDeclaration)
    {
        var match = InterfacePattern.Match(StripClrComment(declaration));
        if (match.Success && legacyGrainInterfaceTypes is not null && GetRecordedGrainInterfaceType(match) is null)
        {
            var name = match.Groups["name"].Value;
            var grainInterfaceType = legacyGrainInterfaceTypes.Resolve(name);
            if (!string.Equals(
                grainInterfaceType,
                GrainInterfaceVersionAnalyzer.GetDefaultGrainInterfaceType(name),
                StringComparison.Ordinal))
            {
                recordedDeclaration = (match.Groups["retired"].Success ? $"{GrainInterfaceVersionAnalyzer.RetiredPrefix} " : string.Empty)
                    + $"interface [GrainInterfaceType(\"{grainInterfaceType}\")] {name} "
                    + $"[Version({match.Groups["version"].Value})]{GetClrComment(declaration)}";
                return true;
            }
        }

        recordedDeclaration = declaration;
        return false;
    }

    internal static bool TryGetGrainClassName(string line, out string name)
    {
        var match = GrainClassPattern.Match(StripClrComment(line));
        if (match.Success)
        {
            name = match.Groups["name"].Value;
            return true;
        }

        name = string.Empty;
        return false;
    }

    internal static bool TryGetGrainClassType(string line, out string grainType)
    {
        var match = GrainClassPattern.Match(StripClrComment(line));
        if (match.Success && match.Groups["alias"].Success)
        {
            grainType = match.Groups["alias"].Value;
            return true;
        }

        grainType = string.Empty;
        return false;
    }

    internal static bool TryGetContractName(string line, out string name)
        => TryGetGrainClassName(line, out name) || TryGetInterfaceName(line, out name);

    internal static bool TryGetMemberSignature(string line, out string signature)
    {
        if (TryGetMemberDeclaration(line, out signature, out var wireIdentity))
        {
            signature = GetCanonicalMemberSignature(signature, wireIdentity);
            return true;
        }

        signature = string.Empty;
        return false;
    }

    internal static bool TryGetMemberDeclaration(
        string line,
        out string signature,
        out MethodWireIdentity wireIdentity)
    {
        var declaration = StripClrComment(line);
        var identityDelimiter = FindIdentityDelimiter(declaration);
        if (identityDelimiter < 0)
        {
            signature = string.Empty;
            wireIdentity = default;
            return false;
        }

        wireIdentity = new MethodWireIdentity(
            UnescapeWireIdentity(declaration.Substring(0, identityDelimiter)));
        declaration = declaration.Substring(identityDelimiter + 2);
        var match = MemberPattern.Match(declaration);
        if (match.Success)
        {
            signature = match.Groups["signature"].Value;
            return true;
        }

        signature = string.Empty;
        wireIdentity = default;
        return false;
    }

    internal static string GetCanonicalMemberSignature(
        string signature,
        MethodWireIdentity wireIdentity)
    {
        var parameterListStart = signature.IndexOf('(');
        return parameterListStart < 0
            ? signature
            : $"{wireIdentity.Value.Length}:{wireIdentity.Value}"
                + GetMethodAritySuffix(signature)
                + signature.Substring(parameterListStart);
    }

    internal static string FormatMemberDeclaration(
        string signature,
        MethodWireIdentity wireIdentity)
    {
        return $"{wireIdentity.ToManifestMetadata()}: {signature}";
    }

    internal static string GetMethodAritySuffix(string signature)
    {
        var parameterListStart = signature.IndexOf('(');
        if (parameterListStart < 0)
        {
            return string.Empty;
        }

        var methodStart = signature.LastIndexOf('.', parameterListStart - 1) + 1;
        var methodName = signature.Substring(methodStart, parameterListStart - methodStart);
        var arityMarker = methodName.LastIndexOf('`');
        if (arityMarker >= 0)
        {
            return methodName.Substring(arityMarker);
        }

        var genericStart = methodName.IndexOf('<');
        var genericEnd = methodName.LastIndexOf('>');
        if (genericStart < 0 || genericEnd <= genericStart)
        {
            return string.Empty;
        }

        var arity = 1;
        for (var index = genericStart + 1; index < genericEnd; index++)
        {
            if (methodName[index] == ',')
            {
                arity++;
            }
        }

        return $"`{arity}";
    }

    private const string ClrCommentPrefix = " # CLR: ";

    internal static string GetClrComment(string line)
    {
        var index = line.IndexOf(ClrCommentPrefix, StringComparison.Ordinal);
        return index < 0 ? string.Empty : line.Substring(index);
    }

    internal static string StripClrComment(string line)
    {
        var index = line.IndexOf(ClrCommentPrefix, StringComparison.Ordinal);
        return (index < 0 ? line : line.Substring(0, index)).Trim();
    }

    private static int FindIdentityDelimiter(string declaration)
    {
        var escaped = false;
        for (var index = 0; index < declaration.Length - 1; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (declaration[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (declaration[index] == ':' && declaration[index + 1] == ' ')
            {
                return index;
            }
        }

        return -1;
    }

    private static string UnescapeWireIdentity(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                if (value[index + 1] == 'u'
                    && index + 5 < value.Length
                    && ushort.TryParse(
                        value.Substring(index + 2, 4),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var codePoint))
                {
                    result.Append((char)codePoint);
                    index += 5;
                    continue;
                }

                index++;
                result.Append(value[index] switch
                {
                    'r' => '\r',
                    'n' => '\n',
                    't' => '\t',
                    's' => ' ',
                    _ => value[index]
                });
                continue;
            }

            result.Append(value[index]);
        }

        return result.ToString();
    }

    public static (GrainInterfaceData Data, List<Diagnostic>? Errors) Parse(SourceText sourceText, string filePath)
    {
        var data = new GrainInterfaceData();
        List<Diagnostic>? errors = null;
        DeclaredGrainInterface? currentInterface = null;

        foreach (var textLine in sourceText.Lines)
        {
            var lineText = StripClrComment(textLine.ToString());

            // 跳过空行和注释。
            if (string.IsNullOrWhiteSpace(lineText) || lineText.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            // 尝试匹配接口声明。
            var grainClassMatch = GrainClassPattern.Match(lineText);
            if (grainClassMatch.Success)
            {
                currentInterface = null;
                var name = grainClassMatch.Groups["name"].Value;
                var alias = grainClassMatch.Groups["alias"].Success ? grainClassMatch.Groups["alias"].Value : null;
                var identity = alias ?? GrainInterfaceVersionAnalyzer.GetDefaultGrainType(name);
                if (data.Classes.Any(candidate =>
                    string.Equals(
                        candidate.Alias ?? GrainInterfaceVersionAnalyzer.GetDefaultGrainType(candidate.Name),
                        identity,
                        StringComparison.Ordinal)))
                {
                    errors ??= new List<Diagnostic>();
                    var location = Location.Create(
                        filePath,
                        textLine.Span,
                        sourceText.Lines.GetLinePositionSpan(textLine.Span));
                    errors.Add(Diagnostic.Create(GrainInterfaceVersionAnalyzer.DuplicateGrainClassDeclarationRule, location, name));
                    continue;
                }

                data.Classes.Add(new DeclaredGrainClass(name)
                {
                    Alias = alias,
                    IsRetired = grainClassMatch.Groups["retired"].Success,
                    Span = textLine.Span
                });
                continue;
            }

            // 尝试匹配接口声明。
            var interfaceMatch = InterfacePattern.Match(lineText);
            if (interfaceMatch.Success)
            {
                var name = interfaceMatch.Groups["name"].Value;
                var alias = interfaceMatch.Groups["alias"].Success ? interfaceMatch.Groups["alias"].Value : null;
                var grainInterfaceType = GetRecordedGrainInterfaceType(interfaceMatch);
                if (!ushort.TryParse(interfaceMatch.Groups["version"].Value, out var version))
                {
                    currentInterface = null;
                    continue;
                }
                var isRetired = interfaceMatch.Groups["retired"].Success;

                // 旧式键由名称推导，因此仅与其他旧式键比较。
                if (data.Interfaces.Any(candidate => grainInterfaceType is not null
                    ? string.Equals(candidate.GrainInterfaceType, grainInterfaceType, StringComparison.Ordinal)
                    : candidate.GrainInterfaceType is null
                        && string.Equals(
                            GrainInterfaceVersionAnalyzer.GetDefaultGrainInterfaceType(candidate.Name),
                            GrainInterfaceVersionAnalyzer.GetDefaultGrainInterfaceType(name),
                            StringComparison.Ordinal)))
                {
                    // 重复声明。
                    errors ??= new List<Diagnostic>();
                    var location = Location.Create(
                        filePath,
                        textLine.Span,
                        sourceText.Lines.GetLinePositionSpan(textLine.Span));
                    errors.Add(Diagnostic.Create(
                        GrainInterfaceVersionAnalyzer.DuplicateInterfaceDeclarationRule,
                        location,
                        name));
                    continue;
                }

                currentInterface = new DeclaredGrainInterface(name)
                {
                    Alias = alias,
                    GrainInterfaceType = grainInterfaceType,
                    Version = version,
                    IsRetired = isRetired,
                    Span = textLine.Span
                };
                data.Interfaces.Add(currentInterface);
                continue;
            }

            // 尝试匹配成员声明。
            if (currentInterface is not null
                && TryGetMemberDeclaration(lineText, out var signature, out var wireIdentity))
            {
                var key = GetCanonicalMemberSignature(signature, wireIdentity);
                currentInterface.Members[key] = new DeclaredGrainMember(signature, wireIdentity)
                {
                    Span = textLine.Span
                };
            }
        }

        return (data, errors);
    }
}
