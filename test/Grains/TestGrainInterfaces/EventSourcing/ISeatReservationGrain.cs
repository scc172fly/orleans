namespace TestGrainInterfaces
{
    // grain 支持一项行动,以保留一个座位
    public interface ISeatReservationGrain : IGrainWithIntegerKey
    {
        // 如果保留成功,返回布尔值
        Task<bool> Reserve(int seatnumber, string userid);
    }



}
