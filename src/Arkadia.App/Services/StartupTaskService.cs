using System.Runtime.InteropServices;
using System.Security.Principal;
using System.IO;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Serilog;

namespace Arkadia.App.Services;

public sealed class StartupTaskService : Core.Services.IStartupTaskService
{
    private const string TaskName = "ARKADIA Startup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ARKADIA";

    public System.Threading.Tasks.Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var taskService = new TaskService();
            var task = taskService.GetTask(TaskName);
            if (task is not null)
            {
                return System.Threading.Tasks.Task.FromResult(task.Enabled);
            }
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Task Scheduler could not read the ARKADIA logon task; checking the per-user Run key.");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Task Scheduler access was denied; checking the per-user Run key.");
        }
        catch (InvalidOperationException exception)
        {
            Log.Warning(exception, "Task Scheduler could not read the ARKADIA logon task; checking the per-user Run key.");
        }
        catch (FileNotFoundException exception)
        {
            Log.Warning(exception, "Task Scheduler is unavailable; checking the per-user Run key.");
        }

        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return System.Threading.Tasks.Task.FromResult(runKey?.GetValue(RunValueName) is string);
    }

    public System.Threading.Tasks.Task SetEnabledAsync(
        bool enabled,
        bool startMinimized,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var taskService = new TaskService();
            if (enabled)
            {
                RegisterTask(taskService, startMinimized);
            }
            else
            {
                taskService.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
            }

            RemoveRunValue();
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Task Scheduler could not update the ARKADIA logon task; using the per-user Run key.");
            SetRunValue(enabled, startMinimized);
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Task Scheduler access was denied; using the per-user Run key.");
            SetRunValue(enabled, startMinimized);
        }
        catch (InvalidOperationException exception)
        {
            Log.Warning(exception, "Task Scheduler rejected the ARKADIA task; using the per-user Run key.");
            SetRunValue(enabled, startMinimized);
        }

        return System.Threading.Tasks.Task.CompletedTask;
    }

    private static void RegisterTask(TaskService taskService, bool startMinimized)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        var userName = WindowsIdentity.GetCurrent().Name;
        var definition = taskService.NewTask();
        definition.RegistrationInfo.Description = "Starts ARKADIA for the current Windows user.";
        definition.Principal.UserId = userName;
        definition.Principal.LogonType = TaskLogonType.InteractiveToken;
        definition.Principal.RunLevel = TaskRunLevel.LUA;
        definition.Triggers.Add(new LogonTrigger
        {
            UserId = userName,
            Delay = TimeSpan.FromSeconds(15)
        });
        definition.Actions.Add(new ExecAction(
            executablePath,
            startMinimized ? "--minimized" : null,
            AppContext.BaseDirectory));
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        taskService.RootFolder.RegisterTaskDefinition(
            TaskName,
            definition,
            TaskCreation.CreateOrUpdate,
            userName,
            null,
            TaskLogonType.InteractiveToken);
    }

    private static void SetRunValue(bool enabled, bool startMinimized)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the current-user startup registry key.");
        if (enabled)
        {
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            runKey.SetValue(
                RunValueName,
                startMinimized ? $"\"{executablePath}\" --minimized" : $"\"{executablePath}\"");
        }
        else
        {
            runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private static void RemoveRunValue()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
    }
}
