using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using Orleans.Serialization.TestKit;

namespace Orleans.Serialization.UnitTests;

/// <summary>
/// 检验论证的验证和无效保留序列化及复制合同
/// 内置代码
/// </summary>
[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public sealed class CodecArgumentValidationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection()
        .AddSerializer()
        .BuildServiceProvider();

    public void Dispose() => _serviceProvider.Dispose();

    private SerializerSessionPool SessionPool => _serviceProvider.GetRequiredService<SerializerSessionPool>();

    private CopyContext GetCopyContext() => _serviceProvider.GetRequiredService<CopyContextPool>().GetContext();

    private T DeepCopyValue<T>(T value) => _serviceProvider.GetRequiredService<DeepCopier<T>>().Copy(value)!;

    /// <summary>
    /// 解决来自《京都议定书》的 <typeparamref name="T"/> 具体代码实例
    /// <see cref="CodecProvider"/>普通 <c>AddSerializer()</c> 仅根据
    /// 其 <see cref="Codecs.IFieldCodec{T}"/> 服务类型,而不是混凝土类,所以
    /// 需要具体类型额外公共成员(e.g. <c>Serialize</c>/
    /// <c>Deserialize</c> ,从 <c>IBaseCodec&lt;T&gt;</c>必须解决并采用这种方式。
    /// </summary>
    private TCodec GetConcreteCodec<T, TCodec>() where TCodec : IFieldCodec<T>
        => (TCodec)_serviceProvider.GetRequiredService<CodecProvider>().GetCodec<T>();

    private T RoundTripSerialize<T>(T value) => RoundTripSerialize(value, out _);

    private T RoundTripSerialize<T>(T value, out long payloadLength)
    {
        var serializer = _serviceProvider.GetRequiredService<Serializer<T>>();
        var buffer = new TestMultiSegmentBufferWriter(maxAllocationSize: 16);

        using (var writerSession = SessionPool.GetSession())
        {
            var writer = Writer.Create(buffer, writerSession);
            serializer.Serialize(value, ref writer);
            writer.Commit();
            payloadLength = writer.Position;
        }

        using var readerSession = SessionPool.GetSession();
        var reader = Reader.Create(buffer.GetReadOnlySequence(maxSegmentSize: 16), readerSession);
        return serializer.Deserialize(ref reader)!;
    }

    /// <summary>
    /// 间谍 <see cref="IDeepCopier{T}"/> 记录援引数并应用
    /// 区分变异,所以测试可以证明两个 “是否引用了这个合作者?”
    /// "副本真的通过这个合作者流经吗?"
    /// </summary>
    private sealed class TrackingCopier<T>(Func<T, T> transform) : IDeepCopier<T>
    {
        public int InvocationCount { get; private set; }

        [return: NotNullIfNotNull(nameof(input))]
        public T? DeepCopy(T? input, CopyContext context)
        {
            InvocationCount++;
            return input is null ? default : transform(input);
        }
    }

    [Fact]
    public void DeepCopierContract_DeclaresCorrelatedNullability()
    {
        var method = typeof(IDeepCopier<string>).GetMethod(nameof(IDeepCopier<string>.DeepCopy))!;
        var input = method.GetParameters()[0];
        var nullability = new NullabilityInfoContext();

        Assert.Equal(NullabilityState.Nullable, nullability.Create(input).ReadState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(method.ReturnParameter).ReadState);
        var correlatedReturn = Assert.Single(
            method.ReturnParameter.GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(NotNullIfNotNullAttribute));
        Assert.Equal("input", correlatedReturn.ConstructorArguments[0].Value);

        var valueTypeMethod = typeof(IDeepCopier<int>).GetMethod(nameof(IDeepCopier<int>.DeepCopy));
        Assert.Equal(typeof(int), valueTypeMethod!.ReturnType);
    }

    [Fact]
    public void DeepCopier_PreservesNullableValueSemantics()
    {
        var copier = _serviceProvider.GetRequiredService<DeepCopier>();

        Assert.Null(copier.Copy<int?>(null));
        Assert.Equal(5, copier.Copy<int?>(5));
        Assert.Equal(0, copier.Copy(0));

        using var context = GetCopyContext();
        Assert.Null(context.DeepCopy<int?>(null));
        Assert.Equal(5, context.DeepCopy<int?>(5));
        Assert.Equal(0, context.DeepCopy(0));
    }

    [Fact]
    public void CopyContext_TryGetCopy_EnforcesRecordedCopyTypeInvariant()
    {
        using var context = GetCopyContext();
        var assignableOriginal = new object();
        var assignableCopy = new List<int> { 1, 2, 3 };
        context.RecordCopy(assignableOriginal, assignableCopy);
        Assert.True(context.TryGetCopy<IList<int>>(assignableOriginal, out var retrievedCopy));
        Assert.Same(assignableCopy, retrievedCopy);

        var incompatibleOriginal = new object();
        context.RecordCopy(incompatibleOriginal, new object());
        Assert.Throws<InvalidCastException>(() => context.TryGetCopy<string>(incompatibleOriginal, out _));

        var nullCopyOriginal = new object();
        context.RecordCopy(nullCopyOriginal, null!);
        Assert.Throws<InvalidCastException>(() => context.TryGetCopy<object>(nullCopyOriginal, out _));
    }

    [Fact]
    public void VoidCopier_PreservesNullAndValidatesContextFirst()
    {
        IDeepCopier copier = new VoidCopier();
        using var context = GetCopyContext();

        Assert.Null(copier.DeepCopy(null, context));
        var exception = Assert.Throws<ArgumentNullException>(() => copier.DeepCopy(null, null!));
        Assert.Equal("context", exception.ParamName);
        Assert.Throws<InvalidOperationException>(() => copier.DeepCopy(new object(), context));
    }

    // ------------------------------------------------------------------------------------
    // 第1组: IDeepCopier<T>。DeepCopy(T输入, CopyContext 上下文) -- -- 2-弧超载。
    // 经核实的合同:无效上下文抛出;无效输入返回无效,不投掷。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ArrayCopier_NullContext_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ArrayCopier<int>(elementCopier);
        var input = new[] { 1, 2, 3 };

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(new[] { 1, 2, 3 }, input);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ArrayCopier_NullInputWithValidContext_ReturnsNullWithoutInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ArrayCopier<int>(elementCopier);
        using var context = GetCopyContext();

        var result = sut.DeepCopy(null!, context);

        Assert.Null(result);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ArrayCopier_NullInputAndNullContext_ThrowsForContextValidatedFirst()
    {
        // 证明有证据证明,不仅每个警卫独立存在:如果输入
        // 上下文前检查, DeepCopy将走"回归无"的道路
        // 因为上下文首先被验证,它必须还是在这里扔。
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ArrayCopier<int>(elementCopier);

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ListCopier_NullContext_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ListCopier<int>(elementCopier);
        var input = new List<int> { 1, 2, 3 };

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal([1, 2, 3], input);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ListCopier_NullInputWithValidContext_ReturnsNullWithoutInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ListCopier<int>(elementCopier);
        using var context = GetCopyContext();

        var result = sut.DeepCopy(null!, context);

        Assert.Null(result);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ListCopier_NullInputAndNullContext_ThrowsForContextValidatedFirst()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ListCopier<int>(elementCopier);

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void DictionaryCopier_NullContext_ThrowsWithExactParamNameBeforeInvokingElementCopiers()
    {
        var keyCopier = new TrackingCopier<string>(v => v + "-copy");
        var valueCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new DictionaryCopier<string, int>(keyCopier, valueCopier);
        var input = new Dictionary<string, int> { ["a"] = 1 };

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(1, input["a"]);
        Assert.Equal(0, keyCopier.InvocationCount);
        Assert.Equal(0, valueCopier.InvocationCount);
    }

    [Fact]
    public void DictionaryCopier_NullInputWithValidContext_ReturnsNullWithoutInvokingElementCopiers()
    {
        var keyCopier = new TrackingCopier<string>(v => v + "-copy");
        var valueCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new DictionaryCopier<string, int>(keyCopier, valueCopier);
        using var context = GetCopyContext();

        var result = sut.DeepCopy(null!, context);

        Assert.Null(result);
        Assert.Equal(0, keyCopier.InvocationCount);
        Assert.Equal(0, valueCopier.InvocationCount);
    }

    [Fact]
    public void DictionaryCopier_NullInputAndNullContext_ThrowsForContextValidatedFirst()
    {
        var keyCopier = new TrackingCopier<string>(v => v + "-copy");
        var valueCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new DictionaryCopier<string, int>(keyCopier, valueCopier);

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(0, keyCopier.InvocationCount);
        Assert.Equal(0, valueCopier.InvocationCount);
    }

    [Fact]
    public void HashSetCopier_NullContext_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new HashSetCopier<int>(elementCopier);
        var input = new HashSet<int> { 1, 2 };

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(new HashSet<int> { 1, 2 }, input);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void HashSetCopier_NullInputWithValidContext_ReturnsNullWithoutInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new HashSetCopier<int>(elementCopier);
        using var context = GetCopyContext();

        var result = sut.DeepCopy(null!, context);

        Assert.Null(result);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void HashSetCopier_NullInputAndNullContext_ThrowsForContextValidatedFirst()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new HashSetCopier<int>(elementCopier);

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ObjectCopier_NullContext_ThrowsWithExactParamNameBeforeInspectingInput()
    {
        var input = new MyValue(11);

        var exception = Assert.Throws<ArgumentNullException>(() => ObjectCopier.DeepCopy(input, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(11, input.Value);
    }

    [Fact]
    public void ObjectCopier_NullInputWithValidContext_ReturnsNullWithoutThrowing()
    {
        using var context = GetCopyContext();

        var result = ObjectCopier.DeepCopy(null, context);

        Assert.Null(result);
    }

    [Fact]
    public void ObjectCopier_NullInputAndNullContext_ThrowsForContextValidatedFirst()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ObjectCopier.DeepCopy(null, null!));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void ObjectCopier_InterfacePath_NullContext_ThrowsWithExactParamName()
    {
        IDeepCopier<object> sut = new ObjectCopier();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(new MyValue(11), null!));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void ObjectCopier_UntypedInterfacePath_NullInputAndContext_ThrowsForContextValidatedFirst()
    {
        IDeepCopier sut = new ObjectCopier();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null, null!));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void ImmutableArrayCopier_NullContext_ThrowsBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<MyValue>(value => new MyValue(value.Value));
        var sut = new ImmutableArrayCopier<MyValue>(elementCopier);
        var input = ImmutableArray.Create(new MyValue(11));

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void FSharpCopiers_PreserveNullInputsAndNonNullWrappers()
    {
        var objectCopier = new TrackingCopier<object?>(static value => value);
        using var context = GetCopyContext();

        var optionCopier = new FSharpOptionCopier<object?>(objectCopier);
        Assert.Null(optionCopier.DeepCopy(null, context));
        var optionCopy = optionCopier.DeepCopy(FSharpOption<object?>.Some(null), context);
        Assert.NotNull(optionCopy);
        Assert.True(FSharpOption<object?>.get_IsSome(optionCopy));
        Assert.Null(optionCopy.Value);
        var option = FSharpOption<object?>.Some(new object());
        var nonNullOptionCopy = optionCopier.DeepCopy(option, context);
        Assert.NotNull(nonNullOptionCopy);
        Assert.NotSame(option, nonNullOptionCopy);
        Assert.Same(option.Value, nonNullOptionCopy.Value);

        var choiceCopier = new FSharpChoiceCopier<object?, int>(objectCopier, new ShallowCopier<int>());
        Assert.Null(choiceCopier.DeepCopy(null, context));
        var choiceCopy = Assert.IsType<FSharpChoice<object?, int>.Choice1Of2>(
            choiceCopier.DeepCopy(FSharpChoice<object?, int>.NewChoice1Of2(null), context));
        Assert.Null(choiceCopy.Item);

        var referenceCopier = new FSharpRefCopier<object?>(objectCopier);
        Assert.Null(referenceCopier.DeepCopy(null, context));
        var referenceCopy = referenceCopier.DeepCopy(new FSharpRef<object?>(null), context);
        Assert.NotNull(referenceCopy);
        Assert.Null(referenceCopy.Value);

        var listCopier = new FSharpListCopier<object?>(objectCopier);
        Assert.Null(listCopier.DeepCopy(null, context));
        var listCopy = listCopier.DeepCopy(ListModule.OfSeq<object?>([null]), context);
        Assert.NotNull(listCopy);
        Assert.Null(Assert.Single(listCopy));
        var list = ListModule.OfSeq<object?>([new object()]);
        var firstListCopy = listCopier.DeepCopy(list, context);
        var secondListCopy = listCopier.DeepCopy(list, context);
        Assert.NotNull(firstListCopy);
        Assert.Same(firstListCopy, secondListCopy);

        Assert.Null(new FSharpSetCopier<string>(new ShallowCopier<string>()).DeepCopy(null, context));
        Assert.Null(new FSharpMapCopier<string, string>(new ShallowCopier<string>(), new ShallowCopier<string>()).DeepCopy(null, context));
    }

    // ------------------------------------------------------------------------------------
    // 第2组: IBaseCopier<T>.DeepCopy(T投入,T产出, CopyContext 3-弧
    // 复制到超载。经验证的合同:所有三个参数在无效时投出, 在无效之前
    // 输出变异或引用任何元素复印机。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ListCopier_CopyIntoNullInput_ThrowsWithExactParamNameBeforeMutatingOutputOrInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ListCopier<int>(elementCopier);
        var output = new List<int> { 7, 8 };
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, output, context));

        Assert.Equal("input", exception.ParamName);
        Assert.Equal([7, 8], output);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ListCopier_CopyIntoNullOutput_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ListCopier<int>(elementCopier);
        var input = new List<int> { 1, 2, 3 };
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!, context));

        Assert.Equal("output", exception.ParamName);
        Assert.Equal([1, 2, 3], input);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ListCopier_CopyIntoNullContext_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ListCopier<int>(elementCopier);
        var input = new List<int> { 1, 2, 3 };
        var output = new List<int> { 7 };

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, output, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal([7], output);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void ListCopier_CopyIntoAllNull_ThrowsForInputValidatedFirst()
    {
        // 补充上面的2-arg“两个无”订购证明:3arg“复制到”
        // 超载, 所有三个参数都以文件形式被投放, 输入/ 输出/ 文本
        // 命令,而不仅仅是单列。
        // 警卫孤立地存在。
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new ListCopier<int>(elementCopier);

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, null!, null!));

        Assert.Equal("input", exception.ParamName);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void DictionaryCopier_CopyIntoNullInput_ThrowsWithExactParamNameBeforeMutatingOutputOrInvokingElementCopiers()
    {
        var keyCopier = new TrackingCopier<string>(v => v + "-copy");
        var valueCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new DictionaryCopier<string, int>(keyCopier, valueCopier);
        var output = new Dictionary<string, int> { ["existing"] = 99 };
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, output, context));

        Assert.Equal("input", exception.ParamName);
        Assert.Equal(99, output["existing"]);
        Assert.Single(output);
        Assert.Equal(0, keyCopier.InvocationCount);
        Assert.Equal(0, valueCopier.InvocationCount);
    }

    [Fact]
    public void DictionaryCopier_CopyIntoNullOutput_ThrowsWithExactParamNameBeforeInvokingElementCopiers()
    {
        var keyCopier = new TrackingCopier<string>(v => v + "-copy");
        var valueCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new DictionaryCopier<string, int>(keyCopier, valueCopier);
        var input = new Dictionary<string, int> { ["a"] = 1 };
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!, context));

        Assert.Equal("output", exception.ParamName);
        Assert.Equal(1, input["a"]);
        Assert.Equal(0, keyCopier.InvocationCount);
        Assert.Equal(0, valueCopier.InvocationCount);
    }

    [Fact]
    public void DictionaryCopier_CopyIntoNullContext_ThrowsWithExactParamNameBeforeInvokingElementCopiers()
    {
        var keyCopier = new TrackingCopier<string>(v => v + "-copy");
        var valueCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new DictionaryCopier<string, int>(keyCopier, valueCopier);
        var input = new Dictionary<string, int> { ["a"] = 1 };
        var output = new Dictionary<string, int> { ["existing"] = 99 };

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, output, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(99, output["existing"]);
        Assert.Single(output);
        Assert.Equal(0, keyCopier.InvocationCount);
        Assert.Equal(0, valueCopier.InvocationCount);
    }

    [Fact]
    public void HashSetCopier_CopyIntoNullInput_ThrowsWithExactParamNameBeforeMutatingOutputOrInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new HashSetCopier<int>(elementCopier);
        var output = new HashSet<int> { 9 };
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, output, context));

        Assert.Equal("input", exception.ParamName);
        Assert.Equal(new HashSet<int> { 9 }, output);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void HashSetCopier_CopyIntoNullOutput_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new HashSetCopier<int>(elementCopier);
        var input = new HashSet<int> { 1, 2, 3 };
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!, context));

        Assert.Equal("output", exception.ParamName);
        Assert.Equal(new HashSet<int> { 1, 2, 3 }, input);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void HashSetCopier_CopyIntoNullContext_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new HashSetCopier<int>(elementCopier);
        var input = new HashSet<int> { 1, 2, 3 };
        var output = new HashSet<int> { 9 };

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, output, null!));

        Assert.Equal("context", exception.ParamName);
        Assert.Equal(new HashSet<int> { 9 }, output);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void StackCopier_CopyIntoNullInput_ThrowsWithExactParamNameBeforeMutatingOutput()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new StackCopier<int>(elementCopier);
        var output = new Stack<int>();
        output.Push(9);
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(null!, output, context));

        Assert.Equal("input", exception.ParamName);
        Assert.Equal(new[] { 9 }, output);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    [Fact]
    public void QueueCopier_CopyIntoNullOutput_ThrowsWithExactParamNameBeforeInvokingElementCopier()
    {
        var elementCopier = new TrackingCopier<int>(v => v + 1000);
        var sut = new QueueCopier<int>(elementCopier);
        var input = new Queue<int>(new[] { 1, 2 });
        using var context = GetCopyContext();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.DeepCopy(input, null!, context));

        Assert.Equal("output", exception.ParamName);
        Assert.Equal(new[] { 1, 2 }, input);
        Assert.Equal(0, elementCopier.InvocationCount);
    }

    // ------------------------------------------------------------------------------------
    // 第3组:独立帮手类型/会期边界。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ReferenceCodec_MarkValueField_NullSession_ThrowsWithExactParamName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ReferenceCodec.MarkValueField(null!));
        Assert.Equal("session", exception.ParamName);
    }

    [Fact]
    public void ReferenceCodec_RecordObject_NullSession_ThrowsWithExactParamName()
    {
        var value = new MyValue(3);
        var exception = Assert.Throws<ArgumentNullException>(() => ReferenceCodec.RecordObject(null!, value));
        Assert.Equal("session", exception.ParamName);
        Assert.Equal(3, value.Value);
    }

    [Fact]
    public void ReferenceCodec_RecordObjectWithReferenceId_NullSession_ThrowsWithExactParamName()
    {
        var value = new MyValue(3);
        var exception = Assert.Throws<ArgumentNullException>(() => ReferenceCodec.RecordObject(null!, value, 1u));
        Assert.Equal("session", exception.ParamName);
        Assert.Equal(3, value.Value);
    }

    [Fact]
    public void ReferenceCodec_CreateRecordPlaceholder_NullSession_ThrowsWithExactParamName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ReferenceCodec.CreateRecordPlaceholder(null!));
        Assert.Equal("session", exception.ParamName);
    }

    [Fact]
    public void CommonCodecTypeFilter_IsAbstractOrFrameworkType_NullType_ThrowsWithExactParamName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CommonCodecTypeFilter.IsAbstractOrFrameworkType(null!));
        Assert.Equal("type", exception.ParamName);
    }

    [Fact]
    public void WellKnownStringComparerCodec_IsSupportedType_NullType_ThrowsWithExactParamName()
    {
        var sut = new WellKnownStringComparerCodec();

        var exception = Assert.Throws<ArgumentNullException>(() => sut.IsSupportedType(null!));

        Assert.Equal("type", exception.ParamName);
    }

    // ------------------------------------------------------------------------------------
    // 第4组: IBaseCodec<T>. 定序/降序边界,直接调用(通过)
    // WriteField的参考短路短路路。可验证:警卫是第一个
    // 语句,因此没有页眉字节的文字,也没有消耗读者字节。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ListCodec_Serialize_NullValue_ThrowsWithExactParamNameBeforeWritingAnyBytes()
    {
        var sut = GetConcreteCodec<List<int>, ListCodec<int>>();
        var buffer = new TestMultiSegmentBufferWriter(maxAllocationSize: 16);
        using var session = SessionPool.GetSession();
        var writer = Writer.Create(buffer, session);

        // Writer<TBufferWriter> 是ref 支架, 所以“ ref” 调用无法从内部
        // lambda (Assert.Throws(行动)需要将其捕获在封闭中);使用 a
        // 改为明示试捕/捕获。
        ArgumentNullException? exception = null;
        try
        {
            sut.Serialize(ref writer, null!);
        }
        catch (ArgumentNullException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        Assert.Equal("value", exception!.ParamName);
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void ListCodec_Deserialize_NullValue_ThrowsWithExactParamNameBeforeReadingAnyBytes()
    {
        var sut = GetConcreteCodec<List<int>, ListCodec<int>>();
        using var session = SessionPool.GetSession();
        var reader = Reader.Create(Array.Empty<byte>(), session);
        var positionBefore = reader.Position;

        ArgumentNullException? exception = null;
        try
        {
            sut.Deserialize(ref reader, null!);
        }
        catch (ArgumentNullException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        Assert.Equal("value", exception!.ParamName);
        Assert.Equal(positionBefore, reader.Position);
    }

    [Fact]
    public void HashSetCodec_Serialize_NullValue_ThrowsWithExactParamNameBeforeWritingAnyBytes()
    {
        var sut = GetConcreteCodec<HashSet<int>, HashSetCodec<int>>();
        var buffer = new TestMultiSegmentBufferWriter(maxAllocationSize: 16);
        using var session = SessionPool.GetSession();
        var writer = Writer.Create(buffer, session);

        ArgumentNullException? exception = null;
        try
        {
            sut.Serialize(ref writer, null!);
        }
        catch (ArgumentNullException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        Assert.Equal("value", exception!.ParamName);
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void CollectionCodec_Deserialize_NullValue_ThrowsWithExactParamNameBeforeReadingAnyBytes()
    {
        var sut = GetConcreteCodec<Collection<int>, CollectionCodec<int>>();
        using var session = SessionPool.GetSession();
        var reader = Reader.Create(Array.Empty<byte>(), session);
        var positionBefore = reader.Position;

        ArgumentNullException? exception = null;
        try
        {
            sut.Deserialize(ref reader, null!);
        }
        catch (ArgumentNullException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        Assert.Equal("value", exception!.ParamName);
        Assert.Equal(positionBefore, reader.Position);
    }

    // ------------------------------------------------------------------------------------
    // 组 5: ReferenceCodec 将无效有效载荷写成无效符号,作为在
    // 代码编码访问值。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ArrayCodec_WriteFieldNullValue_DoesNotThrowAndReadsBackAsNull()
    {
        var codec = GetConcreteCodec<int[], ArrayCodec<int>>();
        var buffer = new TestMultiSegmentBufferWriter(maxAllocationSize: 16);

        using (var writerSession = SessionPool.GetSession())
        {
            var writer = Writer.Create(buffer, writerSession);
            codec.WriteField(ref writer, 0, typeof(int[]), null);
            writer.Commit();
            Assert.True(writer.Position > 0, "Writing a null reference should still emit a null-reference token.");
        }

        using var readerSession = SessionPool.GetSession();
        var reader = Reader.Create(buffer.GetReadOnlySequence(maxSegmentSize: 16), readerSession);
        var field = reader.ReadFieldHeader();

        var result = codec.ReadValue(ref reader, field);

        Assert.Null(result);
    }

    [Fact]
    public void DictionaryCodec_WriteFieldNullValue_DoesNotThrowAndReadsBackAsNull()
    {
        var codec = GetConcreteCodec<Dictionary<string, int>, DictionaryCodec<string, int>>();
        var buffer = new TestMultiSegmentBufferWriter(maxAllocationSize: 16);

        using (var writerSession = SessionPool.GetSession())
        {
            var writer = Writer.Create(buffer, writerSession);
            codec.WriteField(ref writer, 0, typeof(Dictionary<string, int>), null);
            writer.Commit();
            Assert.True(writer.Position > 0, "Writing a null reference should still emit a null-reference token.");
        }

        using var readerSession = SessionPool.GetSession();
        var reader = Reader.Create(buffer.GetReadOnlySequence(maxSegmentSize: 16), readerSession);
        var field = reader.ReadFieldHeader();

        var result = codec.ReadValue(ref reader, field);

        Assert.Null(result);
    }

    [Fact]
    public void ListCodec_NullPayload_RoundTripsAsNullViaSerializer()
    {
        var result = RoundTripSerialize<List<int>>(null!, out var payloadLength);

        Assert.True(payloadLength > 0, "A null-reference token should still be written to the wire.");
        Assert.Null(result);
    }

    [Fact]
    public void HashSetCodec_NullPayload_RoundTripsAsNullViaSerializer()
    {
        var result = RoundTripSerialize<HashSet<int>>(null!, out var payloadLength);

        Assert.True(payloadLength > 0, "A null-reference token should still be written to the wire.");
        Assert.Null(result);
    }

    // ------------------------------------------------------------------------------------
    // 第6组:有效、有代表性的非空集装箱仍深影印和圆环
    // 通过参数验证的编码/复印机正确无误。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ArrayCodec_NonEmptyIntArray_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = new[] { 1, -2, 3, int.MaxValue };

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ArrayCodec_NonEmptyStringArray_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = new[] { "alpha", "beta", "gamma" };

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ListCodec_NonEmptyList_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = new List<int> { 10, 20, 30 };

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void CollectionCodec_NonEmptyCollection_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = new Collection<string> { "first", "second" };

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void DictionaryCodec_NonEmptyDictionary_DeepCopiesAndRoundTripsAllEntries()
    {
        var original = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void HashSetCodec_NonEmptySet_DeepCopiesAndRoundTripsAllElements()
    {
        var original = new HashSet<int> { 5, 6, 7 };

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void QueueCodec_NonEmptyQueue_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = new Queue<int>(new[] { 1, 2, 3 });

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void StackCodec_NonEmptyStack_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = new Stack<int>();
        original.Push(1);
        original.Push(2);
        original.Push(3);

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.NotSame(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ImmutableListCodec_NonEmptyImmutableList_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = ImmutableList.Create(1, 2, 3);

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ImmutableDictionaryCodec_NonEmptyImmutableDictionary_DeepCopiesAndRoundTripsAllEntries()
    {
        var original = ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, int>("a", 1),
            new KeyValuePair<string, int>("b", 2),
        });

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ImmutableStackCodec_NonEmptyImmutableStack_DeepCopiesAndRoundTripsElementsInOrder()
    {
        var original = ImmutableStack.Create(1, 2, 3);

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void TupleCodec_TwoTuple_DeepCopiesAndRoundTripsBothItems()
    {
        var original = Tuple.Create(42, "answer");

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        // 图<T1, T2> 本身是不可变的, 内字和弦都是浅可读的, 所以
        // TupleCopier的 IsShallowCopyable() 快速路径合法返回相同情况
        // 而不是分配新的一个—— 这是正确的行为, 不是遗漏的复制件。
        Assert.Same(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void TupleCodec_ThreeTuple_DeepCopiesAndRoundTripsAllItems()
    {
        var original = Tuple.Create(1, "two", true);

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        // 与两个图例相同的理由理由:内注/弦/布尔均可浅读,因此
        // 永久图普<T1,T2,T3> 与原样合法归还。
        Assert.Same(original, copy);
        Assert.Equal(original, copy);
        Assert.Equal(original, roundTripped);
    }

    // ------------------------------------------------------------------------------------
    // 第7组:必须保留假名和周期,并保持其深度复制和序列。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ListCodec_AliasedElements_PreserveReferenceIdentityAfterDeepCopy()
    {
        var shared = new MyValue(42);
        var original = new List<MyValue> { shared, shared };

        var copy = DeepCopyValue(original);

        Assert.NotSame(shared, copy[0]);
        Assert.Same(copy[0], copy[1]);
        Assert.Equal(42, copy[0].Value);
    }

    [Fact]
    public void ListCodec_AliasedElements_PreserveReferenceIdentityAfterSerializerRoundTrip()
    {
        var shared = new MyValue(42);
        var original = new List<MyValue> { shared, shared };

        var result = RoundTripSerialize(original);

        Assert.Same(result[0], result[1]);
        Assert.Equal(42, result[0].Value);
    }

    [Fact]
    public void ListCodec_SelfReferencingElement_PreservesCycleAfterDeepCopyAndSerializerRoundTrip()
    {
        var node = new RecursiveClass { IntProperty = 7 };
        node.RecursiveProperty = node;
        var original = new List<RecursiveClass> { node };

        var copy = DeepCopyValue(original);
        var roundTripped = RoundTripSerialize(original);

        Assert.Same(copy[0], copy[0].RecursiveProperty);
        Assert.Equal(7, copy[0].IntProperty);
        Assert.Same(roundTripped[0], roundTripped[0].RecursiveProperty);
        Assert.Equal(7, roundTripped[0].IntProperty);
    }

    // ------------------------------------------------------------------------------------
    // 第8组:多形态/参考行为必须通过对象类型保持完整
    // 收藏( 执行 ObjectCodec的快递和 ObjectCopier无效返回路径) 。
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ListOfObjectCodec_MixedRuntimeTypes_PreservesTypesAndValuesAfterDeepCopy()
    {
        var original = new List<object> { 42, "hello", new MyValue(9) };

        var copy = DeepCopyValue(original);

        Assert.Equal(42, Assert.IsType<int>(copy[0]));
        Assert.Equal("hello", Assert.IsType<string>(copy[1]));
        var copiedValue = Assert.IsType<MyValue>(copy[2]);
        Assert.Equal(9, copiedValue.Value);
        Assert.NotSame(original[2], copy[2]);
    }

    [Fact]
    public void ListOfObjectCodec_MixedRuntimeTypes_PreservesTypesAndValuesAfterSerializerRoundTrip()
    {
        var original = new List<object> { 42, "hello", new MyValue(9) };

        var result = RoundTripSerialize(original);

        Assert.Equal(42, Assert.IsType<int>(result[0]));
        Assert.Equal("hello", Assert.IsType<string>(result[1]));
        var resultValue = Assert.IsType<MyValue>(result[2]);
        Assert.Equal(9, resultValue.Value);
    }
}
