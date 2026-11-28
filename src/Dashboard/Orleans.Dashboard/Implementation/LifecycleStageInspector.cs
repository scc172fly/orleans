using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Dashboard.Model;
using Orleans.Runtime;

namespace Orleans.Dashboard.Implementation;

/// <summary>
/// 使用反射从
/// <see cref="ISiloLifecycleSubject"/> 中提取已注册的生命周期观察者，以便 Dashboard 将其呈现为
/// 依赖关系图。
/// </summary>
/// <remarks>
/// Dashboard 没有通过 <c>InternalsVisibleTo</c> 获得对
/// 运行时内部成员的访问权限，因此通过反射遍历 <c>SiloLifecycleSubject</c> 和
/// <c>LifecycleSubject</c> 的私有状态。数据结构
/// （Silo 生命周期主体上的 <c>List&lt;MonitoredObserver&gt;</c>，具有公开的
/// <c>Name</c>、<c>Stage</c>、<c>StageName</c> 属性和私有的
/// <c>observer</c> 字段）是稳定的，且与
/// <c>SiloLifecycleSubject.OnStart</c> 已输出到日志中的数据结构相同。
/// </remarks>
internal static class LifecycleStageInspector
{
    private static readonly HashSet<int> NamedStageValues = BuildNamedStageValues();
    private static readonly Func<CancellationToken, Task> NoOpStart = _ => Task.CompletedTask;
    private static readonly Func<CancellationToken, Task> NoOpStop = _ => Task.CompletedTask;

    public static LifecycleStageInfo[] GetStages(ISiloLifecycleSubject? lifecycle)
    {
        if (lifecycle is null)
        {
            return [];
        }

        // Silo 的生命周期主体保留单独的 `observers` 字段
        // （List<MonitoredObserver>），其中包含比
        // 基类的 `subscribers` 字段更丰富的信息。优先使用前者；但如果反射失败，
        // 则回退到基类列表。
        var monitored = TryReadField<System.Collections.IEnumerable>(lifecycle, "observers");
        if (monitored is not null)
        {
            return BuildStages(EnumerateMonitored(monitored));
        }

        var subscribers = TryReadField<System.Collections.IEnumerable>(lifecycle, "subscribers");
        if (subscribers is not null)
        {
            return BuildStages(EnumerateOrdered(subscribers));
        }

        return [];
    }

    private static LifecycleStageInfo[] BuildStages(IEnumerable<(int Stage, string? StageName, string? Name, object? InnerObserver)> entries)
    {
        return entries
            .GroupBy(e => e.Stage)
            .OrderBy(g => g.Key)
            .Select(group => new LifecycleStageInfo
            {
                Stage = group.Key,
                StageName = group.Select(e => e.StageName).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                    ?? group.Key.ToString(CultureInfo.CurrentCulture),
                IsNamedStage = NamedStageValues.Contains(group.Key),
                Observers = group.Select(e => BuildObserver(e.Name, e.InnerObserver)).ToArray(),
            })
            .ToArray();
    }

    private static IEnumerable<(int Stage, string? StageName, string? Name, object? InnerObserver)> EnumerateMonitored(System.Collections.IEnumerable monitored)
    {
        foreach (var item in monitored)
        {
            if (item is null) continue;
            var type = item.GetType();
            var stage = (int)(type.GetProperty("Stage")?.GetValue(item) ?? 0);
            var stageName = type.GetProperty("StageName")?.GetValue(item) as string;
            var name = type.GetProperty("Name")?.GetValue(item) as string;
            var inner = type.GetField("observer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(item);
            yield return (stage, stageName, name, inner);
        }
    }

    private static IEnumerable<(int Stage, string? StageName, string? Name, object? InnerObserver)> EnumerateOrdered(System.Collections.IEnumerable ordered)
    {
        foreach (var item in ordered)
        {
            if (item is null) continue;
            var type = item.GetType();
            var stage = (int)(type.GetProperty("Stage")?.GetValue(item) ?? 0);
            var inner = type.GetProperty("Observer")?.GetValue(item);
            yield return (stage, null, null, inner);
        }
    }

    private static LifecycleObserverInfo BuildObserver(string? name, object? inner)
    {
        var info = new LifecycleObserverInfo
        {
            Name = name ?? "(unnamed)",
        };

        if (inner is null)
        {
            info.ObserverType = "(disposed)";
            return info;
        }

        // 基类 LifecycleSubject 使用内部的
        // OrderedObserver 包装每次注册。Silo 生命周期主体再用 MonitoredObserver 包装它。
        // 如果反射返回了 MonitoredObserver，则再解开一层包装。
        if (inner.GetType().Name == "MonitoredObserver")
        {
            inner = inner.GetType().GetField("observer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(inner) ?? inner;
        }

        // 尝试提取委托的目标与方法。LifecycleExtensions 中基于委托的
        // Subscribe 重载将委托包装在
        // 私有的 `Observer`（具有 onStart/onStop 字段）或 `StartupObserver`
        // （具有 `_onStart` 字段，OnStop 为空操作）中。
        var innerType = inner.GetType();
        Delegate? onStart = TryReadDelegateField(inner, innerType, "onStart") ?? TryReadDelegateField(inner, innerType, "_onStart");
        Delegate? onStop = TryReadDelegateField(inner, innerType, "onStop") ?? TryReadDelegateField(inner, innerType, "_onStop");

        if (onStart is null && onStop is null && innerType.GetMethod("OnStart") is not null)
        {
            // 具体的 ILifecycleObserver 实现。
            info.ObserverType = FormatType(innerType);
            info.HasOnStart = true;
            info.HasOnStop = true;
            info.OnStartMethod = $"{FormatType(innerType)}.OnStart";
            info.OnStopMethod = $"{FormatType(innerType)}.OnStop";
            return info;
        }

        if (onStart is not null)
        {
            info.HasOnStart = !IsNoOp(onStart);
            info.OnStartMethod = FormatDelegate(onStart);
        }

        if (onStop is not null)
        {
            info.HasOnStop = !IsNoOp(onStop);
            info.OnStopMethod = FormatDelegate(onStop);
        }

        info.ObserverType = ResolveOwningType(onStart, onStop) ?? FormatType(innerType);

        return info;
    }

    private static Delegate? TryReadDelegateField(object owner, Type ownerType, string fieldName)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(owner) as Delegate;
    }

    private static bool IsNoOp(Delegate? d)
    {
        // 将编译器生成的 `_ => Task.CompletedTask` 和
        // 共享的空操作委托视为“空操作”，仅为了让 UI 淡化相应的
        // 连线。对编译器生成闭包的检测采用尽力而为的方式。
        if (d is null) return true;
        if (ReferenceEquals(d, NoOpStart) || ReferenceEquals(d, NoOpStop)) return true;
        var method = d.Method;
        if (method.DeclaringType is { } declaring && declaring.Name.Contains("<>c", StringComparison.Ordinal))
        {
            // Lambda 表达式。不分析 IL 就无法确定，但在
            // 生命周期注册中使用时，它几乎总是空操作。
            return true;
        }
        return false;
    }

    private static string? FormatDelegate(Delegate? d)
    {
        if (d is null) return null;
        var method = d.Method;
        var declaring = method.DeclaringType;
        if (declaring is null) return CleanMethodName(method.Name);

        // 对于闭包，向外查找用户可见的外层类型。
        if (declaring.Name.StartsWith("<>c", StringComparison.Ordinal) && declaring.DeclaringType is not null)
        {
            declaring = declaring.DeclaringType;
        }

        var cleanedMethod = CleanMethodName(method.Name);
        var targetType = d.Target?.GetType();
        if (targetType is not null && targetType != declaring && !targetType.Name.StartsWith("<>", StringComparison.Ordinal))
        {
            return $"{FormatType(targetType)}.{cleanedMethod}";
        }
        return $"{FormatType(declaring)}.{cleanedMethod}";
    }

    /// <summary>
    /// 从 Lambda 表达式生成的方法名中去除编译器生成的前缀
    /// （<c>&lt;Outer&gt;b__N_N</c>）以及局部函数的前缀
    /// （<c>&lt;Outer&gt;g__Name|N_N</c>）。局部函数保留其原始
    /// 名称；匿名 Lambda 表达式呈现为 <c>&lt;lambda in Outer&gt;</c>。
    /// </summary>
    private static string CleanMethodName(string methodName)
    {
        if (string.IsNullOrEmpty(methodName) || methodName[0] != '<')
        {
            return methodName;
        }

        // 查找与开头 '<' 匹配的 '>'，同时考虑类似下例的显式接口实现中的
        // 嵌套尖括号。
        // `<Orleans.ILifecycleParticipant<Orleans.Runtime.ISiloLifecycle>.Participate>g__Name|N`.
        var depth = 0;
        var endOfOuter = -1;
        for (var i = 0; i < methodName.Length; i++)
        {
            var c = methodName[i];
            if (c == '<') depth++;
            else if (c == '>')
            {
                depth--;
                if (depth == 0)
                {
                    endOfOuter = i;
                    break;
                }
            }
        }

        if (endOfOuter < 0)
        {
            return methodName;
        }

        var outer = methodName.AsSpan(1, endOfOuter - 1).ToString();
        // 将类似以下形式的显式接口实现
        // `Orleans.ILifecycleParticipant<...>.Participate` 简化为
        // 末尾的方法名称（Participate）。
        var lastDot = LastTopLevelDot(outer);
        if (lastDot >= 0)
        {
            outer = outer[(lastDot + 1)..];
        }

        var suffix = methodName.AsSpan(endOfOuter + 1);
        if (suffix.Length >= 3 && suffix[0] == 'g' && suffix[1] == '_' && suffix[2] == '_')
        {
            // <Outer>g__LocalName|N_N -> LocalName
            var rest = suffix[3..];
            var pipe = rest.IndexOf('|');
            var localName = pipe >= 0 ? rest[..pipe] : rest;
            return localName.ToString();
        }

        if (suffix.Length >= 3 && suffix[0] == 'b' && suffix[1] == '_' && suffix[2] == '_')
        {
            // <Outer>b__N_N -> <lambda in Outer>
            return $"<lambda in {outer}>";
        }

        return methodName;
    }

    private static int LastTopLevelDot(string value)
    {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var c = value[i];
            if (c == '>') depth++;
            else if (c == '<') depth--;
            else if (c == '.' && depth == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static string? ResolveOwningType(Delegate? onStart, Delegate? onStop)
    {
        var d = onStart ?? onStop;
        if (d is null) return null;
        var target = d.Target?.GetType();
        if (target is not null && !target.Name.StartsWith("<>", StringComparison.Ordinal))
        {
            return FormatType(target);
        }
        var declaring = d.Method.DeclaringType;
        if (declaring is null) return null;
        if (declaring.Name.StartsWith("<>c", StringComparison.Ordinal) && declaring.DeclaringType is not null)
        {
            declaring = declaring.DeclaringType;
        }
        return FormatType(declaring);
    }

    [return: NotNullIfNotNull(nameof(type))]
    private static string? FormatType(Type? type)
    {
        if (type is null) return null;
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        var genericArgs = string.Join(", ", type.GetGenericArguments().Select(FormatType));
        var name = type.Namespace is null ? type.Name : $"{type.Namespace}.{type.Name}";
        var tickIndex = name.IndexOf('`', StringComparison.Ordinal);
        if (tickIndex >= 0) name = name[..tickIndex];
        return $"{name}<{genericArgs}>";
    }

    private static T? TryReadField<T>(object owner, string fieldName) where T : class
    {
        var type = owner.GetType();
        while (type is not null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null && field.GetValue(owner) is T value)
            {
                return value;
            }
            type = type.BaseType;
        }
        return null;
    }

    private static HashSet<int> BuildNamedStageValues()
    {
        var values = new HashSet<int>();
        foreach (var field in typeof(ServiceLifecycleStage).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(int))
            {
                values.Add((int)field.GetRawConstantValue()!);
            }
        }
        return values;
    }
}
