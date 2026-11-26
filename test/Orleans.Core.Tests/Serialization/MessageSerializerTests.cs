/*
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;
using TestExtensions;
using Xunit;

名称空间 UnitTests.Serialization
{
    /// <summary>
    /// Orleans 信件序列化功能测试。
    /// </summary>
    [集合(TestEnvironmentFixture.DefaultCollection)]
    公共类 MessageSerializerTests
    {
        private readonly ITestOutputHelper output;
        private readonly TestEnvironmentFixture fixture;
        private readonly MessageFactory messageFactory;
        private readonly MessageSerializer messageSerializer;
        private readonly SerializerSessionPool _serializerSessionPool;
        private readonly IFieldCodec<GrainAddress> _grainAddressCodec;

        公共 MessageSerializerTests(ITestOutputHelper 输出, TestEnvironmentFixture 固定值)
        {
            this.output =产出;
            this.fixture =固定物;
            this.messageFactory = this.fixture.Services.GetRequiredService<MessageFactory>();
            this.messageSerializer = this.fixture.Services.GetRequiredService<MessageSerializer>();
            · 4个序列号为Pool fixture.Services.GetRequiredService<SerializerSessionPool>();
            地址(_G) fixture.Services.GetRequiredService<IFieldCodec<GrainAddress>>();
        }

        TestSuite("功能")
        TestProvider(“无”)
        [理论, TestCategory(“功能”)
        [InlineData(10_000)]
        [InlineData(0)]
        公开无效 MessageTest_TtlUpdatedOnAccess(int initialTimeToLiveMilliseconds)
        {
            var message = this.messageFactory.CreateMessage(null, InvokeMethodOptions.None);

            message.TimeToLive = TimeSpan.FromMilliseconds(initialTimeToLiveMilliseconds);
            var expirationTimestamp = message._timeToExpiry.GetRawTimestamp();
            WaitForTimestamp(expirationTimestamp - initialTimeToLiveMilliseconds + 10);

            var accessStarted = CoarseStopwatch.GetTimestamp();
            var timeToLive = message.TimeToLive;
            var accessCompleted = CoarseStopwatch.GetTimestamp();

            Assert.NotNull(timeToLive);
            Assert.True(timeToLive.Value < TimeSpan.FromMilliseconds(initialTimeToLiveMilliseconds));
            Assert.InRange(
                timeToLive.Value,
                TimeSpan.FromMilliseconds(expirationTimestamp - accessCompleted),
                TimeSpan.FromMilliseconds(expirationTimestamp - accessStarted));
        }

        TestSuite("功能")
        TestProvider(“无”)
        [理论, TestCategory(“功能”), TestCategory(“序列化”)
        [InlineData(10_000)]
        [InlineData(0)]
        公开无效 MessageTest_TtlUpdatedOnSerialization(int initialTimeToLiveMilliseconds)
        {
            var message = this.messageFactory.CreateMessage(null, InvokeMethodOptions.None);

            message.TimeToLive = TimeSpan.FromMilliseconds(initialTimeToLiveMilliseconds);
            var sourceExpirationTimestamp = message._timeToExpiry.GetRawTimestamp();
            WaitForTimestamp(sourceExpirationTimestamp - initialTimeToLiveMilliseconds + 10);
            var deserializedMessage = RoundTripMessage(message, out var serialization, out var deserialization);
            var deserializedExpirationTimestamp = deserializedMessage._timeToExpiry.GetRawTimestamp();

            Assert.NotNull(deserializedMessage.TimeToLive);
            Assert.InRange(
                deserializedExpirationTimestamp,
                sourceExpirationTimestamp - serialization.Completed + deserialization.Started,
                sourceExpirationTimestamp - serialization.Started + deserialization.Completed);
        }

        私有静态真空 WaitForTimestamp(长时间戳)
            => Assert.True(SpinWait.SpinUntil(() => CoarseStopwatch.GetTimestamp(c) 时间戳, TimeSpan.FromSeconds(1)));

        TestSuite("功能")
        TestProvider(“无”)
        [事实, TestCategory("功能"), TestCategory(“序列化”)
        公共空虚 信件(_S)
        {
            用于 ( var i= 0; i < 10; i++)
            {
                var writeBuffer = new PooledBuffer();
                var readBuffer = new PooledBuffer();
                试
                {
                    var message = CreateTestMessage();
                    var (headerLength, bodyLength) = this.messageSerializer.Write(ref writeBuffer, message);

                    writeBuffer.CopyTo(ref readBuffer);
                    var bufferSlice = readBuffer.Slice();
                    this.messageSerializer.Read(in bufferSlice, headerLength, bodyLength, out var deserializedMessage);

                    CheckMessage(message, deserializedMessage);
                }
                最后最后
                {
                    writeBuffer.Dispose();
                    readBuffer.Dispose();
                    RequestContext.Clear();
                }
            }
        }

        [事实, TestCategory("功能"), TestCategory(“序列化”)
        公共空虚 信件(_S)
        {
            用于 ( var i= 0; i < 10; i++)
            {
                var writeRequest = this.messageHandlerShared.GetSendMessageHandler();
                var message = CreateTestMessage();
                writeRequest.Initialize(message);

                var readRequest = messageHandlerShared.GetReceiveMessageHandler();

                var writeBuffers = writeRequest.Buffers;
                以 writeLength;
                do 做
                {
                    writeLength = (int)Math.Min(writeBuffers.Length, readRequest.Buffer.Length);
                    writeBuffers.Slice(0, writeLength).CopyTo(readRequest.Buffer.Span);
                    writeBuffers = writeBuffers.Slice(writeLength);
                时(!)readRequest.OnProgress(writeLength));

                //var deserializedMessage = readRequest.TestReadMessage();
                CheckMessage(message, deserializedMessage);
            }
        }

        私人静态真空 CheckMessage(信件、信件 deserializedMessage)
        {
            Assert.Equal(message.Id, deserializedMessage.Id);
            Assert.Equal(message.BodyObject, deserializedMessage.BodyObject);
            Assert.Equal(message.SendingGrain, deserializedMessage.SendingGrain);
            Assert.Equal(message.SendingSilo, deserializedMessage.SendingSilo);
            Assert.Equal(message.TargetGrain, deserializedMessage.TargetGrain);
            Assert.Equal(message.TargetSilo, deserializedMessage.TargetSilo);
            Assert.Equal(message.CacheInvalidationHeader.Count, deserializedMessage.CacheInvalidationHeader.Count);
            Assert.Equal(message.ForwardCount, deserializedMessage.ForwardCount);
            Assert.Equal(message.Direction, deserializedMessage.Direction);
            Assert.Equal(message.InterfaceType, deserializedMessage.InterfaceType);
            Assert.Equal(message.InterfaceVersion, deserializedMessage.InterfaceVersion);
            每一页 ( message.CacheInvalidationHeader中的空头)
            {
                Assert.Contains(header, deserializedMessage.CacheInvalidationHeader);
            }

            Assert.Equal(message.BodyObject, deserializedMessage.BodyObject);
        }

        私密信件 CreateTestMessage()
        {
            试
            {
                RequestContext.Set("fancy_feet", "yes");
                var message = this.messageFactory.CreateMessage("ladida", InvokeMethodOptions.None);
                message.SendingGrain = GrainId.Create("测试","软");
                message.TargetGrain = GrainId.Create(“测试2”、“ foo2”);
                message.SendingSilo = SiloAddress.New(IPAddress.Loopback, 12345, 543212345);
                message.TargetSilo = SiloAddress.New(IPAddress.Parse("100.200.1.2"), 12345, 543212345);
                message.CacheInvalidationHeader =新()
                {
                    新 GrainAddress
                    {
                        GrainId = GrainId.Create("测试","软"),
                        ActivationId = ActivationId.NewId(),
                        SiloAddress = SiloAddress.New(IPAddress.Parse("1.2.3.4"), 8285, 11)
                    },

                    新 GrainAddress
                    {
                        GrainId = GrainId.Create("牛","黄珠"),
                        ActivationId = ActivationId.NewId(),
                        SiloAddress = SiloAddress.New(IPAddress.Parse("2.2.2.22"), 1, 123456)
                    }
                };
                return message;
            }
            最后最后
            {
                RequestContext.Clear();
            }
        }

        [事实, TestCategory("功能"), TestCategory(“序列化”)
        公共空虚 消息(_S)
        {
            var buffer = new PooledBuffer();
            试
            {
                // 创建一个荒谬大 RequestContext
                var maxHeaderSize = this.fixture.Services.GetService<IOptions<SiloMessagingOptions>>().Value.MaxMessageHeaderSize;
                RequestContext.Set("big_object", new byte[maxHeaderSize + 1]);

                var message = this.messageFactory.CreateMessage(null, InvokeMethodOptions.None);

                Assert.Throws<OrleansException>(() => this.messageSerializer.Write(ref缓冲,电文));
            }
            最后最后
            {
                buffer.Dispose();
                RequestContext.Clear();
            }
        }

        [事实, TestCategory("功能"), TestCategory(“序列化”)
        公文空白 信件(_S)
        {
            var buffer = new PooledBuffer();
            试
            {
                var maxBodySize = this.fixture.Services.GetService<IOptions<SiloMessagingOptions>>().Value.MaxMessageBodySize;

                // 以荒谬的大辩论创建请求
                var arg = new byte[maxBodySize + 1];
                var request = new[] { arg };
                var message = this.messageFactory.CreateMessage(request, InvokeMethodOptions.None);

                Assert.Throws<OrleansException>(() => this.messageSerializer.Write(ref缓冲,电文));
            }
            最后最后
            {
                buffer.Dispose();
            }
        }

        [事实, TestCategory("功能"), TestCategory(“序列化”)
        公文无效 消息(_Desireize HeaderTooBig)
        {
            var maxSize = this.fixture.Services.GetService<IOptions<SiloMessagingOptions>>().Value.MaxMessageHeaderSize;

            DeserializeFakeMessage(maxSize + 1, 0);
        }

        [事实, TestCategory("功能"), TestCategory(“序列化”)
        公共空虚 消息(_D)
        {
            var maxSize = this.fixture.Services.GetService<IOptions<SiloMessagingOptions>>().Value.MaxMessageHeaderSize;

            DeserializeFakeMessage(0, maxSize + 1);
        }

        私人空 DeserializeFakeMessage(int headerSize, int bodySize)
        {
            var buffer = new PooledBuffer();

            试
            {
                平面<byte> lengthFields = 书架字节[8];
                BinaryPrimitives.WriteInt32LittleEndian(lengthFields, headerSize);
                BinaryPrimitives.WriteInt32LittleEndian(lengthFields[4..], bodySize);
                buffer.Write(lengthFields);

                var reader = buffer.Slice(0);
                Assert.Throws<OrleansException>(() => this.messageSerializer.Read(读者, headerSize, bodySize,发文)));
            }
            最后最后
            {
                buffer.Dispose();
            }
        }

        私人信件 RoundTripMessage(邮件)
        {
            var buffer = new PooledBuffer();

            试
            {
                var (headerSize, bodySize) = this.messageSerializer.Write(ref buffer, message);

                var reader = buffer.Slice();
                this.messageSerializer.Read(in reader, headerSize, bodySize, out var deserializedMessage);
                return deserializedMessage;
            }
            最后最后
            {
                buffer.Dispose();
            }
        }

        私人信件 RoundTripMessage(
            信息留言
            排行(长期启动、长期完成),
            排出( 远程启动、 长期完成) 解密)
        {
            var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
            var writer = pipe.Writer;
            var serializationStarted = CoarseStopwatch.GetTimestamp();
            this.messageSerializer.Write(writer, message);
            var serializationCompleted = CoarseStopwatch.GetTimestamp();
            writer.FlushAsync().AsTask().GetAwaiter().GetResult();

            pipe.Reader.TryRead(out var readResult);
            var reader = readResult.Buffer;
            var deserializationStarted = CoarseStopwatch.GetTimestamp();
            var (requiredBytes, _, _) = this.messageSerializer.TryRead(ref reader, out var deserializedMessage);
            var deserializationCompleted = CoarseStopwatch.GetTimestamp();
            Assert.Equal(0, requiredBytes);
            序列化= (serializationStarted, serializationCompleted);
            衰减序列=(deserializationStarted、 deserializationCompleted);
            return deserializedMessage!;
        }

        TestSuite("功能")
        TestProvider(“无”)
        [理论, TestCategory(“功能”), TestCategory(“序列化”)
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(1024, 1024)]
        [InlineData(1025, 1024)]
        [InlineData(2048, 1024)]
        公文无效 消息_ 请求文本
        {
            Assert.Equal(预期, MessageSerializer.GetRequestContextInitialCapacity(规模);
        }

        TestSuite("功能")
        TestProvider(“无”)
        [事实, TestCategory("功能"), TestCategory(“序列化”)
        公共空白 信息(_R)
        {
            等同 entryCount =2048;
            var requestContext = new Dictionary<string, object>(entryCount);
            用于 ( var I= 0; i < entryCount; i++)
            {
                requestContext[$"key-{i}"] = i;
            }

            var message = this.messageFactory.CreateMessage(null, InvokeMethodOptions.None);
            message.RequestContextData = requestContext;

            var deserializedMessage = RoundTripMessage(message);

            Assert.NotNull(deserializedMessage.RequestContextData);
            Assert.Equal(entryCount, deserializedMessage.RequestContextData.Count);
            用于 ( var I= 0; i < entryCount; i++)
            {
                Assert.Equal(i, deserializedMessage.RequestContextData[$"key-{i}"]);
            }
        }

        [事实, TestCategory("BVT")]
        公共无效 MessageTest_CacheInvalidationHeader_RoundTripCompatibility()
        {
            var newSilo = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 55555), 55555);

            var oldActivations =新列表<GrainAddress>
            {
                GrainAddress.NewActivationAddress(SiloAddress.New)(新的 IPEndPoint(IPAddress.Loopback,11111),111111111), GrainId.Create(“测试”,“1”),
                GrainAddress.NewActivationAddress(SiloAddress.New)(新 IPEndPoint(IPAddress.Loopback, 2222222), 22222, GrainId.Create("测试", "2" ),
                新建(){{ SiloAddress = SiloAddress.New(新 IPEndPoint(IPAddress.Loopback, 33333), 33333), GrainId = GrainId.Create("测试,"3") }
            };

            var newActivations =新列表<GrainAddress>
            {
                GrainAddress.NewActivationAddress(newSilo, GrainId.Create(“测试”, “1”)),
                GrainAddress.NewActivationAddress(newSilo, GrainId.Create(“测试”、“2”),
                { SiloAddress = newSilo, GrainId = GrainId.Create("测试","3")},
            };

            var newUpdates = oldActivations.Zip(newActivations).Select(x => new GrainAddressCacheUpdate(x.First, x.Second)).ToList();

            // 旧到新
            {
                using var writer1Session = _serializerSessionPool.GetSession();
                var writer = Writer.CreatePooled(writer1Session);
                var stub = new MessageSerializerBackwardsCompatibilityStub(_grainAddressCodec);
                var fromOld = oldActivations.ToList();
                stub.WriteCacheInvalidationHeaders(ref writer, fromOld);
                writer.Commit();

                using var reader1Session = _serializerSessionPool.GetSession();
                var reader = Reader.Create(writer.Output.AsReadOnlySequence(), reader1Session);
                var toNew = messageSerializer.ReadCacheInvalidationHeaders(ref reader);
                Assert.NotNull(toNew);
                Assert.Equal(fromOld.Count, toNew.Count);
                用于 ( var I= 0; i < fromOld.Count; i++)
                {
                    // 只有无效的 grain 地址才能被代表。
                    Assert.Equal(fromOld[i], toNew[i].InvalidGrainAddress);
                    Assert.Null(toNew[i].ValidGrainAddress);
                }

                writer.Dispose();
            }

            // 新建到新
            {
                using var writer1Session = _serializerSessionPool.GetSession();
                var writer = Writer.CreatePooled(writer1Session);
                var fromNew = newUpdates.ToList();
                var message = new Message { CacheInvalidationHeader = fromNew };
                messageSerializer.WriteCacheInvalidationHeaders(ref writer, message);
                writer.Commit();

                using var reader1Session = _serializerSessionPool.GetSession();
                var reader = Reader.Create(writer.Output.AsReadOnlySequence(), reader1Session);
                var toNew = messageSerializer.ReadCacheInvalidationHeaders(ref reader);
                Assert.NotNull(toNew);
                Assert.Equal(fromNew.Count, toNew.Count);
                用于 ( var I= 0; i < fromNew.Count; i++)
                {
                    // 期望完全忠诚
                    Assert.Equal(fromNew[i].InvalidGrainAddress, toNew[i].InvalidGrainAddress);
                    Assert.Equal(fromNew[i].ValidGrainAddress, toNew[i].ValidGrainAddress);
                }

                writer.Dispose();
            }

            // 新建到旧
            {
                using var writer1Session = _serializerSessionPool.GetSession();
                var writer = Writer.CreatePooled(writer1Session);
                var fromNew = newUpdates.ToList();
                var message = new Message { CacheInvalidationHeader = fromNew };
                messageSerializer.WriteCacheInvalidationHeaders(ref writer, message);
                writer.Commit();

                using var reader1Session = _serializerSessionPool.GetSession();
                var reader = Reader.Create(writer.Output.AsReadOnlySequence(), reader1Session);
                var stub = new MessageSerializerBackwardsCompatibilityStub(_grainAddressCodec);
                var toOld = stub.ReadCacheInvalidationHeaders(ref reader);
                Assert.NotNull(toOld);
                Assert.Equal(fromNew.Count, toOld.Count);
                用于 ( var I= 0; i < fromNew.Count; i++)
                {
                    // 只有无效的 grain 地址才能被代表。
                    Assert.Equal(fromNew[i].InvalidGrainAddress, toOld[i]);
                }

                writer.Dispose();
            }
        }

        私人类 MessageSerializerBackwardsCompatibilityStub
        {
            private readonly IFieldCodec<GrainAddress> _grainAddressCodec;

            公共公共 MessageSerializerBackwardsCompatibilityStub(IFieldCodec<GrainAddress> grainAddressCodec)
            {
                地址(_G) grainAddressCodec;
            }

            内部清单<GrainAddress> ReadCacheInvalidationHeaders<TInput>(ref reader<TInput> 阅读器)
            {
                var n = (int)reader.ReadVarUInt32();
                if (n > 0)
                {
                    var list = new List<GrainAddress>(n);
                    (一 = 0; i < n; i++)
                    {
                        list.Add(_grainAddressCodec.ReadValue(参考书, reader.ReadFieldHeader()));
                    }

                    return list;
                }

                return new List<GrainAddress>();
            }

            内部空 WriteCacheInvalidationHeaders<TBufferWriter>(ref Writer<TBufferWriter> 作家,列出<GrainAddress> 值) TBufferWriter : IBufferWriter<byte>
            {
                writer.WriteVarUInt32((昆特)value.Count);
                每一( var 条目值)
                {
                    _grainAddressCodec.WriteField(参考书写, 0, 类型(GrainAddress, 条目);
                }
            }
            }
            最后最后
            {
                buffer.Dispose();
            }
        }
    }
}
*/
