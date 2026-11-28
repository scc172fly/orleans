
using System;

namespace Orleans.Serialization.TypeSystem;

/// <summary>
/// 重写 <see cref="TypeSpec"/> 图。
/// </summary>
internal static class RuntimeTypeNameRewriter
{
    /// <summary>
    /// 用于重写 <see cref="QualifiedType"/> 的委托签名。
    /// </summary>
    /// <param name="input">输入。</param>
    /// <param name="state">提供给重写方法的状态。</param>
    /// <returns>重写后的限定类型。</returns>
    public delegate QualifiedType Rewriter<TState>(in QualifiedType input, ref TState state);

    /// <summary>
    /// 用于解析复合类型别名的委托签名。
    /// </summary>
    /// <param name="input">输入。</param>
    /// <param name="state">提供给解析方法的状态。</param>
    /// <returns>解析得到的类型。</returns>
    public delegate TypeSpec CompoundAliasResolver<TState>(TupleTypeSpec input, ref TState state);

    /// <summary>
    /// 使用给定的重写委托重写 <see cref="TypeSpec"/>。
    /// </summary>
    public static TypeSpec Rewrite<TState>(TypeSpec input, Rewriter<TState> rewriter, ref TState state)
    {
        var instance = new TypeRewriter<TState>(rewriter, null, ref state);
        var result = instance.Rewrite(input);
        state = instance._userState;
        return result;
    }

    /// <summary>
    /// 使用给定的重写委托重写 <see cref="TypeSpec"/>。
    /// </summary>
    public static TypeSpec Rewrite<TState>(TypeSpec input, Rewriter<TState> rewriter, CompoundAliasResolver<TState>? compoundAliasRewriter, ref TState state)
    {
        var instance = new TypeRewriter<TState>(rewriter, compoundAliasRewriter, ref state);
        var result = instance.Rewrite(input);
        state = instance._userState;
        return result;
    }

    private struct TypeRewriter<TState>
    {
        private readonly Rewriter<TState> _nameRewriter { get; }
        private readonly CompoundAliasResolver<TState>? _compoundTypeRewriter { get; }
        public TState _userState;

        public TypeRewriter(Rewriter<TState> nameRewriter, CompoundAliasResolver<TState>? compoundTypeRewriter, ref TState initialUserState)
        {
            _nameRewriter = nameRewriter;
            _compoundTypeRewriter = compoundTypeRewriter;
            _userState = initialUserState;
        }

        public TypeSpec Rewrite(TypeSpec input)
        {
            var result = ApplyInner(input, null);
            if (result.Assembly is not null)
            {
                // 如果重写器向上传递了程序集，则在此处添加它。
                return new AssemblyQualifiedTypeSpec(result.Type, result.Assembly);
            }

            return result.Type;
        }

        private (TypeSpec Type, string? Assembly) ApplyInner(TypeSpec input, string? assemblyName) =>
        // 类型的程序集沿类型图向下传递，用户提供的委托对程序集所作的修改则向上传递。
        input switch
        {
            ConstructedGenericTypeSpec type => HandleGeneric(type, assemblyName),
            NamedTypeSpec type => HandleNamedType(type, assemblyName),
            AssemblyQualifiedTypeSpec type => HandleAssembly(type, assemblyName),
            ArrayTypeSpec type => HandleArray(type, assemblyName),
            PointerTypeSpec type => HandlePointer(type, assemblyName),
            ReferenceTypeSpec type => HandleReference(type, assemblyName),
            TupleTypeSpec type => HandleCompoundType(type, assemblyName),
            LiteralTypeSpec type => (type, assemblyName),
            null => throw new ArgumentNullException(nameof(input)),
            _ => throw new NotSupportedException($"Argument of type {input.GetType()} is nut supported"),
        };

        private (TypeSpec Type, string? Assembly) HandleGeneric(ConstructedGenericTypeSpec type, string? assemblyName)
        {
            var (unconstructed, replacementAssembly) = ApplyInner(type.UnconstructedType, assemblyName);
            if (unconstructed is AssemblyQualifiedTypeSpec assemblyQualified)
            {
                unconstructed = assemblyQualified.Type;
                replacementAssembly = assemblyQualified.Assembly;
            }

            var newArguments = new TypeSpec[type.Arguments.Length];
            var didChange = false;
            for (var i = 0; i < type.Arguments.Length; i++)
            {
                // 泛型类型的类型实参不继承该泛型类型的程序集。
                var args = ApplyInner(type.Arguments[i], null);

                if (args.Assembly is not null)
                {
                    newArguments[i] = new AssemblyQualifiedTypeSpec(args.Type, args.Assembly);
                }
                else
                {
                    newArguments[i] = args.Type;
                }

                didChange |= !ReferenceEquals(newArguments[i], type.Arguments[i]);
            }

            if (ReferenceEquals(type.UnconstructedType, unconstructed) && !didChange)
            {
                return (type, replacementAssembly);
            }

            return (new ConstructedGenericTypeSpec(unconstructed, newArguments.Length, newArguments), replacementAssembly);
        }

        private (TypeSpec Type, string? Assembly) HandleNamedType(NamedTypeSpec type, string? assemblyName)
        {
            var nsQualified = type.GetNamespaceQualifiedName();
            var replacementName = _nameRewriter(new QualifiedType(assembly: assemblyName, type: nsQualified), ref _userState);

            if (!string.Equals(nsQualified, replacementName.Type, StringComparison.Ordinal))
            {
                // 更改类型名称，并可能更改程序集。
                var resultType = RuntimeTypeNameParser.Parse(replacementName.Type);
                if (!(resultType is NamedTypeSpec))
                {
                    throw new InvalidOperationException($"Replacement type name, \"{replacementName.Type}\", can not deviate from the original type structure of the input, \"{nsQualified}\"");
                }

                return (resultType, replacementName.Assembly);
            }
            else if (!string.Equals(assemblyName, replacementName.Assembly, StringComparison.Ordinal))
            {
                // 仅更改程序集；
                return (type, replacementName.Assembly);
            }
            else if (type.ContainingType is not null)
            {
                // 让用户有机会更改外层类型，包括其程序集。
                var replacementParent = ApplyInner(type.ContainingType, assemblyName);
                if (ReferenceEquals(replacementParent.Type, type.ContainingType))
                {
                    // 类型未发生更改。
                    return (type, replacementParent.Assembly);
                }

                // 外层类型发生了更改。
                var typedReplacement = (NamedTypeSpec)replacementParent.Type;
                return (new NamedTypeSpec(typedReplacement, type.Name, type.Arity), replacementParent.Assembly);
            }
            else
            {
                return (type, replacementName.Assembly);
            }
        }

        private (TypeSpec Type, string? Assembly) HandleAssembly(AssemblyQualifiedTypeSpec type, string? assemblyName)
        {
            var replacement = ApplyInner(type.Type, type.Assembly);

            // 程序集名称的更改不会向上传递越过程序集限定符节点。
            if (string.IsNullOrWhiteSpace(replacement.Assembly))
            {
                // 移除程序集限定信息。
                return (replacement.Type, assemblyName);
            }
            else if (!string.Equals(replacement.Assembly, type.Assembly, StringComparison.Ordinal) || !ReferenceEquals(replacement.Type, type.Type))
            {
                // 更新程序集或类型。
                return (new AssemblyQualifiedTypeSpec(replacement.Type, replacement.Assembly), assemblyName);
            }

            // 无更新。
            return (type, assemblyName);
        }

        private (TypeSpec Type, string? Assembly) HandleArray(ArrayTypeSpec type, string? assemblyName)
        {
            var element = ApplyInner(type.ElementType, assemblyName);
            if (element.Type is AssemblyQualifiedTypeSpec qualified)
            {
                return (new ArrayTypeSpec(qualified.Type, type.Dimensions), qualified.Assembly);
            }

            if (ReferenceEquals(element.Type, type.ElementType))
            {
                return (type, element.Assembly);
            }

            return (new ArrayTypeSpec(element.Type, type.Dimensions), element.Assembly);
        }

        private (TypeSpec Type, string? Assembly) HandleReference(ReferenceTypeSpec type, string? assemblyName)
        {
            var element = ApplyInner(type.ElementType, assemblyName);
            if (ReferenceEquals(element.Type, type.ElementType))
            {
                return (type, element.Assembly);
            }

            return (new ReferenceTypeSpec(element.Type), element.Assembly);
        }

        private (TypeSpec Type, string? Assembly) HandlePointer(PointerTypeSpec type, string? assemblyName)
        {
            var element = ApplyInner(type.ElementType, assemblyName);
            if (ReferenceEquals(element.Type, type.ElementType))
            {
                return (type, element.Assembly);
            }

            return (new PointerTypeSpec(element.Type), element.Assembly);
        }

        private (TypeSpec Type, string? Assembly) HandleCompoundType(TupleTypeSpec type, string? assemblyName)
        {
            var elements = new TypeSpec[type.Elements.Length];
            for (var i = 0; i < type.Elements.Length; i++)
            {
                (elements[i], _) = ApplyInner(type.Elements[i], assemblyName);
            }

            // 先尝试解析复合类型别名中的各个独立组成部分，再解析整个别名。
            var replacementTypeSpec = new TupleTypeSpec(elements, type.Arity);
            if (_compoundTypeRewriter is { } rewriter)
            {
                var resolved = rewriter(replacementTypeSpec, ref _userState);
                if (resolved is TupleTypeSpec)
                {
                    throw new InvalidOperationException($"Compound type alias resolver resolved {type} into {resolved} which is also an alias type.");
                }

                // 让重写器有机会重写解析后的名称。
                return ApplyInner(resolved, assemblyName);
            }

            return (replacementTypeSpec, assemblyName);
        }
    }
}
