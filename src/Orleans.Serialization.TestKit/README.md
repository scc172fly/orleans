<a id="microsoft-orleans-serialization-test-kit"></a>

# Microsoft Orleans 序列化测试工具包

<a id="introduction"></a>

## 简介
Microsoft Orleans 序列化测试工具包提供用于测试 Orleans 应用程序序列化功能的工具和实用程序。此包可以简化验证自定义类型的序列化与反序列化是否正常工作的测试编写过程。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization.TestKit
```

通常应将此包添加到测试项目中。

<a id="example---testing-serialization"></a>

## 示例 - 测试序列化
```csharp
using Orleans.Serialization.TestKit;
using Xunit.Abstractions;

public class TimeSpanTests(ITestOutputHelper output) : FieldCodecTester<TimeSpan>(output)
{
    protected override TimeSpan CreateValue() => TimeSpan.FromMilliseconds(Guid.NewGuid().GetHashCode());
    protected override TimeSpan[] TestValues => [TimeSpan.MinValue, TimeSpan.MaxValue, TimeSpan.Zero, TimeSpan.FromSeconds(12345)];
    protected override Action<Action<TimeSpan>> ValueProvider => Gen.TimeSpan.ToValueProvider();
}

public class TimeSpanCopierTests(ITestOutputHelper output) : CopierTester<TimeSpan, IDeepCopier<TimeSpan>>(output)
{
    protected override TimeSpan CreateValue() => TimeSpan.FromMilliseconds(Guid.NewGuid().GetHashCode());
    protected override TimeSpan[] TestValues => [TimeSpan.MinValue, TimeSpan.MaxValue, TimeSpan.Zero, TimeSpan.FromSeconds(12345)];
    protected override Action<Action<TimeSpan>> ValueProvider => Gen.TimeSpan.ToValueProvider();
}

public class DateTimeOffsetTests(ITestOutputHelper output) : FieldCodecTester<DateTimeOffset, DateTimeOffsetCodec>(output)
{
    protected override DateTimeOffset CreateValue() => DateTime.UtcNow;
    protected override DateTimeOffset[] TestValues =>
    [
        DateTimeOffset.MinValue,
        DateTimeOffset.MaxValue,
        new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0), TimeSpan.FromHours(11.5)),
        new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0), TimeSpan.FromHours(-11.5)),
    ];

    protected override Action<Action<DateTimeOffset>> ValueProvider => Gen.DateTimeOffset.ToValueProvider();
}

public class DateTimeOffsetCopierTests(ITestOutputHelper output) : CopierTester<DateTimeOffset, IDeepCopier<DateTimeOffset>>(output)
{
    protected override DateTimeOffset CreateValue() => DateTime.UtcNow;
    protected override DateTimeOffset[] TestValues =>
    [
        DateTimeOffset.MinValue,
        DateTimeOffset.MaxValue,
        new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0), TimeSpan.FromHours(11.5)),
        new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0), TimeSpan.FromHours(-11.5)),
    ];

    protected override Action<Action<DateTimeOffset>> ValueProvider => Gen.DateTimeOffset.ToValueProvider();
}
```

<a id="additional-testing-features"></a>

## 其他测试功能
TestKit 提供多种用于测试序列化的实用程序，使你能够专注于测试特定的序列化组件：

```csharp
// Using a specific serializer
var specificSerializer = services.GetRequiredService<Serializer<MyCustomType>>();
byte[] bytes = specificSerializer.SerializeToArray(original);
var deserialized = specificSerializer.Deserialize(bytes);
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [测试 Orleans 应用程序](https://dotnet.github.io/orleans/docs/implementation/testing/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)