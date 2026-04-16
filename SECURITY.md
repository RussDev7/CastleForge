# Security Policy

## Supported scope

Please use responsible disclosure for vulnerabilities involving:

- the CastleForge mod loader
- `ModLoaderExtensions`
- official CastleForge mods
- official server and tool projects in this repository
- release packaging or distribution produced from this repository

## Out of scope

The following are generally out of scope for this repository's security policy:

- third-party community mods not maintained here
- local mod conflicts caused by unsupported combinations
- cheats / griefing reports unrelated to software flaws
- issues that require bypassing game ownership, DRM, or platform protections
- reports without reproducible details

## Reporting a vulnerability

Please do **not** open a public issue for a suspected security problem.

Instead, report it privately with:

- affected component
- impact
- reproduction steps
- proof of concept if available
- version / commit
- whether the issue affects only dev builds or released packages

If private security reporting is enabled for the repository, use GitHub's private reporting flow. Otherwise, use the maintainer contact path documented in the repository profile or README.

## What to include

A strong report usually includes:

- exact file / class / workflow affected
- whether the issue is local-only or remote-reachable
- attack prerequisites
- expected vs actual behavior
- screenshots / logs if helpful
- any proposed mitigation

## Coordinated disclosure

Please allow time to investigate and patch before public disclosure.

Once fixed, the repository may publish:

- a security advisory
- release notes
- mitigation instructions
- affected version ranges
