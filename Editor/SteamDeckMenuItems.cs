using UnityEditor;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  static class SteamDeckMenuItems
  {
    [MenuItem("Build/Build & Deploy to Steam Deck")]
    static async void BuildAndDeployToSteamDeck()
    {
      if (DeployOperation.RejectIfBusy())
        return;

      try
      {
        var settings = SteamDeckDeploySettingsProvider.GetOrCreateSettings();
        await SteamDeckDeploy.BuildActiveTargetAndDeploy(settings.launchAfterDeploy);
      }
      catch (System.Exception e)
      {
        Debug.LogException(e);
      }
    }

    [MenuItem("Build/Build Current Profile to Steam Deck")]
    static async void BuildCurrentProfileToSteamDeck()
    {
      if (DeployOperation.RejectIfBusy())
        return;

      try
      {
        var settings = SteamDeckDeploySettingsProvider.GetOrCreateSettings();
        await SteamDeckDeploy.BuildActiveProfileAndDeploy(settings.launchAfterDeploy);
      }
      catch (System.Exception e)
      {
        Debug.LogException(e);
      }
    }
  }
}
