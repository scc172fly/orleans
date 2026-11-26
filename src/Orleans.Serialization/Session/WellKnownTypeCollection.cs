using Microsoft.Extensions.Options;
using Orleans.Serialization.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// 收集众所周知的类型。
    /// </summary>
    public sealed class WellKnownTypeCollection
    {
        private readonly Dictionary<uint, Type> _wellKnownTypes;
        private readonly Dictionary<Type, uint> _wellKnownTypeToIdMap;

        /// <summary>
        /// 初始化 <see cref="WellKnownTypeCollection"/> 类的新实例。
        /// </summary>
        /// <param name="config">配置。</param>
        public WellKnownTypeCollection(IOptions<TypeManifestOptions> config)
        {
            _wellKnownTypes = config?.Value.WellKnownTypeIds ?? throw new ArgumentNullException(nameof(config));
            _wellKnownTypeToIdMap = new Dictionary<Type, uint>(_wellKnownTypes.Count);
            foreach (var item in _wellKnownTypes)
            {
                _wellKnownTypeToIdMap[item.Value] = item.Key;
            }
        }

        /// <summary>
        /// 获取与所提供的类型识别符相应的类型。
        /// </summary>
        /// <param name="typeId">类型识别符号。</param>
        /// <returns>类型。</returns>
        public Type? GetWellKnownType(uint typeId)
        {
            if (typeId == 0)
            {
                return null;
            }

            return _wellKnownTypes[typeId];
        }

        /// <summary>
        /// 尝试获取与所提供的类型识别符相应的类型。
        /// </summary>
        /// <param name="typeId">类型识别符号。</param>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> 如果找到相应类型,则以其他方式 <see langword="false" /> 。</returns>
        public bool TryGetWellKnownType(uint typeId, out Type? type)
        {
            if (typeId == 0)
            {
                type = null;
                return true;
            }

            return _wellKnownTypes.TryGetValue(typeId, out type);
        }

        /// <summary>
        /// 尝试获得与所提供的类型相应的类型识别符。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <param name="typeId">类型识别符号。</param>
        /// <returns><see langword="true" /> 如果该类型有众所周知的识别符号, <see langword="false" /> 否则。</returns>
        public bool TryGetWellKnownTypeId(Type type, out uint typeId) => _wellKnownTypeToIdMap.TryGetValue(type, out typeId);
    }
}
