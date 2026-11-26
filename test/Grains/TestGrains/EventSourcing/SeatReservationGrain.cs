using TestGrainInterfaces;
using Orleans.Providers;
using Orleans.EventSourcing;

namespace TestGrains
{
    /// <summary>
    /// 记录座位预订的已出版 grain 号记录。
    /// 
    /// 配置为每个集有一个实例 。
    /// 配置后使用默认的存储提供者。
    /// 配置后使用 StateStorage 一致性提供商。
    /// 
    /// 也就是说我们坚持最新状态... 我们不是真正的“活动来源”。
    /// 没有必要在这里坚持所有事件,因为国家已经储存了所有成功的预订。
    /// 
    /// </summary>

    [StorageProvider(ProviderName = "Default")]
    [LogConsistencyProvider(ProviderName = "StateStorage")]
    public class SeatReservationGrain : JournaledGrain<ReservationState, SeatReservation>, ISeatReservationGrain
    {

        public async Task<bool> Reserve(int seatnumber, string userid)
        {
            // 首, 排在请求前
            RaiseEvent(new SeatReservation() { Seat = seatnumber, UserId = userid });

            // 然后,等待传播请求
            await ConfirmEvents();

            // 我们能确定保留是否经过
            // 重新阅读它 - 如果它不在那里, 这意味着另一个用户赢了
            var success = (State.Reservations.ContainsKey(seatnumber)
                                 && State.Reservations[seatnumber].UserId == userid);
            return success;
        }
    }


    /// <summary>
    /// 保留的状况 grain
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
            // 查看此项保留是否针对一个可用的座位
            // 否则,就当做不接
            // (这是第一个作家赢得”冲突解决)
            if (!Reservations.ContainsKey(reservation.Seat))
                Reservations.Add(reservation.Seat, reservation);
        }
    }

    /// <summary>
    /// 当请求保留时定义更新操作的类别
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
