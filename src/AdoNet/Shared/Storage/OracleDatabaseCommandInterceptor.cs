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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 拦截器绕过甲骨文的某些细节
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
        /// 创建汇编的羊羔,设置 BindByName 财产在 OracleCommand 真实的。
        /// </summary>
        /// <returns>采取下列行动的行动: OracleCommand 年为 IDbCommand </returns>
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
        /// 创建汇编的羊羔,将 OracleDbType 属性设置为指定 <paramref name="enumName"/>
        /// </summary>
        /// <param name="enumName">字符串值为 OracleDbType enum 值。</param>
        /// <returns>采取下列行动的行动: OracleParameter 年为 IDbDataParameter.</returns>
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
                //默认情况下, 手动设置将参数以索引而不是名称绑定
                //属性 BindByName 必须设置为真实以改变默认行为
                setCommandBindByNameAction.Value(command);

                //字符串参数被映射到 NVarChar2 OracleDbType ,该字符串限为4000字节
                //将 OracleType 明确设置为 CLOB
                if (commandParameter.ParameterName == "PayloadJson")
                {
                    setClobOracleDbTypeAction.Value(commandParameter);
                    continue;
                }

                //与上列相同
                if (commandParameter.ParameterName == "PayloadXml")
                {
                    setClobOracleDbTypeAction.Value(commandParameter);
                    continue;
                }

                //字节数组被映射为 RAW , 产生问题
                //将 OracleDbType 明确设置为 Blob
                if (commandParameter.ParameterName == "PayloadBinary")
                {
                    setBlobOracleDbTypeAction.Value(commandParameter);
                    continue;
                }

                //甲骨文不支持 DbType.Boolean,相反
                //我们绘制这些位置到 DbType.Int32
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
