using System;
using System.Data;
using System.Linq.Expressions;

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
    /// <summary>
    /// 此拦截器绕过某些 Oracle 特有行为。
    /// </summary>
    internal class OracleCommandInterceptor : ICommandInterceptor
    {
        public static readonly ICommandInterceptor Instance = new OracleCommandInterceptor();

        private readonly Lazy<Action<IDbDataParameter>> setClobOracleDbTypeAction;
        private readonly Lazy<Action<IDbDataParameter>> setBlobOracleDbTypeAction;
        private readonly Lazy<Action<IDbCommand>> setCommandBindByNameAction;


        private OracleCommandInterceptor()
        {
            setClobOracleDbTypeAction = new Lazy<Action<IDbDataParameter>>(() => BuildSetOracleDbTypeAction("Clob"));
            setBlobOracleDbTypeAction = new Lazy<Action<IDbDataParameter>>(() => BuildSetOracleDbTypeAction("Blob"));
            setCommandBindByNameAction = new Lazy<Action<IDbCommand>>(BuildSetBindByNameAction);
        }

        /// <summary>
        /// 创建一个已编译的 Lambda 表达式，将 OracleCommand 的 BindByName 属性设为 true。
        /// </summary>
        /// <returns>以 IDbCommand 形式接收 OracleCommand 的操作。</returns>
        private Action<IDbCommand> BuildSetBindByNameAction()
        {
            var type = Type.GetType("Oracle.ManagedDataAccess.Client.OracleCommand, Oracle.ManagedDataAccess")!;

            var parameterExpression = Expression.Parameter(typeof(IDbCommand), "command");

            var castExpression = Expression.Convert(parameterExpression, type);

            var booleanConstantExpression = Expression.Constant(true);

            var setMethod = type.GetProperty("BindByName")!.GetSetMethod()!;

            var callExpression = Expression.Call(castExpression, setMethod, booleanConstantExpression);

            return Expression.Lambda<Action<IDbCommand>>(callExpression, parameterExpression).Compile();
        }

        /// <summary>
        /// 创建一个已编译的 Lambda 表达式，将 OracleDbType 属性设置为指定的 <paramref name="enumName"/>。
        /// </summary>
        /// <param name="enumName">OracleDbType 枚举值的字符串表示。</param>
        /// <returns>以 IDbDataParameter 形式接收 OracleParameter 的操作。</returns>
        private static Action<IDbDataParameter> BuildSetOracleDbTypeAction(string enumName)
        {
            var type = Type.GetType("Oracle.ManagedDataAccess.Client.OracleParameter, Oracle.ManagedDataAccess")!;

            var parameterExpression = Expression.Parameter(typeof(IDbDataParameter), "dbparameter");

            var castExpression = Expression.Convert(parameterExpression, type);

            var enumType = Type.GetType("Oracle.ManagedDataAccess.Client.OracleDbType, Oracle.ManagedDataAccess")!;

            var clob = Enum.Parse(enumType, enumName);

            var enumConstantExpression = Expression.Constant(clob, enumType);

            var setMethod = type.GetProperty("OracleDbType")!.GetSetMethod()!;

            var callExpression = Expression.Call(castExpression, setMethod, enumConstantExpression);

            return Expression.Lambda<Action<IDbDataParameter>>(callExpression, parameterExpression).Compile();
        }


        public void Intercept(IDbCommand command)
        {
            foreach (IDbDataParameter commandParameter in command.Parameters)
            {
                // 默认情况下，Oracle 按索引而不是按名称绑定参数。
                // 必须将 BindByName 属性设置为 true 才能更改默认行为。
                setCommandBindByNameAction.Value(command);

                // 字符串参数映射到 NVarChar2 OracleDbType，该类型限制为 4000 字节。
                // 这会将 OracleType 显式设置为 CLOB。
                if (commandParameter.ParameterName == "PayloadJson")
                {
                    setClobOracleDbTypeAction.Value(commandParameter);
                    continue;
                }

                // 与上面类似。
                if (commandParameter.ParameterName == "PayloadXml")
                {
                    setClobOracleDbTypeAction.Value(commandParameter);
                    continue;
                }

                // 字节数组映射为 RAW，这会导致问题。
                // 这会将 OracleDbType 显式设置为 BLOB。
                if (commandParameter.ParameterName == "PayloadBinary")
                {
                    setBlobOracleDbTypeAction.Value(commandParameter);
                    continue;
                }

                // Oracle 不支持 DbType.Boolean，因此
                // 将其映射为 DbType.Int32。
                if (commandParameter.DbType == DbType.Boolean)
                {
                    commandParameter.Value = commandParameter.ToString() == bool.TrueString ? 1 : 0;
                    commandParameter.DbType = DbType.Int32;
                }
            }
        }
    }
}

#nullable restore
