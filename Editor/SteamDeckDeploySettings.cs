using System;
using System.IO;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  public class SteamDeckDeploySettings : ScriptableObject
  {
    internal const string AssetPath =
      "Assets/Settings/SteamDeckDeploy/SteamDeckDeploySettings.asset";

    static SteamDeckDeploySettings s_Instance;

    public static SteamDeckDeploySettings Instance
    {
      get
      {
#if UNITY_EDITOR
        if (s_Instance == null)
          s_Instance = UnityEditor.AssetDatabase.LoadAssetAtPath<SteamDeckDeploySettings>(
            AssetPath
          );
#endif
        return s_Instance;
      }
    }

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
