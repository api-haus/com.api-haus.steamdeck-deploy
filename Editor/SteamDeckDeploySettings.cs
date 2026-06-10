using System;
using System.IO;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  public class SteamDeckDeploySettings : ScriptableObject
  {
    static SteamDeckDeploySettings s_Instance;

    public static SteamDeckDeploySettings Instance
    {
      get
      {
#if UNITY_EDITOR
        if (s_Instance == null)
          s_Instance = FindSettingsAsset();
#endif
        return s_Instance;
      }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Locates the settings asset by type, wherever the consumer keeps it. The
    /// package stores no path: a consumer drops the asset anywhere under Assets/
    /// (the settings provider creates one under Assets/Settings/ on first use)
    /// and it is resolved here through the AssetDatabase type index. The first
    /// match wins when more than one exists.
    /// </summary>
    internal static SteamDeckDeploySettings FindSettingsAsset()
    {
      var guids = UnityEditor.AssetDatabase.FindAssets("t:SteamDeckDeploySettings");
      if (guids.Length == 0)
        return null;

      var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
      return UnityEditor.AssetDatabase.LoadAssetAtPath<SteamDeckDeploySettings>(path);
    }
#endif

    [Header("Connection")]
    [Tooltip("IP address of the Steam Deck on the local network")]
    public string ipAddress = "";

    [Tooltip("SSH username on the Steam Deck")]
    public string username = "deck";

    [Tooltip(
      "Path to devkit_rsa private key. Leave empty to auto-detect from SteamOS Devkit Client"
    )]
    public string sshKeyPath = "";

    [Header("Deployment")]
    [Tooltip("Base path on Steam Deck where games are deployed")]
    public string remoteBasePath = "/home/deck/devkit-game";

    [Tooltip("Additional launch arguments passed to the game executable")]
    public string launchArgs = "";

    [Tooltip("Launch the game on Steam Deck after deploying")]
    public bool launchAfterDeploy = true;

    static readonly string[] KeySearchPaths =
    {
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config/steamos-devkit/devkit_rsa"
      ),
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local/share/steamos-devkit/steamos-devkit/devkit_rsa"
      ),
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "steamos-devkit/steamos-devkit/devkit_rsa"
      ),
    };

    public string ResolvedSshKeyPath
    {
      get
      {
        if (!string.IsNullOrEmpty(sshKeyPath))
          return sshKeyPath;

        foreach (var candidate in KeySearchPaths)
        {
          if (File.Exists(candidate))
            return candidate;
        }

        return "";
      }
    }

    public bool Validate(out string error)
    {
      if (string.IsNullOrWhiteSpace(ipAddress))
      {
        error = "Steam Deck IP address is not configured";
        return false;
      }

      var key = ResolvedSshKeyPath;
      if (string.IsNullOrEmpty(key) || !File.Exists(key))
      {
        error = "SSH key not found. Install SteamOS Devkit Client or set the key path manually";
        return false;
      }

      error = null;
      return true;
    }

    void OnEnable() => s_Instance = this;

    void OnDisable()
    {
      if (s_Instance == this)
        s_Instance = null;
    }
  }
}
