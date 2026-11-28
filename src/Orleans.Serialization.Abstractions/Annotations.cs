using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans
{
    /// <summary>
    /// 应用于类型时，指定该类型需要序列化，并应为其生成序列化代码。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
    public sealed class GenerateSerializerAttribute : Attribute
    {
        /// <summary>
        /// 获取或设置是否应自动将主构造函数参数作为可序列化字段包含在内。
        /// 默认值为 <see langword="true"/>（对于 <see langword="record"/> 类型），否则为 <see langword="false"/>。
        /// </summary>
        public bool IncludePrimaryConstructorParameters { get; init; }

        /// <summary>
        /// 获取或设置 Orleans 何时自动分配字段 ID。默认行为是不自动分配字段 ID。
        /// </summary>
        public GenerateFieldIds GenerateFieldIds { get; init; } = GenerateFieldIds.None;
    }

    /// <summary>
    /// 应用于接口时，指定应生成支持代码，以便对接口方法进行远程调用。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
    public sealed class GenerateMethodSerializersAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="GenerateMethodSerializersAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="proxyBase">代理基类型。</param>
        /// <param name="isExtension">如果设置为 <see langword="true"/>，则此接口为扩展接口。</param>
        public GenerateMethodSerializersAttribute(Type proxyBase, bool isExtension = false)
        {
            ProxyBase = proxyBase;
            IsExtension = isExtension;
        }

        /// <summary>
        /// 获取源生成器生成代理类时使用的基类型。
        /// </summary>
        /// <value>代理基类型。</value>
        public Type ProxyBase { get; }

        /// <summary>
        /// 获取指示此接口是否为扩展接口的值。
        /// </summary>
        /// <value>如果此实例为扩展接口，则为 true；否则为 false。</value>
        public bool IsExtension { get; }
    }

    /// <summary>
    /// 应用于可调用接口的方法特性，用于指定提交请求时要在基类型上调用的方法名称。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InvokeMethodNameAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="InvokeMethodNameAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="invokeMethodName">调用方法的名称。</param>
        public InvokeMethodNameAttribute(string invokeMethodName)
        {
            InvokeMethodName = invokeMethodName;
        }

        /// <summary>
        /// 获取调用方法的名称。
        /// </summary>
        /// <value>调用方法的名称。</value>
        public string InvokeMethodName { get; }
    }

    /// <summary>
    /// 应用于代理基类型以及可调用接口方法使用的特性类型，用于指定表示方法调用的可调用对象的基类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class DefaultInvokeMethodNameAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="DefaultInvokeMethodNameAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="returnType">返回类型。</param>
        /// <param name="methodName">方法名称。</param>
        public DefaultInvokeMethodNameAttribute(Type returnType, string methodName)
        {
            ReturnType = returnType;
            MethodName = methodName;
        }

        /// <summary>
        /// 获取此特性适用的接口方法返回类型。
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// 获取调用方法时要在代理基类型上调用的方法名称。
        /// </summary>
        public string MethodName { get; }
    }

    /// <summary>
    /// 应用于接口方法的特性类型，用于指定调用该接口方法时创建的可调用对象上应调用的方法。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class InvokableCustomInitializerAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="InvokableCustomInitializerAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="methodName">方法名称。</param>
        /// <param name="methodArgumentValue">方法实参的值。</param>
        public InvokableCustomInitializerAttribute(string methodName, object methodArgumentValue)
        {
            MethodName = methodName;
            MethodArgumentValue = methodArgumentValue;
            AttributeArgumentIndex = -1;
        }

        /// <summary>
        /// 初始化 <see cref="InvokableCustomInitializerAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="methodName">方法名称。</param>
        public InvokableCustomInitializerAttribute(string methodName)
        {
            MethodName = methodName;
            MethodArgumentValue = null;
            AttributeArgumentIndex = 0;
        }

        /// <summary>
        /// 获取方法名称。
        /// </summary>
        public string MethodName { get; }

        /// <summary>
        /// 获取或设置要传递给自定义初始化方法的特性实参的索引。
        /// </summary>
        public int AttributeArgumentIndex { get; init; }

        /// <summary>
        /// 获取或设置要传递给自定义初始化方法的特性实参的名称。
        /// </summary>
        public int AttributeArgumentName { get; init; }

        /// <summary>
        /// 获取或设置要传递给自定义初始化方法的值。
        /// </summary>
        public object? MethodArgumentValue { get; }
    }

    /// <summary>
    /// 应用于代理基类型以及可调用接口方法使用的特性类型，用于指定表示方法调用的可调用对象的基类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class DefaultInvokableBaseTypeAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="DefaultInvokableBaseTypeAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="returnType">此特性适用的方法的返回类型。</param>
        /// <param name="invokableBaseType">可调用对象的基类型。</param>
        public DefaultInvokableBaseTypeAttribute(Type returnType, Type invokableBaseType)
        {
            ReturnType = returnType;
            InvokableBaseType = invokableBaseType;
        }

        /// <summary>
        /// 获取此特性适用的方法的返回类型。
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// 获取可调用对象类的基类型，该对象用于表示对此特性适用方法的调用。
        /// </summary>
        public Type InvokableBaseType { get; }

        /// <summary>
        /// 获取或设置代理对象上用于发起此方法调用的方法名称。
        /// </summary>
        public string? ProxyInvokeMethodName { get; init; }
    }

    /// <summary>
    /// 应用于返回类型、可调用接口方法使用的特性类型或程序集，用于指定
    /// 表示方法调用的可调用对象的基类型。当返回类型和代理基类型
    /// 分属独立的库时，可以使用程序集级注册。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class InvokableBaseTypeAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="InvokableBaseTypeAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="proxyBaseClass">代理基类。</param>
        /// <param name="returnType">此特性适用的方法的返回类型。</param>
        /// <param name="invokableBaseType">可调用对象的基类型。</param>
        public InvokableBaseTypeAttribute(Type proxyBaseClass, Type returnType, Type invokableBaseType)
        {
            ProxyBaseClass = proxyBaseClass;
            ReturnType = returnType;
            InvokableBaseType = invokableBaseType;
        }

        /// <summary>
        /// 获取此特性适用的代理基类。
        /// </summary>
        public Type ProxyBaseClass { get; }

        /// <summary>
        /// 获取此特性适用的方法返回类型。
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// 获取生成的可调用对象所使用的基类型。
        /// </summary>
        public Type InvokableBaseType { get; }

        /// <summary>
        /// 获取或设置调用此方法时要在代理对象上调用的方法名称。
        /// </summary>
        public string? ProxyInvokeMethodName { get; init; }
    }

    /// <summary>
    /// 应用于可调用接口的方法特性，用于指定获取完成源的方法名称；该完成源会被传递给提交方法，最终返回给调用方。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GetCompletionSourceMethodNameAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="GetCompletionSourceMethodNameAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="methodName">用于为提交给运行时的请求获取完成源的方法名称。</param>
        public GetCompletionSourceMethodNameAttribute(string methodName)
        {
            MethodName = methodName;
        }

        /// <summary>
        /// 获取用于为提交给运行时的请求获取完成源的方法名称。
        /// </summary>
        public string MethodName { get; }
    }

    /// <summary>
    /// 指定成员的唯一标识。
    /// </summary>
    /// <remarks>
    /// 应用了 <see cref="GenerateSerializerAttribute"/> 的类型中，每个可序列化成员都必须应用一个 <see cref="IdAttribute"/> 特性，并具有唯一的 <see cref="IdAttribute.Id"/> 值。
    /// 对于位置记录类型，此特性可以直接应用于主构造函数参数，而非生成的属性。
    /// 对于 RPC 方法参数，显式标识符会覆盖该参数在可序列化参数中的从零开始的序号。
    /// 取消令牌参数不参与可序列化参数的序号计算，且所有最终标识符必须唯一。
    /// </remarks>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(
        AttributeTargets.Field
        | AttributeTargets.Property
        | AttributeTargets.Parameter
        | AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Method)]
    public sealed class IdAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="IdAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="id">标识符。</param>
        public IdAttribute(uint id)
        {
            Id = id;
        }

        /// <summary>
        /// 获取标识符。
        /// </summary>
        /// <value>标识符。</value>
        public uint Id { get; }
    }

    /// <summary>
    /// 为应用了 <see langword="closed"/> 修饰符且标记有 <see cref="GenerateSerializerAttribute"/> 的类型的直接子类型分配分支标识符。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 当封闭类型的直接子类型具有分支标识符时，声明为该封闭类型的字段会序列化为带标记的联合：
    /// 分支标识符编码在保存分支值的字段头中，因此当值的运行时类型恰好为分支类型时，
    /// 不编码其运行时类型。如果值的运行时类型派生自未密封的分支类型，则先编码为该分支，再编码
    /// 其运行时类型。
    /// </para>
    /// <para>
    /// 分支标识符的作用域为该封闭类型，且在其中必须唯一。封闭类型的每个直接子类型都必须具有分支标识符。
    /// 分支标识符是序列化格式的一部分，因此数据序列化后必须保持稳定。
    /// 标识符 0 至 6 的编码不需要额外字节。
    /// </para>
    /// <para>
    /// 添加分支标识符会使写入器改用带标记联合格式。若要在现有类型层次结构上采用它，应协调部署
    /// 更新后的类型层次结构，使其覆盖交换这些值的所有进程。为更新后的层次结构生成的读取器同时支持
    /// 运行时类型格式和带标记联合格式。
    /// </para>
    /// <para>
    /// 当联合类型的各分支是封闭类型的直接子类型，且具有相同的
    /// 分支标识符时，两种类型的序列化表示相同（参见 <see cref="CaseIdsAttribute"/>）。<see langword="null"/> 封闭类型值表示为空联合值。
    /// 因此可以将封闭类型层次结构替换为等效联合类型，而无需更改序列化数据。
    /// 每个分支的自身表示都包含其基类型的成员。因此，在使用联合类型替换封闭基类型且要从分支中移除此基类型时，
    /// 应声明不含可序列化成员的封闭基类型，并将 <see cref="SerializerTransparentAttribute"/> 应用于该类型。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CaseAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="CaseAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="id">分支标识符，在封闭基类型的直接子类型之间唯一。</param>
        public CaseAttribute(uint id)
        {
            Id = id;
        }

        /// <summary>
        /// 获取分支标识符。
        /// </summary>
        public uint Id { get; }
    }

    /// <summary>
    /// 为应用了 <see cref="GenerateSerializerAttribute"/> 的联合类型的各分支分配分支标识符。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 联合值序列化为带标记的联合：分支标识符编码在保存分支值的字段头中。
    /// 默认情况下，每个分支由它在联合声明中的从零开始的位置标识。
    /// 此特性按声明顺序为每个分支指定一个标识符，使得重新排序或移除分支时不会改变
    /// 其余分支的标识符。
    /// </para>
    /// <para>
    /// 标识符必须唯一。标识符 0 至 6 的编码不需要额外字节。
    /// </para>
    /// <para>
    /// 如果联合类型的各分支是封闭类型的直接子类型，且具有相同的分支标识符，则它与
    /// 该封闭类型的序列化表示相同（参见 <see cref="CaseAttribute"/>）。空联合值表示为 <see langword="null"/> 封闭类型值。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false)]
    public sealed class CaseIdsAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="CaseIdsAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="ids">分支标识符，按声明顺序为每个分支指定一个。</param>
        public CaseIdsAttribute(params uint[] ids)
        {
            Ids = ids;
        }

        /// <summary>
        /// 获取分支标识符，按声明顺序每个分支一个。
        /// </summary>
        public uint[] Ids { get; }
    }

    /// <summary>
    /// 指定序列化器根据序列化数据创建新实例时应使用的构造函数。
    /// </summary>
    /// <remarks>
    /// 最多只能为一个构造函数标记此特性。如果多个构造函数均标记了该特性，则忽略该特性。
    /// </remarks>
    /// <seealso cref="ActivatorUtilitiesConstructorAttribute" />
    [AttributeUsage(AttributeTargets.Constructor)]
    [Obsolete("Use GeneratedActivatorConstructorAttribute instead. This attribute is not recognized by Orleans.")]
    public sealed class OrleansConstructorAttribute : ActivatorUtilitiesConstructorAttribute
    {
        /// <summary>
        /// 初始化 <see cref="OrleansConstructorAttribute"/> 类的新实例。
        /// </summary>
        public OrleansConstructorAttribute()
        {
        }
    }

    /// <summary>
    /// 应用于类型或方法时，指定一个可用于标识该类型或方法的已知名称。
    /// </summary>
    /// <remarks>
    /// 对于类型，别名必须全局唯一；对于方法，别名必须在声明该方法的类型内唯一。
    /// </remarks>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(
        AttributeTargets.Class
        | AttributeTargets.Interface
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Method,
        AllowMultiple = true)]
    public sealed class AliasAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="AliasAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="alias">别名。</param>
        /// <remarks>
        /// 对于类型，别名必须全局唯一；对于方法，别名必须在声明该方法的类型内唯一。
        /// 别名不得为空，且不得包含引号、空白字符、控制字符或保留的类型名称分隔符（<c>[](),*&amp;</c>）。
        /// 表示泛型参数个数的后缀必须是位于各嵌套类型名称末尾的十进制整数，所表示的参数个数合计最多为 64。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="alias"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentException"><paramref name="alias"/> 为空，包含引号、空白字符、控制字符或保留的类型名称分隔符，或者具有无效的泛型参数个数后缀。</exception>
        public AliasAttribute(string alias)
        {
            Serialization.IdentityNameValidator.Validate(alias, nameof(alias));
            Alias = alias;
        }

        /// <summary>
        /// 获取别名。
        /// </summary>
        /// <remarks>
        /// 对于类型，别名必须全局唯一；对于方法，别名必须在声明该方法的类型内唯一。
        /// </remarks>
        public string Alias { get; }
    }

    /// <summary>
    /// 应用于类型时，指示应将该类型编码为与某个指定类型的关联。
    /// </summary>
    /// <seealso cref="Attribute" />
    [AttributeUsage(
        AttributeTargets.Class
        | AttributeTargets.Interface
        | AttributeTargets.Struct
        | AttributeTargets.Enum,
        AllowMultiple = true)]
    public sealed class CompoundTypeAliasAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="CompoundTypeAliasAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="components">别名的组成部分，每个部分必须是 <see cref="Components"/> 或 <see cref="string"/>。</param>
        public CompoundTypeAliasAttribute(params object[] components)
        {
            Components = components;
        }

        /// <summary>
        /// 获取别名的各组成部分。
        /// </summary>
        public object[] Components { get; }
    }

    /// <summary>
    /// 应用于类型时，指示该类型是提供程序，并应自动注册。
    /// </summary>
    /// <param name="name">提供程序名称，例如 <c>"AzureTableStorage"</c>。</param>
    /// <param name="kind">提供程序类别，例如 <c>"Clustering"</c>、<c>"Reminders"</c>。</param>
    /// <param name="target">提供程序的预期目标，例如 <c>"Server"</c>、<c>"Client"</c>。</param>
    /// <param name="type">用于配置提供程序的类型。</param>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class RegisterProviderAttribute(
        string name,
        string kind,
        string target,
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        Type type) : Attribute
    {
        /// <summary>
        /// 获取提供程序名称，例如 <c>"AzureTableStorage"</c>。
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// 获取提供程序类别，例如 <c>"Clustering"</c>、<c>"Reminders"</c>。
        /// </summary>
        public string Kind { get; } = kind;

        /// <summary>
        /// 获取用于配置提供程序的类型，例如 <c>"Server"</c>、<c>"Client"</c>。
        /// </summary>
        public string Target { get; } = target;

        /// <summary>
        /// 获取用于配置提供程序的类型。
        /// </summary>
        public Type Type { get; } = type;
    }

    /// <summary>
    /// 应用于类型时，指示该类型是序列化器，并应自动注册。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterSerializerAttribute : Attribute
    {
    }

    /// <summary>
    /// 应用于类型时，指示该类型是激活器，并应自动注册。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterActivatorAttribute : Attribute
    {
    }

    /// <summary>
    /// 应用于类型时，指示该类型是复制器，并应自动注册。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterCopierAttribute : Attribute
    {
    }

    /// <summary>
    /// 应用于类型时，指示该类型是转换器，并应自动注册。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterConverterAttribute : Attribute
    {
    }

    /// <summary>
    /// 应用于类型时，指示应使用已注册的激活器来激活此类型，而不是通过其构造函数或其他机制。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class UseActivatorAttribute : Attribute
    {
    }

    /// <summary>
    /// 应用于类型时，指示为该类型生成的序列化器不应跟踪此类型的引用。
    /// </summary>
    /// <remarks>
    /// 引用跟踪使可序列化类型能够参与具有循环引用的对象图。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SuppressReferenceTrackingAttribute : Attribute
    {
    }

    /// <summary>
    /// 应用于类型时，指示为该类型生成的序列化器应跳过值等于默认值的成员。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class OmitDefaultMemberValuesAttribute : Attribute
    {
    }

    /// <summary>
    /// 指示应将所标记的类型、类型成员、参数或返回值视为不可变，因此无需防御性复制。
    /// 应用于未密封的类时，不保证其派生类型不可变。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class ImmutableAttribute : Attribute
    {
    }

    /// <summary>
    /// 指示序列化时不考虑此特定类型。
    /// 仅适用于不含可序列化字段的抽象类型，实际上会将其从继承层次结构中移除。
    /// 为类型添加或移除此特性将造成序列化协议级别的不兼容（类似于变更类型层次结构）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SerializerTransparentAttribute : Attribute { }

    /// <summary>
    /// 指定应作为应用程序组成部分添加的程序集。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ApplicationPartAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="ApplicationPartAttribute" /> 的新实例。
        /// </summary>
        /// <param name="assemblyName">程序集名称。</param>
        public ApplicationPartAttribute(string assemblyName)
        {
            AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
        }

        /// <summary>
        /// 获取程序集名称。
        /// </summary>
        public string AssemblyName { get; }
    }

    /// <summary>
    /// 指定在对标记此特性的类型的实例执行序列化操作时应实例化并调用的类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class SerializationCallbacksAttribute : Attribute
    {
        /// <summary>
        /// 实例化一个新的 <see cref="SerializationCallbacksAttribute"/> 实例。
        /// </summary>
        /// <param name="hookType">用于调用序列化挂钩的对象的类型。</param>
        public SerializationCallbacksAttribute(Type hookType)
        {
            HookType = hookType;
        }

        /// <summary>
        /// 挂钩类的类型。
        /// </summary>
        /// <remarks>
        /// 此值用于从 <see cref="IServiceProvider"/> 获取挂钩实现。
        /// </remarks>
        public Type HookType { get; }
    }

    /// <summary>
    /// 应用于构造函数时，指示生成的激活器实现应在激活实例时使用该构造函数。
    /// </summary>
    /// <remarks>
    /// 此特性可用于调用需要注入依赖项的构造函数。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Constructor)]
    public sealed class GeneratedActivatorConstructorAttribute : ActivatorUtilitiesConstructorAttribute
    {
        /// <summary>
        /// 初始化 <see cref="GeneratedActivatorConstructorAttribute"/> 类的新实例。
        /// </summary>
        public GeneratedActivatorConstructorAttribute()
        {
        }
    }

    /// <summary>
    /// 指示源生成器还应检查包含指定类型的程序集，并为其生成代码。
    /// </summary>
    /// <remarks>
    /// 可序列化类型及其成员的类型必须对生成的代码可见。
    /// 内部类型可以使用 <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> 授予访问权限。
    /// </remarks>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GenerateCodeForDeclaringAssemblyAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="GenerateCodeForDeclaringAssemblyAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="type">程序集中任意类型；源生成器会检查该程序集并生成源代码。</param>
        public GenerateCodeForDeclaringAssemblyAttribute(Type type)
        {
            Type = type;
        }

        /// <summary>
        /// 获取源生成器应检查并为其生成源代码的程序集中的一个类型。
        /// </summary>
        /// <value>类型。</value>
        public Type Type { get; }
    }

    /// <summary>
    /// 指定标记此特性的接口方法的响应超时时间。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ResponseTimeoutAttribute : Attribute
    {
        /// <summary>
        /// 指定标记此特性的接口方法的响应超时时间。
        /// </summary>
        /// <param name="timeout">响应超时时间，采用 <see cref="TimeSpan.Parse(string)"/> 语法。</param>
        [SuppressMessage("Design", "CA1019:Define accessors for attribute arguments",
            Justification = "Attribute-compatible text is parsed and exposed through the typed TimeSpan Timeout property.")]
        public ResponseTimeoutAttribute(string timeout) => Timeout = TimeSpan.Parse(timeout);

        /// <summary>
        /// 获取或设置此方法的响应超时时间。
        /// </summary>
        public TimeSpan? Timeout { get; init; }
    }

    /// <summary>
    /// 提供在两种类型之间进行转换的功能。
    /// </summary>
    public interface IConverter<TValue, TSurrogate> where TSurrogate : struct
    {
        /// <summary>
        /// 将代理值转换为原始类型的值。
        /// </summary>
        /// <param name="surrogate">代理对象。</param>
        /// <returns>值。</returns>
        TValue ConvertFromSurrogate(in TSurrogate surrogate);

        /// <summary>
        /// 将值转换为值类型。
        /// </summary>
        /// <param name="value">值。</param>
        /// <returns>代理对象。</returns>
        TSurrogate ConvertToSurrogate(in TValue value);
    }

    /// <summary>
    /// 提供使用一种类型填充另一种类型的功能。
    /// </summary>
    public interface IPopulator<TValue, TSurrogate> where TSurrogate : struct where TValue : class
    {
        /// <summary>
        /// 填充 <paramref name="value"/>，数据取自 <paramref name="surrogate"/>。
        /// </summary>
        /// <param name="surrogate">代理对象。</param>
        /// <param name="value">值。</param>
        void Populate(in TSurrogate surrogate, TValue value);
    }
}

namespace Orleans.Invocation
{
    /// <summary>
    /// 应用于可调用对象的基类型（参见 TaskRequest），指示生成的代理方法应直接返回派生类型的实例，而非将实例传递给
    /// 运行时来发起调用。用于支持请求-响应之外的调用模式，例如流式处理。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ReturnValueProxyAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="ReturnValueProxyAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="initializerMethodName">用于初始化生成的请求并返回其代理值的方法名称。</param>
        public ReturnValueProxyAttribute(string initializerMethodName)
        {
            InitializerMethodName = initializerMethodName;
        }

        /// <summary>
        /// 获取用于初始化生成的请求并返回其代理值的方法名称。
        /// </summary>
        public string InitializerMethodName { get; }
    }
}
