#!/usr/bin/env bash
# Produces SHA256SUMS and SHA256SUMS.sig (ECDSA P-256 / SHA-256, DER) for the files given.
#   BLAZE_SIGNING_KEY=/path/release-key.pem tools/sign-release.sh dist/Blaze-0.1.0-x86_64.AppImage
# Blaze embeds the public key (src/Blaze.Core/Updates/ReleaseVerifier.cs) and refuses any update that does not verify.
# The private key must NEVER be committed. Keep it in a password manager / GitHub secret.
set -euo pipefail
[ -n "${BLAZE_SIGNING_KEY:-}" ] && [ -r "$BLAZE_SIGNING_KEY" ] || { echo "set BLAZE_SIGNING_KEY to the private key file" >&2; exit 1; }
[ "$#" -ge 1 ] || { echo "usage: sign-release.sh FILE..." >&2; exit 2; }
dir="$(dirname "$1")"
( cd "$dir" && sha256sum "$(basename "$1")" $(for f in "${@:2}"; do basename "$f"; done) > SHA256SUMS )
openssl dgst -sha256 -sign "$BLAZE_SIGNING_KEY" -out "$dir/SHA256SUMS.sig" "$dir/SHA256SUMS"
echo "signed: $dir/SHA256SUMS + SHA256SUMS.sig"
