using System.Text;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Orleans.CodeGenerator.Model;

namespace Orleans.CodeGenerator;

/// <summary>
/// 生成 Orleans 序列化器、复制器、激活器、RPC 代理、调用器及注册元数据。
/// </summary>
[Generator]
public sealed class OrleansSerializationSourceGenerator : IIncrementalGenerator
{
    internal const string GeneratorOptionsTrackingName = "Orleans.GeneratorOptions";
    internal const string AssemblyNameTrackingName = "Orleans.AssemblyName";
    internal const string SerializableTypeResultsTrackingName = "Orleans.SerializableTypeResults";
    internal const string CollectedSerializableTypesTrackingName = "Orleans.CollectedSerializableTypes";
    internal const string DirectProxyInterfacesTrackingName = "Orleans.DirectProxyInterfaces";
    internal const string InheritedProxyInterfacesTrackingName = "Orleans.InheritedProxyInterfaces";
    internal const string CollectedProxyInterfacesTrackingName = "Orleans.CollectedProxyInterfaces";
    internal const string PreparedProxyOutputsTrackingName = "Orleans.PreparedProxyOutputs";
    internal const string ReferenceAssemblyDataTrackingName = "Orleans.ReferenceAssemblyData";
    internal const string MetadataAggregateTrackingName = "Orleans.MetadataAggregate";
    internal const string SerializerOutputsTrackingName = "Orleans.SerializerOutputs";
    internal const string EnumSerializerPlansTrackingName = "Orleans.EnumSerializerPlans";
    internal const string EnumSerializerOutputsTrackingName = "Orleans.EnumSerializerOutputs";
    internal const string ReferencedSerializerOutputsTrackingName = "Orleans.ReferencedSerializerOutputs";
    internal const string ProxyOutputsTrackingName = "Orleans.ProxyOutputs";
    internal const string MetadataOutputsTrackingName = "Orleans.MetadataOutputs";
    internal const string SerializerContextResultsTrackingName = "Orleans.SerializerContextResults";
    internal const string SerializerContextOutputsTrackingName = "Orleans.SerializerContextOutputs";

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatorOptions = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => SourceGeneratorOptionsParser.ParseOptions(provider.GlobalOptions))
            .WithTrackingName(GeneratorOptionsTrackingName);
        var compilationProvider = context.CompilationProvider;
        var assemblyNameProvider = compilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "assembly")
            .WithTrackingName(AssemblyNameTrackingName);

        var generatedContextOutputs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Orleans.GeneratedContextTypeAttribute`1",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ctx)
            .Where(static ctx =>
            {
                // 一个具有特性的分部声明负责生成；Generate 从完整符号读取根并验证声明。
                var attribute = ctx.TargetSymbol.GetAttributes().First(attribute =>
                    SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass?.OriginalDefinition,
                        ctx.Attributes[0].AttributeClass!.OriginalDefinition));
                var reference = attribute.ApplicationSyntaxReference!;
                return reference.SyntaxTree == ctx.TargetNode.SyntaxTree && ctx.TargetNode.Span.Contains(reference.Span);
            })
            .Combine(generatorOptions)
            .SelectMany(static (input, ct) => SerializerContextGenerator.Generate(input.Left, input.Right, ct))
            .WithTrackingName(SerializerContextResultsTrackingName)
            .Collect();
        var includedContextOutputs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Orleans.GeneratedContextImportAttribute`1",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ctx)
            .Where(static ctx =>
            {
                var rootAttribute = ctx.SemanticModel.Compilation.GetTypeByMetadataName("Orleans.GeneratedContextTypeAttribute`1");
                if (ctx.TargetSymbol.GetAttributes().Any(attribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass?.OriginalDefinition, rootAttribute)))
                    return false;
                var attribute = ctx.TargetSymbol.GetAttributes().First(attribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass?.OriginalDefinition, ctx.Attributes[0].AttributeClass!.OriginalDefinition));
                var reference = attribute.ApplicationSyntaxReference!;
                return reference.SyntaxTree == ctx.TargetNode.SyntaxTree && ctx.TargetNode.Span.Contains(reference.Span);
            })
            .Combine(generatorOptions)
            .SelectMany(static (input, ct) => SerializerContextGenerator.Generate(input.Left, input.Right, ct))
            .WithTrackingName("IncludedSerializerContextResults")
            .Collect();
        var serializerContextOutputs = generatedContextOutputs.Combine(includedContextOutputs)
            .Select(static (outputs, _) => outputs.Left.AddRange(outputs.Right))
            .WithComparer(ImmutableArrayComparer<SerializerContextOutput>.Instance)
            .WithTrackingName(SerializerContextOutputsTrackingName);
        var serializerContextEntries = serializerContextOutputs.SelectMany(static (outputs, _) => outputs);
        context.RegisterSourceOutput(
            serializerContextEntries.Select(static (output, _) => output.Declaration),
            static (productionContext, output) =>
            {
                GeneratedSourceOutput.ReportDiagnostic(productionContext, output);
                GeneratedSourceOutput.EmitSource(productionContext, output);
            });
        context.RegisterImplementationSourceOutput(
            serializerContextEntries.Select(static (output, _) => output.Implementation),
            static (productionContext, output) =>
            {
                GeneratedSourceOutput.ReportDiagnostic(productionContext, output);
                GeneratedSourceOutput.EmitSource(productionContext, output);
            });

        // 对 [GenerateSerializer] 类型进行增量发现。
        var serializableTypeContexts = context.SyntaxProvider
            .ForAttributeWithMetadataName<GeneratorAttributeSyntaxContext>(
                "Orleans.GenerateSerializerAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax or EnumDeclarationSyntax,
                transform: static (ctx, _) => ctx);

        var serializableTypeResults = serializableTypeContexts
            .Combine(generatorOptions)
            .Select(static (input, ct) => SerializableSourceOutputGenerator.CreateSerializableTypeResult(
                input.Left,
                input.Right,
                ct))
            .WithTrackingName(SerializableTypeResultsTrackingName);

        var collectedSerializableTypeResults = serializableTypeResults
            .Collect()
            .Select(static (input, _) => GeneratedSourceOutput.DeduplicateSerializableTypeResults(input))
            .WithComparer(ImmutableArrayComparer<SerializableTypeResult>.Instance);

        var collectedTypes = collectedSerializableTypeResults
            .Select(static (input, _) => GeneratedSourceOutput.GetSerializableTypeModels(input))
            .WithComparer(ImmutableArrayComparer<SerializableTypeModel>.Instance)
            .WithTrackingName(CollectedSerializableTypesTrackingName);

        context.RegisterSourceOutput(collectedSerializableTypeResults.SelectMany(static (input, _) => input), static (productionContext, result) =>
        {
            if (result.Diagnostic is { } diagnostic)
            {
                productionContext.ReportDiagnostic(diagnostic);
            }
        });

        // 基于特性发现 [GenerateMethodSerializers] 接口，并使用
        // 受约束的语法提供程序处理从基接口继承特性的接口。
        var directProxyInterfaces = context.SyntaxProvider
            .ForAttributeWithMetadataName<ProxyInterfaceModel?>(
                "Orleans.GenerateMethodSerializersAttribute",
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.ExtractProxyInterfaceFromAttributeContext(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName(DirectProxyInterfacesTrackingName);

        var inheritedProxyInterfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax { BaseList: not null },
                transform: static (ctx, ct) => ModelExtractor.ExtractInheritedProxyInterfaceFromSyntaxContext(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect()
            .Select(static (input, _) => ModelExtractor.NormalizeProxyInterfaceModels(input))
            .WithComparer(ImmutableArrayComparer<ProxyInterfaceModel>.Instance)
            .WithTrackingName(InheritedProxyInterfacesTrackingName);

        var collectedDirectProxyInterfaces = directProxyInterfaces
            .Collect()
            .Select(static (input, _) => ModelExtractor.NormalizeProxyInterfaceModels(input))
            .WithComparer(ImmutableArrayComparer<ProxyInterfaceModel>.Instance);

        var collectedProxies = collectedDirectProxyInterfaces
            .Combine(inheritedProxyInterfaces)
            .Select(static (input, _) => ModelExtractor.MergeProxyInterfaces(input.Left, input.Right))
            .WithComparer(ImmutableArrayComparer<ProxyInterfaceModel>.Instance)
            .WithTrackingName(CollectedProxyInterfacesTrackingName);

        // 提取引用程序集数据（应用程序部件、已知类型 ID、别名）。
        var refAssemblyDataResults = compilationProvider
            .Combine(generatorOptions)
            .Combine(collectedTypes)
            .Combine(collectedProxies)
            .Select(static (input, ct) =>
            {
                var (((compilation, options), serializableTypes), proxyInterfaces) = input;
                return ReferenceAssemblyDataProvider.CreateReferenceAssemblyDataResult(
                    compilation, options, serializableTypes, proxyInterfaces, ct);
            })
            .WithTrackingName(ReferenceAssemblyDataTrackingName);

        context.RegisterSourceOutput(refAssemblyDataResults, static (productionContext, result) =>
        {
            if (!result.Diagnostics.IsDefaultOrEmpty)
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    productionContext.ReportDiagnostic(diagnostic);
                }
            }
        });

        // 仅在生成实现输出时报告有关生成元数据的诊断。
        context.RegisterImplementationSourceOutput(refAssemblyDataResults, static (productionContext, result) =>
        {
            foreach (var diagnostic in result.ImplementationDiagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic);
            }
        });

        var refAssemblyData = refAssemblyDataResults
            .Select(static (result, _) => result.Model);

        var allProxyInterfaces = collectedProxies
            .Combine(refAssemblyData)
            .Select(static (input, _) => ModelExtractor.MergeProxyInterfaces(input.Left, input.Right.ReferencedProxyInterfaces))
            .WithComparer(ImmutableArrayComparer<ProxyInterfaceModel>.Instance);

        var preparedProxyOutputs = allProxyInterfaces
            .Combine(compilationProvider)
            .Combine(generatorOptions)
            .Select(static (input, ct) => ProxySourceOutputGenerator.CreateProxyOutputPreparation(input.Left.Right, input.Left.Left, input.Right, ct))
            .WithTrackingName(PreparedProxyOutputsTrackingName);

        context.RegisterSourceOutput(preparedProxyOutputs, static (productionContext, input) =>
        {
            foreach (var diagnostic in input.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic);
            }
        });

        var preparedProxyOutputModels = preparedProxyOutputs
            .Select(static (result, _) => result.ProxyOutputModels)
            .WithComparer(ImmutableArrayComparer<ProxyOutputModel>.Instance);

        // 生成元数据前合并源代码模型和引用模型。
        var metadataAggregate = collectedTypes
            .Combine(preparedProxyOutputModels)
            .Combine(refAssemblyData)
            .Select(static (input, ct) => ModelExtractor.CreateMetadataAggregate(
                input.Right.AssemblyName,
                input.Left.Left,
                input.Left.Right,
                input.Right))
            .WithTrackingName(MetadataAggregateTrackingName);

        // 枚举只有一个声明，因此其生成计划可保留发现提供程序按所有者分配的标识。
        // 类型失效时保留 null 槽位：在此处筛选可能在重新解析时破坏 Roslyn 4.5 的跟踪状态。
        var enumSerializerPlans = serializableTypeResults
            .Select(static (result, _) => result.EnumSerializerPlan)
            .WithTrackingName(EnumSerializerPlansTrackingName);
        var enumSerializerOutputs = enumSerializerPlans
            .Select(static (plan, _) => plan is null ? default : EnumSerializerGenerator.GenerateSource(plan))
            .WithTrackingName(EnumSerializerOutputsTrackingName);

        var compilationAwareTypes = collectedSerializableTypeResults
            .Select(static (results, _) => results
                .Where(static result => result.Model is not null && result.EnumSerializerPlan is null)
                .Select(static result => result.Model!)
                .ToImmutableArray())
            .WithComparer(ImmutableArrayComparer<SerializableTypeModel>.Instance);
        var compilationAwareSerializerOutputs = compilationAwareTypes
            .Combine(compilationProvider)
            .Combine(generatorOptions)
            .Select(static (input, ct) => SerializableSourceOutputGenerator.CreateSerializableSourceOutputs(
                input.Left.Right,
                input.Left.Left,
                input.Right,
                ct))
            .WithComparer(ImmutableArrayComparer<SourceOutputResult>.Instance);

        var serializerOutputs = compilationAwareSerializerOutputs
            .Combine(enumSerializerOutputs.Collect())
            .Select(static (input, _) => GeneratedSourceOutput.DeduplicateSourceOutputs(
                input.Left.AddRange(input.Right.Where(static output => output.SourceEntry is not null))))
            .WithComparer(ImmutableArrayComparer<SourceOutputResult>.Instance)
            .WithTrackingName(SerializerOutputsTrackingName);

        // 保持去重后的批次完整：Roslyn 4.5 在展平与此前相等的批次时可能丢失重新添加的条目。
        context.RegisterSourceOutput(serializerOutputs, static (productionContext, outputs) =>
        {
            foreach (var output in outputs)
            {
                GeneratedSourceOutput.ReportDiagnostic(productionContext, output);
            }
        });
        context.RegisterImplementationSourceOutput(serializerOutputs, static (productionContext, outputs) =>
        {
            foreach (var output in outputs)
            {
                GeneratedSourceOutput.EmitSource(productionContext, output);
            }
        });

        var referencedSerializerOutputs = refAssemblyData
            .Combine(compilationProvider)
            .Combine(generatorOptions)
            .Select(static (input, ct) => SerializableSourceOutputGenerator.CreateReferencedSerializableSourceOutputs(
                input.Left.Right,
                input.Left.Left,
                input.Right,
                ct))
            .WithComparer(ImmutableArrayComparer<SourceOutputResult>.Instance)
            .WithTrackingName(ReferencedSerializerOutputsTrackingName);

        GeneratedSourceOutput.RegisterImplementationSourceOutputs(
            context,
            referencedSerializerOutputs.SelectMany(static (input, _) => input));

        var proxyOutputs = preparedProxyOutputs
            .SelectMany(static (result, _) => result.SourceOutputs)
            .WithTrackingName(ProxyOutputsTrackingName);

        GeneratedSourceOutput.RegisterImplementationSourceOutputs(context, proxyOutputs);

        context.RegisterImplementationSourceOutput(assemblyNameProvider, static (productionContext, assemblyName) =>
        {
            productionContext.AddSource($"{assemblyName}.orleans.g.cs", SourceText.From(string.Empty, Encoding.UTF8));
        });

        var supportsModuleInitializers = compilationProvider
            .Select(static (compilation, _) =>
                compilation is CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp9 }
                && compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") is not null);

        var metadataOutputs = metadataAggregate
            .Combine(supportsModuleInitializers)
            .Combine(generatorOptions)
            .Select(static (input, _) => MetadataSourceOutputGenerator.CreateMetadataSourceOutput(input.Left.Left, input.Right, input.Left.Right))
            .WithTrackingName(MetadataOutputsTrackingName);

        context.RegisterSourceOutput(metadataOutputs, GeneratedSourceOutput.ReportDiagnostic);
        context.RegisterImplementationSourceOutput(metadataOutputs, GeneratedSourceOutput.EmitSource);
    }
}
