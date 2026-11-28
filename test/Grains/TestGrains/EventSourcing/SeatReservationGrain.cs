using TestGrainInterfaces;
using Orleans.Providers;
using Orleans.EventSourcing;

namespace TestGrains
{
    /// <summary>
    /// 记录座位预订的日志化 Grain 示例。
    /// 
    /// 配置为每个集群一个实例。
    /// 配置为使用默认存储提供程序。
    /// 配置为使用 StateStorage 一致性提供程序。
    /// 
    /// 这意味着仅持久化最新状态……并非真正的“事件溯源”。
    /// 这里无需持久化所有事件，因为状态已保存全部成功的预订。
    /// 
    /// </summary>

    [StorageProvider(ProviderName = "Default")]
    [LogConsistencyProvider(ProviderName = "StateStorage")]
    public class SeatReservationGrain : JournaledGrain<ReservationState, SeatReservation>, ISeatReservationGrain
    {

        public async Task<bool> Reserve(int seatnumber, string userid)
        {
            // 首先，将请求加入队列
            RaiseEvent(new SeatReservation() { Seat = seatnumber, UserId = userid });

            // 然后等待请求传播完成
            await ConfirmEvents();

            // 可以判断预订是否成功，
            // 方法是重新读取该预订；如果不存在，则说明其他用户已抢先成功
            var success = (State.Reservations.ContainsKey(seatnumber)
                                 && State.Reservations[seatnumber].UserId == userid);
            return success;
        }
    }


    /// <summary>
    /// 预订 Grain 的状态
    /// </summary>
    [Serializable]
    [Orleans.GenerateSerializer]
    public class ReservationState
    {
        [Orleans.Id(0)]
        public Dictionary<int, SeatReservation> Reservations { get; set; }

        public ReservationState()
        {
            Reservations = new Dictionary<int, SeatReservation>();
        }

        private void Apply(SeatReservation reservation)
        {
            // 检查此次预订是否针对可用座位
            // 否则将其视为无操作
            // （这是“先写入者胜出”的冲突解决方式）
            if (!Reservations.ContainsKey(reservation.Seat))
                Reservations.Add(reservation.Seat, reservation);
        }
    }

    /// <summary>
    /// 定义请求预订时的更新操作的类
    /// </summary>
    [Serializable]
    [Orleans.GenerateSerializer]
    public class SeatReservation
    {
        [Orleans.Id(0)]
        public int Seat { get; set; }
        [Orleans.Id(1)]
        public string UserId { get; set; } = null!;
    }


}
