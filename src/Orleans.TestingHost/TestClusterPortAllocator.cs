using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Orleans.TestingHost;

/// <summary>
/// <see cref="ITestClusterPortAllocator"/> 的默认实现，尝试分配未使用的端口。
/// </summary>
public class TestClusterPortAllocator : ITestClusterPortAllocator
{
    internal const int GatewayPortRangeStart = 12_000;
    internal const int GatewayPortRangeEnd = 22_000;
    internal const int SiloPortRangeStart = 22_300;
    internal const int SiloPortRangeEnd = 30_000;

    private bool _disposed;
    private readonly Lock _lockObj = new();
    private readonly Dictionary<int, string> _allocatedPorts = [];

    /// <inheritdoc />
    public (int, int) AllocateConsecutivePortPairs(int numPorts = 5)
    {
        // 检查系统当前的 TCP 连接。
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();

        return AllocateConsecutivePortPairs(
            tcpConnInfoArray,
            numPorts,
            SiloPortRangeStart,
            SiloPortRangeEnd,
            GatewayPortRangeStart,
            GatewayPortRangeEnd);
    }

    internal (int, int) AllocateConsecutivePortPairs(
        IPEndPoint[] tcpConnInfoArray,
        int numPorts,
        int siloPortRangeStart,
        int siloPortRangeEnd,
        int gatewayPortRangeStart,
        int gatewayPortRangeEnd)
    {
        // 在受支持的操作系统上，这些服务端端口范围低于默认的动态客户端端口范围。
        var siloPort = GetAvailableConsecutiveServerPorts(tcpConnInfoArray, siloPortRangeStart, siloPortRangeEnd, numPorts);
        try
        {
            var gatewayPort = GetAvailableConsecutiveServerPorts(tcpConnInfoArray, gatewayPortRangeStart, gatewayPortRangeEnd, numPorts);
            return (siloPort, gatewayPort);
        }
        catch
        {
            ReleaseAllocatedPorts(siloPort, numPorts);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 释放非托管资源，并根据需要释放托管资源。
    /// </summary>
    /// <param name="disposing"><see langword="true" /> 表示释放托管和非托管资源；<see langword="false" /> 表示仅释放非托管资源。</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        lock (_lockObj)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var pair in _allocatedPorts)
            {
                MutexManager.Instance.SignalRelease(pair.Value);
            }

            _allocatedPorts.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// 对 <see cref="TestClusterPortAllocator"/> 类的实例执行终结操作。
    /// </summary>
    ~TestClusterPortAllocator()
    {
        Dispose(false);
    }

    internal int GetAvailableConsecutiveServerPorts(IPEndPoint[] tcpConnInfoArray, int portStartRange, int portEndRange, int consecutivePortsToCheck)
    {
        const int MaxAttempts = 100;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutivePortsToCheck);
        var firstBasePort = portStartRange;
        var remainder = firstBasePort % consecutivePortsToCheck;
        if (remainder != 0)
        {
            firstBasePort += consecutivePortsToCheck - remainder;
        }

        var bucketCount = (portEndRange - firstBasePort) / consecutivePortsToCheck;
        if (bucketCount <= 0)
        {
            throw new ArgumentException("The requested port range cannot contain the required consecutive ports.");
        }

        var stopwatch = Stopwatch.StartNew();
        var allocations = new List<(int Port, string Mutex)>();
        var listenerRejections = 0;
        var socketRejections = 0;
        var localReservationRejections = 0;
        var externalReservationRejections = 0;
        var reservationErrors = 0;
        int? lastListenerPort = null;
        (int Port, SocketError SocketError, int NativeErrorCode)? lastSocketFailure = null;
        int? lastLocallyReservedPort = null;
        int? lastExternallyReservedPort = null;
        Exception? lastReservationError = null;

        for (var attempts = 0; attempts < MaxAttempts; attempts++)
        {
            // 使用对齐的分桶，使并行分配器探测互不重叠的连续范围。
            var basePort = firstBasePort + Random.Shared.Next(bucketCount) * consecutivePortsToCheck;
            var endPort = basePort + consecutivePortsToCheck;

            // 确保该子范围中的端口均未被占用。
            var activeListener = tcpConnInfoArray.FirstOrDefault(endpoint => endpoint.Port >= basePort && endpoint.Port < endPort);
            if (activeListener is not null)
            {
                listenerRejections++;
                lastListenerPort = activeListener.Port;
                continue;
            }

            var portsAvailable = true;
            for (var i = 0; i < consecutivePortsToCheck; i++)
            {
                var port = basePort + i;
                try
                {
                    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                }
                catch (SocketException exception)
                {
                    socketRejections++;
                    lastSocketFailure = (port, exception.SocketErrorCode, exception.NativeErrorCode);
                    portsAvailable = false;
                    break;
                }
            }

            if (!portsAvailable)
            {
                continue;
            }

            for (var i = 0; i < consecutivePortsToCheck; i++)
            {
                var port = basePort + i;
                var name = $"Global.TestCluster.{port.ToString(CultureInfo.InvariantCulture)}";
                MutexAcquisition acquisition;
                try
                {
                    acquisition = MutexManager.Instance.Acquire(name);
                }
                catch
                {
                    ReleaseAllocations(allocations);
                    throw;
                }

                if (acquisition.Result == MutexAcquisitionResult.Acquired)
                {
                    allocations.Add((port, name));
                }
                else
                {
                    if (acquisition.Result == MutexAcquisitionResult.HeldByCurrentProcess)
                    {
                        localReservationRejections++;
                        lastLocallyReservedPort = port;
                    }
                    else if (acquisition.Result == MutexAcquisitionResult.HeldByAnotherProcess)
                    {
                        externalReservationRejections++;
                        lastExternallyReservedPort = port;
                    }
                    else
                    {
                        reservationErrors++;
                        lastReservationError = acquisition.Error;
                    }

                    ReleaseAllocations(allocations);
                    break;
                }
            }

            if (allocations.Count == 0)
            {
                // 尝试其他范围。
                continue;
            }

            lock (_lockObj)
            {
                if (_disposed)
                {
                    ReleaseAllocations(allocations);
                    throw new ObjectDisposedException(nameof(TestClusterPortAllocator));
                }

                foreach (var allocation in allocations)
                {
                    _allocatedPorts[allocation.Port] = allocation.Mutex;
                }
            }

            return basePort;
        }

        var lastSocketFailureDescription = lastSocketFailure is { } socketFailure
            ? $"{socketFailure.Port} ({socketFailure.SocketError}, native error {socketFailure.NativeErrorCode})"
            : "none";
        var lastReservationErrorDescription = lastReservationError is { } reservationError
            ? $"{reservationError.GetType().Name}: {reservationError.Message}"
            : "none";
        throw new InvalidOperationException(
            $"Cannot find {consecutivePortsToCheck} consecutive free ports in range [{portStartRange}, {portEndRange}) after {MaxAttempts} attempts in {stopwatch.ElapsedMilliseconds} ms. "
            + $"Rejected candidate ranges: active TCP listener={listenerRejections} (last port: {lastListenerPort?.ToString(CultureInfo.InvariantCulture) ?? "none"}), "
            + $"socket bind failure={socketRejections} (last failure: {lastSocketFailureDescription}), "
            + $"reservation held by this process={localReservationRejections} (last port: {lastLocallyReservedPort?.ToString(CultureInfo.InvariantCulture) ?? "none"}), "
            + $"reservation held by another process={externalReservationRejections} (last port: {lastExternallyReservedPort?.ToString(CultureInfo.InvariantCulture) ?? "none"}), "
            + $"reservation error={reservationErrors} (last error: {lastReservationErrorDescription}).",
            lastReservationError);

        static void ReleaseAllocations(List<(int Port, string Mutex)> allocations)
        {
            foreach (var allocation in allocations)
            {
                MutexManager.Instance.SignalRelease(allocation.Mutex);
            }

            allocations.Clear();
        }
    }

    private void ReleaseAllocatedPorts(int basePort, int portCount)
    {
        lock (_lockObj)
        {
            for (var port = basePort; port < basePort + portCount; port++)
            {
                if (_allocatedPorts.Remove(port, out var mutex))
                {
                    MutexManager.Instance.SignalRelease(mutex);
                }
            }
        }
    }

    internal enum MutexAcquisitionResult
    {
        Acquired,
        HeldByCurrentProcess,
        HeldByAnotherProcess,
        Error,
    }

    internal readonly record struct MutexAcquisition(MutexAcquisitionResult Result, Exception? Error = null);

    internal sealed class MutexManager : IDisposable
    {
        private readonly Dictionary<string, Mutex> _mutexes = [];
        private readonly BlockingCollection<Action> _workItems = [];
        private readonly Thread _thread;
        private readonly TimeSpan _acquisitionTimeout;
        private readonly Action? _beforeAcquire;
        private int _shutdown;

        public static MutexManager Instance { get; } = new MutexManager();

        private MutexManager() : this(TimeSpan.FromSeconds(10))
        {
        }

        internal MutexManager(TimeSpan acquisitionTimeout, Action? beforeAcquire = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(acquisitionTimeout, TimeSpan.Zero);
            _acquisitionTimeout = acquisitionTimeout;
            _beforeAcquire = beforeAcquire;
            _thread = new Thread(Run)
            {
                Name = "MutexManager.Worker",
                IsBackground = true,
            };
            _thread.Start();
            AppDomain.CurrentDomain.DomainUnload += this.OnAppDomainUnload;
        }

        private void OnAppDomainUnload(object? sender, EventArgs e) => Shutdown();

        public void Dispose()
        {
            AppDomain.CurrentDomain.DomainUnload -= OnAppDomainUnload;
            Shutdown();
        }

        private void Shutdown()
        {
            if (Interlocked.Exchange(ref _shutdown, 1) != 0)
            {
                return;
            }

            _workItems.CompleteAdding();
            _thread.Join();
            _workItems.Dispose();
        }

        internal MutexAcquisition Acquire(string name)
        {
            var result = new[] { MutexAcquisitionResult.HeldByCurrentProcess };
            var acquiredByRequest = new[] { false };
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ExceptionDispatchInfo? error = null;
            _workItems.Add(() =>
            {
                try
                {
                    _beforeAcquire?.Invoke();
                    if (!_mutexes.ContainsKey(name))
                    {
                        Mutex? mutex = new(false, name);
                        var acquired = false;
                        try
                        {
                            try
                            {
                                acquired = mutex.WaitOne(500);
                            }
                            catch (AbandonedMutexException)
                            {
                                acquired = true;
                            }

                            if (acquired)
                            {
                                // 已获取。
                                _mutexes[name] = mutex;
                                mutex = null;
                                result[0] = MutexAcquisitionResult.Acquired;
                                acquiredByRequest[0] = true;
                            }
                            else
                            {
                                result[0] = MutexAcquisitionResult.HeldByAnotherProcess;
                            }
                        }
                        finally
                        {
                            if (mutex is not null)
                            {
                                try
                                {
                                    if (acquired)
                                    {
                                        mutex.ReleaseMutex();
                                    }
                                }
                                finally
                                {
                                    mutex.Close();
                                }
                            }
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
                {
                    result[0] = MutexAcquisitionResult.Error;
                    error = ExceptionDispatchInfo.Capture(exception);
                }
                catch (Exception exception)
                {
                    error = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    signal.SetResult();
                }
            });

            try
            {
                // 端口分配为同步操作；命名互斥体的获取在具有线程亲和性的工作线程上执行。
                signal.Task.WaitAsync(_acquisitionTimeout).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                // 获取操作仍在队列中，并且可能在此调用方放弃等待后完成。
                // 在它之后将条件清理操作排队，确保只释放由此请求获取的互斥体。
                SignalRelease(name, acquiredByRequest);
                throw new TimeoutException("Timed out while waiting for MutexManager to acquire mutex.");
            }

            if (result[0] != MutexAcquisitionResult.Error)
            {
                error?.Throw();
            }

            return new MutexAcquisition(result[0], error?.SourceException);
        }

        public void SignalRelease(string name) => SignalRelease(name, acquiredByRequest: null);

        private void SignalRelease(string name, bool[]? acquiredByRequest)
        {
            try
            {
                if (_workItems.IsAddingCompleted) return;

                _workItems.Add(() =>
                {
                    if ((acquiredByRequest is null || acquiredByRequest[0]) && _mutexes.Remove(name, out var value))
                    {
                        value.ReleaseMutex();
                        value.Close();
                    }
                });
            }
            catch
            {
            }
        }

        private void Run()
        {
            try
            {
                foreach (var action in _workItems.GetConsumingEnumerable())
                {
                    try
                    {
                        action();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                foreach (var mutex in _mutexes.Values)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch { }
                    finally
                    {
                        mutex.Close();
                    }
                }

                _mutexes.Clear();
            }
        }
    }
}
