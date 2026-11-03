using System.Security.Principal;
using System.Threading;

namespace TaskbarFold.App.Ipc;

/// <summary>
/// Guards the resident with a per-user named mutex. Two residents would each open their
/// own flyout for the same pinned button and fight over the config file.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;

    private SingleInstance(Mutex mutex, bool isOwner)
    {
        _mutex = mutex;
        IsOwner = isOwner;
    }

    /// <summary>True when this process is the resident; false when one is already running.</summary>
    public bool IsOwner { get; }

    /// <summary>Whether a resident holds the mutex. Answers immediately, without taking it.</summary>
    public static bool IsRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(MutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // It exists but belongs to an elevated resident, which can still be reached.
            return true;
        }
    }

    private static string MutexName
    {
        get
        {
            string sid;
            try
            {
                sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                sid = "unknown";
            }

            // Local\ rather than Global\: the scope is the logon session, matching the pipe.
            return $"Local\\TaskbarFold.Instance.{sid}";
        }
    }

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);

        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // The previous resident was killed. The mutex is ours now.
            owned = true;
        }

        return new SingleInstance(mutex, owned);
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        if (IsOwner)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
