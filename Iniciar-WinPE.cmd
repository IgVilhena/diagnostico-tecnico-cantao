@echo off
setlocal
rem Nao precisa de PowerShell. O executavel AnyCPU usa o CLR da arquitetura do WinPE.
set "DT_ARCH=%PROCESSOR_ARCHITECTURE%"
if defined PROCESSOR_ARCHITEW6432 set "DT_ARCH=%PROCESSOR_ARCHITEW6432%"
set "DT_FRAMEWORK=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if /i "%DT_ARCH%"=="AMD64" set "DT_FRAMEWORK=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"

if not exist "%~dp0DiagnosticoTecnico.exe" (
  echo DiagnosticoTecnico.exe nao encontrado. Extraia o pacote inteiro antes de abrir.
  pause
  exit /b 1
)
if not exist "%DT_FRAMEWORK%\clr.dll" (
  echo O suporte .NET 4.x da arquitetura %DT_ARCH% nao esta carregado neste WinPE.
  echo O aplicativo precisa do .NET 4.5 ou superior com Windows Forms.
  echo Carregue o suporte .NET da sua midia Strelec, se disponivel, e tente novamente.
  echo Consulte LEIA-ME-WINPE.txt. O Windows instalado no disco nao fornece esse suporte ao WinPE.
  pause
  exit /b 1
)

pushd "%~dp0"
start "" /wait "%~dp0DiagnosticoTecnico.exe"
set "DT_RESULT=%ERRORLEVEL%"
popd
if not "%DT_RESULT%"=="0" (
  echo O programa terminou com codigo %DT_RESULT%.
  echo Consulte erro.log e LEIA-ME-WINPE.txt nesta pasta.
  pause
)
exit /b %DT_RESULT%
