@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /debug- /win32manifest:app.manifest /win32icon:LifeKalendar.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:LifeKalendar.exe LifeKalendar.cs
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /debug- /win32manifest:app.manifest /win32icon:LifeKalendar.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:LifeKalendar.exe,app.exe /out:LifeKalendar-Setup.exe Setup.cs
