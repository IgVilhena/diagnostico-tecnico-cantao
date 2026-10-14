@echo off
rem Compila o DiagnosticoTecnico.exe usando o compilador C# que ja vem no Windows (.NET Framework 4).
rem Nao precisa instalar nada.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Compilador C# do .NET Framework 4 nao encontrado.
  exit /b 1
)

set ICON=
if exist "src\app.ico" set ICON=/win32icon:src\app.ico

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:DiagnosticoTecnico.exe /win32manifest:src\app.manifest %ICON% ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Management.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll ^
  /resource:assets\cantao-logo.jpeg,Cantao.Logo /resource:assets\cantao-fundo.jpg,Cantao.Fundo ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo *** ERRO NA COMPILACAO ***
  exit /b 1
)

if not exist "Ferramentas" mkdir "Ferramentas"
if not exist "OS" mkdir "OS"
echo.
echo OK: DiagnosticoTecnico.exe gerado.
