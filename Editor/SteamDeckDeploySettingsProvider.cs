using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  [InitializeOnLoad]
  static class SteamDeckDeploySettingsProvider
  {
    [SettingsProvider]
    static SettingsProvider Create()
    {
      return new SettingsProvider("Project/Steam Deck Deploy", SettingsScope.Project)
      {
        label = "Steam Deck Deploy",
        guiHandler = ctx =>
        {
          var settings = GetOrCreateSettings();
          var so = new SerializedObject(settings);
          so.Update();

          EditorGUILayout.PropertyField(so.FindProperty("ipAddress"));
          EditorGUILayout.PropertyField(so.FindProperty("username"));

          if (string.IsNullOrWhiteSpace(settings.ipAddress))
          {
            if (GUILayout.Button("Discover Steam Deck on Network", GUILayout.Width(240)))
              DiscoverDevice(settings);
          }

          EditorGUILayout.PropertyField(so.FindProperty("sshKeyPath"));

          var resolvedKey = settings.ResolvedSshKeyPath;
          if (string.IsNullOrEmpty(settings.sshKeyPath) && !string.IsNullOrEmpty(resolvedKey))
            EditorGUILayout.HelpBox($"Auto-detected: {resolvedKey}", MessageType.Info);
          else if (string.IsNullOrEmpty(resolvedKey))
            EditorGUILayout.HelpBox(
              "SSH key not found. Install SteamOS Devkit Client or set the path manually.",
              MessageType.Warning
            );

          EditorGUILayout.Space();
          EditorGUILayout.PropertyField(so.FindProperty("remoteBasePath"));
          EditorGUILayout.PropertyField(so.FindProperty("launchArgs"));
          EditorGUILayout.PropertyField(so.FindProperty("launchAfterDeploy"));

          so.ApplyModifiedProperties();

          EditorGUILayout.Space();

          using (new EditorGUILayout.HorizontalScope())
          {
            if (GUILayout.Button("Test Connection", GUILayout.Width(140)))
              TestConnection(settings);

            if (GUILayout.Button("Deploy Now", GUILayout.Width(140)))
              DeployNow(settings);
          }
        },
        keywords = new HashSet<string> { "Steam", "Deck", "Deploy", "SSH", "rsync", "devkit" },
      };
    }

    static async void TestConnection(SteamDeckDeploySettings settings)
    {
      if (!settings.Validate(out var error))
      {
        EditorUtility.DisplayDialog("Steam Deck Deploy", error, "OK");
        return;
      }

      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Testing connection...", 0.5f);
      try
      {
        var success = await SteamDeckDeploy.TestConnection();
        EditorUtility.DisplayDialog(
          "Steam Deck Deploy",
          success ? "Connection successful" : "Connection failed — check Console for details",
          "OK"
        );
      }
      finally
      {
        EditorUtility.ClearProgressBar();
      }
    }

    static async void DeployNow(SteamDeckDeploySettings settings)
    {
      if (!settings.Validate(out var error))
      {
        EditorUtility.DisplayDialog("Steam Deck Deploy", error, "OK");
        return;
      }

      var location = EditorUserBuildSettings.GetBuildLocation(
        EditorUserBuildSettings.activeBuildTarget
      );
      if (string.IsNullOrEmpty(location))
      {
        EditorUtility.DisplayDialog("Steam Deck Deploy", "No previous build found. Build first.", "OK");
        return;
      }

      var buildDir = Path.GetDirectoryName(Path.GetFullPath(location));
      if (!Directory.Exists(buildDir))
      {
        EditorUtility.DisplayDialog("Steam Deck Deploy", $"Build directory not found: {buildDir}", "OK");
        return;
      }

      await SteamDeckDeploy.Deploy(buildDir, settings.launchAfterDeploy);
    }

    static async void DiscoverDevice(SteamDeckDeploySettings settings)
    {
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Searching for devices...", 0.5f);
      try
      {
        var success = await SteamDeckDeploy.AutoDiscover(settings);
        if (!success)
          EditorUtility.DisplayDialog(
            "Steam Deck Deploy",
            "No Steam Deck found. Ensure devkit mode is enabled and the device is on the same network.",
            "OK"
          );
      }
      finally
      {
        EditorUtility.ClearProgressBar();
      }
    }

    internal static SteamDeckDeploySettings GetOrCreateSettings()
    {
      var settings = AssetDatabase.LoadAssetAtPath<SteamDeckDeploySettings>(
        SteamDeckDeploySettings.AssetPath
      );
      if (settings == null)
      {
        settings = ScriptableObject.CreateInstance<SteamDeckDeploySettings>();
        var dir = Path.GetDirectoryName(SteamDeckDeploySettings.AssetPath);
        if (!Directory.Exists(dir))
          Directory.CreateDirectory(dir);
        AssetDatabase.CreateAsset(settings, SteamDeckDeploySettings.AssetPath);
        AssetDatabase.SaveAssets();
      }

      return settings;
    }
  }
}
