using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  /// <summary>
  /// A non-blocking progress scope covering one long-running deploy operation.
  ///
  /// The upload spends minutes inside awaits. An editor domain reload during
  /// that window tears down the AppDomain along with every pending continuation,
  /// so cleanup scheduled after an await never runs. A modal
  /// EditorUtility.DisplayProgressBar opened before such an await is therefore
  /// unclosable: it is drawn native-side and outlives the managed code that was
  /// supposed to clear it, leaving the editor blocked until restart.
  ///
  /// UnityEditor.Progress avoids both halves of that. Its indicator lives in the
  /// status bar, so nothing is blocked while it is up, and a Managed indicator
  /// is removed by Unity itself when a domain reload happens. It also carries a
  /// cancel button, wired here to a token that <see cref="ProcessRunner"/>
  /// honours by killing the child process.
  ///
  /// Scopes nest by depth: BuildActiveTargetAndDeploy opens one operation and
  /// the Deploy call inside it reports into the same indicator.
  /// </summary>
  sealed class DeployOperation : IDisposable
  {
    internal const string ActiveKey = "ApiHaus.SteamDeckDeploy.OperationActive";

    static DeployOperation s_Current;
    static int s_Depth;

    readonly int m_ProgressId;
    readonly CancellationTokenSource m_Cancellation;
    bool m_Failed;

    DeployOperation(string title, string description)
    {
      m_Cancellation = new CancellationTokenSource();
      m_ProgressId = Progress.Start(title, description, Progress.Options.Managed);

      // An upload reports progress only between steps, which can be minutes
      // apart. Normal priority would have Unity paint it as unresponsive after
      // five silent seconds; Low is displayed the same way but never marked so.
      Progress.SetPriority(m_ProgressId, Progress.Priority.Low);
      Progress.SetTimeDisplayMode(m_ProgressId, Progress.TimeDisplayMode.ShowRunningTime);
      Progress.RegisterCancelCallback(m_ProgressId, RequestCancel);

      SessionState.SetBool(ActiveKey, true);
    }

    /// <summary>
    /// Opens the operation, or joins the one already open. The returned scope is
    /// always safe to dispose — only the outermost disposal finishes the
    /// indicator.
    /// </summary>
    public static DeployOperation Begin(string title, string description)
    {
      s_Depth++;

      if (s_Current == null)
        s_Current = new DeployOperation(title, description);
      else
        s_Current.Describe(description);

      return s_Current;
    }

    public static bool IsActive => s_Current != null;

    /// <summary>
    /// Guards an entry point against a second concurrent run, and says so in the
    /// console. The editor stays interactive during a deploy now, so a user can
    /// reach the menu item again while one is still uploading.
    /// </summary>
    public static bool RejectIfBusy()
    {
      if (s_Current == null)
        return false;

      Debug.LogWarning(
        "[SteamDeckDeploy] A deploy is already running. Watch or cancel it from the "
        + "background task in the status bar."
      );
      return true;
    }

    public CancellationToken Token => m_Cancellation.Token;

    public bool IsCancelled => m_Cancellation.IsCancellationRequested;

    public void Report(float progress, string description)
    {
      if (Progress.Exists(m_ProgressId))
        Progress.Report(m_ProgressId, progress, description);
    }

    public void Describe(string description)
    {
      if (Progress.Exists(m_ProgressId))
        Progress.SetDescription(m_ProgressId, description);
    }

    /// <summary>Marks the operation as failed, so the indicator finishes red.</summary>
    public void Fail() => m_Failed = true;

    public void Dispose()
    {
      if (--s_Depth > 0)
        return;

      s_Depth = 0;
      s_Current = null;
      SessionState.EraseBool(ActiveKey);

      if (Progress.Exists(m_ProgressId))
      {
        Progress.UnregisterCancelCallback(m_ProgressId);
        Progress.Finish(m_ProgressId, FinalStatus());
      }

      m_Cancellation.Dispose();
    }

    bool RequestCancel()
    {
      m_Cancellation.Cancel();
      return true;
    }

    Progress.Status FinalStatus()
    {
      if (m_Cancellation.IsCancellationRequested)
        return Progress.Status.Canceled;

      return m_Failed ? Progress.Status.Failed : Progress.Status.Succeeded;
    }
  }

  /// <summary>
  /// Defers editor domain reload and asset auto-refresh for the lifetime of the
  /// scope. This is the protection that keeps a deploy alive across the editor
  /// regaining focus, an import worker finishing, or a script edit landing
  /// mid-upload — all of which otherwise reload the domain and strand the
  /// deploy's continuations on a thread pool whose results nothing will read.
  ///
  /// Unity refcounts both locks, and this struct refcounts the SessionState
  /// breadcrumb on top, so nested scopes are safe. A deferred reload fires as
  /// soon as the outermost scope disposes.
  /// </summary>
  readonly struct ReloadLock : IDisposable
  {
    internal const string HeldKey = "ApiHaus.SteamDeckDeploy.ReloadLockHeld";

    static int s_Depth;

    public static ReloadLock Acquire()
    {
      if (s_Depth++ == 0)
      {
        EditorApplication.LockReloadAssemblies();
        AssetDatabase.DisallowAutoRefresh();
        SessionState.SetBool(HeldKey, true);
      }

      return default;
    }

    public void Dispose()
    {
      if (--s_Depth > 0)
        return;

      s_Depth = 0;
      SessionState.EraseBool(HeldKey);
      AssetDatabase.AllowAutoRefresh();
      EditorApplication.UnlockReloadAssemblies();
    }
  }

  /// <summary>
  /// Releases state that a domain reload could have stranded. SessionState
  /// survives a reload, so a breadcrumb still set when this runs means the
  /// domain died while a deploy held it.
  /// </summary>
  static class DeployRecovery
  {
    const string Tag = "[SteamDeckDeploy]";

    [InitializeOnLoadMethod]
    static void ReleaseStrandedState()
    {
      if (SessionState.GetBool(ReloadLock.HeldKey, false))
      {
        // ReloadLock is meant to make this unreachable. If a reload got through
        // anyway, the native lock count outlived the managed scope that would
        // have released it, and every later reload would stay blocked.
        SessionState.EraseBool(ReloadLock.HeldKey);
        try
        {
          AssetDatabase.AllowAutoRefresh();
          EditorApplication.UnlockReloadAssemblies();
        }
        catch (Exception e)
        {
          Debug.LogWarning($"{Tag} Could not release a stranded assembly-reload lock: {e.Message}");
        }
      }

      if (!SessionState.GetBool(DeployOperation.ActiveKey, false))
        return;

      SessionState.EraseBool(DeployOperation.ActiveKey);

      // A modal progress bar is drawn native-side and outlives the reload that
      // killed its owner, so nothing else is left that can close it.
      EditorUtility.ClearProgressBar();

      Debug.LogWarning(
        $"{Tag} A deploy was interrupted by a domain reload and did not finish. "
        + "The build on the Steam Deck may be incomplete — run the deploy again."
      );
    }
  }
}
