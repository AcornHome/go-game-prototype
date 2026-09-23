# ChinaGo — itch.io Desktop Release Guide (T123)

How to package the ChinaGo Windows desktop build as a portable zip and upload it to itch.io.
Current version: **1.5.3**

## 1. Prerequisites
- An itch.io account and a project (example project name: `chinago`).
- Install butler: https://itch.io/docs/butler/installing.html
  (Add its folder to PATH, or use the full path to `butler.exe` in the commands below.)
- On the build machine: .NET 10 SDK (to compile). Inno Setup 6 is only needed for the
  installer `.exe`; the portable zip does NOT need it.

## 2. Build (produces `dist\`)
Option A — one-click script (recommended):
```
Build v1.5.3.bat
```
It runs: `dotnet restore` -> `dotnet build -c Release -p:Version=1.5.3` ->
`python .tools\deploy.py` -> Inno Setup. The portable zip only needs the
resulting `dist\` folder (you can ignore the generated installer `.exe`).

Option B — manual / self-contained (zero .NET dependency for players):
```
cd desktop
dotnet publish -c Release -r win-x64 --self-contained true -p:Version=1.5.3 -o ..\dist-self
```
A self-contained publish means players do NOT need to install the .NET 10 Desktop Runtime,
at the cost of a larger download. The guide below uses `dist\`; if you used `dist-self`,
just substitute `dist-self` for `dist` in step 4.

## 3. Package as a portable zip
The `dist\` folder is already a ready-to-run layout:
```
dist\
  GoGame.exe              ; .NET apphost
  GoGame.dll + *.json     ; managed dependencies
  KataGo\                 ; engine (katago.exe / weights\*.bin.gz / default_gtp.cfg)
  THIRD-PARTY-NOTICES.txt, KataGo\License.txt, KataGo\weights\License.txt
  (the five language files Strings.*.xaml are compiled into GoGame.dll — no extra files needed)
```
The game auto-detects the engine from `<exe dir>\KataGo`, so this layout is portable.

Double-click **`pack-itch.bat`** to zip `dist\` into `ChinaGo-1.5.3-win.zip`.
(Or manually: right-click `dist\` -> Send to -> Compressed folder, rename to `ChinaGo-1.5.3-win.zip`.)

## 4. Upload to itch.io (butler)
Log in once (only needed the first time):
```
butler login
```
Push (same command for first upload and every update — butler computes the diff automatically):
```
butler push dist <itch-user>/chinago:windows-stable --user <itch-user>
```
Notes:
- Replace `<itch-user>` with your itch.io username; `chinago` is the project name;
  `windows-stable` is the channel name (you can rename it).
- If you built the self-contained `dist-self`, change `dist` to `dist-self`.
- Each new release just re-runs `butler push`; the version is taken from `GoGame.exe`
  (i.e. 1.5.3). Bump it via `GoSmart.iss` `#define MyAppVersion` + `Build vX.Y.Z.bat` `VER` first.
- In the itch.io dashboard, mark the channel as downloadable and set the price
  (suggested $9.99, modeled on AI Sensei).

## 5. Player-side notes
- Framework-dependent build (`dist\`): players must have the .NET 10 Desktop Runtime
  installed (https://dotnet.microsoft.com/download/dotnet/10.0), otherwise double-clicking
  does nothing.
- Self-contained build (`dist-self`): no runtime needed, unzip and play, but bigger package.
- After unzipping, double-click `GoGame.exe`. The KataGo engine lives in the `KataGo\`
  subfolder and is detected automatically. On first launch KataGo may take ~30s–3min to
  warm up.

## 6. Bumping the version
1. Edit `installer\GoSmart.iss`: `#define MyAppVersion "1.5.3"`
2. Edit / create `Build vX.Y.Z.bat`: `set "VER=1.5.3"` and update the title line.
3. Rebuild, re-run `pack-itch.bat`, then `butler push` again.
