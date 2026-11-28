using System.Data;

#if CLUSTERING_ADONET
namespace Orleans.Clustering.AdoNet.Storage
#elif PERSISTENCE_ADONET
namespace Orleans.Persistence.AdoNet.Storage
#elif REMINDERS_ADONET
namespace Orleans.Reminders.AdoNet.Storage
#elif STREAMING_ADONET
namespace Orleans.Streaming.AdoNet.Storage
#elif GRAINDIRECTORY_ADONET
namespace Orleans.GrainDirectory.AdoNet.Storage
#elif TESTER_SQLUTILS
namespace Orleans.Tests.SqlUtils
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    internal class NoOpCommandInterceptor : ICommandInterceptor
    {
        public static readonly ICommandInterceptor Instance = new NoOpCommandInterceptor();

        private NoOpCommandInterceptor()
        {

        }

        public void Intercept(IDbCommand command)
        {
            //NOP
        }
    }
}
