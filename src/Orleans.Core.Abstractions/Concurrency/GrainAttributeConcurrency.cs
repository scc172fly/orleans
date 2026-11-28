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
    /// ReadOnly 特性用于标记不修改 Grain 状态的方法。
    /// <para>
    /// 将方法标记为 ReadOnly 可使运行时执行多项优化，
    /// 从而可能显著改善应用程序的性能。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 将此特性应用于 Grain 接口的方法声明。只读请求可与其他
    /// 只读请求交错执行。可写请求须等待所有正在处理的只读请求完成，除非其他
    /// 交错执行策略允许它们交错执行。
    /// </remarks>
    [InvokableCustomInitializer(nameof(IRequest.AddInvokeMethodOptions), InvokeMethodOptions.ReadOnly)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ReadOnlyAttribute : Attribute
    {
    }

    /// <summary>
    /// Reentrant 特性用于标记允许在任务执行期间交错处理请求的 Grain 实现类。
    /// <para>
    /// 这是一项高级功能，必须充分了解其影响后再使用。
    /// 不过，允许请求交错执行可使运行时执行多项优化，
    /// 从而可能显著改善应用程序的性能。
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
    /// Unordered 特性用于标记这样的 Grain 接口：
    /// 消息的投递顺序并不重要。
    /// </summary>
    /// <remarks>
    /// 此特性不起作用，将来可能会被移除。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Interface)]
    [Obsolete("Message ordering is not guaranteed regardless of whether this attribute is used. This attribute has no effect.")]
    public sealed class UnorderedAttribute : Attribute
    {
    }

    /// <summary>
    /// StatelessWorker 特性用于标记这样的 Grain 类：不要求在请求之间保留 Grain 状态，
    /// 并允许运行时为同一 Grain 创建多个激活实例。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StatelessWorkerAttribute : PlacementAttribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 初始化 <see cref="StatelessWorkerAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocalWorkers">本地工作者的最大数量。</param>
        public StatelessWorkerAttribute(int maxLocalWorkers)
            : base(new StatelessWorkerPlacement(maxLocalWorkers))
        {
        }

        /// <summary>
        /// 初始化 <see cref="StatelessWorkerAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="maxLocalWorkers">本地工作者的最大数量。</param>
        /// <param name="removeIdleWorkers">指示是否移除空闲工作者激活实例的值。</param>
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
    /// AlwaysInterleaveAttribute 特性用于标记可与任意方法交错执行的方法，包括写入（非 ReadOnly）请求。
    /// </summary>
    /// <remarks>
    /// 请注意，应将此特性应用于 Grain 接口中的方法声明，
    /// 而不是实现类本身的方法。
    /// </remarks>
    [InvokableCustomInitializer(nameof(IRequest.AddInvokeMethodOptions), InvokeMethodOptions.AlwaysInterleave)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AlwaysInterleaveAttribute : Attribute
    {
    }

    /// <summary>
    /// MayInterleaveAttribute 特性用于标记希望通过
    /// 给定的方法回调来控制请求交错执行的类。
    /// </summary>
    /// <remarks>
    /// 回调方法名称应指向同一类上声明的公共静态方法，
    /// 其签名如下：<c>public static bool MayInterleave(IInvokable req)</c>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MayInterleaveAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 初始化 <see cref="MayInterleaveAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="callbackMethodName">
        /// 回调方法的名称。它应解析为具有以下
        /// 签名的方法：<c>public static bool NameOfMethod(IInvokable req)</c>
        /// </param>
        public MayInterleaveAttribute(string callbackMethodName)
        {
            this.CallbackMethodName = callbackMethodName;
        }

        /// <summary>
        /// 获取回调方法的名称。
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
    /// 指示 Grain 接口上的某个方法为单向方法，不会向调用方发送响应消息。
    /// </summary>
    [InvokableCustomInitializer(nameof(IRequest.AddInvokeMethodOptions), InvokeMethodOptions.OneWay)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OneWayAttribute : Attribute
    {
    }
}
