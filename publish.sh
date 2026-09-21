#!/bin/bash
set -euo pipefail

echo "Starting Amwal ECR SDK (NuGet) publishing process..."

PROJECT="src/AmwalPay.Ecr/AmwalPay.Ecr.csproj"
PACKAGE_ID="AmwalPay.Ecr"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

MISSING=()

# --- NuGet credentials -------------------------------------------------------
# Codemagic's environment groups use NUGET_API_KEY; accept the mixed-case form
# the dotnet docs use as well, so the script works however the group was set up.
# The key is only ever read from the environment - never from a file in the repo.
API_KEY="${NUGET_API_KEY:-${NuGetApiKey:-}}"
[ -n "$API_KEY" ] || MISSING+=("NUGET_API_KEY (a nuget.org API key scoped to ${PACKAGE_ID})")

if [ ${#MISSING[@]} -gt 0 ]; then
  {
    echo
    echo "ERROR: refusing to publish - missing credentials:"
    printf '  - %s\n' "${MISSING[@]}"
    echo
    echo "In Codemagic this comes from the 'A' and 'nuget_credentials' environment"
    echo "groups. Locally: export NUGET_API_KEY=... and run ./publish.sh again."
  } >&2
  exit 1
fi

PUBLISHED_VERSION=$(sed -n 's|.*<Version>\([0-9]*\.[0-9]*\.[0-9]*\)</Version>.*|\1|p' "$PROJECT" | head -1)

if [ -z "$PUBLISHED_VERSION" ]; then
  echo "ERROR: could not parse the version out of ${PROJECT}" >&2
  exit 1
fi

echo "Building and packing ${PACKAGE_ID} ${PUBLISHED_VERSION}..."
rm -rf artifacts
# Release + Deterministic (set in the csproj) so the package is reproducible.
# Packing builds both target frameworks - netstandard2.0 and net8.0 - and needs
# no Windows: the SDK is plain managed code.
dotnet pack "$PROJECT" -c Release -o artifacts --nologo

NUPKG="artifacts/${PACKAGE_ID}.${PUBLISHED_VERSION}.nupkg"
if [ ! -f "$NUPKG" ]; then
  echo "ERROR: expected ${NUPKG} was not produced" >&2
  ls -la artifacts >&2 || true
  exit 1
fi

# The shipped assets are exactly netstandard2.0 and net8.0. A lib/net462 (or any other) asset would
# mean the VerifyNetFramework compile gate leaked into the pack, and every .NET Framework app would
# then resolve that asset instead of netstandard2.0 - a different set of dependencies and behaviour
# from the one the docs describe. Refuse rather than publish it.
ASSETS=$(unzip -Z1 "$NUPKG" | grep -E '^lib/[^/]+/[^/]+\.dll$' | sed -E 's|^lib/([^/]+)/.*|\1|' | sort | tr '\n' ' ')
if [ "$ASSETS" != "net8.0 netstandard2.0 " ]; then
  echo "ERROR: unexpected package assets: ${ASSETS}(expected: net8.0 netstandard2.0)" >&2
  echo "Is -p:VerifyNetFramework=true set in the environment or a props file?" >&2
  exit 1
fi

echo "Pushing ${NUPKG} to ${NUGET_SOURCE}..."
# --skip-duplicate: a re-run of the same version is not a failure, it just means
# the earlier push already landed. The symbol package (.snupkg) rides along.
dotnet nuget push "$NUPKG" \
  --api-key "$API_KEY" \
  --source "$NUGET_SOURCE" \
  --skip-duplicate

# --- Verify it actually landed ----------------------------------------------
# The push reporting success and the package being fetchable are two different
# things; the flat container index lags by a few minutes. Poll rather than assume.
LOWER_ID=$(printf '%s' "$PACKAGE_ID" | tr '[:upper:]' '[:lower:]')
ARTIFACT_URL="https://api.nuget.org/v3-flatcontainer/${LOWER_ID}/${PUBLISHED_VERSION}/${LOWER_ID}.${PUBLISHED_VERSION}.nupkg"
echo "Verifying ${ARTIFACT_URL} ..."

for attempt in $(seq 1 20); do
  STATUS=$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 "$ARTIFACT_URL" || echo "000")
  if [ "$STATUS" = "200" ]; then
    echo "Verified: ${PACKAGE_ID} ${PUBLISHED_VERSION} is live on NuGet"
    exit 0
  fi
  echo "  attempt ${attempt}/20: HTTP ${STATUS}, waiting for NuGet to index..."
  sleep 30
done

echo "WARNING: push reported success but ${ARTIFACT_URL} is still not fetchable." >&2
echo "This is usually just slow indexing - check the package's status on nuget.org." >&2
exit 1
