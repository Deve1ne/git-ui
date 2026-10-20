# Installers

```
python packaging/package.py                  # every target
python packaging/package.py win deb-amd64    # some of them
```

Needs the .NET SDK and Python 3 (standard library only). Everything lands in `dist/`.
Each package carries a self-contained .NET runtime; **git must be installed** on the machine
(the app writes through the git CLI). The version comes from `<Version>` in
`src/GitUi.Presentation/GitUi.Presentation.csproj`.

| Target      | Output                              | Built on                 |
|-------------|-------------------------------------|--------------------------|
| `win`       | `GitUi-<v>-win-x64.msi`             | Windows (WiX 5, `dotnet tool restore`) |
| `deb-amd64` | `gitui_<v>_amd64.deb`               | any                      |
| `deb-arm64` | `gitui_<v>_arm64.deb`               | any                      |
| `mac-arm64` | `GitUi-<v>-macos-arm64.zip` (Apple Silicon) | any              |
| `mac-x64`   | `GitUi-<v>-macos-x64.zip` (Intel)   | any                      |

## Installing

**Windows**: run the `.msi`. It installs for the current user, without administrator rights, in
`%LocalAppData%\Programs\GitUi`, with Start menu and desktop shortcuts; uninstall from
*Settings > Apps*. A newer MSI upgrades the installed version.

**Debian / Ubuntu**:

```
sudo apt install ./gitui_<v>_amd64.deb
```

Installs to `/opt/gitui`, with a `gitui` command and an application menu entry. Installed through
`sudo`, it also puts a launcher on that user's desktop (the localized one, from `xdg-user-dir`).
Remove with `sudo apt remove gitui`, which removes the desktop launcher too.

**macOS**: unzip, then right-click `Install Git UI.command` > *Open* (it is not signed, so a plain
double-click is refused the first time). It copies `GitUi.app` to *Applications* (Launchpad), clears
the download quarantine (the app is not notarized), adds it to the Dock, links it on the desktop and
starts it. By hand instead: move `GitUi.app` to *Applications*, then

```
xattr -dr com.apple.quarantine /Applications/GitUi.app
```

Settings and logs stay in the user's application data folder (`%APPDATA%\GitUi`,
`~/.config/GitUi` on Linux and macOS), so reinstalling keeps them.
