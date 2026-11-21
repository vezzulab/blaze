---
description: Pre-release checklist for Blaze
---
Follow `docs/MAINTAINING.md`. Check, and report each as pass or fail with evidence: tests green, `tools/ui.py contract` green in light and dark, versions in both csproj files equal the intended tag, `engine.env` checksums match Mozilla's `SHA256SUMS`, AppImage builds and starts (`BLAZE_HEADLESS=1 BLAZE_SELFTEST=1`), release notes written and readable in 14 lines. Do not publish anything; the owner decides.
