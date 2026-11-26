---
title: "提供方作者结构"
description: "设计设计设计设计设计设计设计设计设计设计设计设计设计设计设计设计设计设计设计 Orleans 提供有指定服务、配置装订、验证、生命周期和运行期延长合同的供应商。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="provider-authoring-architecture"></a>

# 提供方作者结构

Orleans 供应商根据界定的合同调整外部系统或替代运行时间实施。存储、集聚、提醒、 grain 目录和持续流有不同的数据-平面界面,但其主机结构则遵循以 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options) 和 [补充性注射](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview)为基础的相同模式:

1. 绑定一个命名配置区域;
1. 登记指定的服务和选择;
1. 在主机启动期间验证配置;
1. (b) 当资源需要启动时参与生命周期;以及
1. 表面衰竭而不是静默地降低对另一个后端的侮辱。

<a id="configuration-driven-provider-builders"></a>

## 配置驱动的供应商建设

<xref:Orleans.Providers.IProviderBuilder`1> 是从 Orleans 配置到 silo 或客户端构建器的桥梁:

提供方软件包将提供者类型字符串和类别与使用组装元数据的构建器连接。主机从配置中选择构建器,通过供应商的名称和区段,让构建者调用代码一配置所使用的相同的公共注册 API 。

Azure 队列执行显示了这种模式: <xref:Orleans.Hosting.AzureQueueStreamProviderBuilder> 安装 <xref:Orleans.Hosting.ISiloBuilder> 和 <xref:Orleans.Hosting.IClientBuilder>的建筑工件。见其 [执行中](https://github.com/dotnet/orleans/blob/main/src/Azure/Orleans.Streaming.AzureStorage/Hosting/AzureQueueStreamProviderBuilder.cs)。

<a id="named-service-composition"></a>

## 命名服务构成

许多供应商类型允许多个实例。因此,供应商名称是服务身份、选项身份、记录身份和运行时间查询的一部分。 <xref:Orleans.Hosting.NamedServiceConfigurator> 及其衍生配置人登记了键入组件和命名选项,但不创建私营服务提供商。

登记展期应:

- 要求指定供应商类别时使用非空白名称;
- 通过 `AddOptions<T>(name)` 或 Orleans 配置器注册选项;
- 在同一密钥下登记合同和履约情况;
- 仅对真正共享的默认值使用 <xref:Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd*> ;并且
- 包括验证和诊断中的名称。

解决一个未加密单吨的指定组件,可以使第一个供应商的选择方案渗入其他每个供应商。

<a id="separate-control-plane-and-data-plane"></a>

## 单独的控制平面和数据平面

供应商的建造者和选项是控制平面。

| 种类的提供者 | 数据机合同 |
| --- | --- |
| 分组成员 | <xref:Orleans.IMembershipTable> |
| Grain 储存 | <xref:Orleans.Storage.IGrainStorage> |
| Grain 目录 | <xref:Orleans.GrainDirectory.IGrainDirectory> |
| 提醒表格 | <xref:Orleans.IReminderTable> |
| 持久性流 | <xref:Orleans.Streams.IQueueAdapterFactory> 及其适配器组件 |

不允许配置问题削弱数据机合同。 比如,会员提供商必须保留有条件更新和定购版本,无论证书来自连接字符串、密钥SDK客户端还是管理身份。

<a id="validation"></a>

## 审定

选项验证应在 silo 加入数据组之前失败,或开始接受数据集。 Orleans 供应商通常登记一个 <xref:Orleans.IConfigurationValidator> ,因此验证可包括指定的选项和普通数据批注验证无法表示的服务依赖性。

校验至少 :

- 要求的终点、客户或证明来源;
- 相互排斥的组合形式;
- 具体供应商的命名和范围限制;
- 配对部件之间的兼容性;以及
- 运行时合同所需的能力。

避免处理将无法获取的后端转换为空结果的广泛例外。 空白的成员表、 缺失 grain 状态或空流队列具有域义, 且不能代表吞没的基础设施故障 。

<a id="lifecycle-and-ownership"></a>

## 生命周期和所有权

分配客户、接收人、租赁或背景代理的供应商应实施或登记 <xref:Orleans.ILifecycleParticipant`1>。在规定运行时间后启动服务已准备好,并在这些服务消失之前停止。

所有权必须明确。 如果应用程序提供带有密钥的 SDK 客户端, 供应商一般不应该处置它所不拥有的物件。 如果服务提供商每队列创建接收器, 它应该在队列所有权移动时停止并处理它们 。

持续流提供商说明了分阶段生命周期构成:它在初始化期间创建了适配器,在活动阶段开始拉动剂,然后在关闭前停止试剂。见 <xref:Orleans.Providers.Streams.Common.PersistentStreamProvider.Participate*?displayProperty=nameWithType> 及其 [执行中](https://github.com/dotnet/orleans/blob/main/src/Orleans.Streaming/PersistentStreams/PersistentStreamProvider.cs)。

会员电话用户使用 <xref:Orleans.IMembershipTable> 的取消-认知方法初始化、读取和写。内建供应商将标牌转发到后端 API, 支持取消并约束在无名SDK操作上等待, 同时观察逾期错误。 用户用户根据其后端运行和资源寿命执行取消。 取消可以与承诺书进行竞争, 因此有条件书写和表格版本会管理后续更新 。

返回完成后端结果, 在开始进一步的 I/ O 之前观察取消。 绘制已经返回的结果保存了该操作的结果 。

共享会籍更新在会员经理处理之前直播。 每个来电者拥有自己的等候权, 而定期维护及其排队清理请求则随着 silo 生命周期停止。 这样可以让会员在关闭时使用。

成员名单RPC将取消与申请理由分开。每个接收者都采用其执行的取消行为 。

当生命周期回调必须执行取消或清理逻辑时, 以 <xref:System.Threading.Tasks.Task.Run*> 安排时间并将取消标记传给回调内的操作。 回调将拥有如何完成其工作的自有权 。

<a id="membership-snapshots"></a>

## 成员简况

使用 <xref:Orleans.IMembershipTable.ReadAllAsync*> 来以一次原子快照获取成员行及其相应的表格版本。 <xref:Orleans.MembershipTableData.TryGet*> 从该快照中选择一个 silo条目和行ETag。

有条件写作使用所观察到的快照中的 <xref:Orleans.TableVersion.Next*> 并返回成功状态。 刷新快照以在选择另一突变之前获取当前表 ETag 。 列 ETags 遵循每个提供者的元数据政策, 并在只心跳活动期间可以改变。 常见更新需要现有的行和一个匹配的表 ETag; 其他行条件仍然不动。

<a id="testing-a-provider"></a>

## 测试提供者

合同测试应涵盖不止成功的往返旅行:

- 有条件同时更新和删除版本拒绝;
- 重复登记或交付行为;
- 取消和推广超时;
- 启动启动验证;
- 后端无法无声回退;
- (a) 生命周期停止后的资源清理;
- 多个有孤立选项的多命名实例;以及
- 储存或传送数据之间的滚动更新兼容性。

使用 [TestingHost 结构架构](testing.md) 来理解测试集束替代品的运行时间服务。 依赖真实后端的供应商测试应该说明这些先决条件,而不应该将模拟器的薄弱一致性作为生产合同的证据。

<a id="membership-provider-conformance"></a>

### 成员供应商合规性

`Microsoft.Orleans.Clustering.TestKit` 供应框架中立测试 <xref:Orleans.IMembershipTable>。为每个直接情景创建新的固定装置,由独立供应商处理共享后端和使用固定装置的孤立集号识别码。固定装置拥有初始化和拆卸功能。

提供固定设备在原始支持范围上的只读删除探测器。 工具包检查了人口数据, 然后删除并核实本地删除或存储无效后再处置所有者。 删除结束历史; 随后生成的病例使用新的固定装置和所有者。

将直选方法作为个体测试,运行由协议人生成的套房,并装上一个新的固定工厂。这些测试包括现有行中的原子表变换化学文摘社、连贯的康尼读物、不干扰表格版本和先前采集的最新输入材料的心跳、死投清理和集束隔离。列ETags是供应商定义的元数据,在心跳后可以变化。原始活性读物会滞后,而全行写作可以覆盖心跳。提供者-Native仪器核查每段心跳中都有一个无先决条件的盲线活性书写读或比较和擦拭操作。保留复制历史的失败诊断。 [软件包 README](https://github.com/dotnet/orleans/blob/main/src/Orleans.Clustering.TestKit/README.md) 提供了工厂实例和情景清单。
