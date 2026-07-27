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

          // A deploy no longer blocks the editor, so these stay reachable while
          // one is running. Disable them for the duration instead.
          using (new EditorGUI.DisabledScope(DeployOperation.IsActive))
          using (new EditorGUILayout.HorizontalScope())
          {
            if (GUILayout.Button("Test Connection", GUILayout.Width(140)))
              TestConnection(settings);

            if (GUILayout.Button("Deploy Now", GUILayout.Width(140)))
              DeployNow(settings);
          }

          if (DeployOperation.IsActive)
            EditorGUILayout.HelpBox(
              "A deploy is running. Track or cancel it from the background task in the status bar.",
              MessageType.Info
            );
        },
        keywords = new HashSet<string> { "Steam", "Deck", "Deploy", "SSH", "rsync", "devkit" },
      };
    }

    // Progress for every one of these lives in SteamDeckDeploy, on a non-modal
    // UnityEditor.Progress indicator that a domain reload cannot strand. See
    // DeployOperation for why a modal progress bar cannot be used here.
    static async void TestConnection(SteamDeckDeploySettings settings)
    {
      if (!settings.Validate(out var error))
      {
        EditorUtility.DisplayDialog("Steam Deck Deploy", error, "OK");
        return;
      }

      var success = await SteamDeckDeploy.TestConnection();
      SteamDeckResultDialog.Show(
        "Steam Deck Deploy",
        success ? "Connection successful" : "Connection failed — check Console for details",
        success
      );
    }

    static async void DeployNow(SteamDeckDeploySettings settings)
    {
      if (DeployOperation.RejectIfBusy())
        return;

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
      if (DeployOperation.RejectIfBusy())
        return;

      var success = await SteamDeckDeploy.AutoDiscover(settings);
      if (!success)
        EditorUtility.DisplayDialog(
          "Steam Deck Deploy",
          "No Steam Deck found. Ensure devkit mode is enabled and the device is on the same network.",
          "OK"
        );
    }

    // Consumer-side default home for a freshly created settings asset. Creation
    // lands here; resolution is by type (FindSettingsAsset), so a consumer may
    // move the asset anywhere under Assets/ without the package needing to know.
    const string DefaultSettingsFolder = "Assets/Settings";
    const string DefaultSettingsPath = DefaultSettingsFolder + "/SteamDeckDeploySettings.asset";

    internal static SteamDeckDeploySettings GetOrCreateSettings()
    {
      var settings = SteamDeckDeploySettings.FindSettingsAsset();
      if (settings == null)
      {
        settings = ScriptableObject.CreateInstance<SteamDeckDeploySettings>();
        if (!AssetDatabase.IsValidFolder(DefaultSettingsFolder))
          AssetDatabase.CreateFolder("Assets", "Settings");
        AssetDatabase.CreateAsset(settings, DefaultSettingsPath);
        AssetDatabase.SaveAssets();
      }

      return settings;
    }
  }
}
