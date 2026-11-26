using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Analyzers;

/// <summary>
/// 分析器, 当 grain 代码使用 ConfigureAwait(假) 或 ConfigureAwait(ConfigureAwaitOptions) 时警告它
/// 没有 ContinueOnCapturedContext 标志。
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

        // 检查是否是 ConfigureAwait 调用
        if (!IsConfigureAwaitCall(invocation, out var methodName))
        {
            return;
        }

        // 检查此代码是否在 grain 类中
        if (!IsInsideGrainClass(invocation, context.SemanticModel))
        {
            return;
        }

        // 获取援引的符号来分析参数
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // 只检查 ConfigureAwait 方法
        if (!string.Equals(methodSymbol.Name, "ConfigureAwait", StringComparison.Ordinal))
        {
            return;
        }

        // 检查是否是类似任务类型的 ConfigureAwait 方法
        var containingType = methodSymbol.ContainingType;
        if (!IsTaskLikeType(containingType))
        {
            return;
        }

        // 获取参数
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

        // 检查 ConfigureAwait(bool) 超载
        if (argumentType.SpecialType == SpecialType.System_Boolean)
        {
            var constantValue = context.SemanticModel.GetConstantValue(firstArgument.Expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is false)
            {
                // ConfigureAwait(假)不允许
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
            }
            return;
        }

        // 检查 ConfigureAwait(ConfigureAwaitOptions)超载
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
        // 上到上找到包含声明的
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
                // 如果我们在找到 grain 类之前打中断断或记录, 我们便不是在 grain
                // (台词和记录不能是 grains)
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

        // 检查具有 ConfigureAwait的通用任务类型
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
        // ConfigureAwaitOptions.ContinueOnCapturedContext 值为 1
        const int ContinueOnCapturedContextValue = 1;

        // 尝试获取常值
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue && constantValue.Value is int intValue)
        {
            // 检查是否设置 ContinueOnCapturedContext 标志( 值 1)
            return (intValue & ContinueOnCapturedContextValue) != 0;
        }

        // 如果我们无法在编译时间确定值, 我们需要分析表达式
        // 检查是否包含 ContinueOnCapturedContext
        return ExpressionIncludesContinueOnCapturedContext(expression, semanticModel, cancellationToken);
    }

    private static bool ExpressionIncludesContinueOnCapturedContext(ExpressionSyntax expression, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        // 处理成员访问像 ConfigureAwaitOptions.ContinueOnCapturedContext
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;
            if (string.Equals(memberName, "ContinueOnCapturedContext", StringComparison.Ordinal))
            {
                return true;
            }
        }

        // 处理二进制或表达式 ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding
        if (expression is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression))
        {
            return ExpressionIncludesContinueOnCapturedContext(binaryExpression.Left, semanticModel, cancellationToken)
                || ExpressionIncludesContinueOnCapturedContext(binaryExpression.Right, semanticModel, cancellationToken);
        }

        // 处理括号表达式
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return ExpressionIncludesContinueOnCapturedContext(parenthesized.Expression, semanticModel, cancellationToken);
        }

        // 处理投石表达式
        if (expression is CastExpressionSyntax castExpression)
        {
            return ExpressionIncludesContinueOnCapturedContext(castExpression.Expression, semanticModel, cancellationToken);
        }

        // 如果我们遇到变数或方法的呼叫, 我们无法静态地确定国旗
        // 在这种情况下,我们给出了怀疑的利弊 而不报告
        if (expression is IdentifierNameSyntax or InvocationExpressionSyntax)
        {
            // 尝试将常值作为后退值
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is int intValue)
            {
                const int ContinueOnCapturedContextValue = 1;
                return (intValue & ContinueOnCapturedContextValue) != 0;
            }

            // 无法确定 - 不要报告假阳性
            return true;
        }

        return false;
    }
}
