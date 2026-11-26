using Orleans.Serialization.Codecs;
using Orleans.Serialization.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
#if NET6_0_OR_GREATER
using System.Reflection.Metadata;
#endif
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// 一组在序列、机密失效或复制时被引用的物体。
    /// </summary>
    public sealed class ReferencedObjectCollection
    {
        private struct ReferencePair
        {
            public ReferencePair(uint id, object @object)
            {
                Id = id;
                Object = @object;
            }

            public uint Id;
            public object Object;
        }

        /// <summary>
        /// 获取或设置对象计数的引用。
        /// </summary>
        /// <value>对象计数的引用。</value>
        internal int ReferenceToObjectCount;
        private readonly ReferencePair[] _referenceToObject = new ReferencePair[64];

        private int _objectToReferenceCount;
        private readonly ReferencePair[] _objectToReference = new ReferencePair[64];

        private Dictionary<uint, object>? _referenceToObjectOverflow;
        private Dictionary<object, uint>? _objectToReferenceOverflow;
        private uint _currentReferenceId;

        /// <summary>
        /// 尝试用指定的代号获取引用对象。
        /// </summary>
        /// <param name="reference">参考。</param>
        /// <returns>引用对象,如果找到指定代号, <see langword="null" /> 否则。</returns>
        public object? TryGetReferencedObject(uint reference)
        {
            var refs = _referenceToObject.AsSpan(0, ReferenceToObjectCount);
            for (int i = 0; i < refs.Length; ++i)
            {
                if (refs[i].Id == reference)
                    return refs[i].Object;
            }

            if (_referenceToObjectOverflow is { } overflow && overflow.TryGetValue(reference, out var value))
                return value;

            return null;
        }

        /// <summary>
        /// 标记一个值字段。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkValueField() => ++_currentReferenceId;

        internal uint CreateRecordPlaceholder() => ++_currentReferenceId;

        /// <summary>
        /// 获取或添加引用。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <param name="reference">参考。</param>
        /// <returns><see langword="true" /> 如果已有参考文献, <see langword="false" /> 否则。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetOrAddReference(object? value, out uint reference)
        {
            // 无条件地顶撞参考柜台,因为呼吁采用这种方法就意味着可能提及。
            var nextReference = ++_currentReferenceId;

            // 无效总是在参考 0
            if (value is null)
            {
                reference = 0;
                return true;
            }

            var objects = _objectToReference.AsSpan(0, _objectToReferenceCount);
            for (int i = 0; i < objects.Length; ++i)
            {
                if (objects[i].Object == value)
                {
                    reference = objects[i].Id;
                    return true;
                }
            }

            if (_objectToReferenceOverflow is { } overflow)
            {
#if NET6_0_OR_GREATER
                ref var refValue = ref CollectionsMarshal.GetValueRefOrAddDefault(overflow, value, out var exists);
                if (exists)
                {
                    reference = refValue;
                    return true;
                }

                refValue = nextReference;
                Unsafe.SkipInit(out reference);
                return false;
#else
                if (overflow.TryGetValue(value, out var existing))
                {
                    reference = existing;
                    return true;
                }
                else
                {
                    overflow[value] = nextReference;
                    Unsafe.SkipInit(out reference);
                    return false;
                }
#endif
            }

            // 添加引用。
            var objectsArray = _objectToReference;
            var objectsCount = _objectToReferenceCount;
            if ((uint)objectsCount < (uint)objectsArray.Length)
            {
                _objectToReferenceCount = objectsCount + 1;
                objectsArray[objectsCount].Id = nextReference;
                objectsArray[objectsCount].Object = value;
            }
            else
            {
                CreateObjectToReferenceOverflow(value);
            }

            Unsafe.SkipInit(out reference);
            return false;
        }

        /// <summary>
        /// 获取引用的索引,或者如果该对象以前未曾遇到过,则获取 <c>-1</c> 。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>引用索引,或 <c>-1</c> 如果该对象以前未曾遇到,则该对象在此前未曾遇到过。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetReferenceIndex(object? value)
        {
            if (value is null)
            {
                return -1;
            }

            var refs = _referenceToObject.AsSpan(0, ReferenceToObjectCount);
            for (int i = 0; i < refs.Length; ++i)
            {
                if (refs[i].Object == value)
                {
                    return i;
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateObjectToReferenceOverflow(object value)
        {
            var result = new Dictionary<object, uint>(_objectToReferenceCount * 2, ReferenceEqualsComparer.Default);
            var objects = _objectToReference;
            for (var i = 0; i < objects.Length; i++)
            {
                var record = objects[i];
                result[record.Object] = record.Id;
                objects[i] = default;
            }

            result[value] = _currentReferenceId;

            _objectToReferenceCount = 0;
            _objectToReferenceOverflow = result;
        }

        private void AddToReferences(object value, uint reference)
        {
            if (_referenceToObjectOverflow is { } overflow)
            {
#if NET6_0_OR_GREATER
                ref var refValue = ref CollectionsMarshal.GetValueRefOrAddDefault(overflow, reference, out var exists);
                if (exists && value is not UnknownFieldMarker && refValue is not UnknownFieldMarker)
                {
                    // 一旦知道类型, 未知的字段标记即可替换 。
                    ThrowReferenceExistsException(reference);
                }

                refValue = value;
#else
                if (overflow.TryGetValue(reference, out var existing) && value is not UnknownFieldMarker && existing is not UnknownFieldMarker)
                {
                    // 一旦知道类型, 未知的字段标记即可替换 。
                    ThrowReferenceExistsException(reference);
                }

                overflow[reference] = value;
#endif
            }
            else
            {
                var refs = _referenceToObject.AsSpan(0, ReferenceToObjectCount);
                for (var i = 0; i < refs.Length; i++)
                {
                    if (refs[i].Id == reference)
                    {
                        if (value is not UnknownFieldMarker && refs[i].Object is not UnknownFieldMarker)
                        {
                            // 一旦知道类型, 未知的字段标记即可替换 。
                            ThrowReferenceExistsException(reference);
                        }
                        refs[i].Object = value;
                        return;
                    }
                }

                _referenceToObject[ReferenceToObjectCount++] = new ReferencePair(reference, value);

                if (ReferenceToObjectCount >= _referenceToObject.Length)
                {
                    CreateReferenceToObjectOverflow();
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateReferenceToObjectOverflow()
            {
                var result = new Dictionary<uint, object>(ReferenceToObjectCount * 2);
                var refs = _referenceToObject.AsSpan(0, ReferenceToObjectCount);
                for (var i = 0; i < refs.Length; i++)
                {
                    var record = refs[i];
                    result[record.Id] = record.Object;
                    refs[i] = default;
                }

                ReferenceToObjectCount = 0;
                _referenceToObjectOverflow = result;
            }
        }

        [DoesNotReturn]
        private static void ThrowReferenceExistsException(uint reference) => throw new InvalidOperationException($"Reference {reference} already exists");

        /// <summary>
        /// 记录一个参考字段。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordReferenceField(object? value) => RecordReferenceField(value, ++_currentReferenceId);

        /// <summary>
        /// 记录带有指定标识符的引用字段。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <param name="referenceId">参考标识符。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordReferenceField(object? value, uint referenceId)
        {
            if (value is null)
            {
                return;
            }

            AddToReferences(value, referenceId);
        }

        /// <summary>
        /// 参考表副本。
        /// </summary>
        /// <returns>参考表副本。</returns>
        public Dictionary<uint, object> CopyReferenceTable() => _referenceToObject.Take(ReferenceToObjectCount).ToDictionary(r => r.Id, r => r.Object);

        /// <summary>
        /// 复制识别表。
        /// </summary>
        /// <returns>识别表副本。</returns>
        public Dictionary<object, uint> CopyIdTable() => _objectToReference.Take(_objectToReferenceCount).ToDictionary(r => r.Object, r => r.Id);

        /// <summary>
        /// 获取或设置当前引用标识符。
        /// </summary>
        /// <value>当前参考标识符。</value>
        public uint CurrentReferenceId { get => _currentReferenceId; set => _currentReferenceId = value; }

        /// <summary>
        /// 重设此实例 。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _referenceToObject.AsSpan(0, ReferenceToObjectCount).Clear();
            _objectToReference.AsSpan(0, _objectToReferenceCount).Clear();

            ReferenceToObjectCount = 0;
            _objectToReferenceCount = 0;
            CurrentReferenceId = 0;

            _referenceToObjectOverflow = null;
            _objectToReferenceOverflow = null;
        }
    }
}
