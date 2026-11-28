using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Analyzers;

/// <summary>
/// 当 Grain 代码使用 ConfigureAwait(false) 或使用 ConfigureAwait(ConfigureAwaitOptions) 但
/// 未指定 ContinueOnCapturedContext 标志时发出警告的分析器。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ConfigureAwaitAnalyzer : DiagnosticAnalyzer
{
    public const string RuleId = "ORLEANS0014";

    private static readonly LocalizableString Title = new LocalizableResourceString(
        nameof(Resources.AvoidConfigureAwaitFalseInGrainTitle),
        Resources.ResourceManager,
        typeof(Resources));

    private static readonly LocalizableString MessageFormat = new LocalizableResourceString(
        nameof(Resources.AvoidConfigureAwaitFalseInGrainMessageFormat),
        Resources.ResourceManager,
        typeof(Resources));

    private static readonly LocalizableString Description = new LocalizableResourceString(
        nameof(Resources.AvoidConfigureAwaitFalseInGrainDescription),
        Resources.ResourceManager,
        typeof(Resources));

    private static readonly DiagnosticDescriptor Rule = new(
        id: RuleId,
        title: Title,
        messageFormat: MessageFormat,
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: Constants.GetDiagnosticHelpLink(RuleId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // 检查是否为 ConfigureAwait 调用。
        if (!IsConfigureAwaitCall(invocation, out var methodName))
        {
            return;
        }

        // 检查此代码是否位于 Grain 类内部。
        if (!IsInsideGrainClass(invocation, context.SemanticModel))
        {
            return;
        }

        // 获取调用的符号，以分析其参数。
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // 仅检查 ConfigureAwait 方法。
        if (!string.Equals(methodSymbol.Name, "ConfigureAwait", StringComparison.Ordinal))
        {
            return;
        }

        // 检查是否为 Task 类似类型上的 ConfigureAwait 方法。
        var containingType = methodSymbol.ContainingType;
        if (!IsTaskLikeType(containingType))
        {
            return;
        }

        // 获取实参。
        var arguments = invocation.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count == 0)
        {
            return;
        }

        var firstArgument = arguments.Value[0];
        var argumentType = context.SemanticModel.GetTypeInfo(firstArgument.Expression, context.CancellationToken).Type;

        if (argumentType is null)
        {
            return;
        }

        // 检查 ConfigureAwait(bool) 重载。
        if (argumentType.SpecialType == SpecialType.System_Boolean)
        {
            var constantValue = context.SemanticModel.GetConstantValue(firstArgument.Expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is false)
            {
                // 不允许使用 ConfigureAwait(false)。
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
            }
            return;
        }

        // 检查 ConfigureAwait(ConfigureAwaitOptions) 重载。
        if (IsConfigureAwaitOptionsType(argumentType))
        {
            if (!HasContinueOnCapturedContextFlag(firstArgument.Expression, context.SemanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
            }
        }
    }

    private static bool IsConfigureAwaitCall(InvocationExpressionSyntax invocation, [NotNullWhen(true)] out string? methodName)
    {
        methodName = null;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
            return string.Equals(methodName, "ConfigureAwait", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsInsideGrainClass(SyntaxNode node, SemanticModel semanticModel)
    {
        // 向上遍历以查找包含此代码的类型声明。
        var current = node.Parent;
        while (current is not null)
        {
            if (current is ClassDeclarationSyntax classDeclaration)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
                if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGrainClass())
                {
                    return true;
                }
            }
            else if (current is StructDeclarationSyntax or RecordDeclarationSyntax)
            {
                // 如果在找到 Grain 类之前遇到结构体或 record，则当前代码不在 Grain 中
                // （结构体和 record 不能作为 Grain）。
                return false;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsTaskLikeType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var fullName = type.ToDisplayString(NullableFlowState.None);

        // 检查具有 ConfigureAwait 的常见 Task 类似类型。
        return fullName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
            || fullName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal)
            || fullName.StartsWith("System.Runtime.CompilerServices.ConfiguredTaskAwaitable", StringComparison.Ordinal)
            || fullName.StartsWith("System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Generic.IAsyncEnumerable", StringComparison.Ordinal)
            || fullName.StartsWith("System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable", StringComparison.Ordinal);
    }

    private static bool IsConfigureAwaitOptionsType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return string.Equals(
            type.ToDisplayString(NullableFlowState.None),
            "System.Threading.Tasks.ConfigureAwaitOptions",
            StringComparison.Ordinal);
    }

    private static bool HasContinueOnCapturedContextFlag(ExpressionSyntax expression, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        // ConfigureAwaitOptions.ContinueOnCapturedContext 的值为 1。
        const int ContinueOnCapturedContextValue = 1;

        // 尝试获取常量值。
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue && constantValue.Value is int intValue)
        {
            // 检查是否设置了 ContinueOnCapturedContext 标志（值为 1）。
            return (intValue & ContinueOnCapturedContextValue) != 0;
        }

        // 如果无法在编译时确定值，则需要分析表达式，
        // 检查其中是否包含 ContinueOnCapturedContext。
        return ExpressionIncludesContinueOnCapturedContext(expression, semanticModel, cancellationToken);
    }

    private static bool ExpressionIncludesContinueOnCapturedContext(ExpressionSyntax expression, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        // 处理 ConfigureAwaitOptions.ContinueOnCapturedContext 等成员访问表达式。
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;
            if (string.Equals(memberName, "ContinueOnCapturedContext", StringComparison.Ordinal))
            {
                return true;
            }
        }

        // 处理 ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding 等二元按位或表达式。
        if (expression is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression))
        {
            return ExpressionIncludesContinueOnCapturedContext(binaryExpression.Left, semanticModel, cancellationToken)
                || ExpressionIncludesContinueOnCapturedContext(binaryExpression.Right, semanticModel, cancellationToken);
        }

        // 处理带括号的表达式。
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return ExpressionIncludesContinueOnCapturedContext(parenthesized.Expression, semanticModel, cancellationToken);
        }

        // 处理类型转换表达式。
        if (expression is CastExpressionSyntax castExpression)
        {
            return ExpressionIncludesContinueOnCapturedContext(castExpression.Expression, semanticModel, cancellationToken);
        }

        // 若遇到变量或方法调用，则无法静态确定标志。
        // 在这种情况下，不报告无法确认的问题。
        if (expression is IdentifierNameSyntax or InvocationExpressionSyntax)
        {
            // 尝试获取常量值作为后备方案。
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is int intValue)
            {
                const int ContinueOnCapturedContextValue = 1;
                return (intValue & ContinueOnCapturedContextValue) != 0;
            }

            // 无法确定时不报告，以免误报。
            return true;
        }

        return false;
    }
}
