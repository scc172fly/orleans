using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 指定在 grain 可以用于收集和禁用之前的不活动期。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class CollectionAgeLimitAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        private TimeSpan? _value;

        /// <summary>
        /// 指定在 grain 可以用于收集和禁用之前的不活动期。
        /// </summary>
        /// <remarks>
        /// 使用 <see cref="Minutes"/>、 <see cref="Days"/>和 <see cref="Hours"/> 属性或 <see cref="AlwaysActive"/> 设定限制。
        /// </remarks>
        public CollectionAgeLimitAttribute() { }

        /// <summary>
        /// 指定在 grain 可以用于收集和禁用之前的不活动期。
        /// </summary>
        /// <param name="inactivityPeriod">之前的无活动期 grain 可用于收集和禁用,以字符串表示 <see cref="TimeSpan.Parse(string)"/> 语法。</param>
        [SuppressMessage("Design", "CA1019:Define accessors for attribute arguments",
            Justification = "The parsed inactivity period is exposed through the typed TimeSpan AgeLimit property.")]
        public CollectionAgeLimitAttribute(string inactivityPeriod) => _value = TimeSpan.Parse(inactivityPeriod);

        /// <summary>
        /// 达到最低激活年龄。
        /// </summary>
        public static readonly TimeSpan MinAgeLimit = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置延迟收集闲置激活的天数。
        /// </summary>
        public double Days { get; set; }

        /// <summary>
        /// 获取或设定延迟收集闲置激活的小时数 。
        /// </summary>
        public double Hours { get; set; }

        /// <summary>
        /// 获取或设置延迟收集闲置激活的分钟数。
        /// </summary>
        public double Minutes { get; set; }

        /// <summary>
        /// 获取或设置一个值,以显示闲置激活收藏器是否永远不应该收集此 grain 。
        /// </summary>
        public bool AlwaysActive { get; set; }

        /// <summary>
        /// 获得闲置的激活收集年龄。
        /// </summary>
        public TimeSpan AgeLimit => _value ??= CalculateValue();

        /// <inheritdoc />
        public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            ArgumentNullException.ThrowIfNull(properties);

            string idleDeactivationPeriod;

            if (AlwaysActive)
            {
                idleDeactivationPeriod = WellKnownGrainTypeProperties.IndefiniteIdleDeactivationPeriodValue;
            }
            else
            {
                idleDeactivationPeriod = AgeLimit.ToString("c");
            }

            properties[WellKnownGrainTypeProperties.IdleDeactivationPeriod] = idleDeactivationPeriod;
        }

        private TimeSpan CalculateValue()
        {
            var span = AlwaysActive
            ? TimeSpan.FromDays(short.MaxValue)
            : TimeSpan.FromDays(Days) + TimeSpan.FromHours(Hours) + TimeSpan.FromMinutes(Minutes);
            return span < MinAgeLimit
                ? MinAgeLimit
                : span;
        }
    }

    /// <summary>
    /// 当应用到 grain 执行类型时, 此属性指定了闲置激活收藏器不应收集 grain 的启动 。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class KeepAliveAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <inheritdoc />
        public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            ArgumentNullException.ThrowIfNull(properties);
            properties[WellKnownGrainTypeProperties.IdleDeactivationPeriod] = WellKnownGrainTypeProperties.IndefiniteIdleDeactivationPeriodValue;
        }
    }
}
