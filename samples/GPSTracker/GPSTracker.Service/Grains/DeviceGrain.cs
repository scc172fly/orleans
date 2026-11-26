using GPSTracker.Common;
using GPSTracker.GrainInterface;
using Orleans.Concurrency;

namespace GPSTracker.GrainImplementation;

[Reentrant]
public class DeviceGrain : Grain, IDeviceGrain
{
    private DeviceMessage _lastMessage = null!;

    private readonly IPushNotifierGrain _pushNotifier;

    public DeviceGrain() =>
        _pushNotifier = GrainFactory.GetGrain<IPushNotifierGrain>(0);

    public async ValueTask ProcessMessage(DeviceMessage message)
    {
        if (_lastMessage is null || _lastMessage.Latitude != message.Latitude || _lastMessage.Longitude != message.Longitude)
        {
            // 仅在职位改变时才发出通知
            double speed = GetSpeed(_lastMessage, message);

            // 记录最后一条消息
            _lastMessage = message;

            // 将信件转发给通知人 grain
            var velocityMessage = new VelocityMessage(message, speed);
            await _pushNotifier.SendMessage(velocityMessage);
        }
        else
        {
            // 位置没有改变, 只记录最后一条消息
            _lastMessage = message;
        }
    }

    private static double GetSpeed(DeviceMessage? message1, DeviceMessage message2)
    {
        // 使用 grain的内部状态计算设备的速度
        if (message1 is null || message2 is null)
        {
            return 0;
        }

        const double R = 6_371 * 1_000;
        double x = (message2.Longitude - message1.Longitude) * Math.Cos((message2.Latitude + message1.Latitude) / 2);
        double y = message2.Latitude - message1.Latitude;
        double distance = Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2)) * R;
        double time = (message2.Timestamp - message1.Timestamp).TotalSeconds;
        return time switch
        {
            0 => 0,
            _ => distance / time,
        };
    }
}
