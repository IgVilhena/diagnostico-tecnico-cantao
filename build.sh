#!/usr/bin/env bash
# Compila o DiagnosticoTecnico.exe no Linux usando as reference assemblies do .NET Framework 4.5
# (/usr/lib/mono/4.5-api), garantindo compatibilidade binária total com Windows 10/11 e WinPE.
set -euo pipefail
cd "$(dirname "$0")"

ICON=()
if [[ -f "src/app.ico" ]]; then
  ICON=("-win32icon:src/app.ico")
fi

API="/usr/lib/mono/4.5-api"

if command -v csc >/dev/null 2>&1 && [[ -d "$API" ]]; then
  csc -nologo -noconfig -nostdlib -target:winexe -platform:anycpu -optimize+ -codepage:65001 \
    -out:DiagnosticoTecnico.exe -win32manifest:src/app.manifest "${ICON[@]}" \
    -r:"$API/mscorlib.dll" \
    -r:"$API/System.dll" \
    -r:"$API/System.Core.dll" \
    -r:"$API/System.Drawing.dll" \
    -r:"$API/System.Windows.Forms.dll" \
    -r:"$API/System.Management.dll" \
    -r:"$API/System.Runtime.Serialization.dll" \
    -r:"$API/System.Xml.dll" \
    src/*.cs
elif command -v mcs >/dev/null 2>&1; then
  mcs -sdk:4.5 -nologo -target:winexe -platform:anycpu -optimize+ -codepage:65001 \
    -out:DiagnosticoTecnico.exe -win32manifest:src/app.manifest "${ICON[@]}" \
    -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
    -r:System.Management.dll -r:System.Runtime.Serialization.dll -r:System.Xml.dll \
    src/*.cs
else
  echo "Compilador C# do Mono (csc/mcs) não encontrado."
  echo "Instale o Mono (ex.: sudo pacman -S mono  ou  sudo apt install mono-devel)."
  exit 1
fi

mkdir -p Ferramentas OS
echo ""
echo "OK: DiagnosticoTecnico.exe gerado (compatível com .NET Framework 4.5+ / Windows / WinPE)."

if [[ "${1:-}" == "--run" ]]; then
  exec mono DiagnosticoTecnico.exe
fi
