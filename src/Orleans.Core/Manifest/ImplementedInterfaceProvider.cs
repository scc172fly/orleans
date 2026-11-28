using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Metadata
{
    /// <summary>
    /// 将 Grain 类实现的 Grain 接口填入 Grain 接口属性。
    /// </summary>
    internal sealed class ImplementedInterfaceProvider : IGrainPropertiesProvider
    {
        private readonly GrainInterfaceTypeResolver interfaceTypeResolver;
        private readonly string[] _cachedKeys = new string[16];

        /// <summary>
        /// 初始化 <see cref="ImplementedInterfaceProvider"/> 类的新实例。
        /// </summary>
        /// <param name="interfaceTypeResolver">接口类型解析器。</param>
        public ImplementedInterfaceProvider(GrainInterfaceTypeResolver interfaceTypeResolver)
        {
            this.interfaceTypeResolver = interfaceTypeResolver;
        }

        /// <inheritdoc/>
        public void Populate(Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            var counter = 0;
            foreach (var @interface in grainClass.GetInterfaces())
            {
                if (!IsGrainInterface(@interface)) continue;

                var type = @interface switch
                {
                    { IsGenericType: true } when grainClass is { IsGenericType: true } => @interface.GetGenericTypeDefinition(),
                    _ => @interface
                };
                var interfaceId = this.interfaceTypeResolver.GetGrainInterfaceType(type);
                var key = (uint)counter < (uint)_cachedKeys.Length ? (_cachedKeys[counter] ??= GetKey(counter)) : GetKey(counter);
                properties[key] = interfaceId.ToString();
                ++counter;
            }
        }

        private static string GetKey(int counter) => $"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{counter}";

        /// <summary>
        /// 获取指示指定类型是否为 Grain 接口类型的值。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <returns>指示指定类型是否为 Grain 接口类型的值。</returns>
        public static bool IsGrainInterface(Type type)
        {
            if (type.IsClass)
                return false;
            if (type == typeof(IGrainObserver) || type == typeof(IAddressable) || type == typeof(IGrainExtension))
                return false;
            if (type == typeof(IGrain) || type == typeof(IGrainWithGuidKey) || type == typeof(IGrainWithIntegerKey)
                || type == typeof(IGrainWithGuidCompoundKey) || type == typeof(IGrainWithIntegerCompoundKey))
                return false;
            if (type == typeof(ISystemTarget))
                return false;

            return typeof(IAddressable).IsAssignableFrom(type);
        }
    }
}
