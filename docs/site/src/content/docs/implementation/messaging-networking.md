---
title: "运输和内部联网"
description: "解释 Orleans 如何维持 silo 连接、框架信息以及处理运输故障和停机。"
ms.date: 08/11/2026
ms.topic: concept-article
---

<a id="transport-and-networking-internals"></a>

# 运输和内部联网

Orleans 将电文路由与运输管理分开。 `MessageCenter` 决定电文去向; `ConnectionManager` 获得目标 silo的可用连接;连接执行批次和框信息与电文传输请求的连接。这种分离让路由在运输管理读、写和完成回馈时,可以发送修复启动地址。

<a id="connection-lifecycle"></a>

## 连接生命周期

对于每个远程 `SiloAddress`, `ConnectionManager` 都保留一个 `ConnectionEntry` ,其中含有主动连接、待完成连接尝试和最后一次故障时间。 `GetConnection` 再利用适当的主动连接。当主动连接耗尽后,开始一个共享的等待尝试端点,同时发送者等待这一尝试。

连接设置有一条连接 `OpenConnectionTimeout`。 失败的尝试清除了未完成的任务, 移除了已关闭的连接, 记录了失败, 并在另一次尝试之前应用配置的重试延迟。 超时将运输尝试归类为失败; 应用程序处理结果需要分别对账 。

Silo 连接在对等点协议序言验证后, 本地序言写完之后, 信件发送可获取链接。 此握手边界使每个序言都提前于运输工具上设定的信息, 包括当对等点在本地书写排队前发出其序言时。 TCP、 Unix- domain 套接字、 内线连接和 TLS 装饰连接共享此发布边界 。 TLS 认证在运输进程之前完成 。 Orleans 序言和发文请求。

外部机构通过连接器创建、 TLS 认证和协议初始化流程流出其关闭链接的 `OpenConnectionTimeout` 标记。 在分配或登记终点之前, 听器约束性检查启动取消。 入境听众通过接受和初始化流程流出关闭标志。 TLS 认证链接取消、 基本运输关闭 和 `HandshakeTimeout`; 最早信号中断了认证并完成排队失败的请求。 协议初始化检查在发布准备前取消。 一旦初始化完成, 连接寿命由运输关闭和拥有管理者或倾听者决定。

经理和听众亲身拥有的交通中断了等待取消的办公场所的序言读写。

来源: [`ConnectionManager`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Networking/ConnectionManager.cs)、 [`Connection`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Networking/Connection.cs)和 [`SiloConnection`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Networking/SiloConnection.cs)。

TCP 传输工具能够让外向和被接受的插座保持持续运行。 默认情况下, 操作系统在闲置时间90秒后发送第一个探测器, 然后每30秒进行检测, 允许10个未解答的探测器在终止连接前进行检测。 接受的套接器使用当前选项来接收其命名的听众, 所以每个都使用这些选项。 silo 或网关听众将其配置的维持性策略应用到新连接。 当平台报告一个不支持的保持性选项时, 该选项的传输日志会保留支持的设置; 继续为其他支持选项进行调试 。

<a id="message-path-and-framing"></a>

## 信件路径和框架

何时 `MessageCenter.SendMessage` 目标 silo,它重新使用现有的连接或从 `ConnectionManager`当地目标用途直接处理,已知的死目标导致对请求和单向信息暂时拒绝,过期信息在消费运输工作之前被删除。

连接管道执行协议序言,然后交换被设框的有效载荷。 `MessageSerializer` 用 Orleans 序列化编码信头和机体。 `MessageReadRequest` 在消耗信头和机体之前验证框架长度。在解除、转发或处置前,机体保持缓冲,直到解除音质、传送或处置释放。 TLS 在保存 Orleans 设置和电文协议的同时,对电文传输进行装饰。

连接通过无约束的队列接收发出的信件。 它发送的工人将信件分解成写请求, 运输作家会看到流量控制, 同时同时发送者继续向连接队列添加信件。 因此, 缓慢的连接可以累积队列信件直到它恢复或关闭。 一个成功的写入完成请求并释放其缓冲信件体; grain 结果确认应用程序请求完成。 断开连接会从端点条目中删除连接, 导致稍后发送以建立替换 。

<a id="failure-and-shutdown"></a>

## 故障和停机

连接终止、写请求失败、排队发送工作通过特定连接的路径转换信件 `RetryMessage` 执行后,执行后将信息反馈给 `MessageCenter` 部分书面请求可以在失败前达到目标,因此应用软件使用交付和应急要求来调和模糊的结果。

关闭第一个系统将新的应用程序交通堵塞,同时允许回复和会员交通完成停止程序。 `MessageCenter` 拒绝或丢弃封存信件, 停止接受客户端信件, 然后关闭连接。 内向应用程序请求到达停止 silo 接收瞬时拒绝的中继拒绝,并有针对性地撤销缓存,使发送者能够移除通往此路径 silo。其他入境应用程序信息被丢弃,缓冲身体被释放。

`ConnectionManager` 关闭营业所, 然后取消未完成的尝试并发出连接信号。 每个注册的生产商在公布连接或完成故障清理之前都拥有所有权, 包括连接运行者。 关闭后关闭任何迟发出版物, 通过中继软件清理排空其余的出场跑者。 关闭取消源一直有效直到这些操作结束; 取消的主机停止保存着它, 操作仍然松开。 入境听众不再接受连接, 关闭跟踪连接, 等待经理的闭路信号并处置他们的运输听筒。

连接关闭停止发送工作进入,并关闭信息传输以在 TLS 握手、序言交换和运输 I/O 之前释放电文。 在连接运行器和发送工人的松动后,运输处置会释放装饰式运输链。 排队运行者开始之前关闭的连接会在协议启动前完成运输关闭。

<a id="design-trade-offs"></a>

## 设计权衡

- 中点连接状态避免全球协调,但意味着每个 silo 必须遵守和修复自己破碎的道路。
- 重新利用连接可以减少握手和分配成本,而多重连接可以改善输送量并避免头顶堵塞。
- 丢弃过期信件保护饱和的运行时间, 使其不会被回调期限过期的工作所覆盖。 应用程序使用操作身份和耐久状态来调节操作结果 。

关于端至端语义学,见 [信息传递和发送](messaging-delivery-guarantees.md)。网络终端选择和防火墙要求属于 [地形、联网和集群](../deployment/networking.md)。
