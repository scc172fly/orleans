using Microsoft.Extensions.Options;
using Orleans.Serialization.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// 已知类型的集合。
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
        /// 获取与给定类型标识符对应的类型。
        /// </summary>
        /// <param name="typeId">类型标识符。</param>
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
        /// 尝试获取与给定类型标识符对应的类型。
        /// </summary>
        /// <param name="typeId">类型标识符。</param>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" />，如果找到对应类型；否则为 <see langword="false" />。</returns>
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
        /// 尝试获取与给定类型对应的类型标识符。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <param name="typeId">类型标识符。</param>
        /// <returns><see langword="true" />，如果该类型具有已知标识符；否则为 <see langword="false" />。</returns>
        public bool TryGetWellKnownTypeId(Type type, out uint typeId) => _wellKnownTypeToIdMap.TryGetValue(type, out typeId);
    }
}
