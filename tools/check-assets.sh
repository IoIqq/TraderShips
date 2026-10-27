#!/usr/bin/env bash
# Assets referenced by the deployed defs must exist inside the deployed mod.
T="H:/steam/steamapps/common/RimWorld/Mods/TraderShipsDev"
fail=0

check() {  # $1=relative path without extension, $2=description
  if ls "$T/$1".* >/dev/null 2>&1; then
    :
  else
    echo "  [缺失] $2 -> $1"
    fail=$((fail+1))
  fi
}

echo "=== 贴图引用 (texPath / uiIconPath) ==="
grep -rhoE '<(texPath|uiIconPath|texPath)>[^<]+' "$T/1.7/Defs" \
  | sed -E 's/<[a-zA-Z]+>//' | sort -u | while read -r p; do
    if ls "$T/Textures/$p".* >/dev/null 2>&1; then
      echo "  OK   $p"
    else
      echo "  MISS $p"
    fi
done

echo
echo "=== 声音引用 (clipPath) ==="
grep -rhoE '<clipPath>[^<]+' "$T/1.7/Defs" | sed 's/<clipPath>//' | sort -u | while read -r p; do
  if ls "$T/Sounds/$p".* >/dev/null 2>&1; then
    echo "  OK   $p"
  else
    echo "  MISS $p"
  fi
done

echo
echo "=== 阴影引用 (shadow) ==="
grep -rhoE '<shadow>[^<]+' "$T/1.7/Defs" | sed 's/<shadow>//' | sort -u | while read -r p; do
  if ls "$T/Textures/$p".* >/dev/null 2>&1; then
    echo "  OK   $p"
  else
    echo "  MISS (可能来自 Core) $p"
  fi
done

echo
echo "=== shaderType 引用 ==="
grep -rhoE '<shaderType>[^<]+' "$T/1.7/Defs" | sed 's/<shaderType>//' | sort | uniq -c
