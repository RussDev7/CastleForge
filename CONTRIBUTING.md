# Contributing to CastleForge

Thanks for helping improve CastleForge.

CastleForge is split into a few different contribution lanes so PRs stay focused and easy to review:

- **Core framework** changes:
  - `CastleForge/ModLoaderFramework/ModLoader`
  - `CastleForge/ModLoaderFramework/ModLoaderExtensions`
- **Official mod** changes:
  - `CastleForge/Mods/<ModName>`
- **Server / tooling** changes:
  - `CastleForge/Servers/*`
  - `CastleForge/Tools/*`
- **Documentation** changes:
  - root `README.md`
  - project `README.md` files
  - `docs/*`

## Before you start

Please open an issue first for:

- large refactors
- new official mods
- new shared framework features
- breaking changes to config, load order, packaging, or mod APIs

Small bug fixes, typo fixes, and README improvements can usually go straight to a PR.

## What belongs in this repository

This repository is for:

- the CastleForge mod loader
- CastleForge shared infrastructure
- official / first-party mods maintained here
- official tooling and server projects
- core documentation

This repository is **not** the default home for third-party community mods.

Community mod submissions should go through the separate community catalog / community-mods repository. A community mod can later be promoted into this repository if it becomes actively maintained as an official CastleForge project.

## Pull request expectations

Keep PRs narrow in scope.

Good examples:

- one bug fix in one mod
- one new command with docs
- one README overhaul
- one packaging workflow improvement

Avoid mixing unrelated work such as:

- gameplay features + docs + refactors + release changes in one PR
- multiple mods changed for unrelated reasons
- style-only churn across many files without clear value

## Coding expectations

Please match the repository's existing style:

- keep the current naming / folder conventions
- preserve summaries, region organization, and flow where already used
- prefer defensive guards for game hooks / Harmony patches
- avoid silent behavioral changes without documentation
- update comments and README sections when behavior changes

For mod patches specifically:

- explain the intent of the patch
- note whether it is defensive, gameplay-facing, or QoL
- include config notes when behavior is toggled
- avoid broad catch-all exception swallowing unless there is a clear reason

## Documentation expectations

If your PR changes user-facing behavior, also update one or more of:

- the relevant project `README.md`
- the root `README.md`
- `docs/*`
- config examples / command lists / screenshots placeholders where needed

## Build and test checklist

Before opening a PR, please try to:

1. Build the affected project(s) in `Release`.
2. Verify packaging or embedded assets still resolve.
3. Smoke-test in a real CastleMiner Z session when applicable.
4. Confirm logs do not introduce noisy spam without a setting.
5. Update docs for config keys, commands, workflows, or install steps.

## Official mod promotion

A third-party mod may be considered for promotion into the main CastleForge repo when it is:

- broadly useful
- maintained
- documented
- compatible with CastleForge conventions
- legally safe to host and redistribute
- something the main repo maintainers are willing to support

## License and ownership

By contributing, you agree that your contribution may be distributed under this repository's license terms.

If your PR contains third-party code, assets, fonts, binaries, or other redistributed material, clearly state that in the PR and confirm redistribution rights.
