using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Xunit;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 复制器的测试方法。
    /// </summary>
    [Trait("Category", "BVT")]
    [Trait("Suite", "BVT")]
    [Trait("Provider", "None")]
    [Trait("Area", "Serialization")]
    [ExcludeFromCodeCoverage]
    public abstract class CopierTester<TValue, TCopier> : SerializationTester where TCopier : class, IDeepCopier<TValue>
    {
        private CodecProvider? _codecProviderValue;
        private CodecProvider _codecProvider => _codecProviderValue ??= ServiceProvider.GetRequiredService<CodecProvider>();

        /// <summary>
        /// 初始化新的 <see cref="CopierTester{TValue, TCopier}"/> 实例。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="output"/> 为 <see langword="null"/>。</exception>
        protected CopierTester(ITestOutputHelper output) : base(output ?? throw new ArgumentNullException(nameof(output)))
        {
            output.WriteLine($"Random seed: {RandomSeed}");
        }

        /// <summary>
        /// 初始化新的 <see cref="CopierTester{TValue, TCopier}"/> 实例。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="output"/> 或 <paramref name="fixture"/> 为 <see langword="null"/>。</exception>
        protected CopierTester(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output ?? throw new ArgumentNullException(nameof(output)), fixture)
        {
            output.WriteLine($"Random seed: {RandomSeed}");
        }

        /// <inheritdoc/>
        protected override IServiceProvider CreateServiceProvider()
            => CreateServiceProviderCore(Configure);

        internal static IServiceProvider CreateServiceProviderCore(Action<ISerializerBuilder> configure)
        {
            var services = new ServiceCollection();
            _ = services.AddSerializer(builder => builder.Configure(config => config.AddCopier(typeof(TCopier))));

            if (!typeof(TCopier).IsAbstract && !typeof(TCopier).IsInterface)
            {
                _ = services.AddSingleton<TCopier>();
            }

            _ = services.AddSerializer(configure);

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// 获取一个值，指示此编解码器复制的类型是否不可变。
        /// </summary>
        protected virtual bool IsImmutable => false;

        /// <summary>
        /// 获取一个值，指示此编解码器复制的类型是否使用池化机制。
        /// </summary>
        protected virtual bool IsPooled => false;

        /// <summary>
        /// 配置序列化器。
        /// </summary>
        protected virtual void Configure(ISerializerBuilder builder)
        {
        }

        /// <summary>
        /// 创建用于测试的复制器实例。
        /// </summary>
        protected virtual TCopier CreateCopier() => ServiceProvider.GetRequiredService<TCopier>();

        /// <summary>
        /// 创建要复制的值。
        /// </summary>
        protected abstract TValue CreateValue();

        /// <summary>
        /// 获取测试值数组。
        /// </summary>
        protected abstract TValue[] TestValues { get; }

        /// <summary>
        /// 比较两个值，相等时返回 <see langword="true"/>，不相等时返回 <see langword="false"/>。
        /// </summary>
        protected virtual bool Equals(TValue? left, TValue? right) => EqualityComparer<TValue>.Default.Equals(left!, right!);

        /// <summary>
        /// 获取值提供程序委托。
        /// </summary>
        protected virtual Action<Action<TValue>>? ValueProvider { get; }

        /// <summary>
        /// 检查复制后的值是否相等。
        /// </summary>
        [Fact]
        public void CopiedValuesAreEqual()
        {
            var copier = CreateCopier();
            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                using var context = new CopyContext(_codecProvider, _ => { });
                var output = copier.DeepCopy(original, context);
                var isEqual = Equals(original, output);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Copy value \"{output}\" must equal original value \"{original}\"");
            }
        }

        /// <summary>
        /// 检查引用是否已添加到复制上下文。
        /// </summary>
        [Fact]
        public void ReferencesAreAddedToCopyContext()
        {
            if (typeof(TValue).IsValueType || IsPooled)
            {
                return;
            }

            var value = CreateValue();
            var array = new TValue[] { value, value };
            var arrayCopier = ServiceProvider.GetRequiredService<DeepCopier<TValue[]>>();
            var arrayCopy = arrayCopier.Copy(array)!;
            Assert.Same(arrayCopy[0], arrayCopy[1]);

            if (IsImmutable)
            {
                Assert.Same(value, arrayCopy[0]);
            }
            else
            {
                Assert.NotSame(value, arrayCopy[0]);
            }
        }

        /// <summary>
        /// 检查包含该字段类型的强类型元组能否被复制。
        /// </summary>
        [Fact]
        public void CanCopyTupleViaSerializer()
        {
            var copier = ServiceProvider.GetRequiredService<DeepCopier<(string, TValue, TValue, string)>>();

            var original = (Guid.NewGuid().ToString(), CreateValue(), CreateValue(), Guid.NewGuid().ToString());

            var copy = copier.Copy(original)!;

            var isEqual = Equals(original.Item1, copy.Item1);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 1, \"{copy}\", must equal original value, \"{original}\"");
            isEqual = Equals(original.Item2, copy.Item2);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 2, \"{copy}\", must equal original value, \"{original}\"");
            isEqual = Equals(original.Item3, copy.Item3);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 3, \"{copy}\", must equal original value, \"{original}\"");
            isEqual = Equals(original.Item4, copy.Item4);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 4, \"{copy}\", must equal original value, \"{original}\"");
        }

        /// <summary>
        /// 检查包含该字段类型的、元素类型为 object 的元组能否被复制。
        /// </summary>
        [Fact]
        public void CanCopyUntypedTupleViaSerializer()
        {
            var copier = ServiceProvider.GetRequiredService<DeepCopier<(string, object?, object?, string)>>();
            var value = ((IEnumerable<TValue>)TestValues).Reverse().Concat(new[] { CreateValue(), CreateValue() }).Take(2).ToArray();

            var original = (Guid.NewGuid().ToString(), (object?)value[0], (object?)value[1], Guid.NewGuid().ToString());

            var copy = copier.Copy(original)!;

            var isEqual = Equals(original.Item1, copy.Item1);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 1, \"{copy.Item1}\", must equal original value, \"{original.Item1}\"");
            isEqual = Equals((TValue?)original.Item2, (TValue?)copy.Item2);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 2, \"{copy.Item2}\", must equal original value, \"{original.Item2}\"");
            isEqual = Equals((TValue?)original.Item3, (TValue?)copy.Item3);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 3, \"{copy.Item3}\", must equal original value, \"{original.Item3}\"");
            isEqual = Equals(original.Item4, copy.Item4);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Copied value for item 4, \"{copy.Item4}\", must equal original value, \"{original.Item4}\"");
        }

        /// <summary>
        /// 检查将值作为强类型列表中的元素时能否完成往返转换。
        /// </summary>
        [Fact]
        public void CanCopyCollectionViaSerializer()
        {
            var copier = ServiceProvider.GetRequiredService<DeepCopier<List<TValue>>>();

            var original = new List<TValue>();
            original.AddRange(TestValues);
            for (var i = 0; i < 5; i++)
            {
                original.Add(CreateValue());
            }

            var copy = copier.Copy(original)!;

            Assert.Equal(original.Count, copy.Count);
            for (var i = 0; i < original.Count; ++i)
            {
                var isEqual = Equals(original[i], copy[i]);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Copied value at index {i}, \"{copy}\", must equal original value, \"{original}\"");
            }
        }

        /// <summary>
        /// 检查将值作为 object 类型列表中的元素时能否完成往返转换。
        /// </summary>
        [Fact]
        public void CanCopyCollectionViaUntypedSerializer()
        {
            var copier = ServiceProvider.GetRequiredService<DeepCopier<List<object?>>>();

            var original = new List<object?>();
            foreach (var value in TestValues)
            {
                original.Add(value);
            }

            for (var i = 0; i < 5; i++)
            {
                original.Add(CreateValue());
            }

            var copy = copier.Copy(original)!;

            Assert.Equal(original.Count, copy.Count);
            for (var i = 0; i < original.Count; ++i)
            {
                var isEqual = Equals((TValue?)original[i], (TValue?)copy[i]);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Copied value at index {i}, \"{copy}\", must equal original value, \"{original}\"");
            }
        }
    }

}
