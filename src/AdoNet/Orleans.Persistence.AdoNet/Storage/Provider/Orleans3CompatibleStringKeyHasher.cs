using System;
using System.Buffers;
using System.Text;

namespace Orleans.Storage
{
    /// <summary>
    /// 与 Orleans v3 兼容的哈希算法实现，适用于纯字符串类型的 Grain 键 ID。
    /// </summary>
    internal class Orleans3CompatibleStringKeyHasher : IHasher
    {
        private readonly Orleans3CompatibleHasher _innerHasher;
        private readonly string _grainType;

        public Orleans3CompatibleStringKeyHasher(Orleans3CompatibleHasher innerHasher, string grainType)
        {
            _innerHasher = innerHasher;
            _grainType = grainType;
        }

        /// <summary>
        /// <see cref="IHasher.Description"/>
        /// </summary>
        public string Description { get; } = $"Orleans v3 hash function ({nameof(JenkinsHash)}).";

        /// <summary>
        /// <see cref="IHasher.Hash(byte[])"/>.
        /// </summary>
        public int Hash(byte[] data)
        {
            // Orleans v3 将纯字符串键视为带扩展的整数键（AdoGrainKey.IsLongKey == true），
            // 因此必须对纯字符串 Grain 键的数据进行扩展。
            // 但 AdoNetGrainStorage 实现还使用以下代码：
            //    ...
            //    var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState).Hash(grainId.GetHashBytes());
            //    var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState).Hash(Encoding.UTF8.GetBytes(baseGrainType));
            //    ...
            // 两次调用的 PickHasher 参数相同，因此需要分析数据内容以区分这两种情况。
            // 如果字符串键等于 Grain 类型名称，此方式将不起作用，但我们认为这种边界情况极其罕见，可以忽略。

            if (IsGrainTypeName(data))
                return _innerHasher.Hash(data);

            var extendedLength = data.Length + 8;

            const int maxOnStack = 256;
            byte[]? rentedBuffer = null;

            // 假定下面的代码不会抛出异常，因此为了 JIT 优化，在不使用 try/finally 块的情况下调用 ArrayPool.Return。

            var buffer = extendedLength > maxOnStack
                ? (rentedBuffer = ArrayPool<byte>.Shared.Rent(extendedLength)).AsSpan()
                : stackalloc byte[maxOnStack];

            buffer = buffer[..extendedLength];

            data.AsSpan().CopyTo(buffer);
            // 缓冲区可能包含任意数据，因此将“extension”段设置为零。
            buffer[data.Length..].Clear();

            var hash = _innerHasher.Hash(buffer);

            if (rentedBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedBuffer);

            return hash;
        }

        private bool IsGrainTypeName(byte[] data)
        {
            // 每个字符至少占用 1 字节。
            if (data.Length < _grainType.Length)
                return false;

            var grainTypeByteCount = Encoding.UTF8.GetByteCount(_grainType);
            if (grainTypeByteCount != data.Length)
                return false;

            const int maxOnStack = 256;
            byte[]? rentedBuffer = null;

            // 假定下面的代码不会抛出异常，因此为了 JIT 优化，在不使用 try/finally 块的情况下调用 ArrayPool.Return。

            var buffer = grainTypeByteCount > maxOnStack
                ? (rentedBuffer = ArrayPool<byte>.Shared.Rent(grainTypeByteCount)).AsSpan()
                : stackalloc byte[maxOnStack];

            buffer = buffer[..grainTypeByteCount];

            var bytesWritten = Encoding.UTF8.GetBytes(_grainType, buffer);
            var isGrainType = buffer[..bytesWritten].SequenceEqual(data);
            if (rentedBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedBuffer);

            return isGrainType;
        }
    }
}
