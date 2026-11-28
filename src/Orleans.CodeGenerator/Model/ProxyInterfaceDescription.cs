using Orleans.CodeGenerator.SyntaxGeneration;
using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.CodeGenerator;

[DebuggerDisplay("{InterfaceType} (proxy base {ProxyBaseType})")]
internal class ProxyInterfaceDescription : IEquatable<ProxyInterfaceDescription>
{
    private static readonly char[] FilteredNameChars = ['`', '.'];

    public ProxyInterfaceDescription(
        ProxyGenerationContext generationContext,
        INamedTypeSymbol proxyBaseType,
        INamedTypeSymbol interfaceType)
    {
        ValidateBaseClass(generationContext.LibraryTypes, proxyBaseType);

        var prop = interfaceType.GetAllMembers<IPropertySymbol>().FirstOrDefault();
        if (prop is { })
        {
            throw new OrleansGeneratorDiagnosticAnalysisException(RpcInterfacePropertyDiagnostic.CreateDiagnostic(interfaceType, prop));
        }

        GenerationContext = generationContext;
        InterfaceType = interfaceType;
        Name = generationContext.GetAlias(interfaceType) ?? interfaceType.Name;
        ProxyBaseType = proxyBaseType;

        // 如果名称是指定了泛型元数的用户定义名称，则现在移除元数的反引号后缀。
        if (Name.IndexOfAny(FilteredNameChars) >= 0)
        {
            foreach (var c in FilteredNameChars)
            {
                Name = Name.Replace(c, '_');
            }
        }

        GeneratedNamespace = InterfaceType.GetNamespaceAndNesting() switch
        {
            { Length: > 0 } ns => $"{GeneratedCodeUtilities.CodeGeneratorName}.{ns}",
            _ => GeneratedCodeUtilities.CodeGeneratorName
        };

        var names = new HashSet<string>(StringComparer.Ordinal);
        TypeParameters = new List<(string Name, ITypeParameterSymbol Parameter)>();

        foreach (var tp in interfaceType.GetAllTypeParameters())
        {
            var tpName = GetTypeParameterName(names, tp);
            TypeParameters.Add((tpName, tp));
        }

        static string GetTypeParameterName(HashSet<string> names, ITypeParameterSymbol tp)
        {
            var count = 0;
            var result = tp.Name;
            while (names.Contains(result))
            {
                result = $"{tp.Name}_{++count}";
            }

            names.Add(result);
            return result;
        }
    }

    public ProxyGenerationContext GenerationContext { get; }

    private List<ProxyMethodDescription> GetMethods()
    {
        var result = new List<ProxyMethodDescription>();
        foreach (var iface in GetAllInterfaces(InterfaceType))
        {
            foreach (var method in iface.GetDeclaredInstanceMembers<IMethodSymbol>())
            {
                if (method.MethodKind == MethodKind.ExplicitInterfaceImplementation)
                {
                    // 生成代理时可以忽略显式实现。
                    // 代理必须显式实现每个方法，以确保忠实再现接口行为。
                    // 在调用方，如果派生类型未重写显式实现，则将调用该实现。
                    continue;
                }

                var methodDescription = GenerationContext.GetProxyMethodDescription(InterfaceType, method: method);
                result.Add(methodDescription);
            }
        }

        return result;

        static IEnumerable<INamedTypeSymbol> GetAllInterfaces(INamedTypeSymbol s)
        {
            if (s.TypeKind == TypeKind.Interface)
            {
                yield return s;
            }

            foreach (var i in s.AllInterfaces)

            {
                yield return i;
            }
        }
    }

    public string Name { get; }
    public INamedTypeSymbol InterfaceType { get; }
    public List<ProxyMethodDescription> Methods => field ??= GetMethods();
    public string GeneratedNamespace { get; }
    public List<(string Name, ITypeParameterSymbol Parameter)> TypeParameters { get; }
    public INamedTypeSymbol ProxyBaseType { get; }

    private static void ValidateBaseClass(LibraryTypes l, INamedTypeSymbol baseClass)
    {
        ValidateGenericInvokeAsync(l, baseClass);
        ValidateNonGenericInvokeAsync(l, baseClass);

        static void ValidateGenericInvokeAsync(LibraryTypes l, INamedTypeSymbol baseClass)
        {
            var found = false;
            string? complaint = null;
            ISymbol? complaintMember = null;
            foreach (var member in baseClass.GetMembers("InvokeAsync"))
            {
                if (member is not IMethodSymbol method)
                {
                    complaintMember = member;
                    complaint = "not a method";
                    continue;
                }

                if (method.TypeParameters.Length != 1)
                {
                    complaintMember = member;
                    complaint = "incorrect number of type parameters (expected one type parameter)";
                    continue;
                }

                if (method.Parameters.Length != 1)
                {
                    complaintMember = member;
                    complaint = $"missing parameter (expected a parameter of type {l.IInvokable.ToDisplayString()})";
                    continue;
                }

                var paramType = method.Parameters[0].Type;
                if (!SymbolEqualityComparer.Default.Equals(paramType, l.IInvokable))
                {
                    var implementsIInvokable = false;
                    foreach (var @interface in paramType.AllInterfaces)
                    {
                        if (SymbolEqualityComparer.Default.Equals(@interface, l.IInvokable))
                        {
                            implementsIInvokable = true;
                            break;
                        }
                    }

                    if (!implementsIInvokable)
                    {
                        complaintMember = member;
                        complaint = $"incorrect parameter type (found {paramType}, expected {l.IInvokable} or a type which implements {l.IInvokable})";
                        continue;
                    }
                }

                var expectedReturnType = l.ValueTask_1.Construct(method.TypeParameters[0]);
                if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, expectedReturnType))
                {
                    complaintMember = member;
                    complaint = $"incorrect return type (found: {method.ReturnType.ToDisplayString()}, expected {expectedReturnType.ToDisplayString()})";
                    continue;
                }

                found = true;
            }

            if (!found)
            {
                var notFoundMessage = $"Proxy base class {baseClass} does not contain a definition for ValueTask<T> InvokeAsync<T>(IInvokable)";
                var locationMember = complaintMember ?? baseClass;
                var complaintMessage = complaint switch
                {
                    { Length: > 0 } when complaintMember is not null => $"{notFoundMessage}. Complaint: {complaint} for symbol: {complaintMember.ToDisplayString()}",
                    _ => notFoundMessage,
                };
                var diagnostic = IncorrectProxyBaseClassSpecificationDiagnostic.CreateDiagnostic(baseClass, locationMember.Locations.First(), complaintMessage);
                throw new OrleansGeneratorDiagnosticAnalysisException(diagnostic);
            }
        }

        static void ValidateNonGenericInvokeAsync(LibraryTypes l, INamedTypeSymbol baseClass)
        {
            var found = false;
            string? complaint = null;
            ISymbol? complaintMember = null;
            foreach (var member in baseClass.GetMembers("InvokeAsync"))
            {
                if (member is not IMethodSymbol method)
                {
                    complaintMember = member;
                    complaint = "not a method";
                    continue;
                }

                if (method.TypeParameters.Length != 0)
                {
                    complaintMember = member;
                    complaint = "incorrect number of type parameters (expected zero)";
                    continue;
                }

                if (method.Parameters.Length != 1)
                {
                    complaintMember = member;
                    complaint = $"missing parameter (expected a parameter of type {l.IInvokable.ToDisplayString()})";
                    continue;
                }

                var paramType = method.Parameters[0].Type;
                if (!SymbolEqualityComparer.Default.Equals(paramType, l.IInvokable))
                {
                    var implementsIInvokable = false;
                    foreach (var @interface in paramType.AllInterfaces)
                    {
                        if (SymbolEqualityComparer.Default.Equals(@interface, l.IInvokable))
                        {
                            implementsIInvokable = true;
                            break;
                        }
                    }

                    if (!implementsIInvokable)
                    {
                        complaintMember = member;
                        complaint = $"incorrect parameter type (found {method.Parameters[0].Type}, expected {l.IInvokable})";
                        continue;
                    }
                }

                if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, l.ValueTask))
                {
                    complaintMember = member;
                    complaint = $"incorrect return type (found: {method.ReturnType.ToDisplayString()}, expected {l.ValueTask.ToDisplayString()})";
                    continue;
                }

                found = true;
            }

            if (!found)
            {
                var notFoundMessage = $"Proxy base class {baseClass} does not contain a definition for ValueTask InvokeAsync(IInvokable)";
                var locationMember = complaintMember ?? baseClass;
                var complaintMessage = complaint switch
                {
                    { Length: > 0 } when complaintMember is not null => $"{notFoundMessage}. Complaint: {complaint} for symbol: {complaintMember.ToDisplayString()}",
                    _ => notFoundMessage,
                };
                var diagnostic = IncorrectProxyBaseClassSpecificationDiagnostic.CreateDiagnostic(baseClass, locationMember.Locations.First(), complaintMessage);
                throw new OrleansGeneratorDiagnosticAnalysisException(diagnostic);
            }
        }
    }

    public bool Equals([DisallowNull] ProxyInterfaceDescription? other) => other is not null && SymbolEqualityComparer.Default.Equals(InterfaceType, other.InterfaceType) && SymbolEqualityComparer.Default.Equals(ProxyBaseType, other.ProxyBaseType);
    public override bool Equals(object? obj) => obj is ProxyInterfaceDescription other && Equals(other);
    public override int GetHashCode() => SymbolEqualityComparer.Default.GetHashCode(InterfaceType) * 17 ^ SymbolEqualityComparer.Default.GetHashCode(ProxyBaseType);
    public override string ToString() => $"Type: {InterfaceType}, ProxyBaseType: {ProxyBaseType}";
}
