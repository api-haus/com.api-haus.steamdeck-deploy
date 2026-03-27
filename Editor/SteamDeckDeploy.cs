using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  public static class SteamDeckDeploy
  {
    const string Tag = "[SteamDeckDeploy]";
    const string RemoteScriptsDir = "~/unity-scripts";

    public static async Task<bool> Deploy(string buildOutputPath, bool launch = true)
    {
      var settings = SteamDeckDeploySettings.Instance;
      if (settings == null)
      {
        Debug.LogError(
          $"{Tag} Settings asset not found. Open Project Settings > Steam Deck Deploy."
        );
        return false;
      }

      // Auto-discover if IP not configured
      if (string.IsNullOrWhiteSpace(settings.ipAddress))
      {
        EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Discovering devices...", 0.05f);
        if (!await AutoDiscover(settings))
          return Fail("No Steam Deck found on network. Enable devkit mode on your Steam Deck.");
      }

      if (!settings.Validate(out var error))
      {
        Debug.LogError($"{Tag} {error}");
        return false;
      }

      buildOutputPath = Path.GetFullPath(buildOutputPath);
      if (!Directory.Exists(buildOutputPath))
      {
        Debug.LogError($"{Tag} Build directory not found: {buildOutputPath}");
        return false;
      }

      var productName = PlayerSettings.productName;
      var remoteGameDir = $"{settings.remoteBasePath}/{productName}_Linux";
      var executable = FindExecutable(buildOutputPath, productName);

      // Step 1: Upload helper scripts
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Uploading scripts...", 0.1f);
      if (!await UploadScripts())
        return Fail("Script upload failed");

      // Step 2: Rsync build to Steam Deck
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Uploading build via rsync...", 0.2f);
      if (!await Rsync(buildOutputPath, remoteGameDir))
        return Fail("Rsync failed");

      // Step 3: Register game shortcut
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Registering game shortcut...", 0.6f);
      var launchArgsFull = $"./{executable}";
      if (!string.IsNullOrEmpty(settings.launchArgs))
        launchArgsFull += $" {settings.launchArgs}";

      if (!await RegisterGame(productName, remoteGameDir, launchArgsFull))
        return Fail("Game registration failed");

      // Step 4: Launch (optional)
      if (launch)
      {
        EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Launching game...", 0.9f);
        if (!await LaunchGame(productName))
          return Fail("Game launch failed");
      }

      EditorUtility.ClearProgressBar();
      Debug.Log($"{Tag} Deploy completed successfully to {settings.ipAddress}");
      return true;
    }

    public static async Task<bool> TestConnection()
    {
      var settings = SteamDeckDeploySettings.Instance;
      if (settings == null)
      {
        Debug.LogError($"{Tag} Settings asset not found");
        return false;
      }

      if (!settings.Validate(out var error))
      {
        Debug.LogError($"{Tag} {error}");
        return false;
      }

      var success = await SshCommand("echo ok");

      if (success)
        Debug.Log($"{Tag} Connection to {settings.ipAddress} successful");

      return success;
    }

    public static async Task<bool> Rsync(string localPath, string remotePath)
    {
      var settings = SteamDeckDeploySettings.Instance;
      var sshKeyPath = settings.ResolvedSshKeyPath;
      var sshCmd = $"ssh -i \"{sshKeyPath}\" -o StrictHostKeyChecking=accept-new";
      var remote = $"{settings.username}@{settings.ipAddress}:{remotePath}/";

      // Trailing slash on source means "copy contents, not directory itself"
      var args =
        $"-avz --delete "
        + $"--exclude=\"*.pdb\" --exclude=\"Saved/\" --exclude=\"*_DoNotShip\" --exclude=\"*_BackUpThisFolder_*\" "
        + $"-e \"{sshCmd}\" "
        + $"\"{localPath}/\" \"{remote}\"";

      Debug.Log($"{Tag} rsync → {settings.ipAddress}:{remotePath}");

      var result = await ProcessRunner.RunAsync("rsync", args);

      if (!result.Success)
        Debug.LogError($"{Tag} rsync failed (exit {result.ExitCode}):\n{result.Error}");
      else
        Debug.Log($"{Tag} rsync complete:\n{result.Output}");

      return result.Success;
    }

    public static async Task<bool> RegisterGame(string gameName, string remoteGameDir, string argv)
    {
      var gameId = $"{gameName}_Linux";

      var parms =
        $"{{\"gameid\":\"{gameId}\","
        + $"\"directory\":\"{remoteGameDir}\","
        + $"\"argv\":[\"{argv}\"],"
        + $"\"settings\":{{\"steam_play\":\"0\"}}}}";

      var cmd = $"python3 ~/devkit-utils/steam-client-create-shortcut --parms '{parms}'";

      Debug.Log($"{Tag} Registering shortcut: {gameId}");
      return await SshCommand(cmd);
    }

    public static async Task<bool> LaunchGame(string gameName)
    {
      var gameId = $"{gameName}_Linux";
      var parms = $"{{\"gameid\":\"{gameId}\"}}";
      var cmd = $"python3 {RemoteScriptsDir}/unity-run-game --parms '{parms}'";

      Debug.Log($"{Tag} Launching: {gameId}");
      return await SshCommand(cmd);
    }

    public static async Task<bool> Push(
      string localPath,
      string remotePath,
      string extraArgs = null
    )
    {
      var settings = SteamDeckDeploySettings.Instance;
      if (!settings.Validate(out var error))
      {
        Debug.LogError($"{Tag} {error}");
        return false;
      }

      var sshCmd = $"ssh -i \"{settings.ResolvedSshKeyPath}\" -o StrictHostKeyChecking=accept-new";
      var remote = $"{settings.username}@{settings.ipAddress}:{remotePath}/";

      var args = $"-avz {extraArgs ?? ""} -e \"{sshCmd}\" \"{localPath}/\" \"{remote}\"".Trim();

      Debug.Log($"{Tag} push → {settings.ipAddress}:{remotePath}");
      var result = await ProcessRunner.RunAsync("rsync", args);

      if (!result.Success)
        Debug.LogError($"{Tag} push failed (exit {result.ExitCode}):\n{result.Error}");

      return result.Success;
    }

    public static async Task<bool> Pull(
      string remotePath,
      string localPath,
      string extraArgs = null
    )
    {
      var settings = SteamDeckDeploySettings.Instance;
      if (!settings.Validate(out var error))
      {
        Debug.LogError($"{Tag} {error}");
        return false;
      }

      var sshCmd = $"ssh -i \"{settings.ResolvedSshKeyPath}\" -o StrictHostKeyChecking=accept-new";
      var remote = $"{settings.username}@{settings.ipAddress}:{remotePath}/";

      Directory.CreateDirectory(localPath);

      var args = $"-avz {extraArgs ?? ""} -e \"{sshCmd}\" \"{remote}\" \"{localPath}/\"".Trim();

      Debug.Log($"{Tag} pull ← {settings.ipAddress}:{remotePath}");
      var result = await ProcessRunner.RunAsync("rsync", args);

      if (!result.Success)
        Debug.LogError($"{Tag} pull failed (exit {result.ExitCode}):\n{result.Error}");

      return result.Success;
    }

    public static async Task<(bool success, string output)> Ssh(string command, int timeoutMs = 30_000)
    {
      var settings = SteamDeckDeploySettings.Instance;
      if (!settings.Validate(out var error))
      {
        Debug.LogError($"{Tag} {error}");
        return (false, error);
      }

      var result = await ProcessRunner.RunAsync(
        "ssh",
        new[]
        {
          "-i", settings.ResolvedSshKeyPath,
          "-o", "StrictHostKeyChecking=accept-new",
          "-o", "ConnectTimeout=10",
          $"{settings.username}@{settings.ipAddress}",
          command,
        },
        timeoutMs
      );

      if (!result.Success)
        Debug.LogError($"{Tag} SSH failed (exit {result.ExitCode}):\n{result.Error}");

      return (result.Success, result.Output);
    }

    static async Task<bool> UploadScripts()
    {
      // Find Scripts~ directory relative to the package
      var scriptsDir = Path.GetFullPath(
        Path.Combine("Packages", "com.api-haus.steamdeck-deploy", "Scripts~")
      );

      if (!Directory.Exists(scriptsDir))
      {
        Debug.LogError($"{Tag} Scripts~ directory not found at: {scriptsDir}");
        return false;
      }

      Debug.Log($"{Tag} Uploading helper scripts to {RemoteScriptsDir}");
      return await Rsync(scriptsDir, RemoteScriptsDir);
    }

    static async Task<bool> SshCommand(string command)
    {
      var settings = SteamDeckDeploySettings.Instance;
      var keyPath = settings.ResolvedSshKeyPath;

      // Use ArgumentList to avoid all shell quoting issues — each element is passed as-is
      var result = await ProcessRunner.RunAsync(
        "ssh",
        new[]
        {
          "-i",
          keyPath,
          "-o",
          "StrictHostKeyChecking=accept-new",
          "-o",
          "ConnectTimeout=10",
          $"{settings.username}@{settings.ipAddress}",
          command,
        },
        30_000
      );

      if (!result.Success)
        Debug.LogError($"{Tag} SSH command failed (exit {result.ExitCode}):\n{result.Error}");

      return result.Success;
    }

    internal static async Task<bool> AutoDiscover(SteamDeckDeploySettings settings)
    {
      var devices = await DevkitDiscovery.Discover();
      if (devices.Count == 0)
        return false;

      var device = devices[0];
      settings.ipAddress = device.Address;
      settings.username = device.Username;
      EditorUtility.SetDirty(settings);
      AssetDatabase.SaveAssets();

      Debug.Log(
        $"{Tag} Auto-discovered Steam Deck: {device.Name} at {device.Address} (user: {device.Username})"
      );
      return true;
    }

    static string FindExecutable(string buildDir, string productName)
    {
      // Look for the executable: .x86_64 (Linux), .exe (Windows/Proton), or bare name (macOS)
      string[] extensions = { ".x86_64", ".exe", "" };
      foreach (var ext in extensions)
      {
        var candidate = Path.Combine(buildDir, productName + ext);
        if (File.Exists(candidate))
          return productName + ext;
      }

      // Fallback: first .x86_64 file in the directory
      foreach (var file in Directory.GetFiles(buildDir, "*.x86_64"))
        return Path.GetFileName(file);

      return $"{productName}.x86_64";
    }

    static bool Fail(string message)
    {
      EditorUtility.ClearProgressBar();
      Debug.LogError($"{Tag} {message}");
      return false;
    }
  }
}
