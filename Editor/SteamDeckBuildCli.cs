using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  /// <summary>
  /// CLI entry points for build + deploy via unity-cli exec.
  /// Uses EditorApplication.update to defer blocking BuildPlayer, then chains async deploy.
  /// Poll the result file for completion.
  /// </summary>
  public static class SteamDeckBuildCli
  {
    const string ResultFile = "Builds/Linux/.build_result";
    const string Tag = "[SteamDeckBuildCli]";

    /// <summary>
    /// Build dev profile + deploy to Steam Deck. Returns "started" immediately.
    /// Poll Builds/Linux/.build_result for "Succeeded" or "FAILED: ...".
    /// </summary>
    public static string BuildAndDeploy(bool launch = true)
    {
      if (File.Exists(ResultFile))
        File.Delete(ResultFile);

      var capturedLaunch = launch;
      void Once()
      {
        EditorApplication.update -= Once;
        RunBuildAndDeploy(capturedLaunch);
      }
      EditorApplication.update += Once;
      return "started";
    }

    static async void RunBuildAndDeploy(bool launch)
    {
      var profile = BuildProfile.GetActiveBuildProfile();
      if (profile == null)
      {
        WriteResult("FAILED: no active build profile");
        return;
      }

      var target = EditorUserBuildSettings.activeBuildTarget;
      var outputPath = target switch
      {
        BuildTarget.StandaloneWindows64 => "Builds/Windows/woweyreey.exe",
        BuildTarget.StandaloneOSX => "Builds/macOS/woweyreey",
        BuildTarget.StandaloneLinux64 => "Builds/Linux/woweyreey.x86_64",
        _ => $"Builds/{target}/woweyreey",
      };

      Debug.Log($"{Tag} Building with active profile: {profile.name}");

      var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
      {
        buildProfile = profile,
        locationPathName = outputPath,
        options = BuildOptions.None,
      });

      if (report.summary.result != BuildResult.Succeeded)
      {
        Debug.LogError($"{Tag} Build failed: {report.summary.result}");
        WriteResult($"FAILED: build {report.summary.result}");
        return;
      }

      Debug.Log($"{Tag} Build succeeded, deploying to Steam Deck...");

      var buildDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
      var deployed = await SteamDeckDeploy.Deploy(buildDir, launch);

      if (deployed)
        Debug.Log($"{Tag} Deploy completed successfully");
      else
        Debug.LogError($"{Tag} Deploy failed");

      WriteResult(deployed ? "Succeeded" : "FAILED: deploy");
    }

    static void WriteResult(string result)
    {
      var dir = Path.GetDirectoryName(ResultFile);
      if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
      File.WriteAllText(ResultFile, result);
    }
  }
}
