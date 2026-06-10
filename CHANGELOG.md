# [2.0.0](https://github.com/api-haus/com.api-haus.steamdeck-deploy/compare/v1.2.2...v2.0.0) (2026-06-10)


* feat!: drop all hardcoded project paths — consumer-agnostic deploy ([103fb0f](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/103fb0f2f1f83592ce0f51b5f11adc6d8c89a62a))


### BREAKING CHANGES

* SteamDeckBuildCli is removed — use the menu item or
SteamDeckDeploy.BuildActiveProfileAndDeploy(launch). SteamDeckDeploySettings.AssetPath
is removed; the settings asset is located by type and created under
Assets/Settings/. The "Deploy to Steam Deck" and "Build and Deploy to Steam
Deck" menu items are replaced by a single "Build Current Profile to Steam Deck".

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>

## [1.2.2](https://github.com/api-haus/com.api-haus.steamdeck-deploy/compare/v1.2.1...v1.2.2) (2026-06-01)


### Bug Fixes

* repoint SteamDeckDeploySettings AssetPath to is.zori.testbed ([f6d6a54](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/f6d6a54db5512756b94b9b828e9010d779642c56))

## [1.2.1](https://github.com/api-haus/com.api-haus.steamdeck-deploy/compare/v1.2.0...v1.2.1) (2026-04-18)


### Bug Fixes

* kill stale instances across Product Name renames ([3f5c06c](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/3f5c06c0c1e2da1a9c388b7c9f1376698bff498e))
* support spaces in product name via launch.sh wrapper ([95217db](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/95217dbe5f74973d46dceebe5d784efd76321139))

# [1.2.0](https://github.com/api-haus/com.api-haus.steamdeck-deploy/compare/v1.1.0...v1.2.0) (2026-03-27)


### Features

* push/pull functions to rsync files to and fro the deck ([99a151f](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/99a151feee1714146a54621f0f0b3e67e7cb4691))

# [1.1.0](https://github.com/api-haus/com.api-haus.steamdeck-deploy/compare/v1.0.0...v1.1.0) (2026-03-26)


### Features

* kill previous game instance before launching new one ([5624f4c](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/5624f4cbd6e036998733a09903342bd88febc112))

# 1.0.0 (2026-03-26)


### Bug Fixes

* add .releaserc to disable npm publish ([589eef9](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/589eef9fbdc861c13b70856c4bff240769c9e520))


### Features

* initial release — Steam Deck deploy for Unity ([ba7fa26](https://github.com/api-haus/com.api-haus.steamdeck-deploy/commit/ba7fa26a5ea914df82d5510fa290c9c097ae4200))
