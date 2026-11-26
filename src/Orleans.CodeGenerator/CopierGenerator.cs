using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Orleans.CodeGenerator.Model;
using Orleans.CodeGenerator.SyntaxGeneration;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Orleans.CodeGenerator.InvokableGenerator;
using static Orleans.CodeGenerator.SerializerGenerator;

namespace Orleans.CodeGenerator;

internal class CopierGenerator(IGeneratorServices generatorServices)
{
    private const string BaseTypeCopierFieldName = "_baseTypeCopier";
    private const string ActivatorFieldName = "_activator";
    private readonly IGeneratorServices _generatorServices = generatorServices;

    private LibraryTypes LibraryTypes => _generatorServices.LibraryTypes;

    private bool HotReloadSafe => _generatorServices.Options.HotReloadSafe;

    private bool UseHotReloadShape(ISerializableTypeDescription type)
        => HotReloadSafe && !type.IsEnumType && type.SupportsHotReloadMemberAddition;

    /// <summary>
    /// 返回 <paramref name="type"/>的复印声明,该声明在不附加缩进的情况下写成,
    /// 或 <see langword="null"/> ,当注册的是一个共享浅印件副本代替了该副本时。
    /// </summary>
    public string? GenerateCopier(
        ISerializableTypeDescription type,
        Dictionary<ISerializableTypeDescription, TypeSyntax> defaultCopiers)
    {
        var plan = CreatePlan(type, defaultCopiers);
        if (plan is null)
        {
            return null;
        }

        var writer = new SourceWriter();
        Write(writer, plan);
        return writer.ToString();
    }

    internal CopierEmissionPlan? CreatePlan(
        ISerializableTypeDescription type,
        Dictionary<ISerializableTypeDescription, TypeSyntax> defaultCopiers,
        SerializableTypeEmissionPlan? typePlan = null)
    {
        var isShallowCopyable = type.IsShallowCopyable;
        if (isShallowCopyable && !type.IsGenericType)
        {
            defaultCopiers.Add(type, LibraryTypes.ShallowCopier.ToTypeSyntax(type.TypeSyntax));
            return null;
        }

        if (!isShallowCopyable && type is SerializableTypeDescription { UnionCases: { } unionCases })
        {
            return new UnionCodecGenerator(_generatorServices).CreateCopierPlan(type, unionCases, typePlan);
        }

        var members = new List<ISerializableMember>();
        foreach (var member in type.Members)
        {
            if (!member.IsCopyable)
            {
                continue;
            }

            if (member is ISerializableMember serializable)
            {
                members.Add(serializable);
            }
            else if (member is IFieldDescription or IPropertyDescription)
            {
                members.Add(new SerializableMember(_generatorServices, member));
            }
            else if (member is MethodParameterFieldDescription methodParameter)
            {
                members.Add(new SerializableMethodMember(methodParameter));
            }
        }

        var isExceptionType = type.IsExceptionType && type.SerializationHooks.Count == 0;

        var baseType = isExceptionType
            ? $"global::Orleans.Serialization.GeneratedCodeHelpers.OrleansGeneratedCodeHelper.ExceptionCopier<{CodeText.Type(type.TypeSyntax)}, {CodeText.Type(type.BaseType.ToTypeSyntax())}>"
            : CodeText.Type((isShallowCopyable ? LibraryTypes.ShallowCopier : LibraryTypes.DeepCopier_1).ToTypeSyntax(type.TypeSyntax));

        var fields = new List<GeneratedFieldDescription>();
        var onlyDeepFields = false;
        var memberPlans = ImmutableArray<MemberEmissionPlan>.Empty;
        if (!isShallowCopyable)
        {
            fields = GetFieldDescriptions(type, members, isExceptionType, out onlyDeepFields);
            var copiers = type.IsUnsealedImmutable ? null : fields.OfType<ICopierDescription>().Concat(LibraryTypes.StaticCopiers).ToList();
            // 价值类型计划省略浅薄成员:复制原始结构已经保存了这些字段。
            memberPlans = members.Where(member => !onlyDeepFields || !member.IsShallowCopyable).Select(member =>
            {
                var getter = member.GetGetter();
                var setter = member.GetSetter();
                return new MemberEmissionPlan(member.Member.FieldId, member.IsPrimaryConstructorParameter,
                    member.IsValueType, getter, setter, null, null, GenerateMemberCopy(fields, copiers, member));
            }).ToImmutableArray();
        }

        var construction = new GeneratedTypeConstruction(LibraryTypes, fields,
            !isShallowCopyable && !isExceptionType && !type.IsSealedType, isExceptionType && !isShallowCopyable);
        return new(typePlan ?? SerializableTypeEmissionPlan.Create(type, LibraryTypes), isShallowCopyable, isExceptionType,
            onlyDeepFields, baseType, CodeText.Type(LibraryTypes.BaseCopier_1.ToTypeSyntax(type.TypeSyntax)),
            ConstructionEmissionPlan.Create(construction),
            fields.Select(FieldEmissionPlan.Create).ToImmutableArray(), memberPlans);
    }

    internal static void Write(SourceWriter writer, CopierEmissionPlan plan)
    {
        var type = plan.Type;
        var simpleClassName = GetSimpleClassName(type.Name);
        var isExceptionType = plan.IsExceptionType;

        var declaration = new StringBuilder();
        declaration.Append(type.IsPublic ? "public" : "internal").Append(" sealed class ").Append(simpleClassName);
        type.AppendTypeParameterList(declaration);
        declaration.Append(" : ").Append(plan.BaseType);
        if (!plan.IsShallowCopyable && plan.Construction.HasBaseContract)
        {
            declaration.Append(", ").Append(plan.AdditionalInterface);
        }

        writer.WriteLine(GeneratedCodeUtilities.GeneratedCodeAttributeText);
        GenericParameterEmissionPlan.WriteDeclarationLine(writer, declaration, type.TypeParameters);
        writer.OpenBlock();
        if (!plan.IsShallowCopyable)
        {
            var members = plan.Members.ToList();
            WriteFieldDeclarations(writer, plan.Fields);
            if (plan.UnionCases is { } unionCases)
            {
                UnionCodecGenerator.WriteUnionCopyMethod(writer, type, unionCases);
            }
            else if (!isExceptionType)
            {
                WriteMemberwiseDeepCopyMethod(writer, type, plan.Fields, members, plan.OnlyDeepFields);
            }

            if (isExceptionType || plan.Fields.Any(HasConstructorStatement))
            {
                WriteConstructorDeclaration(writer, simpleClassName, plan.Construction, isExceptionType ? "base(codecProvider)" : null);
                writer.OpenBlock();
                WriteConstructorStatements(writer, plan.Fields);
                writer.CloseBlock();
            }

            if (isExceptionType || plan.Construction.HasBaseContract)
            {
                WriteBaseCopierDeepCopyMethod(writer, type, members, isExceptionType);
            }
        }

        writer.CloseBlock();
    }

    internal GeneratedTypeConstruction GetConstruction(ISerializableTypeDescription type)
    {
        if (type.IsShallowCopyable)
            return new(LibraryTypes, [], hasBaseContract: false);

        var isExceptionType = type.IsExceptionType && type.SerializationHooks.Count == 0;
        return new(LibraryTypes, GetFieldDescriptions(type, [], isExceptionType, out _),
            !isExceptionType && !type.IsSealedType, isExceptionType);
    }

    public static string GetSimpleClassName(ISerializableTypeDescription serializableType) => GetSimpleClassName(serializableType.Name);

    public static string GetSimpleClassName(string name) => $"Copier_{name}";

    private List<GeneratedFieldDescription> GetFieldDescriptions(
        ISerializableTypeDescription serializableTypeDescription,
        List<ISerializableMember> members,
        bool isExceptionType,
        out bool onlyDeepFields)
    {
        var serializationHooks = serializableTypeDescription.SerializationHooks;
        onlyDeepFields = serializableTypeDescription.IsValueType && serializationHooks.Count == 0;

        var fields = new List<GeneratedFieldDescription>();

        var hotReloadShape = UseHotReloadShape(serializableTypeDescription);
        if (hotReloadShape)
        {
            fields.Add(new CodecProviderFieldDescription(LibraryTypes.ICodecProvider.ToTypeSyntax()));
        }

        if (!isExceptionType && serializableTypeDescription.HasComplexBaseType)
        {
            fields.Add(GetBaseTypeField(serializableTypeDescription));
        }

        if (!serializableTypeDescription.IsImmutable)
        {
            if (!isExceptionType && serializableTypeDescription.UseActivator && !serializableTypeDescription.IsAbstractType)
            {
                onlyDeepFields = false;
                fields.Add(new ActivatorFieldDescription(LibraryTypes.IActivator_1.ToTypeSyntax(serializableTypeDescription.TypeSyntax), ActivatorFieldName));
            }

            // 目标中没有任何静态复印机的字段, 添加一个复制场 。
            GetCopierFieldDescriptions(serializableTypeDescription.Members, fields, hotReloadShape);
        }

        foreach (var member in members)
        {
            if (onlyDeepFields && member.IsShallowCopyable) continue;

            AddFieldAccessorDescriptions(fields, member);
        }

        for (var hookIndex = 0; hookIndex < serializationHooks.Count; ++hookIndex)
        {
            var hookType = serializationHooks[hookIndex];
            fields.Add(new SerializationHookFieldDescription(hookType.ToTypeSyntax(), $"_hook{hookIndex}"));
        }

        return fields;
    }

    private BaseCopierFieldDescription GetBaseTypeField(ISerializableTypeDescription serializableTypeDescription)
    {
        var baseType = serializableTypeDescription.BaseType;
        if (baseType.HasAttribute(LibraryTypes.GenerateSerializerAttribute)
            && (SymbolEqualityComparer.Default.Equals(baseType.ContainingAssembly, LibraryTypes.Compilation.Assembly) || baseType.ContainingAssembly.HasAttribute(LibraryTypes.TypeManifestProviderAttribute))
            && baseType is not INamedTypeSymbol { IsGenericType: true })
        {
            // 使用混凝土生成的类型,避免昂贵的接口发送(可返回 IBaseCopier<T>的通用类型除外)
            return new(QualifiedName(ParseName(GetGeneratedNamespaceName(baseType)), IdentifierName(GetSimpleClassName(baseType.Name))), true);
        }

        return new(LibraryTypes.BaseCopier_1.ToTypeSyntax(serializableTypeDescription.BaseTypeSyntax));
    }

    public void GetCopierFieldDescriptions(IEnumerable<IMemberDescription> members, List<GeneratedFieldDescription> fields, bool lazyInitialization = false)
    {
        var copierMembers = new List<IMemberDescription>();
        var uniqueTypes = new HashSet<IMemberDescription>(MemberDescriptionTypeComparer.Default);
        foreach (var member in members)
        {
            if (!member.IsCopyable)
            {
                continue;
            }

            var t = member.Type;

            if (LibraryTypes.IsShallowCopyable(t))
                continue;

            if (LibraryTypes.StaticCopiers.Any(c => SymbolEqualityComparer.Default.Equals(c.UnderlyingType, t)))
                continue;

            if (member.Symbol.HasAttribute(LibraryTypes.ImmutableAttribute))
                continue;

            if (!uniqueTypes.Add(member))
                continue;

            copierMembers.Add(member);
        }

        var fieldNames = GeneratedFieldNames.ForTypes("_copier", copierMembers);
        for (var i = 0; i < copierMembers.Count; i++)
        {
            var member = copierMembers[i];
            fields.Add(new CopierFieldDescription(GetCopierType(member), fieldNames[i], member.Type, lazyInitialization));
        }
    }

    private TypeSyntax GetCopierType(IMemberDescription member)
    {
        var t = member.Type;
        if (LanguageFeatureSymbols.IsUnion(t)
            && !SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, LibraryTypes.Compilation.Assembly))
        {
            // 参考浅质工会可注册 ShallowCopier<T> ,而不发混凝土生成的复印机。
            return LibraryTypes.DeepCopier_1.ToTypeSyntax(member.TypeSyntax);
        }

        if (t.HasAttribute(LibraryTypes.GenerateSerializerAttribute)
            && (SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, LibraryTypes.Compilation.Assembly) || t.ContainingAssembly.HasAttribute(LibraryTypes.TypeManifestProviderAttribute))
            && t is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 0 })
        {
            // 使用混凝土生成的类型,避免昂贵的接口发送(复杂的嵌套案例除外,这些复杂案例将返回 IDeepCopier<T>)
            SimpleNameSyntax name;
            if (t is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
            {
                // 构造全通用类型名称
                name = GenericName(Identifier(GetSimpleClassName(t.Name)), TypeArgumentList(SeparatedList(namedTypeSymbol.TypeArguments.Select(arg => arg.ToTypeSyntax()))));
            }
            else
            {
                name = IdentifierName(GetSimpleClassName(t.Name));
            }

            return QualifiedName(ParseName(GetGeneratedNamespaceName(t)), name);
        }

        if (t is IArrayTypeSymbol { IsSZArray: true } array)
        {
            return LibraryTypes.ArrayCopier.Construct(array.ElementType).ToTypeSyntax();
        }

        if (LibraryTypes.WellKnownCopiers.FindByUnderlyingType(t) is { } copier)
        {
            // 复印机不是静态复印机,也不是通用复印机。
            return copier.CopierType.ToTypeSyntax();
        }

        if (t is INamedTypeSymbol { ConstructedFrom: ISymbol unboundFieldType } named && LibraryTypes.WellKnownCopiers.FindByUnderlyingType(unboundFieldType) is { } genericCopier)
        {
            // 使用字段类型参数构造通用复印机类型。
            return genericCopier.CopierType.Construct([.. named.TypeArguments]).ToTypeSyntax();
        }

        // 使用 IDeepCopier<T> 接口
        return LibraryTypes.DeepCopier_1.ToTypeSyntax(member.TypeSyntax);
    }

    private static void WriteMemberwiseDeepCopyMethod(
        SourceWriter writer,
        SerializableTypeEmissionPlan type,
        EquatableArray<FieldEmissionPlan> copierFields,
        List<MemberEmissionPlan> members,
        bool onlyDeepFields)
    {
        // 一种数值类型,其成员都是经过深层复印的原版副本,因此其参数就是结果。
        var originalParam = type.IsValueType && onlyDeepFields && !type.IsAbstractType && !type.IsUnsealedImmutable ? "result" : "original";
        writer.WriteLine(GeneratedCodeUtilities.MethodImplAttributeText);
        writer.WriteLine($"public {type.Type} DeepCopy({type.Type} {originalParam}, {type.Library.CopyContext} context)");
        writer.OpenBlock();

        var membersCopied = false;
        if (type.IsAbstractType)
        {
            writer.WriteLine("return context.DeepCopy(original);");
            membersCopied = true;
        }
        else if (type.IsUnsealedImmutable)
        {
            writer.WriteLine($"return original is null || original.GetType() == typeof({type.Type}) ? original : context.DeepCopy(original);");
            membersCopied = true;
        }
        else if (!type.IsValueType)
        {
            if (type.TrackReferences)
            {
                writer.WriteLine($"if (context.TryGetCopy(original, out {type.Type} existing))");
                WriteEmbedded(writer, "return existing;");
            }
            else
            {
                writer.WriteLine("if (original is null)");
                WriteEmbedded(writer, "return null;");
            }

            if (!type.IsSealedType)
            {
                writer.WriteLine($"if (original.GetType() != typeof({type.Type}))");
                WriteEmbedded(writer, "return context.DeepCopy(original);");
            }

            writer.WriteLine($"var result = {GetCreateValueExpression(type, copierFields)};");
            if (type.TrackReferences)
            {
                writer.WriteLine("context.RecordCopy(original, result);");
            }

            if (!type.IsSealedType)
            {
                writer.WriteLine("DeepCopy(original, result, context);");
                writer.WriteLine("return result;");
                membersCopied = true;
            }
            else if (type.HasComplexBaseType)
            {
                writer.WriteLine($"{BaseTypeCopierFieldName}.DeepCopy(original, result, context);");
            }
        }
        else if (!onlyDeepFields)
        {
            writer.WriteLine($"var result = {GetCreateValueExpression(type, copierFields)};");
        }

        if (!membersCopied)
        {
            WriteMemberwiseCopy(writer, type, members, originalParam, "context", "result");
            writer.WriteLine("return result;");
        }

        writer.CloseBlock();

        static void WriteEmbedded(SourceWriter writer, string statement)
        {
            writer.Indent();
            writer.WriteLine(statement);
            writer.Unindent();
        }
    }

    private static string GetCreateValueExpression(SerializableTypeEmissionPlan type, EquatableArray<FieldEmissionPlan> copierFields)
    {
        if (!type.UseActivator)
        {
            return type.ObjectCreation;
        }

        var activator = copierFields.FirstOrDefault(static field => field.Kind == EmissionFieldKind.Service && field.FieldName == ActivatorFieldName);
        Debug.Assert(activator is not null);
        return $"{activator!.FieldName}.Create()";
    }

    private static void WriteBaseCopierDeepCopyMethod(
        SourceWriter writer,
        SerializableTypeEmissionPlan type,
        List<MemberEmissionPlan> members,
        bool isExceptionType)
    {
        // 例外复印机继承其基本副本,除非产生的类型增加成员或回调。
        if (isExceptionType && members.Count == 0
            && !type.SerializationHooks.Any(static callback => callback.Method is "OnCopying" or "OnCopied"))
        {
            return;
        }

        writer.WriteLine(GeneratedCodeUtilities.MethodImplAttributeText);
        writer.WriteLine($"public {(isExceptionType ? "override " : string.Empty)}void DeepCopy({type.Type} input, {type.Type} output, {type.Library.CopyContext} context)");
        writer.OpenBlock();
        if (type.HasComplexBaseType)
        {
            writer.WriteLine($"{(isExceptionType ? "base" : BaseTypeCopierFieldName)}.DeepCopy(input, output, context);");
        }

        WriteMemberwiseCopy(writer, type, members, "input", "context", "output");
        writer.CloseBlock();
    }

    private static void WriteMemberwiseCopy(
        SourceWriter writer,
        SerializableTypeEmissionPlan type,
        List<MemberEmissionPlan> members,
        string source,
        string context,
        string destination)
    {
        WriteSerializationCallbacks(writer, type, source, destination, "OnCopying");
        foreach (var member in members.OrderBy(static m => m.FieldId))
        {
            writer.WriteLine(member.GetSetter(destination, member.GetCopy(source, context)) + ";");
        }

        WriteSerializationCallbacks(writer, type, source, destination, "OnCopied");
    }

    /// <summary>
    /// 计划成员值的复制件: 浅值被指定, 其他数值被静态生成、生成、复制或复制;
    /// 或上下文已解析的复印机。
    /// </summary>
    public MemberCopyPlan GenerateMemberCopy(
        List<GeneratedFieldDescription> copierFields,
        List<ICopierDescription>? copiers,
        ISerializableMember member)
    {
        if (copiers is null || member.IsShallowCopyable)
            return MemberCopyPlan.Value;

        var description = member.Member;

        // 复印机可以是静态分类,也可以是注入制造器。
        // 无论哪种方式,成员签名是相同的。
        var memberType = description.Type;
        var copier = copiers.Find(f => SymbolEqualityComparer.Default.Equals(f.UnderlyingType, memberType));
        if (copier is null)
        {
            return new(MemberCopyKind.Context);
        }

        string copierExpression;
        var staticCopier = LibraryTypes.StaticCopiers.FindByUnderlyingType(memberType);
        if (staticCopier != null)
        {
            copierExpression = CodeText.Type(staticCopier.CopierType.ToNameSyntax());
        }
        else
        {
            var instanceCopier = copierFields.OfType<CopierFieldDescription>().First(f => SymbolEqualityComparer.Default.Equals(f.UnderlyingType, memberType));
            copierExpression = GetCodecExpression(instanceCopier);
        }

        // 如果成员类型不同于复印机类型(例如,由于成员是一个阵列),请投下结果。
        var castType = SymbolEqualityComparer.Default.Equals(copier.UnderlyingType, member.Member.Type) ? null : CodeText.Type(description.TypeSyntax);
        return new(MemberCopyKind.Copier, copierExpression, castType);
    }

    internal static void WriteSerializationCallbacks(SourceWriter writer, SerializableTypeEmissionPlan type, string originalInstance, string resultInstance, string callbackMethodName)
    {
        foreach (var callback in type.SerializationHooks)
        {
            if (callback.Method != callbackMethodName)
            {
                continue;
            }

            writer.WriteLine($"_hook{callback.HookIndex}.{callbackMethodName}({(callback.FirstByRef ? "ref " : string.Empty)}{originalInstance}, {(callback.SecondByRef ? "ref " : string.Empty)}{resultInstance});");
        }
    }

    internal sealed class BaseCopierFieldDescription(TypeSyntax fieldType, bool concreteType = false) : GeneratedFieldDescription(fieldType, BaseTypeCopierFieldName)
    {
        public override bool IsInjected { get; } = !concreteType;
    }

    internal sealed class CopierFieldDescription(TypeSyntax fieldType, string fieldName, ITypeSymbol underlyingType, bool lazyInitialization = false) : GeneratedFieldDescription(fieldType, fieldName), ICopierDescription
    {
        public ITypeSymbol UnderlyingType { get; } = underlyingType;
        public override bool IsInjected => false;
        public override bool LazyInitialization { get; } = lazyInitialization;
    }

}
