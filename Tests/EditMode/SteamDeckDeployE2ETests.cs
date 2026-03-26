using System.IO;
using System.Threading.Tasks;
using ApiHaus.SteamDeckDeploy.Editor;
using NUnit.Framework;

[TestFixture]
public class SteamDeckDeployE2ETests
{
  [Test]
  public void Parse_RealAvahiBrowseOutput_ExtractsDevice()
  {
    // Real avahi-browse -t -r -p output captured from a Steam Deck on the network
    const string output =
      "+;wlan0;IPv4;steamdeck;_steamos-devkit._tcp;local\n"
      + "=;wlan0;IPv4;steamdeck;_steamos-devkit._tcp;local;steamdeck.local;192.168.50.214;32000;"
      + "\"devkit1=devkit-1\" \"login=deck\" \"settings={}\" \"txtvers=1\"\n";

    var devices = DevkitDiscovery.Parse(output);

    Assert.AreEqual(1, devices.Count);
    Assert.AreEqual("steamdeck", devices[0].Name);
    Assert.AreEqual("192.168.50.214", devices[0].Address);
    Assert.AreEqual(32000, devices[0].Port);
    Assert.AreEqual("deck", devices[0].Username);
    Assert.AreEqual("steamdeck.local", devices[0].Hostname);
  }

  [Test]
  public void Parse_MultipleDevices_DeduplicatesByAddress()
  {
    const string output =
      "=;wlan0;IPv4;deck1;_steamos-devkit._tcp;local;deck1.local;10.0.0.1;32000;\"login=deck\" \"txtvers=1\"\n"
      + "=;eth0;IPv4;deck1;_steamos-devkit._tcp;local;deck1.local;10.0.0.1;32000;\"login=deck\" \"txtvers=1\"\n"
      + "=;wlan0;IPv4;deck2;_steamos-devkit._tcp;local;deck2.local;10.0.0.2;32000;\"login=gamer\" \"txtvers=1\"\n";

    var devices = DevkitDiscovery.Parse(output);

    Assert.AreEqual(2, devices.Count);
    Assert.AreEqual("10.0.0.1", devices[0].Address);
    Assert.AreEqual("10.0.0.2", devices[1].Address);
    Assert.AreEqual("gamer", devices[1].Username);
  }

  [Test]
  public void Parse_EmptyOutput_ReturnsEmpty()
  {
    var devices = DevkitDiscovery.Parse("");
    Assert.AreEqual(0, devices.Count);
  }

  [Test]
  public void Parse_NoResolvedEntries_ReturnsEmpty()
  {
    // Only + lines (browse hits), no = lines (resolved)
    const string output = "+;wlan0;IPv4;steamdeck;_steamos-devkit._tcp;local\n";

    var devices = DevkitDiscovery.Parse(output);
    Assert.AreEqual(0, devices.Count);
  }

  [Test]
  public void Parse_MissingLoginField_DefaultsToDeck()
  {
    const string output =
      "=;wlan0;IPv4;mydevice;_steamos-devkit._tcp;local;mydevice.local;10.0.0.5;32000;\"txtvers=1\"\n";

    var devices = DevkitDiscovery.Parse(output);

    Assert.AreEqual(1, devices.Count);
    Assert.AreEqual("deck", devices[0].Username);
  }

  [Test]
  public void ParseTxtField_ExtractsLogin()
  {
    const string txt = "\"devkit1=devkit-1\" \"login=deck\" \"settings={}\" \"txtvers=1\"";
    Assert.AreEqual("deck", DevkitDiscovery.ParseTxtField(txt, "login"));
  }

  [Test]
  public void ParseTxtField_ExtractsCustomUsername()
  {
    const string txt = "\"login=gamer\" \"txtvers=1\"";
    Assert.AreEqual("gamer", DevkitDiscovery.ParseTxtField(txt, "login"));
  }

  [Test]
  public void ParseTxtField_MissingKey_ReturnsNull()
  {
    const string txt = "\"txtvers=1\"";
    Assert.IsNull(DevkitDiscovery.ParseTxtField(txt, "login"));
  }

  [Test]
  public void ProcessRunner_EchoTrue_Succeeds()
  {
    var result = ProcessRunner.Run("true", "", 5000);
    Assert.IsTrue(result.Success);
    Assert.AreEqual(0, result.ExitCode);
  }

  [Test]
  public void ProcessRunner_EchoFalse_Fails()
  {
    var result = ProcessRunner.Run("false", "", 5000);
    Assert.IsFalse(result.Success);
    Assert.AreEqual(1, result.ExitCode);
  }

  [Test]
  public void ProcessRunner_CapturesStdout()
  {
    var result = ProcessRunner.Run("echo", "hello", 5000);
    Assert.IsTrue(result.Success);
    StringAssert.Contains("hello", result.Output);
  }

  [Test]
  public void ProcessRunner_ArgumentList_PreservesQuotesAndSpaces()
  {
    // Verify ArgumentList passes args without shell interpretation
    var result = ProcessRunner.Run("echo", new[] { "hello world", "foo\"bar" }, 5000);
    Assert.IsTrue(result.Success);
    StringAssert.Contains("hello world", result.Output);
    StringAssert.Contains("foo\"bar", result.Output);
  }

  [Test]
  public void ProcessRunner_Timeout_ReturnsNegativeOne()
  {
    var result = ProcessRunner.Run("sleep", "10", 500);
    Assert.AreEqual(-1, result.ExitCode);
    Assert.IsFalse(result.Success);
  }

  [Test]
  public void SshKeyDiscovery_FindsKeyOnDisk()
  {
    // This test verifies the real key discovery logic on this machine
    var settings = SteamDeckDeploySettings.CreateInstance<SteamDeckDeploySettings>();
    try
    {
      var resolved = settings.ResolvedSshKeyPath;

      // On a machine with steamos-devkit installed, key should be found
      if (
        File.Exists(
          Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".config/steamos-devkit/devkit_rsa"
          )
        )
      )
      {
        Assert.IsNotEmpty(resolved);
        Assert.IsTrue(File.Exists(resolved));
        StringAssert.Contains("devkit_rsa", resolved);
      }
      else
      {
        // No devkit installed — resolved should be empty
        Assert.IsEmpty(resolved);
      }
    }
    finally
    {
      UnityEngine.Object.DestroyImmediate(settings);
    }
  }

  [Test]
  public async Task LiveDiscovery_FindsDevicesOnNetwork()
  {
    // Real mDNS discovery — requires avahi-daemon running and a Steam Deck on the network
    var devices = await DevkitDiscovery.Discover(5000);

    // This is a live test — it may find 0 devices if no Steam Deck is on the network
    // We just verify it doesn't throw and returns a valid list
    Assert.IsNotNull(devices);

    if (devices.Count > 0)
    {
      var device = devices[0];
      Assert.IsNotEmpty(device.Address);
      Assert.IsNotEmpty(device.Username);
      Assert.IsNotEmpty(device.Name);
      Assert.Greater(device.Port, 0);
    }
  }

  [Test]
  public async Task Deploy_FullPipeline_ViaPublicAPI()
  {
    // Ensure settings asset exists (same path the button uses)
    var settings = SteamDeckDeploySettingsProvider.GetOrCreateSettings();
    Assume.That(settings, Is.Not.Null, "Could not create settings asset");
    Assume.That(
      settings.ResolvedSshKeyPath,
      Is.Not.Empty,
      "No devkit_rsa found — skipping deploy test"
    );

    // Create a temp build directory with dummy files
    var tempDir = Path.Combine(
      Path.GetTempPath(),
      $"steamdeck_deploy_test_{System.Guid.NewGuid():N}"
    );
    Directory.CreateDirectory(tempDir);
    File.WriteAllText(
      Path.Combine(tempDir, $"{UnityEditor.PlayerSettings.productName}.x86_64"),
      "not a real binary"
    );

    try
    {
      // Call the exact same API as the Deploy Now button — no launch since it's a fake binary
      var success = await SteamDeckDeploy.Deploy(tempDir, launch: false);

      Assert.IsTrue(success, "Deploy() returned false — check Console for errors");
    }
    finally
    {
      Directory.Delete(tempDir, true);
    }
  }
}
