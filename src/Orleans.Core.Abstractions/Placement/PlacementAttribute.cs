using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;
using Orleans.Runtime;
using static Orleans.Placement.ImmovableAttribute;

namespace Orleans.Placement
{
    /// <summary>
    /// 所有放置策略标记特性的基类。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public abstract class PlacementAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 获取与此特性关联的放置策略。
        /// </summary>
        public PlacementStrategy PlacementStrategy { get; private set; }

        /// <summary>
        /// 初始化 <see cref="PlacementAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="placement">与此特性关联的放置策略。</param>
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
    /// 标记 Grain 类使用 <see cref="RandomPlacement"/> 策略。
    /// </summary>
    /// <remarks>
    /// 这是默认放置策略，因此普通 Grain 无需使用此特性。
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
    /// 标记 Grain 类使用 <see cref="HashBasedPlacement"/> 策略。
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
    /// 标记 Grain 类使用 <see cref="PreferLocalPlacement"/> 策略。
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
    /// 标记 Grain 类使用 <see cref="ActivationCountBasedPlacement"/> 策略，该策略尝试根据
    /// 各服务器上近期活跃 Grain 的相对数量来均衡 Grain 放置。
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
    /// 标记 Grain 类使用 <see cref="SiloRoleBasedPlacement"/> 策略。
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
    /// 标记 Grain 类使用 <see cref="ResourceOptimizedPlacement"/> 策略。
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
    /// 确保此 Grain 类型的激活实例不会被自动迁移。
    /// </summary>
    /// <remarks>用户主动发起的代码仍可迁移激活实例。</remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ImmovableAttribute(ImmovableKind kind = ImmovableKind.Any)
        : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 不可移动性的类型。
        /// </summary>
        public ImmovableKind Kind { get; } = kind;

        /// <inheritdoc/>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "AttributeGrainPropertiesProvider validates the properties collection before dispatching to attributes.")]
        public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
            => properties[WellKnownGrainTypeProperties.Immovable] = ((byte)Kind).ToString();
    }

    /// <summary>
    /// 强调不可移动性仅限于某些组件。
    /// </summary>
    [Flags]
    public enum ImmovableKind : byte
    {
        /// <summary>
        /// 此 Grain 类型的激活实例不会被重新分区器迁移。
        /// </summary>
        Repartitioner = 1,
        /// <summary>
        /// 此 Grain 类型的激活实例不会被再均衡器迁移。
        /// </summary>
        Rebalancer = 2,
        /// <summary>
        /// 此 Grain 类型的激活实例不会被任何机制迁移。
        /// </summary>
        Any = Repartitioner | Rebalancer
    }
}
