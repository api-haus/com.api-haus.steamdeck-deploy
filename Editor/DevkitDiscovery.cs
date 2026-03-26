using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace ApiHaus.SteamDeckDeploy.Editor
{
  struct DevkitDevice
  {
    public string Name;
    public string Address;
    public int Port;
    public string Username;
    public string Hostname;
  }

  static class DevkitDiscovery
  {
    const string Tag = "[SteamDeckDeploy]";
    const string ServiceType = "_steamos-devkit._tcp";

    public static async Task<List<DevkitDevice>> Discover(int timeoutMs = 5000)
    {
      var result = await ProcessRunner.RunAsync(
        "avahi-browse",
        $"-t -r -p {ServiceType}",
        timeoutMs
      );

      if (!result.Success)
      {
        Debug.LogWarning($"{Tag} avahi-browse failed (is avahi-daemon running?): {result.Error}");
        return new List<DevkitDevice>();
      }

      return Parse(result.Output);
    }

    internal static List<DevkitDevice> Parse(string output)
    {
      // avahi-browse -p (parseable) output format:
      // =;interface;protocol;name;type;domain;hostname;address;port;txt
      var devices = new List<DevkitDevice>();
      var seen = new HashSet<string>();

      foreach (var line in output.Split('\n'))
      {
        if (!line.StartsWith("="))
          continue;

        var fields = line.Split(';');
        if (fields.Length < 10)
          continue;

        var address = fields[7];
        if (seen.Contains(address))
          continue;
        seen.Add(address);

        var device = new DevkitDevice
        {
          Name = fields[3],
          Hostname = fields[6],
          Address = address,
          Port = int.TryParse(fields[8], out var p) ? p : 0,
          Username = ParseTxtField(fields[9], "login") ?? "deck",
        };

        devices.Add(device);
      }

      return devices;
    }

    internal static string ParseTxtField(string txt, string key)
    {
      // TXT records look like: "key1=val1" "key2=val2"
      var pattern = $"\"{Regex.Escape(key)}=([^\"]*)\"";
      var match = Regex.Match(txt, pattern);
      return match.Success ? match.Groups[1].Value : null;
    }
  }
}
