<a id="microsoft-orleans-serialization-test-kit"></a>

# Microsoft Orleans 序列化测试箱

<a id="introduction"></a>

## 简介
Microsoft Orleans 序列测试工具箱提供工具和工具,帮助测试 Orleans 应用程序中的序列功能。该软件包简化了书面测试,以正确核实自定义类型工作的序列和取消序列。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Serialization.TestKit
```

你通常会把这个包添加到测试项目里

<a id="example---testing-serialization"></a>

## 实例 - 试验序列化 
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

## 附加测试功能
TestKit 提供若干功能,用于测试序列,并允许你侧重于测试具体的序列组件:

```csharp
// Using a specific serializer
var specificSerializer = services.GetRequiredService<Serializer<MyCustomType>>();
byte[] bytes = specificSerializer.SerializeToArray(original);
var deserialized = specificSerializer.Deserialize(bytes);
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序号](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [测试 Orleans 应用](https://dotnet.github.io/orleans/docs/implementation/testing/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证