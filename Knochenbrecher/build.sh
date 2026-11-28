#!/usr/bin/env bash
# Baut den Mod und packt ihn als ZIP (Thunderstore/r2modman-Format) nach dist/.
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(python3 -c "import json; print(json.load(open('package/manifest.json'))['version_number'])")
dotnet build -c Release -p:Version="$VERSION"

STAGE=$(mktemp -d)
cp package/manifest.json package/icon.png package/knochenbrecher_icon.png "$STAGE"/
cp README.md "$STAGE"/README.md
cp ../LICENSE "$STAGE"/LICENSE.txt
# Textdateien für den Windows-Editor: Versionsnummer einsetzen, Windows-Zeilenenden, UTF-8 mit BOM
mkdir -p "$STAGE/Testmodus"
cp package/Testmodus/ady.valheim.knochenbrecher.cfg "$STAGE/Testmodus/"
python3 - "$VERSION" "$STAGE" <<'PY'
import sys
version, stage = sys.argv[1:3]
for name in ("INSTALLATION.txt", "Testmodus/TESTEN.txt"):
    text = open("package/" + name, encoding="utf-8").read().replace("{version}", version)
    open(stage + "/" + name, "w", encoding="utf-8-sig", newline="\r\n").write(text)
PY
cp bin/Release/net48/Knochenbrecher.dll "$STAGE"/

mkdir -p dist
rm -f "dist/Knochenbrecher-$VERSION.zip"
(cd "$STAGE" && zip -q -r "$OLDPWD/dist/Knochenbrecher-$VERSION.zip" .)
rm -rf "$STAGE"
echo "Fertig: dist/Knochenbrecher-$VERSION.zip"
