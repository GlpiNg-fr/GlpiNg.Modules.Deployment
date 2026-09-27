# GlpiNg.Modules.Deployment

*[Version française](README.md)*

GlpiNg's Deployment module: package deployment through GLPI-Agent, and network discovery.

> **Disclaimer** — GlpiNg is an independent project. It is not affiliated with, endorsed,
> supported or sponsored by Teclib' or the GLPI project. "GLPI" and "GLPI-Agent" are trademarks
> of their respective owners; they are mentioned here only to describe GlpiNg's compatibility
> with the GLPI-Agent protocol and import from a GLPI database.

## Contents

- Deployment packages, jobs and tasks served to agents (`/inventory`)
- Dynamic computer groups, rules, time slots, mirror servers
- Collect definitions
- IP ranges, SNMP credentials, network tasks and discovered devices
- Wake-on-LAN
- Deployment reports

## Usage

This repository is a submodule of [GlpiNg](https://github.com/GlpiNg-fr/GlpiNg), under
`src/GlpiNg.Modules.Deployment`. It does not build on its own: it references `GlpiNg.Modules.Abstractions`, `GlpiNg.Modules.Inventory` by relative path.

```bash
git clone --recurse-submodules https://github.com/GlpiNg-fr/GlpiNg.git
```

The host registers it with `services.AddDeploymentModule()` (see `Program.cs`).

## License

[GNU Affero General Public License v3.0](LICENSE).
