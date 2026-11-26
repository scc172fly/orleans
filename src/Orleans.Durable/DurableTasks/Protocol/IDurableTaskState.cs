using System;
using System.Collections.Generic;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Protocol;

internal interface IDurableTaskState
{
    /// <summary>
    /// 获取任务记录所要操作的类型, 当运行时间创建记录时, 运行时间会设置操作 。
    /// </summary>
    public DurableTaskRecordKind Kind { get; }

    /// <summary>
    /// 如果任务尚未完成,则该任务的结果将是 <see langword="null"/> 。
    /// </summary>
    public DurableTaskResponse? Result { get; }

    /// <summary>
    /// 其他 grains或本 grain的任务,必须从中获取结果。
    /// </summary>
    /// <remarks>
    /// 援引者在它用来处理这项任务的识别符号下订阅,并等待一个持久的执行,等待
    /// 此项任务有 <c>GetDurableTask</c> 在其子子的标识符下订阅;另一个 grain的用户
    /// 使用其线条标识符命名任务。
    /// grain 在发件箱中有一个待完成的完成,直到发件箱将它留存,任务保留到那时;
    /// 订阅者 grain 本地嵌套任务没有
    /// 订户。
    /// </remarks>
    public IReadOnlySet<DurableTaskSubscriber> CompletionDestinations { get; }

    /// <summary>
    /// 不可置疑的要求
    /// </summary>
    /// <exception cref="Exception">存储的请求不再解码,例如因为其类型被删除。</exception>
    public IDurableTaskRequest? Request { get; }

    /// <summary>
    /// 获取任务是否存储请求, 而不解码 。
    /// </summary>
    /// <remarks>
    /// 运行时根据任务类型和种类分类任务,因此存储的请求不再解码只会影响
    /// 解码操作,例如开始任务。
    /// </remarks>
    public bool HasRequest { get; }

    /// <summary>
    /// 任务完成的时间。
    /// </summary>
    public DateTimeOffset? CompletedAt { get; }

    /// <summary>
    /// 请求取消的时间。
    /// </summary>
    public DateTimeOffset? CancellationRequestedAt { get; }

    /// <summary>
    /// 任务取消请求随附的理由,或 <see langword="null"/> 取消时间
    /// 请求或请求没有任何理由。
    /// </summary>
    /// <remarks>第一次取消请求决定了原因。</remarks>
    public string? CancellationReason { get; }

    /// <summary>
    /// 了解任务的父母是否开始保护它 所以它的祖先的持久取消没有达到
    /// </summary>
    /// <remarks>其自己取消、树被中止、树木被砍掉,</remarks>
    public bool Shielded { get; }

    /// <summary>
    /// 任务创建的时间 。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? DueTime { get; }

    public long ResumeGeneration { get; }

    /// <summary>
    /// 获得记录存储的指纹:援引请求的可引用类型和序列式的散列
    /// 或,为了记录处决本身的操作,其种类和身份,例如独立起步
    /// 目标任务和根本任务。
    /// </summary>
    /// <remarks>
    /// 请求书的论据是诊断性的,它用争论的序列形式改变,所以运行时间从来就没有了
    /// 将之比作一个比较,以决定重放或重试是同一操作。 <see cref="Method"/> 决定此操作。
    /// </remarks>
    public string? RequestFingerprint { get; }

    /// <summary>
    /// 获取任务所引用的界面方法的身份,或该任务对另一个 grain 的呼叫所引用的接口方法,或
    /// 其独立启动点援引或 <see langword="null"/> 记录,但没有任何方法引用。
    /// </summary>
    /// <remarks>
    /// 身份是该方法可被引用的方法的格式类型。
    /// 根任务在标识符下列表, 当记录类型、目标及目标为同一操作时, 其值与底任务相同 。
    /// 方法; 参数没有比较 。
    /// </remarks>
    public string? Method { get; }

    /// <summary>
    /// 当终端结果的留存因完成时间已过而重新启动时, 就会发现
    /// <see langword="null"/> 保留从 <see cref="CompletedAt"/>.
    /// </summary>
    public DateTimeOffset? RetainedSince { get; }

    public GrainId RemoteTarget { get; }

    /// <summary>
    /// 获得可援引类型和记录调用 <see cref="RemoteTarget"/>的序列参数的散列,
    /// 诊断报告。
    /// </summary>
    /// <remarks>电话的身份是其目标, <see cref="Method"/>而不是这个散列。</remarks>
    public string? RemoteRequestFingerprint { get; }

    /// <summary>
    /// 是否了解 grain 记录在另一个 grain 长期接受这项任务,
    /// <see langword="true"/>或启动失败、 <see langword="false"/>或 <see langword="null"/> 直至已知的任一日期。
    /// </summary>
    /// <remarks>
    /// 长期聘用或独立工作的记录记录其任职退休时接受的聘用,或
    /// 当任务的结果首先达到时,它就会被其目标拒绝的援引,例如一个开始,其识别符号
    /// 已经找到另一个请求, 或者发件箱用不字母的字串、 失败了开始, 并将失败存储为
    /// 除非记录已经存在其结果,例如取消记录等。
    /// </remarks>
    public bool? Accepted { get; }

    /// <summary>
    /// 获取通过耐久信息创建此任务的 grain 。
    /// </summary>
    public GrainId CallerId { get; }

    /// <summary>
    /// 获取用于识别引用任务运行过程的无
    /// 或 <see cref="System.Guid.Empty"/> ,以进行一项未被援引的任务。
    /// </summary>
    /// <remarks>
    /// 任务树上的每棵执行结果都报告为 <see cref="DurableTaskContext.Incarnation"/>。
    /// 同一标识符, 在删除此标识符后, 还有一个 。
    /// </remarks>
    public System.Guid Incarnation { get; }

    /// <summary>
    /// 获得 grain-全方位完成序列,该完成序列在任务处于阶段时标记任务终端结果,或当任务被推进时加盖,或者当
    /// 任务没有结果,或者其储存没有订购完成。
    /// </summary>
    /// <remarks>
    /// 存储印章的每个储存结果的顺序都高于每个储存任务,以与
    /// 组合器选择最早完成的最早完成时间,例如
    /// <c>WhenAny</c> 或 <c>WhenEach</c>中选择完成的候选人,其顺序最低,在
    /// 未经排行顺序的候选人由有记录的决定选出。
    /// </remarks>
    public long CompletionSequence { get; }
}
