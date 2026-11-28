using Orleans.Runtime;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// 测试 IdSpan，这是一种以字节序列表示标识的基元类型。
    /// 验证相等性、哈希码一致性以及 null 与空数组的正确处理。
    /// </summary>
    [TestCategory("BVT")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class IdSpanTests
    {
        /// <summary>
        /// 测试 IdSpan.Create(string.Empty) 和 default(IdSpan) 不相等。
        /// 它们应具有不同的内部状态（空数组与 null），不应被视为相等。
        /// </summary>
        [Fact]
        public void IdSpan_CreateEmptyString_NotEqualToDefault()
        {
            IdSpan createdFromEmptyString = IdSpan.Create(string.Empty);
            IdSpan defaultIdSpan = default;

            Assert.NotEqual(createdFromEmptyString, defaultIdSpan);
            Assert.False(createdFromEmptyString.Equals(defaultIdSpan));
            Assert.False(createdFromEmptyString == defaultIdSpan);
            Assert.True(createdFromEmptyString != defaultIdSpan);
        }

        /// <summary>
        /// 测试哈希码与相等性一致。
        /// 若两个 IdSpan 相等，它们必须具有相同的哈希码。
        /// </summary>
        [Fact]
        public void IdSpan_HashCode_ConsistentWithEquality()
        {
            IdSpan id1 = IdSpan.Create("test");
            IdSpan id2 = IdSpan.Create("test");
            IdSpan id3 = IdSpan.Create("different");

            // 相等的对象必须具有相同的哈希码。
            Assert.Equal(id1, id2);
            Assert.Equal(id1.GetHashCode(), id2.GetHashCode());

            // 不相等的对象可能具有不同的哈希码（并非必需，但符合预期）。
            Assert.NotEqual(id1, id3);
        }

        /// <summary>
        /// 测试默认 IdSpan 是否具有预期属性。
        /// </summary>
        [Fact]
        public void IdSpan_Default_HasExpectedProperties()
        {
            IdSpan defaultIdSpan = default;

            Assert.True(defaultIdSpan.IsDefault);
            Assert.Equal(0, defaultIdSpan.GetHashCode());
            Assert.Equal("", defaultIdSpan.ToString());
        }

        /// <summary>
        /// 测试从空字符串创建的 IdSpan 是否具有预期属性。
        /// </summary>
        [Fact]
        public void IdSpan_CreateEmptyString_HasExpectedProperties()
        {
            IdSpan emptyStringIdSpan = IdSpan.Create(string.Empty);

            Assert.True(emptyStringIdSpan.IsDefault);
            Assert.Equal("", emptyStringIdSpan.ToString());
            // 哈希码应根据空字节数组计算，而不是 0。
            Assert.NotEqual(0, emptyStringIdSpan.GetHashCode());
        }

        /// <summary>
        /// 测试内容相同的 IdSpan 相等。
        /// </summary>
        [Fact]
        public void IdSpan_SameContent_AreEqual()
        {
            IdSpan id1 = IdSpan.Create("test123");
            IdSpan id2 = IdSpan.Create("test123");

            Assert.Equal(id1, id2);
            Assert.True(id1 == id2);
            Assert.False(id1 != id2);
            Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
        }

        /// <summary>
        /// 测试内容不同的 IdSpan 不相等。
        /// </summary>
        [Fact]
        public void IdSpan_DifferentContent_AreNotEqual()
        {
            IdSpan id1 = IdSpan.Create("test1");
            IdSpan id2 = IdSpan.Create("test2");

            Assert.NotEqual(id1, id2);
            Assert.False(id1 == id2);
            Assert.True(id1 != id2);
        }

        /// <summary>
        /// 测试 CompareTo 对 null 和空数组的处理行为。
        /// </summary>
        [Fact]
        public void IdSpan_CompareTo_HandlesNullAndEmpty()
        {
            IdSpan defaultIdSpan = default;
            IdSpan emptyStringIdSpan = IdSpan.Create(string.Empty);
            IdSpan normalIdSpan = IdSpan.Create("test");

            // 默认值（null）的比较结果应不同于空字符串。
            Assert.NotEqual(0, defaultIdSpan.CompareTo(emptyStringIdSpan));
            Assert.NotEqual(0, emptyStringIdSpan.CompareTo(defaultIdSpan));

            // 两者都应小于普通的 IdSpan。
            Assert.True(defaultIdSpan.CompareTo(normalIdSpan) < 0);
            Assert.True(emptyStringIdSpan.CompareTo(normalIdSpan) < 0);
        }
    }
}
