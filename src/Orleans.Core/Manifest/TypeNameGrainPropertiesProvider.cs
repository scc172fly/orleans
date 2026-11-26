using System;
using System.Collections.Generic;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Metadata
{
    /// <summary>
    /// 在 grain 属性和 grain 接口属性上弹出类型名称。
    /// </summary>
    internal sealed class TypeNameGrainPropertiesProvider : IGrainPropertiesProvider, IGrainInterfacePropertiesProvider
    {
        /// <inheritdoc/>
        public void Populate(Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainTypeProperties.TypeName] = grainClass.Name;
            properties[WellKnownGrainTypeProperties.FullTypeName] = grainClass.FullName!;
            properties["diag.type"] = RuntimeTypeNameFormatter.Format(grainClass);
            properties["diag.asm"] = CachedTypeResolver.GetName(grainClass.Assembly);
        }

        /// <inheritdoc/>
        public void Populate(Type interfaceType, GrainInterfaceType interfaceId, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainInterfaceProperties.TypeName] = interfaceType.Name;
            properties["diag.type"] = RuntimeTypeNameFormatter.Format(interfaceType);
            properties["diag.asm"] = CachedTypeResolver.GetName(interfaceType.Assembly);
        }
    }
}
