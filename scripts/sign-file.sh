#!/bin/bash
# Authenticode-signs a PE file in place (SHA-256 + RFC 3161 timestamp).
# Uses signing/private/codesign.pfx by default; set SIGN_PFX / SIGN_PASSWORD_FILE to use a CA-issued certificate instead.
set -euo pipefail
HERE="$(cd "$(dirname "$0")/.." && pwd)"
PFX="${SIGN_PFX:-$HERE/signing/private/codesign.pfx}"
PWF="${SIGN_PASSWORD_FILE:-$HERE/signing/private/key-password.txt}"
IN="$1"; OUT="$1.signed.tmp"
rm -f "$OUT"
for TS in http://timestamp.digicert.com http://timestamp.sectigo.com http://timestamp.globalsign.com/tsa/r6advanced1 ""; do
  if [ -n "$TS" ]; then TSARG=(-ts "$TS"); else TSARG=(); echo "WARNING: signing WITHOUT timestamp" >&2; fi
  if osslsigncode sign -pkcs12 "$PFX" -readpass "$PWF" -h sha256 \
       -n "Student Timer Clock" -i "https://github.com/" "${TSARG[@]}" -in "$IN" -out "$OUT" >/dev/null 2>&1; then
    mv -f "$OUT" "$IN"; echo "signed: $(basename "$IN") ${TS:+(timestamp: $TS)}"; exit 0
  fi
  rm -f "$OUT"
done
echo "ERROR: signing failed for $IN" >&2; exit 1
