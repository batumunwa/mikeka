@echo off
rem Opens the Chrome window Mikeka works in: runs only add tabs here, no second browser is opened.
rem Uses its own profile (C:\MikekaChrome) because Chrome refuses remote control on your everyday profile.
set CHROME=%ProgramFiles%\Google\Chrome\Application\chrome.exe
if not exist "%CHROME%" set CHROME=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe
if not exist "%CHROME%" set CHROME=%LocalAppData%\Google\Chrome\Application\chrome.exe
start "" "%CHROME%" --remote-debugging-port=9222 --user-data-dir="C:\MikekaChrome" --no-first-run --no-default-browser-check
