using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// 引用的 <see cref="Type"/> 实例集。
    /// </summary>
    public sealed class ReferencedTypeCollection
    {
        private readonly Dictionary<uint, Type?> _referencedTypes = new();
        private readonly Dictionary<Type, uint> _referencedTypeToIdMap = new();

        private uint _currentReferenceId;

        /// <summary>
        /// 获取指定引用代号的类型。
        /// </summary>
        /// <param name="reference">参考代号。</param>
        /// <returns>引用类型。</returns>
        public Type? GetReferencedType(uint reference)
        {
            if (!_referencedTypes.TryGetValue(reference, out var type))
                ThrowUnknownReferencedType(reference);
            return type;
        }

        private static void ThrowUnknownReferencedType(uint id) => throw new UnknownReferencedTypeException(id);

        /// <summary>
        /// 获取指定引用代号的类型。
        /// </summary>
        /// <param name="reference">参考代号。</param>
        /// <param name="type">引用类型。</param>
        /// <returns><see langword="true" /> 如果找到参考类型,则以其他方式 <see langword="false" /> 。</returns>
        public bool TryGetReferencedType(uint reference, out Type? type) => _referencedTypes.TryGetValue(reference, out type);

        /// <summary>
        /// 记录带有指定标识符的类型。
        /// </summary>
        public void RecordReferencedType(Type? type) => _referencedTypes.Add(++_currentReferenceId, type);

        /// <summary>
        /// 获取指定类型的身份识别符。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <param name="reference">参考。</param>
        /// <returns><see langword="true" /> 如果该类型被指为先天性, <see langword="false" /> 否则。</returns>
        public bool TryGetTypeReference(Type type, out uint reference) => _referencedTypeToIdMap.TryGetValue(type, out reference);

        /// <summary>
        /// 获取或添加指定类型的标识符。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetOrAddTypeReference(Type type)
        {
#if NET6_0_OR_GREATER
            ref var refValue = ref CollectionsMarshal.GetValueRefOrAddDefault(_referencedTypeToIdMap, type, out var exists);
            if (exists)
                return refValue;

            refValue = ++_currentReferenceId;
#else
            if (_referencedTypeToIdMap.TryGetValue(type, out var existing))
            {
                return existing;
            }
            else
            {
                _referencedTypeToIdMap[type] = ++_currentReferenceId;
            }
#endif
            return 0;
        }

        /// <summary>
        /// 重设此实例 。
        /// </summary>
        public void Reset()
        {
            _currentReferenceId = 0;
            _referencedTypes.Clear();
            _referencedTypeToIdMap.Clear();
        }
    }
}
