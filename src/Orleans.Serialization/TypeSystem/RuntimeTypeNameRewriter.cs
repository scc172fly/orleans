
using System;

namespace Orleans.Serialization.TypeSystem;

/// <summary>
/// 重写 <see cref="TypeSpec"/> 图形。
/// </summary>
internal static class RuntimeTypeNameRewriter
{
    /// <summary>
    /// 代表重写 <see cref="QualifiedType"/>签名。
    /// </summary>
    /// <param name="input">输入。</param>
    /// <param name="state">国家向再版方法提供的数据。</param>
    /// <returns>重写合格类型。</returns>
    public delegate QualifiedType Rewriter<TState>(in QualifiedType input, ref TState state);

    /// <summary>
    /// 代表要签名解决复合型别名。
    /// </summary>
    /// <param name="input">输入。</param>
    /// <param name="state">国家提供解析方法。</param>
    /// <returns>溶解型号。</returns>
    public delegate TypeSpec CompoundAliasResolver<TState>(TupleTypeSpec input, ref TState state);

    /// <summary>
    /// 使用所提供的重写代表重写一个 <see cref="TypeSpec"/> 。
    /// </summary>
    public static TypeSpec Rewrite<TState>(TypeSpec input, Rewriter<TState> rewriter, ref TState state)
    {
        var instance = new TypeRewriter<TState>(rewriter, null, ref state);
        var result = instance.Rewrite(input);
        state = instance._userState;
        return result;
    }

    /// <summary>
    /// 使用所提供的重写代表重写一个 <see cref="TypeSpec"/> 。
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
                // 如果再写器泡沫 组装,在这里添加。 
                return new AssemblyQualifiedTypeSpec(result.Type, result.Assembly);
            }

            return result.Type;
        }

        private (TypeSpec Type, string? Assembly) ApplyInner(TypeSpec input, string? assemblyName) =>
        // 类型组装通过图向下传递,对组装的修改(由用户提供的代表)向上流动。
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
                // 通用类型参数不继承通用类型的组装。
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
                // 更改类型名称并可能修改组装。
                var resultType = RuntimeTypeNameParser.Parse(replacementName.Type);
                if (!(resultType is NamedTypeSpec))
                {
                    throw new InvalidOperationException($"Replacement type name, \"{replacementName.Type}\", can not deviate from the original type structure of the input, \"{nsQualified}\"");
                }

                return (resultType, replacementName.Assembly);
            }
            else if (!string.Equals(assemblyName, replacementName.Assembly, StringComparison.Ordinal))
            {
                // 只改变集会;
                return (type, replacementName.Assembly);
            }
            else if (type.ContainingType is not null)
            {
                // 用户有机会改变父/母(包括集会)的父(母)关系。
                var replacementParent = ApplyInner(type.ContainingType, assemblyName);
                if (ReferenceEquals(replacementParent.Type, type.ContainingType))
                {
                    // 对类型无变化。
                    return (type, replacementParent.Assembly);
                }

                // 家长类型改变了。
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

            // 组别名称更改不会在组合限定节点上出现泡沫。
            if (string.IsNullOrWhiteSpace(replacement.Assembly))
            {
                // 删除组装资格
                return (replacement.Type, assemblyName);
            }
            else if (!string.Equals(replacement.Assembly, type.Assembly, StringComparison.Ordinal) || !ReferenceEquals(replacement.Type, type.Type))
            {
                // 更新组件或类型。
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

            // 在首次试图解决每个元素之后,先确定化合物别名。
            var replacementTypeSpec = new TupleTypeSpec(elements, type.Arity);
            if (_compoundTypeRewriter is { } rewriter)
            {
                var resolved = rewriter(replacementTypeSpec, ref _userState);
                if (resolved is TupleTypeSpec)
                {
                    throw new InvalidOperationException($"Compound type alias resolver resolved {type} into {resolved} which is also an alias type.");
                }

                // 给重新写入者一个重写已解决名称的机会。
                return ApplyInner(resolved, assemblyName);
            }

            return (replacementTypeSpec, assemblyName);
        }
    }
}
