using Orleans.Runtime;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// IdSpan测试,这是代表字节序列的身份的原始类型。
    /// 验证平等、散列码一致性,并正确处理空阵列和空阵列。
    /// </summary>
    [TestCategory("BVT")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class IdSpanTests
    {
        /// <summary>
        /// 测试 IdSpan.Create(string.Empty) 和默认(IdSpan) 不相等 。
        /// 它们应有不同的内部状态(空阵列与空阵列),不应被视为平等。
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
        /// 大麻守则的检验符合平等原则。
        /// 如果两个 IdSpans 相等,它们必须具有相同的散列码。
        /// </summary>
        [Fact]
        public void IdSpan_HashCode_ConsistentWithEquality()
        {
            IdSpan id1 = IdSpan.Create("test");
            IdSpan id2 = IdSpan.Create("test");
            IdSpan id3 = IdSpan.Create("different");

            // 等物必须具有平等的散列法则
            Assert.Equal(id1, id2);
            Assert.Equal(id1.GetHashCode(), id2.GetHashCode());

            // 不同对象的散列代码可能不同(不需要但预期),但并非相等对象
            Assert.NotEqual(id1, id3);
        }

        /// <summary>
        /// 测试默认 IdSpan 有预期属性 。
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
        /// 测试空字符串创建的 IdSpan 有预期属性。
        /// </summary>
        [Fact]
        public void IdSpan_CreateEmptyString_HasExpectedProperties()
        {
            IdSpan emptyStringIdSpan = IdSpan.Create(string.Empty);

            Assert.True(emptyStringIdSpan.IsDefault);
            Assert.Equal("", emptyStringIdSpan.ToString());
            // 散码应该从空字节数组而不是 0 计算出来
            Assert.NotEqual(0, emptyStringIdSpan.GetHashCode());
        }

        /// <summary>
        /// 测试内容相同的 IdSpans 。
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
        /// 测试内容不同的 IdSpans 不相等。
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
        /// 以空数组测试 CompareTo 行为。
        /// </summary>
        [Fact]
        public void IdSpan_CompareTo_HandlesNullAndEmpty()
        {
            IdSpan defaultIdSpan = default;
            IdSpan emptyStringIdSpan = IdSpan.Create(string.Empty);
            IdSpan normalIdSpan = IdSpan.Create("test");

            // 默认( null) 的比较应该与空字符串不同
            Assert.NotEqual(0, defaultIdSpan.CompareTo(emptyStringIdSpan));
            Assert.NotEqual(0, emptyStringIdSpan.CompareTo(defaultIdSpan));

            // 两者的间隔应少于正常时间
            Assert.True(defaultIdSpan.CompareTo(normalIdSpan) < 0);
            Assert.True(emptyStringIdSpan.CompareTo(normalIdSpan) < 0);
        }
    }
}
