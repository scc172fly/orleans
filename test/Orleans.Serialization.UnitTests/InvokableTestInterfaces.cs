using Orleans.Serialization.Cloning;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.UnitTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public interface IHasNoNamespace : IMyInvokableBaseType
{
}

namespace Orleans.Serialization.UnitTests
{
    [Alias("_my_proxy_base_")]
    [DefaultInvokableBaseType(typeof(ValueTask<>), typeof(UnitTestRequest<>))]
    [DefaultInvokableBaseType(typeof(ValueTask), typeof(UnitTestRequest))]
    [DefaultInvokableBaseType(typeof(Task<>), typeof(UnitTestTaskRequest<>))]
    [DefaultInvokableBaseType(typeof(Task), typeof(UnitTestTaskRequest))]
    [DefaultInvokableBaseType(typeof(void), typeof(UnitTestVoidRequest))]
    public abstract class MyInvokableProxyBase
    {
        public Action<IInvokable>? OnInvoke { get; set; }

        protected MyInvokableProxyBase(CopyContextPool copyContextPool, CodecProvider codecProvider)
        {
            CopyContextPool = copyContextPool;
            CodecProvider = codecProvider;
        }

        protected TInvokable GetInvokable<TInvokable>() where TInvokable : class, IInvokable, new() => InvokablePool.Get<TInvokable>();

        protected ValueTask<T> InvokeAsync<T>(IInvokable body)
        {
            OnInvoke?.Invoke(body);
            return default;
        }

        protected ValueTask InvokeAsync(IInvokable body)
        {
            OnInvoke?.Invoke(body);
            return default;
        }

        protected CopyContextPool CopyContextPool { get; }

        protected CodecProvider CodecProvider { get; }
    }

    [Alias("groan")]
    [DefaultInvokableBaseType(typeof(ValueTask<>), typeof(UnitTestRequest<>))]
    [DefaultInvokableBaseType(typeof(ValueTask), typeof(UnitTestRequest))]
    [DefaultInvokableBaseType(typeof(Task<>), typeof(UnitTestTaskRequest<>))]
    [DefaultInvokableBaseType(typeof(Task), typeof(UnitTestTaskRequest))]
    [DefaultInvokableBaseType(typeof(void), typeof(UnitTestVoidRequest))]
    public abstract class AltInvokableProxyBase
    {
        protected TInvokable GetInvokable<TInvokable>() where TInvokable : class, IInvokable, new() => InvokablePool.Get<TInvokable>();

        protected ValueTask<T> InvokeAsync<T>(IInvokable body) => default;

        protected ValueTask InvokeAsync(IInvokable body) => default;

        protected CopyContextPool? CopyContextPool { get; }

        protected CodecProvider? CodecProvider { get; }
    }

    [Alias("my_interface")]
    [GenerateMethodSerializers(typeof(MyInvokableProxyBase))]
    public interface IMyInvokableBaseType
    {
    }

    [Alias("_proxy_alias_test_")]
    [GenerateMethodSerializers(typeof(MyInvokableProxyBase))]
    public interface IProxyAliasTestGrain
    {
        [Id(125)]
        ValueTask Method();

        [Alias("Method")]
        ValueTask Method(int a);

        [Alias("StringMethod")]
        ValueTask Method(string a);

        [Alias("MyOtherMethod")]
        ValueTask OtherMethod();
    }

    [Alias("test.IGenericProxyAliasTestGrain`3"), GenerateMethodSerializers(typeof(MyInvokableProxyBase))]
    public interface IGenericProxyAliasTestGrain<T, U, V>
    {
        [Id(777)]
        ValueTask Method<W, X, Y>();
    }

    public interface IG2<T1, T2> : IMyInvokableBaseType
    { }

    public class HalfOpenGrain1<T> : IG2<T, int>
    { }
    public class HalfOpenGrain2<T> : IG2<int, T>
    { }

    public class OpenGeneric<T2, T1> : IG2<T2, T1>
    { }

    public class ClosedGeneric : IG2<Dummy1, Dummy2>
    { }

    public class ClosedGenericWithManyInterfaces : IG2<Dummy1, Dummy2>, IG2<Dummy2, Dummy1>
    { }

    public class Dummy1 { }

    public class Dummy2 { }

    public interface IG<T> : IMyInvokableBaseType
    {
    }

    public class G1<T1, T2, T3, T4> : Root<T1>.IA<T2, T3, T4>
    {
    }

    public class Root<TRoot>
    {
        public interface IA<T1, T2, T3> : IMyInvokableBaseType
        {

        }

        public class G<T1, T2, T3> : IG<IA<T1, T2, T3>>
        {
        }
    }

    public interface IGrainWithGenericMethods : IMyInvokableBaseType
    {
        Task<Type[]> GetTypesExplicit<T, U, V>();
        Task<Type[]> GetTypesInferred<T, U, V>(T t, U u, V v);
        Task<Type[]> GetTypesInferred<T, U>(T t, U u, int v);
        Task<T> RoundTrip<T>(T val);
        Task<int> RoundTrip(int val);
        Task<T?> Default<T>();
        Task<string> Default();
        Task<TGrain> Constraints<TGrain>(TGrain grain) where TGrain : IMyInvokableBaseType;
        ValueTask<int> ValueTaskMethod(bool useCache);
    }

    public class GrainWithGenericMethods : IGrainWithGenericMethods
    {
        private object? state;

        public Task<Type[]> GetTypesExplicit<T, U, V>()
        {
            return Task.FromResult(new[] { typeof(T), typeof(U), typeof(V) });
        }

        public Task<Type[]> GetTypesInferred<T, U, V>(T t, U u, V v)
        {
            return Task.FromResult(new[] { typeof(T), typeof(U), typeof(V) });
        }

        public Task<Type[]> GetTypesInferred<T, U>(T t, U u, int v)
        {
            return Task.FromResult(new[] { typeof(T), typeof(U) });
        }

        public Task<T> RoundTrip<T>(T val)
        {
            return Task.FromResult(val);
        }

        public Task<int> RoundTrip(int val)
        {
            return Task.FromResult(-val);
        }

        public Task<T?> Default<T>()
        {
            return Task.FromResult(default(T));
        }

        public Task<string> Default()
        {
            return Task.FromResult("default string");
        }

        public Task<TGrain> Constraints<TGrain>(TGrain grain) where TGrain : IMyInvokableBaseType
        {
            return Task.FromResult(grain);
        }

        public void SetValue<T>(T? value)
        {
            this.state = value;
        }

        public Task<T?> GetValue<T>() => Task.FromResult((T?)this.state);

        public ValueTask<int> ValueTaskMethod(bool useCache)
        {
            if (useCache)
            {
                return new ValueTask<int>(1);
            }

            return new ValueTask<int>(Task.FromResult(2));
        }
    }

    public interface IGenericGrainWithGenericMethods<T> : IMyInvokableBaseType
    {
        Task<T> Method(T value);
#pragma warning disable 693
        Task<T> Method<T>(T value);
#pragma warning restore 693
    }

    public interface IRuntimeCodeGenGrain<T> : IMyInvokableBaseType
    {
        /// <summary>
        /// 设置并返回 Grain 的状态。
        /// </summary>
        /// <param name="value">新状态。</param>
        /// <returns>当前状态。</returns>
        Task<T> SetState(T value);

        /// <summary>
        /// 测试代码生成能否正确处理使用保留关键字作为标识符的方法。
        /// </summary>
        /// <returns>当前状态的 event。</returns>
        Task<@event> @static();
    }

    [Serializable]
    [GenerateSerializer]
    public class GenericGrainState<T>
    {
        [Id(1)]
        public T? @event { get; set; }
    }

    /// <summary>
    /// 用于测试代码生成能否正确处理保留关键字的类。
    /// </summary>
    [GenerateSerializer]
    public class @event : IEquatable<@event?>
    {
        private static readonly IEqualityComparer<@event> EventComparerInstance = new EventEqualityComparer();

        public enum @enum
        {
            @async,
            @int,
        }

        /// <summary>
        /// 公共字段。
        /// </summary>
        [Id(0)]
        public Guid Id;

        /// <summary>
        /// 私有字段。
        /// </summary>
        [Id(1)]
        private Guid privateId;

        /// <summary>
        /// 类型和标识符均使用保留关键字的属性。
        /// </summary>
        [Id(2)]
        public @event? @public { get; set; }

        /// <summary>
        /// 获取或设置枚举。
        /// </summary>
        [Id(3)]
        public @enum Enum { get; set; }

        /// <summary>
        /// 泛型类型和标识符均使用保留关键字的属性。
        /// </summary>
        [Id(4)]
        public List<@event>? @if { get; set; }

        public static IEqualityComparer<@event> EventComparer
        {
            get
            {
                return EventComparerInstance;
            }
        }

        /// <summary>
        /// 获取或设置私有 id。
        /// </summary>
        // ReSharper disable once ConvertToAutoProperty
        public Guid PrivateId
        {
            get
            {
                return this.privateId;
            }

            set
            {
                this.privateId = value;
            }
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            if (obj.GetType() != this.GetType())
            {
                return false;
            }

            return this.Equals((@event)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (this.@if != null ? this.@if.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (this.@public != null ? this.@public.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ this.privateId.GetHashCode();
                hashCode = (hashCode * 397) ^ this.Id.GetHashCode();
                return hashCode;
            }
        }

        public bool Equals(@event? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (this.@if != other.@if)
            {
                // 用于相等性比较的序列化输入会保留 event 列表是否存在的信息。
                if (this.@if != null && !this.@if.SequenceEqual(other.@if!, EventComparer))
                {
                    return false;
                }
            }

            if (!Equals(this.@public, other.@public))
            {
                if (this.@public != null && !this.@public.Equals(other.@public))
                {
                    return false;
                }
            }

            return this.privateId.Equals(other.privateId) && this.Id.Equals(other.Id) && this.Enum == other.Enum;
        }

        private sealed class EventEqualityComparer : IEqualityComparer<@event>
        {
            public bool Equals(@event? x, @event? y)
            {
                // 本测试中的列表声明其 event 元素不可为 null。
                return x!.Equals(y);
            }

            public int GetHashCode(@event obj)
            {
                return obj.GetHashCode();
            }
        }
    }

    [GenerateSerializer]
    public class NestedGeneric<T>
    {
        [Id(0)]
        public Nested? Payload { get; set; }

        [GenerateSerializer]
        public class Nested
        {
            [Id(0)]
            public T? Value { get; set; }
        }
    }

    [GenerateSerializer]
    public class NestedConstructedGeneric
    {
        [Id(0)]
        public Nested<int>? Payload { get; set; }

        [GenerateSerializer]
        public class Nested<T>
        {
            [Id(0)]
            public T? Value { get; set; }
        }
    }

    public interface INestedGenericGrain : IMyInvokableBaseType
    {
        Task<int> Do(NestedGeneric<int> value);
        Task<int> Do(NestedConstructedGeneric value);
    }

    /// <summary>
    /// 测试嵌套类不会导致代码生成失败。
    /// </summary>
    public class NestedGenericGrain : INestedGenericGrain
    {
        public Task<int> Do(NestedGeneric<int> value)
        {
            // 本测试中生成的请求始终提供已初始化的负载。
            return Task.FromResult(value.Payload!.Value);
        }

        public Task<int> Do(NestedConstructedGeneric value)
        {
            // 本测试中生成的请求始终提供已初始化的负载。
            return Task.FromResult(value.Payload!.Value);
        }
    }
}
