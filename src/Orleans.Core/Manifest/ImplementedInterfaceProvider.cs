using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Metadata
{
    /// <summary>
    /// 弹出 grain 界面属性的 grain 由 grain 类.
    /// </summary>
    internal sealed class ImplementedInterfaceProvider : IGrainPropertiesProvider
    {
        private readonly GrainInterfaceTypeResolver interfaceTypeResolver;
        private readonly string[] _cachedKeys = new string[16];

        /// <summary>
        /// 初始化 <see cref="ImplementedInterfaceProvider"/> 类的新实例。
        /// </summary>
        /// <param name="interfaceTypeResolver">接口类型解答器。</param>
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
        /// 获得一个值,以显示指定类型是否为 grain 接口类型。
        /// </summary>
        /// <param name="type">检查的类型。</param>
        /// <returns>表示指定类型是否为 grain 接口类型的值。</returns>
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
