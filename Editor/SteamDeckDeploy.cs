using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
      var gameId = BuildGameId(productName);
      var remoteGameDir = $"{settings.remoteBasePath}/{gameId}";
      var executable = FindExecutable(buildOutputPath, productName);

      // Step 1: Upload helper scripts
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Uploading scripts...", 0.1f);
      if (!await UploadScripts())
        return Fail("Script upload failed");

      // Step 2: Rsync build to Steam Deck
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Uploading build via rsync...", 0.2f);
      if (!await Rsync(buildOutputPath, remoteGameDir))
        return Fail("Rsync failed");

      // Step 2.5: Install launch.sh wrapper on the deck. Steam devkit's
      // create-shortcut IPC tokenizes argv on whitespace, so executables with
      // spaces (e.g. product names with spaces) would otherwise fail to
      // register. Routing through a fixed-name wrapper keeps argv space-free.
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Installing launch wrapper...", 0.55f);
      if (!await WriteLaunchScript(remoteGameDir, executable))
        return Fail("Launch wrapper install failed");

      // Step 3: Register game shortcut (argv[0] = wrapper, extras as separate elements)
      EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Registering game shortcut...", 0.6f);
      var extraArgs = SplitLaunchArgs(settings.launchArgs);
      if (!await RegisterGame(gameId, remoteGameDir, "./launch.sh", extraArgs))
        return Fail("Game registration failed");

      // Step 4: Launch (optional)
      if (launch)
      {
        EditorUtility.DisplayProgressBar("Steam Deck Deploy", "Launching game...", 0.9f);
        if (!await LaunchGame(gameId))
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

    public static async Task<bool> RegisterGame(
      string gameId,
      string remoteGameDir,
      string exe,
      IEnumerable<string> extraArgs = null
    )
    {
      var argvElements = new List<string> { exe };
      if (extraArgs != null)
        argvElements.AddRange(extraArgs.Where(a => !string.IsNullOrEmpty(a)));

      var argvJson =
        "[" + string.Join(",", argvElements.Select(a => "\"" + JsonEscape(a) + "\"")) + "]";
      var parms =
        "{\"gameid\":\"" + JsonEscape(gameId) + "\","
        + "\"directory\":\"" + JsonEscape(remoteGameDir) + "\","
        + "\"argv\":" + argvJson + ","
        + "\"settings\":{\"steam_play\":\"0\"}}";

      var cmd = "python3 ~/devkit-utils/steam-client-create-shortcut --parms " + ShellSingleQuote(parms);

      Debug.Log($"{Tag} Registering shortcut: {gameId} → {exe}");
      return await SshCommand(cmd);
    }

    public static async Task<bool> LaunchGame(string gameId)
    {
      var parms = "{\"gameid\":\"" + JsonEscape(gameId) + "\"}";
      var cmd = $"python3 {RemoteScriptsDir}/unity-run-game --parms " + ShellSingleQuote(parms);

      Debug.Log($"{Tag} Launching: {gameId}");
      return await SshCommand(cmd);
    }

    /// <summary>
    /// Writes a bash launcher to the remote game directory that cd's into the
    /// directory and execs the real game binary. This keeps Steam's devkit IPC
    /// (which tokenizes argv on whitespace) from ever seeing the real exe name,
    /// so product names with spaces or special characters work reliably.
    /// </summary>
    static async Task<bool> WriteLaunchScript(string remoteGameDir, string exeFileName)
    {
      var script =
        "#!/usr/bin/env bash\n"
        + "set -e\n"
        + "cd \"$(dirname \"$(readlink -f \"$0\")\")\"\n"
        + "exec " + ShellSingleQuote("./" + exeFileName) + " \"$@\"\n";

      var launchPath = remoteGameDir + "/launch.sh";
      // Use a heredoc with a sentinel unlikely to appear in the script body.
      // The outer command is already passed to SshCommand as a single argv
      // element, so we only need shell-safe quoting around the target paths.
      var remoteCmd =
        "mkdir -p " + ShellSingleQuote(remoteGameDir) + " && "
        + "cat > " + ShellSingleQuote(launchPath) + " <<'__APIHAUS_LAUNCH_EOF__'\n"
        + script
        + "__APIHAUS_LAUNCH_EOF__\n"
        + "chmod +x " + ShellSingleQuote(launchPath);

      Debug.Log($"{Tag} Installing launch.sh → {launchPath}");
      return await SshCommand(remoteCmd);
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

    /// <summary>
    /// Derives a whitespace- and punctuation-free gameid from a product name.
    /// Used as the Steam shortcut key and as the remote game directory name —
    /// both paths flow through shell commands and Steam URL params that cannot
    /// tolerate spaces. Preserves case, digits, underscores, and hyphens.
    /// </summary>
    static string BuildGameId(string productName)
    {
      if (string.IsNullOrWhiteSpace(productName))
        return "unity_game_Linux";

      var sb = new StringBuilder(productName.Length);
      foreach (var ch in productName)
      {
        if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
          sb.Append(ch);
        else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
          sb.Append('_');
      }

      var slug = sb.ToString().Trim('_');
      if (string.IsNullOrEmpty(slug))
        slug = "unity_game";
      return slug + "_Linux";
    }

    /// <summary>
    /// Splits user-provided launch args on whitespace. Simple split is
    /// sufficient — args containing spaces must be supplied via a settings
    /// field upgrade (not currently exposed).
    /// </summary>
    static string[] SplitLaunchArgs(string raw)
    {
      if (string.IsNullOrWhiteSpace(raw))
        return Array.Empty<string>();
      return raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    static string JsonEscape(string s)
    {
      if (string.IsNullOrEmpty(s))
        return string.Empty;

      var sb = new StringBuilder(s.Length + 2);
      foreach (var c in s)
      {
        switch (c)
        {
          case '"': sb.Append("\\\""); break;
          case '\\': sb.Append("\\\\"); break;
          case '\b': sb.Append("\\b"); break;
          case '\f': sb.Append("\\f"); break;
          case '\n': sb.Append("\\n"); break;
          case '\r': sb.Append("\\r"); break;
          case '\t': sb.Append("\\t"); break;
          default:
            if (c < 0x20)
              sb.Append("\\u").Append(((int)c).ToString("x4"));
            else
              sb.Append(c);
            break;
        }
      }
      return sb.ToString();
    }

    /// <summary>
    /// Wraps a string in single quotes for safe inclusion in a bash command
    /// line. Single quotes inside the input are handled via the standard
    /// '\'' escape sequence.
    /// </summary>
    static string ShellSingleQuote(string s)
    {
      if (s == null)
        return "''";
      return "'" + s.Replace("'", "'\\''") + "'";
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
