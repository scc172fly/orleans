using System;
using System.Collections.Generic;
using System.Globalization;
using Orleans.Metadata;

namespace Orleans.CodeGeneration
{
    /// <summary>
    /// VersionAttribute 用于指定接口的版本号。
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class VersionAttribute : Attribute, IGrainInterfacePropertiesProviderAttribute
    {
        /// <summary>
        /// 初始化 <see cref="VersionAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="version">
        /// 版本号。
        /// </param>
        public VersionAttribute(ushort version)
        {
            Version = version;
        }

        /// <summary>
        /// 获取版本号。
        /// </summary>
        public ushort Version { get; private set; }

        /// <inheritdoc />
        void IGrainInterfacePropertiesProviderAttribute.Populate(IServiceProvider services, Type type, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainInterfaceProperties.Version] = this.Version.ToString(CultureInfo.InvariantCulture);
        }
    }
}
