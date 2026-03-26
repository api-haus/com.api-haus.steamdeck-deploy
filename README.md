# Steam Deck Deploy

Deploy Unity builds to Steam Deck over SSH/rsync. Auto-discovers devices on the network via mDNS and registers games as Steam shortcuts.

## Requirements

- Unity 6+ (uses `BuildProfile` API)
- Linux or macOS host with `ssh`, `rsync`, and `avahi-browse`
- Steam Deck with [Developer Mode](https://partner.steamgames.com/doc/steamdeck/devkitaccess) enabled
- SSH key from the SteamOS Devkit Client (auto-detected)

## Install

Add to your `manifest.json`:

```json
"com.api-haus.steamdeck-deploy": "https://github.com/api-haus/com.api-haus.steamdeck-deploy.git"
```

Or use the Unity Package Manager window: **Add package from git URL**.

## Usage

### Menu Items

- **Build > Deploy to Steam Deck** -- rsyncs the last build to the deck
- **Build > Build and Deploy to Steam Deck** -- builds the active profile, then deploys

### Settings

**Project Settings > Steam Deck Deploy** to configure:

- IP address (auto-discovered if left empty)
- Username (default: `deck`)
- SSH key path (auto-detected from SteamOS Devkit Client)
- Remote base path, launch args, auto-launch toggle

### API

```csharp
using ApiHaus.SteamDeckDeploy.Editor;

// Deploy an existing build
await SteamDeckDeploy.Deploy("Builds/Linux");

// Deploy without launching
await SteamDeckDeploy.Deploy("Builds/Linux", launch: false);

// Test connection
await SteamDeckDeploy.TestConnection();
```

## How It Works

1. **Discover** -- finds Steam Deck on the LAN via `avahi-browse _steamos-devkit._tcp`
2. **Upload scripts** -- rsyncs `unity-run-game` helper to `~/unity-scripts/` on the device
3. **Rsync build** -- transfers the build to `~/devkit-game/{productName}_Linux/`, excluding `*_DoNotShip`, `*_BackUpThisFolder_*`, `*.pdb`, and `Saved/`
4. **Register shortcut** -- calls `steam-client-create-shortcut` via SSH to add the game to Steam
5. **Launch** -- runs the game through the Steam client IPC

## License

MIT
