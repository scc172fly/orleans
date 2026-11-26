using Orleans.CodeGeneration;
using Orleans.Metadata;
using Orleans.Placement;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Orleans.Concurrency
{
    /// <summary>
    /// ReadOnly 属性用于标记不修改 grain状态的方法。
    /// <para>
    /// 标记方法 ReadOnly 使运行时系统能够进行若干优化
    /// 申请后,您可大大改善申请的绩效。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 将此属性应用到 grain 接口方法声明。只读请求可与其他
    /// 仅读只读请求。可写的请求等待所有当前只读只读请求完成后再提出,除非其他
    /// 相互交错的政策允许它们重叠。
    /// </remarks>
    [InvokableCustomInitializer(nameof(IRequest.AddInvokeMethodOptions), InvokeMethodOptions.ReadOnly)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ReadOnlyAttribute : Attribute
    {
    }

    /// <summary>
    /// 重新输入属性用于标记 grain 执行类别,允许在任务内进行请求中断。
    /// <para>
    /// 这是一个先进的特征,除非充分了解其影响,否则不应使用。
    /// 尽管如此,允许请求相互互换使运行时系统能够进行一些优化
    /// 申请后,您可大大改善申请的绩效。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ReentrantAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <inheritdoc/>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "AttributeGrainPropertiesProvider validates the properties collection before dispatching to attributes.")]
        public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainTypeProperties.Reentrant] = "true";
        }
    }

    /// <summary>
    /// 无顺序的属性用于标记 grain 接口,其中交付顺序
    /// 信件不重要 。
    /// </summary>
    /// <remarks>
    /// 这一属性没有效力,将来可予以删除。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Interface)]
    [Obsolete("Message ordering is not guaranteed regardless of whether this attribute is used. This attribute has no effect.")]
    public sealed class UnorderedAttribute : Attribute
    {
    }

    /// <summary>
    /// StatelessWorker 属性用于标记无预期的 grain 类
    /// 保留 grain 状态的请求与允许在运行时多次启动同一 grain 的状态之间。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StatelessWorkerAttribute : PlacementAttribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 初始化 <see cref="StatelessWorkerAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocalWorkers">当地工人最多。</param>
        public StatelessWorkerAttribute(int maxLocalWorkers)
            : base(new StatelessWorkerPlacement(maxLocalWorkers))
        {
        }

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocalWorkers">当地工人最多。</param>
        /// <param name="removeIdleWorkers">表示是否删除闲置工人激活的值。</param>
        public StatelessWorkerAttribute(int maxLocalWorkers, bool removeIdleWorkers)
            : base(new StatelessWorkerPlacement(maxLocalWorkers, removeIdleWorkers))
        {
        }

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerAttribute"/> 类的新实例。
        /// </summary>
        public StatelessWorkerAttribute()
            : base(new StatelessWorkerPlacement())
        {
        }

        /// <inheritdoc/>
        public override void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            base.Populate(services, grainClass, grainType, properties);
            properties[WellKnownGrainTypeProperties.Unordered] = "true";
        }
    }

    /// <summary>
    /// AlwaysInterleaveAttribute 属性用于标示可以与任何方法交叉使用的方法,包括写(非 ReadOnly)请求。
    /// </summary>
    /// <remarks>
    /// 请注意,此属性适用于 grain 接口的方法声明,
    /// 而不是执行舱本身的方法。
    /// </remarks>
    [InvokableCustomInitializer(nameof(IRequest.AddInvokeMethodOptions), InvokeMethodOptions.AlwaysInterleave)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AlwaysInterleaveAttribute : Attribute
    {
    }

    /// <summary>
    /// MayInterleaveAttribute 属性用于标记类
    /// 想要控制请求通过提供的方法回调中断的连接。
    /// </summary>
    /// <remarks>
    /// 回回调方法名称应指在同一类上宣布的公共静态函数
    /// 具有以下签名: <c>public static bool MayInterleave(IInvokable req)</c>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MayInterleaveAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 初始化 <see cref="MayInterleaveAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="callbackMethodName">
        /// 回调方法名称。 这应该用 
        /// 以下签字: <c>public static bool NameOfMethod(IInvokable req)</c>
        /// </param>
        public MayInterleaveAttribute(string callbackMethodName)
        {
            this.CallbackMethodName = callbackMethodName;
        }

        /// <summary>
        /// 获取回调方法的名称
        /// </summary>
        internal string CallbackMethodName { get; private set; }


        /// <inheritdoc/>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "AttributeGrainPropertiesProvider validates the properties collection before dispatching to attributes.")]
        public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainTypeProperties.MayInterleavePredicate] = this.CallbackMethodName;
        }
    }

    /// <summary>
    /// 表示 grain 接口上的一种方法是单向的,不会将任何回复信息发送到呼叫者。
    /// </summary>
    [InvokableCustomInitializer(nameof(IRequest.AddInvokeMethodOptions), InvokeMethodOptions.OneWay)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OneWayAttribute : Attribute
    {
    }
}
