# GlpiNg.Modules.Deployment

*[English version](README.en.md)*

Module Déploiement de GlpiNg : déploiement de paquets par les GLPI-Agent, et découverte réseau.

> **Avertissement** — GlpiNg est un projet indépendant. Il n'est ni affilié à, ni approuvé,
> soutenu ou sponsorisé par Teclib' ou le projet GLPI. « GLPI » et « GLPI-Agent » sont des
> marques de leurs propriétaires respectifs ; elles ne sont citées ici que pour décrire la
> compatibilité de GlpiNg avec le protocole GLPI-Agent et l'import depuis une base GLPI.

## Contenu

- Paquets, jobs et tâches de déploiement servis aux agents (`/inventory`)
- Groupes d'ordinateurs dynamiques, règles, créneaux horaires, serveurs miroirs
- Définitions de collecte
- Plages IP, identifiants SNMP, tâches réseau et équipements découverts
- Wake-on-LAN
- Rapports de déploiement

## Utilisation

Ce dépôt est un sous-module de [GlpiNg](https://github.com/GlpiNg-fr/GlpiNg), sous
`src/GlpiNg.Modules.Deployment`. Il ne se compile pas seul : il référence `GlpiNg.Modules.Abstractions`, `GlpiNg.Modules.Inventory` par chemin relatif.

```bash
git clone --recurse-submodules https://github.com/GlpiNg-fr/GlpiNg.git
```

L'hôte l'enregistre par `services.AddDeploymentModule()` (voir `Program.cs`).

## Licence

[GNU Affero General Public License v3.0](LICENSE).
