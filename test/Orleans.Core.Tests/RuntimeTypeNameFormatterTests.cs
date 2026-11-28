using System.Collections.Specialized;
using Orleans.Serialization.TypeSystem;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// Orleans RuntimeTypeNameFormatter 的测试，该组件负责设置及解析 .NET 类型名称的格式，
    /// 使其能在不同环境中恢复且保持一致。这对 Orleans 的序列化
    /// 系统以及 Silo 与客户端通信时的类型解析至关重要。
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
        /// 测试由 <see cref="RuntimeTypeNameFormatter"/> 格式化的各种字符串能否通过 <see cref="Type.GetType(string)"/> 加载。
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
        /// 测试由 <see cref="RuntimeTypeNameParser"/> 解析的各种字符串能否重新格式化为与输入完全相同的字符串（前提是输入由 <see cref="RuntimeTypeNameFormatter"/> 生成）。
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
        /// 测试解析无效类型名称时是否生成包含位置指示符的描述性错误消息。
        /// 这有助于开发人员调试 Orleans 序列化中的类型解析问题。
        /// </summary>
        [Fact]
        public void InvalidNamesThrowDescriptiveErrorMessage()
        {
            var input = "MalformedName[`";
            var exception = Assert.Throws<InvalidOperationException>(() => RuntimeTypeNameParser.Parse(input));
            _output.WriteLine(exception.Message);
            Assert.Contains(input, exception.Message);
            Assert.Contains("^", exception.Message); // 位置指示符
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
