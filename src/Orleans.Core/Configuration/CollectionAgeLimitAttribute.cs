using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 指定 Grain 可以被回收和停用之前所需的空闲时间。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class CollectionAgeLimitAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        private TimeSpan? _value;

        /// <summary>
        /// 指定 Grain 可以被回收和停用之前所需的空闲时间。
        /// </summary>
        /// <remarks>
        /// 使用 <see cref="Minutes"/>、<see cref="Days"/> 和 <see cref="Hours"/> 属性或 <see cref="AlwaysActive"/> 设置此限制。
        /// </remarks>
        public CollectionAgeLimitAttribute() { }

        /// <summary>
        /// 指定 Grain 可以被回收和停用之前所需的空闲时间。
        /// </summary>
        /// <param name="inactivityPeriod">Grain 可以被回收和停用之前所需的空闲时间，使用符合 <see cref="TimeSpan.Parse(string)"/> 语法的字符串表示。</param>
        [SuppressMessage("Design", "CA1019:Define accessors for attribute arguments",
            Justification = "The parsed inactivity period is exposed through the typed TimeSpan AgeLimit property.")]
        public CollectionAgeLimitAttribute(string inactivityPeriod) => _value = TimeSpan.Parse(inactivityPeriod);

        /// <summary>
        /// 获取激活实例回收时限的最小值。
        /// </summary>
        public static readonly TimeSpan MinAgeLimit = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置延迟回收空闲激活实例的天数。
        /// </summary>
        public double Days { get; set; }

        /// <summary>
        /// 获取或设置延迟回收空闲激活实例的小时数。
        /// </summary>
        public double Hours { get; set; }

        /// <summary>
        /// 获取或设置延迟回收空闲激活实例的分钟数。
        /// </summary>
        public double Minutes { get; set; }

        /// <summary>
        /// 获取或设置指示此 Grain 是否永远不应被空闲激活实例回收器回收的值。
        /// </summary>
        public bool AlwaysActive { get; set; }

        /// <summary>
        /// 获取空闲激活实例的回收时限。
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
    /// 将此特性应用于 Grain 实现类型时，表示该 Grain 的激活实例不应被空闲激活实例回收器回收。
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
