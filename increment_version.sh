#!/bin/bash
set -euo pipefail

# Check if GitHub token is set. The variable keeps the spelling the Amwal
# Codemagic environment group already defines (GITHUB_ACCES_TOKEN, one S), so this
# repo can share group "A" with ECR-simulator without renaming a secret.
if [ -z "${GITHUB_ACCES_TOKEN:-}" ]; then
    echo "Error: GITHUB_ACCES_TOKEN environment variable is not set"
    exit 1
fi

PROJECT="src/AmwalPay.Ecr/AmwalPay.Ecr.csproj"
PACKAGE_ID="AmwalPay.Ecr"

# Get current version from the <Version> element of the SDK project
CURRENT_VERSION=$(sed -n 's|.*<Version>\([0-9]*\.[0-9]*\.[0-9]*\)</Version>.*|\1|p' "$PROJECT" | head -1)

if [ -z "$CURRENT_VERSION" ]; then
    echo "Error: Could not find version for ${PACKAGE_ID}"
    exit 1
fi

IFS='.' read -r major minor patch <<< "$CURRENT_VERSION"
NEW_PATCH=$((patch + 1))
NEW_VERSION="$major.$minor.$NEW_PATCH"

echo "Updating ${PACKAGE_ID} from $CURRENT_VERSION to $NEW_VERSION"

if [[ "$OSTYPE" == "darwin"* ]]; then
    sed -i '' "s|<Version>${CURRENT_VERSION}</Version>|<Version>${NEW_VERSION}</Version>|" "$PROJECT"
else
    sed -i "s|<Version>${CURRENT_VERSION}</Version>|<Version>${NEW_VERSION}</Version>|" "$PROJECT"
fi

# Export for publish.sh when sourced in the same shell (Codemagic runs scripts
# separately, so publish.sh also re-reads the version from the project).
ecr_sdk_version="$NEW_VERSION"
export ecr_sdk_version

git config --global user.email "amr.elskaan@amwal-pay.com"
git config --global user.name "Amr Said"

git add "$PROJECT"
git commit -m "Bump ${PACKAGE_ID} version to ${NEW_VERSION}"
echo "Pushing version bump to GitHub..."
git remote set-url origin "https://oauth2:${GITHUB_ACCES_TOKEN}@github.com/amwal-pay/AmwalpayEcrWindows.git"
git push origin HEAD

echo "Version updated successfully: ${PACKAGE_ID} ${NEW_VERSION}"
