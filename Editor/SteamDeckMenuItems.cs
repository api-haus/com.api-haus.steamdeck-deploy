using UnityEditor;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  static class SteamDeckMenuItems
  {
    [MenuItem("Build/Build Current Profile to Steam Deck")]
    static async void BuildCurrentProfileToSteamDeck()
    {
      try
      {
        var settings = SteamDeckDeploySettingsProvider.GetOrCreateSettings();
        await SteamDeckDeploy.BuildActiveProfileAndDeploy(settings.launchAfterDeploy);
      }
      catch (System.Exception e)
      {
        EditorUtility.ClearProgressBar();
        Debug.LogException(e);
      }
    }
  }
}
