namespace TestGrainInterfaces
{
    // 此 Grain 支持座位预订操作
    public interface ISeatReservationGrain : IGrainWithIntegerKey
    {
        // 返回布尔值，表示预订是否成功
        Task<bool> Reserve(int seatnumber, string userid);
    }



}
