using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>报告变更名称或支持范围不是常量的版本检查。</summary>
/// <remarks>
/// <c>DurableTask.GetVersion</c> 以变更名称记录执行实例的版本；部署的支持
/// 范围属于基于版本进行分支判断的代码，因此二者都是调用代码中的常量。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskVersionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DurableTaskDiagnostics.NonConstantVersion);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (DurableTaskSymbols.Create(start.Compilation) is not { } symbols)
            {
                return;
            }

            start.RegisterOperationAction(operation => AnalyzeInvocation(operation, symbols), OperationKind.Invocation);
            if (symbols.DurableVersion is { } durableVersion)
            {
                start.RegisterOperationAction(operation => AnalyzeCreation(operation, durableVersion), OperationKind.ObjectCreation);
            }
        });
    }

    private static void AnalyzeCreation(OperationAnalysisContext context, INamedTypeSymbol durableVersion)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (SymbolEqualityComparer.Default.Equals(creation.Type, durableVersion))
        {
            ReportNonConstantArguments(context, creation.Arguments, "DurableVersion");
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, DurableTaskSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod is not { Name: "GetVersion", IsStatic: true } method
            || !SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.DurableTask))
        {
            return;
        }

        ReportNonConstantArguments(context, invocation.Arguments, "DurableTask.GetVersion");
    }

    private static void ReportNonConstantArguments(
        OperationAnalysisContext context,
        ImmutableArray<IArgumentOperation> arguments,
        string target)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter is { } parameter && !argument.Value.ConstantValue.HasValue)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableTaskDiagnostics.NonConstantVersion,
                    argument.Value.Syntax.GetLocation(),
                    parameter.Name,
                    target));
            }
        }
    }
}
