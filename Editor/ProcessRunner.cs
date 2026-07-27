using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  readonly struct ProcessResult
  {
    public readonly int ExitCode;
    public readonly string Output;
    public readonly string Error;

    public ProcessResult(int exitCode, string output, string error)
    {
      ExitCode = exitCode;
      Output = output;
      Error = error;
    }

    public bool Success => ExitCode == 0;
  }

  static class ProcessRunner
  {
    /// <summary>
    /// How often the wait loop wakes to check the cancellation token. Short
    /// enough that pressing cancel in the editor's background-task list feels
    /// immediate, long enough not to spin.
    /// </summary>
    const int PollIntervalMs = 200;

    public static ProcessResult Run(
      string fileName,
      string arguments,
      int timeoutMs = 300_000,
      CancellationToken cancellation = default
    )
    {
      var startInfo = new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      return RunProcess(startInfo, timeoutMs, cancellation);
    }

    public static ProcessResult Run(
      string fileName,
      string[] args,
      int timeoutMs = 300_000,
      CancellationToken cancellation = default
    )
    {
      var startInfo = new ProcessStartInfo
      {
        FileName = fileName,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      foreach (var arg in args)
        startInfo.ArgumentList.Add(arg);
      return RunProcess(startInfo, timeoutMs, cancellation);
    }

    static ProcessResult RunProcess(
      ProcessStartInfo startInfo,
      int timeoutMs,
      CancellationToken cancellation
    )
    {
      var stdout = new StringBuilder();
      var stderr = new StringBuilder();

      using var process = new Process();
      process.StartInfo = startInfo;

      process.OutputDataReceived += (_, e) =>
      {
        if (e.Data != null)
          stdout.AppendLine(e.Data);
      };
      process.ErrorDataReceived += (_, e) =>
      {
        if (e.Data != null)
          stderr.AppendLine(e.Data);
      };

      process.Start();
      process.BeginOutputReadLine();
      process.BeginErrorReadLine();

      if (!WaitForExit(process, timeoutMs, cancellation))
      {
        var reason = cancellation.IsCancellationRequested ? "Process cancelled" : "Process timed out";
        Terminate(process);
        return new ProcessResult(-1, stdout.ToString(), reason);
      }

      // Flush async readers
      process.WaitForExit();

      return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Waits for the process, giving up on either the timeout or a cancellation
    /// request. Returns false when the caller should terminate the process.
    /// </summary>
    static bool WaitForExit(Process process, int timeoutMs, CancellationToken cancellation)
    {
      var clock = Stopwatch.StartNew();

      while (true)
      {
        var remaining = timeoutMs - clock.ElapsedMilliseconds;
        if (remaining <= 0)
          return false;

        if (process.WaitForExit((int)Math.Min(remaining, PollIntervalMs)))
          return true;

        if (cancellation.IsCancellationRequested)
          return false;
      }
    }

    static void Terminate(Process process)
    {
      try
      {
        if (!process.HasExited)
          process.Kill();
      }
      catch (InvalidOperationException)
      {
        // The process exited between the check and the kill — nothing to do.
      }
    }

    public static Task<ProcessResult> RunAsync(
      string fileName,
      string arguments,
      int timeoutMs = 300_000,
      CancellationToken cancellation = default
    )
    {
      return Task.Run(() => Run(fileName, arguments, timeoutMs, cancellation));
    }

    public static Task<ProcessResult> RunAsync(
      string fileName,
      string[] args,
      int timeoutMs = 300_000,
      CancellationToken cancellation = default
    )
    {
      return Task.Run(() => Run(fileName, args, timeoutMs, cancellation));
    }
  }
}
