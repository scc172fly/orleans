using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Xunit;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 序列化测试助手的基础类。
    /// </summary>
    [ExcludeFromCodeCoverage]
    public abstract class SerializationTester : IDisposable
    {
        private readonly bool _ownsServiceProvider;
        private readonly Lazy<IServiceProvider>? _serviceProvider;
        private readonly SerializationTesterFixture? _fixture;
        private int _disposed;

        /// <summary>
        /// 初始化一个新的 <see cref="SerializationTester"/> 实例。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="output"/> 是 <see langword="null"/>.</exception>
        protected SerializationTester(ITestOutputHelper output)
        {
            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            RandomSeed = CreateRandomSeed();
            Random = new(RandomSeed);
            _serviceProvider = new(CreateServiceProvider);
            _ownsServiceProvider = true;
        }

        /// <summary>
        /// 初始化一个新的 <see cref="SerializationTester"/> 实例。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="output"/> 或 <paramref name="fixture"/> 是 <see langword="null"/>。</exception>
        protected SerializationTester(ITestOutputHelper output, SerializationTesterFixture fixture)
        {
            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (fixture is null)
            {
                throw new ArgumentNullException(nameof(fixture));
            }

            RandomSeed = CreateRandomSeed();
            Random = new(RandomSeed);
            _fixture = fixture;
            fixture.SetServiceProviderFactory(this);
        }

        private static int CreateRandomSeed()
        {
#if NET6_0_OR_GREATER
            return Random.Shared.Next();
#else
            return new Random().Next();
#endif
        }

        /// <summary>
        /// 随机获得数字生成器。
        /// </summary>
        protected Random Random { get; }

        internal int RandomSeed { get; }

        /// <summary>
        /// 得到了服务供应商。
        /// </summary>
        protected IServiceProvider ServiceProvider
        {
            get
            {
                if (_fixture is null)
                {
                    return _serviceProvider!.Value;
                }

                var serviceProvider = _fixture.ServiceProvider;
                GC.KeepAlive(this);
                return serviceProvider;
            }
        }

        /// <summary>
        /// 创建此测试类的序列器服务供应商。
        /// </summary>
        protected abstract IServiceProvider CreateServiceProvider();

        internal IServiceProvider CreateFixtureServiceProvider()
        {
            ThrowIfDisposed();
            return CreateServiceProvider();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        /// <summary>
        /// 释放本例所使用的资源。
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _ownsServiceProvider && _serviceProvider!.IsValueCreated)
            {
                (_serviceProvider.Value as IDisposable)?.Dispose();
            }
        }

        /// <inheritdoc/>
        void IDisposable.Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 定着物,拥有一个序列器服务提供商 由所有试验级的 情况共享。
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class SerializationTesterFixture : IDisposable
    {
        private readonly object _lock = new();
        private IServiceProvider? _serviceProvider;
        private WeakReference<SerializationTester>? _serviceProviderFactory;
        private bool _isCreatingServiceProvider;

        /// <summary>
        /// 初始化一个新的 <see cref="SerializationTesterFixture"/> 实例。
        /// </summary>
        public SerializationTesterFixture()
        {
        }

        /// <summary>
        /// 获取由测试者使用此固定装置的验证实例共享的服务供应商。
        /// 在创建之前,最新的测试器提供服务提供者配置。
        /// </summary>
        /// <exception cref="InvalidOperationException">没有测试器可以创建服务提供人,或者服务提供人工厂可以反复进入该属性。</exception>
        /// <exception cref="ObjectDisposedException">可用于创建服务供应商的测试器已经处理。</exception>
        public IServiceProvider ServiceProvider
        {
            get
            {
                lock (_lock)
                {
                    if (_serviceProvider is { } serviceProvider)
                    {
                        return serviceProvider;
                    }

                    if (_isCreatingServiceProvider)
                    {
                        throw new InvalidOperationException("The service provider factory cannot access the service provider while it is being created.");
                    }

                    if (_serviceProviderFactory is null || !_serviceProviderFactory.TryGetTarget(out var tester))
                    {
                        throw new InvalidOperationException("The serialization tester which configures the service provider is no longer available.");
                    }

                    _isCreatingServiceProvider = true;
                    try
                    {
                        return _serviceProvider = tester.CreateFixtureServiceProvider();
                    }
                    finally
                    {
                        _isCreatingServiceProvider = false;
                    }
                }
            }
        }

        internal void SetServiceProviderFactory(SerializationTester tester)
        {
            lock (_lock)
            {
                if (_serviceProvider is not null)
                {
                    return;
                }

                if (_serviceProviderFactory is null)
                {
                    _serviceProviderFactory = new(tester);
                }
                else
                {
                    _serviceProviderFactory.SetTarget(tester);
                }
            }
        }

        /// <summary>
        /// 释放本例所使用的资源。
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            IServiceProvider? serviceProvider;
            lock (_lock)
            {
                serviceProvider = _serviceProvider;
            }

            (serviceProvider as IDisposable)?.Dispose();
        }

        /// <inheritdoc/>
        void IDisposable.Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

    }
}
