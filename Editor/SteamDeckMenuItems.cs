using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  static class SteamDeckMenuItems
  {
    [MenuItem("Build/Deploy to Steam Deck")]
    static async void DeployToSteamDeck()
    {
      var buildDir = GetLastBuildDirectory();
      if (string.IsNullOrEmpty(buildDir))
      {
        EditorUtility.DisplayDialog("Steam Deck Deploy", "No previous build found. Use Build and Deploy.", "OK");
        return;
      }

      try
      {
        var settings = SteamDeckDeploySettingsProvider.GetOrCreateSettings();
        await SteamDeckDeploy.Deploy(buildDir, settings.launchAfterDeploy);
      }
      catch (System.Exception e)
      {
        EditorUtility.ClearProgressBar();
        Debug.LogException(e);
      }
    }

    [MenuItem("Build/Deploy to Steam Deck", validate = true)]
    static bool ValidateDeployToSteamDeck()
    {
      var dir = GetLastBuildDirectory();
      return !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
    }

    [MenuItem("Build/Build and Deploy to Steam Deck")]
    static async void BuildAndDeployToSteamDeck()
    {
      var profile = BuildProfile.GetActiveBuildProfile();
      if (profile == null)
      {
        Debug.LogError("[SteamDeckDeploy] No active build profile found");
        return;
      }

      var target = EditorUserBuildSettings.activeBuildTarget;
      var lastLocation = EditorUserBuildSettings.GetBuildLocation(target);
      if (string.IsNullOrEmpty(lastLocation))
      {
        // Fallback: construct a reasonable default
        lastLocation = $"Builds/{target}/{PlayerSettings.productName}";
      }

      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Building...", 0.1f);

      var report = BuildPipeline.BuildPlayer(
        new BuildPlayerWithProfileOptions
        {
          buildProfile = profile,
          locationPathName = lastLocation,
          options = BuildOptions.None,
        }
      );

      if (report.summary.result != BuildResult.Succeeded)
      {
        EditorUtility.ClearProgressBar();
        Debug.LogError($"[SteamDeckDeploy] Build failed: {report.summary.result}");
        return;
      }

      var buildDir = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));

      try
      {
        var settings = SteamDeckDeploySettingsProvider.GetOrCreateSettings();
        await SteamDeckDeploy.Deploy(buildDir, settings.launchAfterDeploy);
      }
      catch (System.Exception e)
      {
        EditorUtility.ClearProgressBar();
        Debug.LogException(e);
      }
    }

    static string GetLastBuildDirectory()
    {
      var location = EditorUserBuildSettings.GetBuildLocation(
        EditorUserBuildSettings.activeBuildTarget
      );
      if (string.IsNullOrEmpty(location))
        return null;

      // GetBuildLocation returns the executable path — we need the directory
      var dir = Path.GetDirectoryName(Path.GetFullPath(location));
      return dir;
    }
  }
}
