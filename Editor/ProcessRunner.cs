using System;
using System.Diagnostics;
using System.Text;
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
    public static ProcessResult Run(string fileName, string arguments, int timeoutMs = 300_000)
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
      return RunProcess(startInfo, timeoutMs);
    }

    public static ProcessResult Run(string fileName, string[] args, int timeoutMs = 300_000)
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
      return RunProcess(startInfo, timeoutMs);
    }

    static ProcessResult RunProcess(ProcessStartInfo startInfo, int timeoutMs)
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

      if (!process.WaitForExit(timeoutMs))
      {
        process.Kill();
        return new ProcessResult(-1, stdout.ToString(), "Process timed out");
      }

      // Flush async readers
      process.WaitForExit();

      return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public static Task<ProcessResult> RunAsync(
      string fileName,
      string arguments,
      int timeoutMs = 300_000
    )
    {
      return Task.Run(() => Run(fileName, arguments, timeoutMs));
    }

    public static Task<ProcessResult> RunAsync(
      string fileName,
      string[] args,
      int timeoutMs = 300_000
    )
    {
      return Task.Run(() => Run(fileName, args, timeoutMs));
    }
  }
}
