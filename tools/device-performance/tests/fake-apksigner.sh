#!/usr/bin/env bash
set -euo pipefail

[ "${1:-}" = "verify" ] && [ "${2:-}" = "--print-certs" ] || exit 2
digest="9d99de1f064d9ec03fa55ced4e49b7b20991bb68b082bd388ff42d3b4a6f4c94"
if [ "${FAKE_SIGNER_MISMATCH:-0}" = "1" ] && [[ "${3:-}" == *candidate.apk ]]; then
    digest="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
fi
echo "Signer #1 certificate SHA-256 digest: $digest"
