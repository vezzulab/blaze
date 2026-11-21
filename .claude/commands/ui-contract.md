---
description: Check that the Blaze interface still matches what the CSS relies on, then show a screenshot
---
Run `python3 tools/ui.py contract`. Report every FAIL with its detail. For a failing check, inspect the real DOM with `tools/ui.py` before editing `profile/chrome/userChrome.css`; never guess selectors. When it passes, run `python3 tools/ui.py shot --scheme dark --out /tmp/blaze-dark.png`, look at the image, and say what you see, including anything that looks wrong even though the contract passed.
