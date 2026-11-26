using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.GrainDirectory
{
    /// <summary>
    /// 指定 grain 要用于 grain 此属性应用到的类 。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GrainDirectoryAttribute : Attribute, IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 默认 grain 目录。
        /// </summary>
        public const string DEFAULT_GRAIN_DIRECTORY = "default";

        /// <summary>
        /// 初始化 <see cref="GrainDirectoryAttribute"/> 类的新实例。
        /// </summary>
        public GrainDirectoryAttribute() : this(DEFAULT_GRAIN_DIRECTORY)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainDirectoryAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="grainDirectoryName">
        /// grain 目录提供者名称。
        /// </param>
        public GrainDirectoryAttribute(string grainDirectoryName)
        {
            this.GrainDirectoryName = grainDirectoryName;
        }

        /// <summary>
        /// 获取或设置 grain 目录提供者名称。
        /// </summary>
        public string GrainDirectoryName { get; set; }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "AttributeGrainPropertiesProvider validates the properties collection before dispatching to attributes.")]
        public void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainTypeProperties.GrainDirectory] = this.GrainDirectoryName ?? DEFAULT_GRAIN_DIRECTORY;
        }
    }
}
