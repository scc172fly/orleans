// ReSharper disable InconsistentNaming
namespace Orleans.Reminders;

/// <summary>
/// Orleans 运行时库用于记录提醒器错误的一组错误代码。
/// </summary>
public enum RSErrorCode
{
    /// <summary>定义提醒器服务日志事件代码的基准值。</summary>
    ReminderServiceBase = /* 运行时 */ 100000 + 2900,

    /// <summary>标识 <c>RS_Register_TableError</c> 日志事件。</summary>
    RS_Register_TableError = ReminderServiceBase + 5,

    /// <summary>标识 <c>RS_Register_AlreadyRegistered</c> 日志事件。</summary>
    RS_Register_AlreadyRegistered = ReminderServiceBase + 7,

    /// <summary>标识 <c>RS_Register_InvalidPeriod</c> 日志事件。</summary>
    RS_Register_InvalidPeriod = ReminderServiceBase + 8,

    /// <summary>标识 <c>RS_Register_NotRemindable</c> 日志事件。</summary>
    RS_Register_NotRemindable = ReminderServiceBase + 9,

    /// <summary>标识 <c>RS_NotResponsible</c> 日志事件。</summary>
    RS_NotResponsible = ReminderServiceBase + 10,

    /// <summary>标识 <c>RS_Unregister_NotFoundLocally</c> 日志事件。</summary>
    RS_Unregister_NotFoundLocally = ReminderServiceBase + 11,

    /// <summary>标识 <c>RS_Unregister_TableError</c> 日志事件。</summary>
    RS_Unregister_TableError = ReminderServiceBase + 12,

    /// <summary>标识 <c>RS_Table_Insert</c> 日志事件。</summary>
    RS_Table_Insert = ReminderServiceBase + 13,

    /// <summary>标识 <c>RS_Table_Remove</c> 日志事件。</summary>
    RS_Table_Remove = ReminderServiceBase + 14,

    /// <summary>标识 <c>RS_Tick_Delivery_Error</c> 日志事件。</summary>
    RS_Tick_Delivery_Error = ReminderServiceBase + 15,

    /// <summary>标识 <c>RS_Not_Started</c> 日志事件。</summary>
    RS_Not_Started = ReminderServiceBase + 16,

    /// <summary>标识 <c>RS_UnregisterGrain_TableError</c> 日志事件。</summary>
    RS_UnregisterGrain_TableError = ReminderServiceBase + 17,

    /// <summary>标识 <c>RS_GrainBasedTable1</c> 日志事件。</summary>
    RS_GrainBasedTable1 = ReminderServiceBase + 18,

    /// <summary>标识 <c>RS_Factory1</c> 日志事件。</summary>
    RS_Factory1 = ReminderServiceBase + 19,

    /// <summary>标识 <c>RS_FailedToReadTableAndStartTimer</c> 日志事件。</summary>
    RS_FailedToReadTableAndStartTimer = ReminderServiceBase + 20,

    /// <summary>标识 <c>RS_TableGrainInit1</c> 日志事件。</summary>
    RS_TableGrainInit1 = ReminderServiceBase + 21,

    /// <summary>标识 <c>RS_TableGrainInit2</c> 日志事件。</summary>
    RS_TableGrainInit2 = ReminderServiceBase + 22,

    /// <summary>标识 <c>RS_TableGrainInit3</c> 日志事件。</summary>
    RS_TableGrainInit3 = ReminderServiceBase + 23,

    /// <summary>标识 <c>RS_GrainBasedTable2</c> 日志事件。</summary>
    RS_GrainBasedTable2 = ReminderServiceBase + 24,

    /// <summary>标识 <c>RS_ServiceStarting</c> 日志事件。</summary>
    RS_ServiceStarting = ReminderServiceBase + 25,

    /// <summary>标识 <c>RS_ServiceStarted</c> 日志事件。</summary>
    RS_ServiceStarted = ReminderServiceBase + 26,

    /// <summary>标识 <c>RS_ServiceStopping</c> 日志事件。</summary>
    RS_ServiceStopping = ReminderServiceBase + 27,

    /// <summary>标识 <c>RS_RegisterOrUpdate</c> 日志事件。</summary>
    RS_RegisterOrUpdate = ReminderServiceBase + 28,

    /// <summary>标识 <c>RS_Unregister</c> 日志事件。</summary>
    RS_Unregister = ReminderServiceBase + 29,

    /// <summary>标识 <c>RS_Stop</c> 日志事件。</summary>
    RS_Stop = ReminderServiceBase + 30,

    /// <summary>标识 <c>RS_RemoveFromTable</c> 日志事件。</summary>
    RS_RemoveFromTable = ReminderServiceBase + 31,

    /// <summary>标识 <c>RS_GetReminder</c> 日志事件。</summary>
    RS_GetReminder = ReminderServiceBase + 32,

    /// <summary>标识 <c>RS_GetReminders</c> 日志事件。</summary>
    RS_GetReminders = ReminderServiceBase + 33,

    /// <summary>标识 <c>RS_RangeChanged</c> 日志事件。</summary>
    RS_RangeChanged = ReminderServiceBase + 34,

    /// <summary>标识 <c>RS_LocalStop</c> 日志事件。</summary>
    RS_LocalStop = ReminderServiceBase + 35,

    /// <summary>标识 <c>RS_Started</c> 日志事件。</summary>
    RS_Started = ReminderServiceBase + 36,

    /// <summary>标识 <c>RS_ServiceInitialLoadFailing</c> 日志事件。</summary>
    RS_ServiceInitialLoadFailing = ReminderServiceBase + 37,

    /// <summary>标识 <c>RS_ServiceInitialLoadFailed</c> 日志事件。</summary>
    RS_ServiceInitialLoadFailed = ReminderServiceBase + 38,

    /// <summary>标识 <c>RS_FastReminderInterval</c> 日志事件。</summary>
    RS_FastReminderInterval = ReminderServiceBase + 39,
}
