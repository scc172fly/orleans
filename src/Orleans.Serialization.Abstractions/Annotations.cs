using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans
{
    /// <summary>
    /// 当适用于某一类型时,规定该类型打算按序列顺序排列,并且应为该类型生成序列编码。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
    public sealed class GenerateSerializerAttribute : Attribute
    {
        /// <summary>
        /// 如果主建构参数应自动作为可序列字段列入,则获取或设置。
        /// 默认默认为 <see langword="true"/> 用于 <see langword="record"/> 类型, <see langword="false"/> 否则。
        /// </summary>
        public bool IncludePrimaryConstructorParameters { get; init; }

        /// <summary>
        /// 当 Orleans 自动指定字段代号时, 获取或设置。 默认行为是不自动指定字段代号 。
        /// </summary>
        public GenerateFieldIds GenerateFieldIds { get; init; } = GenerateFieldIds.None;
    }

    /// <summary>
    /// 当应用到接口时, 指定应生成支持代码, 以便重新移动界面调用 。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
    public sealed class GenerateMethodSerializersAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="GenerateMethodSerializersAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="proxyBase">代理基类型。</param>
        /// <param name="isExtension">设定为 <see langword="true"/>,这是一个扩展界面。</param>
        public GenerateMethodSerializersAttribute(Type proxyBase, bool isExtension = false)
        {
            ProxyBase = proxyBase;
            IsExtension = isExtension;
        }

        /// <summary>
        /// 获取源生成器将用来生成代理类的基准类型。
        /// </summary>
        /// <value>代理基类型。</value>
        public Type ProxyBase { get; }

        /// <summary>
        /// 获得一个值, 显示该接口是否是一个扩展界面 。
        /// </summary>
        /// <value>如果此实例为扩展名, 则属真实; 否则, 假 。</value>
        public bool IsExtension { get; }
    }

    /// <summary>
    /// 用于在可引用界面上应用方法属性,以指定提交请求时要调用的基准类型上的方法名称。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InvokeMethodNameAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="InvokeMethodNameAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="invokeMethodName">援引方法的名称。</param>
        public InvokeMethodNameAttribute(string invokeMethodName)
        {
            InvokeMethodName = invokeMethodName;
        }

        /// <summary>
        /// 获取引用方法的名称。
        /// </summary>
        /// <value>援引方法的名称。</value>
        public string InvokeMethodName { get; }
    }

    /// <summary>
    /// 适用于代理基类型和用于说明可置换界面方法所用类型,以指定代表方法调用的可置换对象的基准类型。
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
        /// 获取此属性应用的界面方法返回类型。
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// 获取在使用方法时使用的代理基类型上的方法名称。
        /// </summary>
        public string MethodName { get; }
    }

    /// <summary>
    /// 用于应用界面方法属性类型来指定在引用该接口方法时创建的可援引对象需要调用的方法。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class InvokableCustomInitializerAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="InvokableCustomInitializerAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="methodName">方法名称。</param>
        /// <param name="methodArgumentValue">方法参数值。</param>
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
        /// 获取方法的名称 。
        /// </summary>
        public string MethodName { get; }

        /// <summary>
        /// 获取或设置要向自定义初始化器方法传播的属性参数参数索引。
        /// </summary>
        public int AttributeArgumentIndex { get; init; }

        /// <summary>
        /// 获取或设置要向自定义初始化方法传播的属性参数参数的名称。
        /// </summary>
        public int AttributeArgumentName { get; init; }

        /// <summary>
        /// 获取或设定要传递到自定义初始化方法的值。
        /// </summary>
        public object? MethodArgumentValue { get; }
    }

    /// <summary>
    /// 适用于代理基类型和用于说明可置换界面方法所用类型,以指定代表方法调用的可置换对象的基准类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class DefaultInvokableBaseTypeAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="DefaultInvokableBaseTypeAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="returnType">此属性适用于的返回方法类型。</param>
        /// <param name="invokableBaseType">易爆基数的类型。</param>
        public DefaultInvokableBaseTypeAttribute(Type returnType, Type invokableBaseType)
        {
            ReturnType = returnType;
            InvokableBaseType = invokableBaseType;
        }

        /// <summary>
        /// 获取此属性应用的返回方法类型。
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// 获取可引用对象类的基类型,该基类型将用来代表此属性应用的方法的方法的调用。
        /// </summary>
        public Type InvokableBaseType { get; }

        /// <summary>
        /// 获取或设置用于使用此方法的代理对象上的方法的名称。
        /// </summary>
        public string? ProxyInvokeMethodName { get; init; }
    }

    /// <summary>
    /// 用于返回类型、可援引界面方法使用的属性类型或用于指定
    /// 代表方法调用的可变不可变对象。 当返回类型和代理基础时,可以使用大会一级的登记
    /// 类型由独立的图书馆所有。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class InvokableBaseTypeAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="InvokableBaseTypeAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="proxyBaseClass">代理基级。</param>
        /// <param name="returnType">此属性适用于的返回方法类型。</param>
        /// <param name="invokableBaseType">易爆基数的类型。</param>
        public InvokableBaseTypeAttribute(Type proxyBaseClass, Type returnType, Type invokableBaseType)
        {
            ProxyBaseClass = proxyBaseClass;
            ReturnType = returnType;
            InvokableBaseType = invokableBaseType;
        }

        /// <summary>
        /// 获取此属性适用的代理服务器基类。
        /// </summary>
        public Type ProxyBaseClass { get; }

        /// <summary>
        /// 获取此属性应用的方法返回类型。
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// 获取生成可置换对象的基准类型。
        /// </summary>
        public Type InvokableBaseType { get; }

        /// <summary>
        /// 获取或设置在代用对象上使用该方法的方法的名称,以在调用此方法时引用。
        /// </summary>
        public string? ProxyInvokeMethodName { get; init; }
    }

    /// <summary>
    /// 用于在可引用界面上应用方法属性,指定调用的方法名称,以获得填充源,该源提交到提交方法,并最终返回到调用者。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GetCompletionSourceMethodNameAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="GetCompletionSourceMethodNameAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="methodName">用于获取提交运行时的请求的完成源的方法名称。</param>
        public GetCompletionSourceMethodNameAttribute(string methodName)
        {
            MethodName = methodName;
        }

        /// <summary>
        /// 获取用于获得提交运行时间的完成源的请求的填写源的方法名称。
        /// </summary>
        public string MethodName { get; }
    }

    /// <summary>
    /// 指定成员的独特身份。
    /// </summary>
    /// <remarks>
    /// 每一可连号成员在申请的 <see cref="GenerateSerializerAttribute"/> 类型下,必须有一个 <see cref="IdAttribute"/> 属性,该属性应具有独一 <see cref="IdAttribute.Id"/> 值。
    /// 对于位置记录,此属性可直接应用于主建构参数,而不是生成的属性。
    /// 在辐射常委会方法参数方面,明确的识别符号取代了按序列参数排列的参数零基正弦。
    /// 取消代号参数参数从序列参数顺序中排除,所有产生的识别符号必须具有独特性。
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
        /// <param name="id">识别符号。</param>
        public IdAttribute(uint id)
        {
            Id = id;
        }

        /// <summary>
        /// 获得识别资料。
        /// </summary>
        /// <value>识别符号。</value>
        public uint Id { get; }
    }

    /// <summary>
    /// 将一个案件识别符号指定为直接的子类型 <see langword="closed"/> 类型类型 <see cref="GenerateSerializerAttribute"/> 适用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 当封闭型的直接子类型有案件识别特征时,被宣布为封闭型的字段将按贴标签的工会顺序排列:
    /// 用于记录一个值的运行时间类型,因此没有编码
    /// 运行时间类型来自未密封案件类型的数值,按该情况编码,随后是
    /// 他们的运行时间类型。
    /// </para>
    /// <para>
    /// 大小写识别符的范围是针对封闭型号,必须是其中独有的。
    /// 案件识别特征是序列格式的一部分,一旦数据被序列化,它们必须保持稳定。
    /// 0至6的识别符编码没有额外的字节。
    /// </para>
    /// <para>
    /// 添加案件识别符号,将作者改变为贴标签工会格式。使用协调部署方式在现有的等级中采用
    /// 更新后等级结构的读者接受两种方法
    /// 运行时间类型格式和贴标签的工会格式。
    /// </para>
    /// <para>
    /// 封闭型和工会型在工会案件为关闭型直接子类型时,具有相同的连续代表,而该类是与此相同的封闭型直接子类型。
    /// 复选识别符号(见 <see cref="CaseIdsAttribute"/>)。一个 <see langword="null"/> 封闭型数值作为空的组合值表示。
    /// 因此,封闭型等级可以由等同型工会取代,而不改变序列数据。
    /// 每个案件自己的代表包括其基本类型成员,以便在用工会取代封闭基地时,将该基地从案件中删除;
    /// 宣布关闭基地,但不连号成员,并对其适用 <see cref="SerializerTransparentAttribute"/> 。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CaseAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="CaseAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="id">案件识别符号,这是封闭基底类型直接子型中独特的。</param>
        public CaseAttribute(uint id)
        {
            Id = id;
        }

        /// <summary>
        /// 获得案件识别资料。
        /// </summary>
        public uint Id { get; }
    }

    /// <summary>
    /// 给适用 <see cref="GenerateSerializerAttribute"/> 的工会类型的情况指定案例识别符。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 工会价值以贴有标签的工会编号:案件识别符号编码在持有案件价值的实地页头。
    /// 默认情况下,每个案例都以其在工会宣言中的零基立场来识别。
    /// 此属性指定每个案件一个识别特征, 以声明顺序排列, 这样可以重新排序或删除案件而无需更改
    /// 其余案件的识别资料。
    /// </para>
    /// <para>
    /// 标识必须独有。 0至6 的标识无需额外的字节就编码了。
    /// </para>
    /// <para>
    /// 工会类型,其案件是封闭型直接子类型,具有相同案例识别特征的直属亚型,具有相同的案例识别特征,具有同样的序列式代表
    /// 即封闭类型(见 <see cref="CaseAttribute"/>))空工会值作为 <see langword="null"/> 封闭型价值。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false)]
    public sealed class CaseIdsAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="CaseIdsAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="ids">案件识别资料,每个案件按申报顺序列出。</param>
        public CaseIdsAttribute(params uint[] ids)
        {
            Ids = ids;
        }

        /// <summary>
        /// 获得案件识别资料,每个案件按申报顺序一份。
        /// </summary>
        public uint[] Ids { get; }
    }

    /// <summary>
    /// 指定从序列数据创建新实例时应使用的序列器的构建符。
    /// </summary>
    /// <remarks>
    /// 最多可以用此属性附加一个构建符。 如果多构建符附加说明, 则忽略该属性的存在 。
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
    /// 当适用于某类型或方法时,指定一个众所周知的名称,用以识别该类型或方法。
    /// </summary>
    /// <remarks>
    /// 在一种情况下,别名必须是全球独特的,在一种方法的情况下,别名必须是申报类型所独有的。
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
        /// 在一种情况下,别名必须是全球独特的,在一种方法的情况下,别名必须是申报类型所独有的。
        /// 异名必须是非空的,不包含引号、空白空间、控制字符或保留型号名称分隔符(<c>[](),*&amp;</c>)。
        /// 通用数后缀必须是每个嵌套类型名称结尾的十进制整数,总计最多64。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="alias"/> 是 <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="alias"/> 是空的, 含有引号、 空白、 控制字符或保留类型名称分隔符, 或者无效的通用性等字尾。</exception>
        public AliasAttribute(string alias)
        {
            Serialization.IdentityNameValidator.Validate(alias, nameof(alias));
            Alias = alias;
        }

        /// <summary>
        /// 取化名来
        /// </summary>
        /// <remarks>
        /// 在一种情况下,别名必须是全球独特的,在一种方法的情况下,别名必须是申报类型所独有的。
        /// </remarks>
        public string Alias { get; }
    }

    /// <summary>
    /// 当适用于类型时,表示该类型应作为特定类型的关系编码。
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
        /// <param name="components">假名组成部分,每个必须包括 <see cref="Components"/> 或 a/ <see cref="string"/>.</param>
        public CompoundTypeAliasAttribute(params object[] components)
        {
            Components = components;
        }

        /// <summary>
        /// 获取化名组件。
        /// </summary>
        public object[] Components { get; }
    }

    /// <summary>
    /// 在适用于某一类型时,应表明该类型是提供者,并应自动登记。
    /// </summary>
    /// <param name="name">例如,提供商名称 <c>"AzureTableStorage"</c>.</param>
    /// <param name="kind">例如,提供者的类型, <c>"Clustering"</c>, <c>"Reminders"</c>.</param>
    /// <param name="target">例如,供应商的预期目标 <c>"Server"</c>, <c>"Client"</c>.</param>
    /// <param name="type">用于配置提供者的类型。</param>
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
        /// 获取服务提供人的名称,例如 <c>"AzureTableStorage"</c>。
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// 获取提供者的类型,例如 <c>"Clustering"</c>、 <c>"Reminders"</c>。
        /// </summary>
        public string Kind { get; } = kind;

        /// <summary>
        /// 获取配置提供者所使用的类型,例如 <c>"Server"</c>、 <c>"Client"</c>。
        /// </summary>
        public string Target { get; } = target;

        /// <summary>
        /// 获取配置提供者所使用的类型。
        /// </summary>
        public Type Type { get; } = type;
    }

    /// <summary>
    /// 当应用到类型时,请注明该类型是序列器,并应自动登记。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterSerializerAttribute : Attribute
    {
    }

    /// <summary>
    /// 当应用到类型时,请注明该类型是一个激活器,并且应当自动登记。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterActivatorAttribute : Attribute
    {
    }

    /// <summary>
    /// 适用于类型时,应表明该类型是复印机,并应自动登记。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterCopierAttribute : Attribute
    {
    }

    /// <summary>
    /// 当应用到类型时,请注明该类型是转换器,并应自动登记。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class RegisterConverterAttribute : Attribute
    {
    }

    /// <summary>
    /// 在适用于某类型时,应表明该类型应当使用已登记的活性器而不是通过其制造者或其他机制启动。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class UseActivatorAttribute : Attribute
    {
    }

    /// <summary>
    /// 适用于类型时,请表明该类型的生成序列器不应跟踪该类型的引用。
    /// </summary>
    /// <remarks>
    /// 参考跟踪使可连号类型能够参与环形物体图。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SuppressReferenceTrackingAttribute : Attribute
    {
    }

    /// <summary>
    /// 当应用于类型时,请指出该类型的生成序列器应避免成员序列化,如果成员值等于其默认值。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class OmitDefaultMemberValuesAttribute : Attribute
    {
    }

    /// <summary>
    /// 表示应用该数值的类型、类型成员、参数或返回值应作为不可更改的处理,因此从不需要防伪副本。
    /// 适用于非密封类别时,不能保证衍生的种类是不可改变的。
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class ImmutableAttribute : Attribute
    {
    }

    /// <summary>
    /// 表示为序列目的,具体类型是隐形的。
    /// 只能用于没有连续田地的抽象类型,并有效地将其从继承等级体系中消除。
    /// 从类型中添加/删除此属性将导致序列化协议级别不兼容(类似类型的等级变化)。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SerializerTransparentAttribute : Attribute { }

    /// <summary>
    /// 指定要添加的组装作为应用程序部分。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ApplicationPartAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="ApplicationPartAttribute" />新实例。
        /// </summary>
        /// <param name="assemblyName">组装名称。</param>
        public ApplicationPartAttribute(string assemblyName)
        {
            AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
        }

        /// <summary>
        /// 获得组装名称。
        /// </summary>
        public string AssemblyName { get; }
    }

    /// <summary>
    /// 指定一个类型,在进行序列操作时,该属性所附类型的连续作业时要即时引用和援引。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class SerializationCallbacksAttribute : Attribute
    {
        /// <summary>
        /// 证明新的 <see cref="SerializationCallbacksAttribute"/> 实例。
        /// </summary>
        /// <param name="hookType">用于援引序列钩的物体类型。</param>
        public SerializationCallbacksAttribute(Type hookType)
        {
            HookType = hookType;
        }

        /// <summary>
        /// 钩类的类型。
        /// </summary>
        /// <remarks>
        /// 此值用于从 <see cref="IServiceProvider"/>获取钩子执行 。
        /// </remarks>
        public Type HookType { get; }
    }

    /// <summary>
    /// 当应用到一个构造器时, 表示生成的活性装置在激活事件时应使用该构建器。
    /// </summary>
    /// <remarks>
    /// 这一属性可用于呼叫需要注射依赖性的建筑工。
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
    /// 表示源生成器还应检查并生成装配编码,其中应包含指定类型的组件。
    /// </summary>
    /// <remarks>
    /// 生成代码必须能够进入可分层使用的类型及其成员类型。
    /// 内部类型可以使用 <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>提供访问。
    /// </remarks>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GenerateCodeForDeclaringAssemblyAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="GenerateCodeForDeclaringAssemblyAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="type">源发电机应检查和产生源的组装中的任何类型的来源。</param>
        public GenerateCodeForDeclaringAssemblyAttribute(Type type)
        {
            Type = type;
        }

        /// <summary>
        /// 获取组装中的一种类型,源生成器应检查该类型并产生源。
        /// </summary>
        /// <value>类型。</value>
        public Type Type { get; }
    }

    /// <summary>
    /// 指定用于指定的界面方法的响应超时。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ResponseTimeoutAttribute : Attribute
    {
        /// <summary>
        /// 指定用于指定的界面方法的响应超时。
        /// </summary>
        /// <param name="timeout">响应超时,使用 <see cref="TimeSpan.Parse(string)"/> 语法。</param>
        [SuppressMessage("Design", "CA1019:Define accessors for attribute arguments",
            Justification = "Attribute-compatible text is parsed and exposed through the typed TimeSpan Timeout property.")]
        public ResponseTimeoutAttribute(string timeout) => Timeout = TimeSpan.Parse(timeout);

        /// <summary>
        /// 获取或设置此方法的反应超时 。
        /// </summary>
        public TimeSpan? Timeout { get; init; }
    }

    /// <summary>
    /// 两种类型之间转换的功能。
    /// </summary>
    public interface IConverter<TValue, TSurrogate> where TSurrogate : struct
    {
        /// <summary>
        /// 将替代值转换为值类型。
        /// </summary>
        /// <param name="surrogate">代孕人。</param>
        /// <returns>数值。</returns>
        TValue ConvertFromSurrogate(in TSurrogate surrogate);

        /// <summary>
        /// 将值转换为值类型。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>代孕人。</returns>
        TSurrogate ConvertToSurrogate(in TValue value);
    }

    /// <summary>
    /// 一种类型的活动从另一种类型中传播的功能。
    /// </summary>
    public interface IPopulator<TValue, TSurrogate> where TSurrogate : struct where TValue : class
    {
        /// <summary>
        /// 弹出 <paramref name="value"/> 含有数值的 <paramref name="surrogate"/>.
        /// </summary>
        /// <param name="surrogate">代孕人。</param>
        /// <param name="value">数值。</param>
        void Populate(in TSurrogate surrogate, TValue value);
    }
}

namespace Orleans.Invocation
{
    /// <summary>
    /// 适用于可援引基类型(见 TaskRequest),以表明衍生类型的实例应当从产生的替代方法直接返回而不是传递给
    /// 调用时间。 它用于支持请求回复以外的调用模式, 如流动 。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ReturnValueProxyAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="ReturnValueProxyAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="initializerMethodName">初始化生成请求并返回其代理值所用方法的名称。</param>
        public ReturnValueProxyAttribute(string initializerMethodName)
        {
            InitializerMethodName = initializerMethodName;
        }

        /// <summary>
        /// 获取初始化生成请求并返回其代理值的方法的名称。
        /// </summary>
        public string InitializerMethodName { get; }
    }
}
