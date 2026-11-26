using System.Diagnostics;
using GPSTracker.GrainInterface;

namespace GPSTracker.Common;

public static class LoadDriver
{
    private static Random Random => Random.Shared;

    // 旧金山:大致边界。
    private const double SFLatMin = 37.708;
    private const double SFLatMax = 37.78;
    private const double SFLonMin = -122.50;
    private const double SFLonMax = -122.39;

    private static int s_counter = 0;

    public static async Task DriveLoad(IGrainFactory client, int numDevices, CancellationToken cancellationToken)
    {
        // 模拟一些设备
        var deviceModels = new List<Model>();
        for (int i = 0; i < numDevices; i++)
        {
            deviceModels.Add(new Model
            {
                DeviceId = i,
                Lat = NextDouble(SFLatMin, SFLatMax),
                Lon = NextDouble(SFLonMin, SFLonMax),
                Direction = NextDouble(-Math.PI, Math.PI),
                Speed = NextDouble(0, 0.0005)
            });
        }

        // 以循环方式更新每个设备 。
        var tasks = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (Model model in deviceModels)
            {
                tasks.Add(DeviceLoop(client, model, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            tasks.Clear();
        }
    }

    private static async Task DeviceLoop(IGrainFactory grainFactory, Model model, CancellationToken cancellationToken, int count = -1)
    {
        await Task.Yield();

        // 将消息发送到服务
        IDeviceGrain device = grainFactory.GetGrain<IDeviceGrain>(model.DeviceId);

        while (!cancellationToken.IsCancellationRequested && (count == -1 || count-- > 0))
        {
            try
            {
                // 这些价值观没有什么特别之处,它们只是模拟随机行走。
                double delta = model.TimeSinceLastUpdate.Elapsed.TotalMilliseconds;

                // 模拟设备移动
                model.Acceleration = Math.Clamp(model.Acceleration + delta / 100 * NextDouble(-0.0005, 0.0005), -10, 10);
                model.Speed = Math.Clamp(model.Speed + model.Acceleration * delta / 100, -0.0005, 0.0005);

                model.AngularVelocity = Math.Clamp(model.AngularVelocity + delta * NextDouble(-0.005, 0.005), -0.01, 0.01);
                model.Direction += model.AngularVelocity * delta;

                double lastLat = model.Lat;
                double lastLon = model.Lon;

                UpdateDevicePosition(model, delta);

                if (lastLat == model.Lat || lastLon == model.Lon)
                {
                    // 装置已进入边界,所以改变方向
                    model.Direction += NextDouble(-Math.PI, Math.PI);

                    UpdateDevicePosition(model, delta);
                }

                model.TimeSinceLastUpdate.Restart();

                await device.ProcessMessage(
                    new DeviceMessage(
                        model.Lat,
                        model.Lon,
                        ++model.MessageId,
                        model.DeviceId,
                        DateTime.UtcNow)).ConfigureAwait(false);

                Interlocked.Increment(ref s_counter);

                // 确保更新间隔间至少15米的通行证
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, 15 - model.TimeSinceLastUpdate.ElapsedMilliseconds)));
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Exception sending message: {exception}");
            }
        }
    }

    private static void UpdateDevicePosition(Model model, double delta)
    {
        model.Lat += Math.Cos(model.Direction) * (model.Speed * delta / 10);
        model.Lon += Math.Sin(model.Direction) * (model.Speed * delta / 10);
        model.Lat = Math.Clamp(model.Lat, SFLatMin, SFLatMax);
        model.Lon = Math.Clamp(model.Lon, SFLonMin, SFLonMax);
    }

    public static double NextDouble(double min, double max) => Random.NextDouble() * (max - min) + min;

    private class Model
    {
        public Stopwatch TimeSinceLastUpdate { get; } = Stopwatch.StartNew();
        public int DeviceId { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Direction { get; set; }
        public double AngularVelocity { get; set; }
        public double Acceleration { get; set; }
        public double Speed { get; set; }
        public long MessageId { get; set; }
    }
}
