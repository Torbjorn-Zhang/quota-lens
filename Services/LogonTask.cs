using System.IO;

namespace QuotaLens.Services;

/// <summary>
/// Thin wrapper over the Task Scheduler 2.0 COM API (<c>Schedule.Service</c>) using late binding, so
/// no interop assembly is required. Creates a per-user "run when I sign in" task that starts the
/// given executable with the interactive token of the current user and no elevation. A standard
/// user can register such a task for themselves.
/// </summary>
internal static class LogonTask
{
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExec = 0;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelLua = 0;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskInstancesIgnoreNew = 2;
    private const int NormalPriority = 5;
    private const int ErrorFileNotFound = unchecked((int)0x80070002);

    internal static void Register(string taskName, string executable)
    {
        dynamic service = Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Starts Quota Lens when you sign in to Windows.";
        definition.Principal.LogonType = TaskLogonInteractiveToken;
        definition.Principal.RunLevel = TaskRunLevelLua;

        dynamic settings = definition.Settings;
        settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false;
        settings.StartWhenAvailable = true;
        settings.ExecutionTimeLimit = "PT0S";
        settings.AllowHardTerminate = false;
        settings.MultipleInstances = TaskInstancesIgnoreNew;
        settings.Priority = NormalPriority;

        dynamic trigger = definition.Triggers.Create(TaskTriggerLogon);
        trigger.UserId = CurrentUser();
        trigger.Delay = "PT5S";

        dynamic action = definition.Actions.Create(TaskActionExec);
        action.Path = executable;
        action.WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty;

        folder.RegisterTaskDefinition(
            taskName,
            definition,
            TaskCreateOrUpdate,
            null,
            null,
            TaskLogonInteractiveToken,
            null);
    }

    internal static void Delete(string taskName)
    {
        dynamic service = Connect();
        dynamic folder = service.GetFolder("\\");
        try
        {
            folder.DeleteTask(taskName, 0);
        }
        catch (Exception ex) when (ex.HResult == ErrorFileNotFound)
        {
            // Nothing registered; that is the desired end state.
        }
    }

    /// <summary>Reads the registered task back for logging. Never throws.</summary>
    internal static string Describe(string taskName)
    {
        try
        {
            dynamic service = Connect();
            dynamic folder = service.GetFolder("\\");
            dynamic task = folder.GetTask(taskName);
            dynamic action = task.Definition.Actions.Item(1);
            return $"enabled={task.Enabled} state={task.State} path={action.Path}";
        }
        catch (Exception ex) when (ex.HResult == ErrorFileNotFound)
        {
            return "absent";
        }
        catch (Exception ex)
        {
            return $"unreadable ({ex.GetType().Name})";
        }
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
                   ?? throw new InvalidOperationException("Task Scheduler 服务不可用。");
        dynamic service = Activator.CreateInstance(type)
                          ?? throw new InvalidOperationException("无法创建 Task Scheduler 服务对象。");
        service.Connect();
        return service;
    }

    private static string CurrentUser() => $"{Environment.UserDomainName}\\{Environment.UserName}";
}
