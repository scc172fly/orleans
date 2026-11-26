using System.Collections.Specialized;
using Orleans.Serialization.TypeSystem;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// 测试 Orleans RuntimeTypeNameFormatter,负责格式化和分割 .NET 字词名名称
    /// 以可恢复和在不同环境中保持一致的方式,对于 Orleans的序列化至关重要
    /// silos 与客户进行沟通时,应使用系统类型和型号分辨率。
    /// </summary>
    [TestCategory("BVT")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Serialization")]
    public class RuntimeTypeNameFormatterTests
    {
        public interface IMyBaseType<T> { }
        public interface IMyArrayType<T> : IMyBaseType<T[]> { }
        private readonly ITestOutputHelper _output;
        private readonly List<Type> _types = new()
            {
                typeof(NameValueCollection),
                typeof(int),
                typeof(int[]),
                typeof(int*[]),
                typeof(int[]),
                typeof(List<>),
                typeof(List<int>),
                typeof(List<int*[]>),
                typeof(Inner<int[,,]>.InnerInner<string, List<int>>.Bottom[,]),
                typeof(Inner<>.InnerInner<,>.Bottom),
                typeof(RuntimeTypeNameFormatterTests),
                typeof(TestGrainInterfaces.CircularStateTestState),
                typeof(int).MakeByRefType(),
                typeof(Inner<int[]>.InnerInner<string, List<int>>.Bottom[,])
                    .MakePointerType()
                    .MakePointerType()
                    .MakeArrayType(10)
                    .MakeByRefType(),
                typeof(NameValueCollection)
            };

        public RuntimeTypeNameFormatterTests(ITestOutputHelper output)
        {
            _output = output;
            _types.Add(typeof(List<>).MakeGenericType(typeof(Inner<int>.Middle).MakeArrayType()));
            _types.Add(typeof(List<>).MakeGenericType(typeof(Inner<>.Middle).MakeArrayType()));
            typeof(IMyArrayType<>).MakeGenericType(typeof(Inner<>.Middle)).GetInterfaces().ToList().ForEach(_types.Add);
            typeof(IMyArrayType<>).GetInterfaces().ToList().ForEach(_types.Add);
        }

        /// <summary>
        /// 测试,以 <see cref="RuntimeTypeNameFormatter"/> 格式格式排列的各种字符串格式可以用 <see cref="Type.GetType(string)"/>装入。
        /// </summary>
        [Fact]
        public void FormattedTypeNamesAreRecoverable()
        {
            var resolver = new CachedTypeResolver();
            foreach (var type in _types)
            {
                if (string.IsNullOrWhiteSpace(type.FullName)) continue;
                var formatted = RuntimeTypeNameFormatter.Format(type);
                _output.WriteLine($"Full Name: {type.FullName}");
                _output.WriteLine($"Formatted: {formatted}");
                var isRecoverable = resolver.TryResolveType(formatted, out var resolved) && resolved == type;
                var resolvedFormatted = resolved is not null ? RuntimeTypeNameFormatter.Format(resolved) : "null";
                Assert.True(isRecoverable, $"Type.GetType(\"{formatted}\") must be equal to the original type. Got: {resolvedFormatted}");
            }
        }

        /// <summary>
        /// 测试 <see cref="RuntimeTypeNameParser"/> 所分析的各种字符串在输入由 <see cref="RuntimeTypeNameFormatter"/>生成时被重新配置,与输入相同。
        /// </summary>
        [Fact]
        public void ParsedTypeNamesAreIdenticalToFormattedNames()
        {
            foreach (var type in _types)
            {
                var formatted = RuntimeTypeNameFormatter.Format(type);
                var parsed = RuntimeTypeNameParser.Parse(formatted);
                _output.WriteLine($"Type.FullName: {type.FullName}");
                _output.WriteLine($"Formatted    : {formatted}");
                _output.WriteLine($"Parsed       : {parsed}");
                Assert.Equal(formatted, parsed.Format());

                var reparsed = RuntimeTypeNameParser.Parse(parsed.Format());
                _output.WriteLine($"Reparsed     : {reparsed}");
                Assert.Equal(formatted, reparsed.Format());
            }
        }

        /// <summary>
        /// 测试解析无效类型名称会产生带有位置指标的描述性错误信息。
        /// 这有助于开发者在 Orleans 序列化中调试类型解答问题。
        /// </summary>
        [Fact]
        public void InvalidNamesThrowDescriptiveErrorMessage()
        {
            var input = "MalformedName[`";
            var exception = Assert.Throws<InvalidOperationException>(() => RuntimeTypeNameParser.Parse(input));
            _output.WriteLine(exception.Message);
            Assert.Contains(input, exception.Message);
            Assert.Contains("^", exception.Message); // 位置指标
        }

        public class Inner<T>
        {
            public class Middle { }
            public class InnerInner<U, V>
            {
                public class Bottom { }
            }
        }
    }
}
