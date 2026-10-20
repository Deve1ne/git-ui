#!/bin/bash
# Installs Git UI from the unzipped folder: copies GitUi.app to /Applications (so it shows in
# Launchpad), clears the download quarantine, adds it to the Dock and links it on the desktop.
# First run: right-click > Open (the script is not signed either), or `bash "Install Git UI.command"`.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
src="$here/GitUi.app"
dest="/Applications/GitUi.app"
if [ ! -d "$src" ]; then
    echo "GitUi.app was not found next to this script."
    exit 1
fi

echo "Copying GitUi.app to /Applications..."
if [ -w /Applications ]; then
    rm -rf "$dest"
    ditto "$src" "$dest"
    xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true
else
    sudo rm -rf "$dest"
    sudo ditto "$src" "$dest"
    sudo xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true
fi

if ! defaults read com.apple.dock persistent-apps 2>/dev/null | grep -q "/Applications/GitUi.app"; then
    echo "Adding Git UI to the Dock..."
    defaults write com.apple.dock persistent-apps -array-add \
        "<dict><key>tile-data</key><dict><key>file-data</key><dict><key>_CFURLString</key><string>file:///Applications/GitUi.app/</string><key>_CFURLStringType</key><integer>15</integer></dict></dict></dict>"
    killall Dock 2>/dev/null || true
fi

echo "Linking Git UI on the desktop..."
ln -sfn "$dest" "$HOME/Desktop/Git UI.app"

echo "Done. Starting Git UI."
open "$dest"
