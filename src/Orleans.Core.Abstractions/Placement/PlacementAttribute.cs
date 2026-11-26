using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;
using Orleans.Runtime;
using static Orleans.Placement.ImmovableAttribute;

namespace Orleans.Placement
{
    /// <summary>
    /// 用于所有职位安排政策标记属性的基础。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public abstract class PlacementAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 获取与此属性相关的定位策略。
        /// </summary>
        public PlacementStrategy PlacementStrategy { get; private set; }

        /// <summary>
        /// 初始化 <see cref="PlacementAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="placement">与此属性相关的职位安排策略。</param>
        protected PlacementAttribute(PlacementStrategy placement)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));

            this.PlacementStrategy = placement;
        }

        /// <inheritdoc />
        public virtual void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            this.PlacementStrategy?.PopulateGrainProperties(services, grainClass, grainType, properties);
        }
    }

    /// <summary>
    /// 标记a grain 类作为使用 <see cref="RandomPlacement"/> 政策。
    /// </summary>
    /// <remarks>
    /// 这是默认安放策略, 所以此属性不需要用于正常 grains。
    /// </remarks>
    /// <inheritdoc cref="RandomPlacement"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RandomPlacementAttribute : PlacementAttribute
    {
        /// <summary>
        /// 初始化 <see cref="RandomPlacementAttribute"/> 类的新实例。
        /// </summary>
        public RandomPlacementAttribute() :
            base(RandomPlacement.Singleton)
        {
        }
    }

    /// <summary>
    /// 标记a grain 类作为使用 <see cref="HashBasedPlacement"/> 政策。
    /// </summary>
    /// <inheritdoc cref="HashBasedPlacement"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class HashBasedPlacementAttribute : PlacementAttribute
    {
        /// <summary>
        /// 初始化 <see cref="HashBasedPlacementAttribute"/> 类的新实例。
        /// </summary>
        public HashBasedPlacementAttribute() :
            base(HashBasedPlacement.Singleton)
        { }
    }

    /// <summary>
    /// 标记a grain 类作为使用 <see cref="PreferLocalPlacement"/> 政策。
    /// </summary>
    /// <inheritdoc cref="PreferLocalPlacement"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class PreferLocalPlacementAttribute : PlacementAttribute
    {
        /// <summary>
        /// 初始化 <see cref="PreferLocalPlacementAttribute"/> 类的新实例。
        /// </summary>
        public PreferLocalPlacementAttribute()
            : base(PreferLocalPlacement.Singleton)
        {
        }
    }

    /// <summary>
    /// 标记一个 grain 类为使用试图平衡的 <see cref="ActivationCountBasedPlacement"/> 策略
    /// grain 跨服务器的放置,根据每个服务器最近运行的 grains 相对数量。
    /// </summary>
    /// <inheritdoc cref="ActivationCountBasedPlacement"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ActivationCountBasedPlacementAttribute : PlacementAttribute
    {
        /// <summary>
        /// 初始化 <see cref="ActivationCountBasedPlacementAttribute"/> 类的新实例。
        /// </summary>
        public ActivationCountBasedPlacementAttribute()
            : base(ActivationCountBasedPlacement.Singleton)
        {
        }
    }

    /// <summary>
    /// 标记a grain 类作为使用 <see cref="SiloRoleBasedPlacement"/> 政策。
    /// </summary>
    /// <inheritdoc cref="SiloRoleBasedPlacement"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SiloRoleBasedPlacementAttribute : PlacementAttribute
    {
        /// <summary>
        /// 初始化 <see cref="SiloRoleBasedPlacementAttribute"/> 类的新实例。
        /// </summary>
        public SiloRoleBasedPlacementAttribute() :
            base(SiloRoleBasedPlacement.Singleton)
        { }
    }

    /// <summary>
    /// 标记a grain 类作为使用 <see cref="ResourceOptimizedPlacement"/> 政策。
    /// </summary>
    /// <inheritdoc cref="ResourceOptimizedPlacement"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ResourceOptimizedPlacementAttribute : PlacementAttribute
    {
        /// <summary>
        /// 初始化 <see cref="ResourceOptimizedPlacementAttribute"/> 类的新实例。
        /// </summary>
        public ResourceOptimizedPlacementAttribute() :
            base(ResourceOptimizedPlacement.Singleton)
        { }
    }

    /// <summary>
    /// 确保不自动迁移该 grain 型号的激活。
    /// </summary>
    /// <remarks>活动仍可以通过用户启动的代码迁移。</remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ImmovableAttribute(ImmovableKind kind = ImmovableKind.Any)
        : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 那种无法移动的
        /// </summary>
        public ImmovableKind Kind { get; } = kind;

        /// <inheritdoc/>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "AttributeGrainPropertiesProvider validates the properties collection before dispatching to attributes.")]
        public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
            => properties[WellKnownGrainTypeProperties.Immovable] = ((byte)Kind).ToString();
    }

    /// <summary>
    /// 强调不得迁移仅限于某些组成部分。
    /// </summary>
    [Flags]
    public enum ImmovableKind : byte
    {
        /// <summary>
        /// 此 grain 类型的激活不会被重新分配者迁移。
        /// </summary>
        Repartitioner = 1,
        /// <summary>
        /// 此 grain 类型的激活不会由再平衡器迁移。
        /// </summary>
        Rebalancer = 2,
        /// <summary>
        /// 此类型 grain 的激活不会被任何物体移动。
        /// </summary>
        Any = Repartitioner | Rebalancer
    }
}
