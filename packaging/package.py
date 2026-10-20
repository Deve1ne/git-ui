#!/usr/bin/env python3
"""Builds the Git UI installers into dist/.

    python packaging/package.py                 # every target
    python packaging/package.py win deb-amd64   # some of them

Targets:
  win          GitUi-<version>-win-x64.msi  (per-user, WiX 5 from the local dotnet tool manifest)
  deb-amd64    gitui_<version>_amd64.deb    (Debian / Ubuntu)
  deb-arm64    gitui_<version>_arm64.deb
  mac-arm64    GitUi-<version>-macos-arm64.zip (GitUi.app, Apple Silicon)
  mac-x64      GitUi-<version>-macos-x64.zip   (GitUi.app, Intel)

Every target is a self-contained .NET publish, so no runtime is needed on the
machine; git itself must be installed there (writes go through the git CLI).
Runs on Windows, Linux or macOS with the .NET SDK and Python 3 (standard
library only); only the MSI needs Windows.
"""
import gzip
import hashlib
import io
import os
import re
import shutil
import struct
import subprocess
import sys
import tarfile
import time
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGING = os.path.join(ROOT, 'packaging')
PROJECT = os.path.join(ROOT, 'src', 'GitUi.Presentation', 'GitUi.Presentation.csproj')
ASSETS = os.path.join(ROOT, 'src', 'GitUi.Presentation', 'Assets')
DIST = os.path.join(ROOT, 'dist')
EXE = 'GitUi.Presentation'

TARGETS = {
    'win': 'win-x64',
    'deb-amd64': 'linux-x64',
    'deb-arm64': 'linux-arm64',
    'mac-arm64': 'osx-arm64',
    'mac-x64': 'osx-x64',
}

DEB_DEPENDS = ', '.join([
    'git',
    'libc6', 'libgcc-s1', 'libstdc++6', 'zlib1g',
    'libicu76 | libicu74 | libicu72 | libicu70 | libicu67',
    'libx11-6', 'libice6', 'libsm6', 'libfontconfig1',
])


def version():
    # The release workflow passes the version of the pushed tag (v1.2.3 -> 1.2.3).
    if os.environ.get('GITUI_VERSION'):
        return os.environ['GITUI_VERSION'].strip()
    with open(PROJECT, encoding='utf-8') as f:
        match = re.search(r'<Version>([^<]+)</Version>', f.read())
    if not match:
        sys.exit('No <Version> in ' + PROJECT)
    return match.group(1).strip()


def run(*args):
    print('>', ' '.join(args), flush=True)
    subprocess.run(args, check=True, cwd=ROOT)


def publish(rid):
    out = os.path.join(DIST, 'publish', rid)
    shutil.rmtree(out, ignore_errors=True)
    run('dotnet', 'publish', PROJECT, '-c', 'Release', '-r', rid, '--self-contained', 'true',
        '-p:DebugType=None', '-p:DebugSymbols=false', '-p:Version=' + version(), '-o', out)
    return out


def walk(directory):
    """Relative paths (forward slashes) of every file below directory, sorted."""
    files = []
    for base, _, names in os.walk(directory):
        for name in names:
            full = os.path.join(base, name)
            files.append(os.path.relpath(full, directory).replace(os.sep, '/'))
    return sorted(files)


def read_lf(path):
    """A text file from packaging/ with LF endings, whatever the checkout's line endings."""
    with open(path, 'rb') as f:
        return f.read().replace(b'\r\n', b'\n')


def is_executable(rel_path):
    """Files needing the exec bit on Unix: the app host and native libraries."""
    name = rel_path.rsplit('/', 1)[-1]
    return name in (EXE, 'createdump') or name.endswith(('.so', '.dylib'))


# ---------------------------------------------------------------- Windows MSI

WIX_EXTENSIONS = ('WixToolset.UI.wixext/5.0.2', 'WixToolset.Util.wixext/5.0.2')


def license_rtf():
    """LICENSE as the RTF the installer's license page shows."""
    with open(os.path.join(ROOT, 'LICENSE'), encoding='utf-8') as f:
        text = f.read().replace('\\', '\\\\').replace('{', '\\{').replace('}', '\\}')
    text = ''.join(c if ord(c) < 128 else '\\u%d?' % ord(c) for c in text)
    paragraphs = [' '.join(p.split()) for p in re.split(r'\n\s*\n', text.strip())]
    body = '\\par\\par\n'.join(paragraphs)
    path = os.path.join(DIST, 'License.rtf')
    with open(path, 'w', encoding='ascii', newline='\n') as f:
        f.write('{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}\\f0\\fs18\n' + body + '\\par\n}\n')
    return path


def build_win(ver):
    out = publish(TARGETS['win'])
    msi = os.path.join(DIST, 'GitUi-%s-win-x64.msi' % ver)
    run('dotnet', 'tool', 'restore')
    for ext in WIX_EXTENSIONS:
        run('dotnet', 'wix', 'extension', 'add', '-g', ext)
    run('dotnet', 'wix', 'build', os.path.join(PACKAGING, 'windows', 'GitUi.wxs'),
        '-arch', 'x64',
        '-ext', 'WixToolset.UI.wixext', '-ext', 'WixToolset.Util.wixext',
        '-d', 'Version=' + ver,
        '-d', 'PublishDir=' + out,
        '-d', 'IconFile=' + os.path.join(ASSETS, 'gitui.ico'),
        '-d', 'LicenseRtf=' + license_rtf(),
        '-o', msi)
    return msi


# ---------------------------------------------------------------- Debian .deb

def tar_gz(entries):
    """entries: (path, bytes | None for a directory | str for a symlink target, mode)."""
    buffer = io.BytesIO()
    now = int(time.time())
    with gzip.GzipFile(fileobj=buffer, mode='wb', mtime=now) as gz:
        with tarfile.open(fileobj=gz, mode='w', format=tarfile.GNU_FORMAT) as tar:
            for path, content, mode in entries:
                info = tarfile.TarInfo('./' + path if path else '.')
                info.uid = info.gid = 0
                info.uname = info.gname = 'root'
                info.mtime = now
                info.mode = mode
                if content is None:
                    info.type = tarfile.DIRTYPE
                    tar.addfile(info)
                elif isinstance(content, str):
                    info.type = tarfile.SYMTYPE
                    info.linkname = content
                    tar.addfile(info)
                else:
                    info.size = len(content)
                    tar.addfile(info, io.BytesIO(content))
    return buffer.getvalue()


def ar(members):
    """The ar archive a .deb is: members are (name, bytes), in order."""
    out = bytearray(b'!<arch>\n')
    now = int(time.time())
    for name, data in members:
        header = '%-16s%-12d%-6d%-6d%-8s%-10d`\n' % (name, now, 0, 0, '100644', len(data))
        out += header.encode('ascii') + data
        if len(data) % 2:
            out += b'\n'
    return bytes(out)


def build_deb(ver, arch):
    out = publish(TARGETS['deb-' + arch])
    files = {}  # path inside the package -> (bytes | str symlink, mode)
    for rel in walk(out):
        with open(os.path.join(out, rel), 'rb') as f:
            files['opt/gitui/' + rel] = (f.read(), 0o755 if is_executable(rel) else 0o644)
    files['usr/bin/gitui'] = ('/opt/gitui/' + EXE, 0o777)
    files['usr/share/applications/gitui.desktop'] = (read_lf(os.path.join(PACKAGING, 'linux', 'gitui.desktop')), 0o644)
    with open(os.path.join(ASSETS, 'gitui-256.png'), 'rb') as f:
        files['usr/share/icons/hicolor/256x256/apps/gitui.png'] = (f.read(), 0o644)

    directories = {''}
    for path in files:
        parts = path.split('/')[:-1]
        for i in range(1, len(parts) + 1):
            directories.add('/'.join(parts[:i]))
    entries = [(d, None, 0o755) for d in sorted(directories)]
    entries += [(p, content, mode) for p, (content, mode) in sorted(files.items())]

    installed_kb = sum(len(c) for c, _ in files.values() if isinstance(c, bytes)) // 1024 + 1
    control = (
        'Package: gitui\n'
        'Version: %s\n'
        'Architecture: %s\n'
        'Maintainer: Git UI <gitui@localhost>\n'
        'Installed-Size: %d\n'
        'Depends: %s\n'
        'Section: devel\n'
        'Priority: optional\n'
        'Description: Graphical Git client\n'
        ' Commit graph, staging, merge tool, rebase and more, in the style of GitKraken.\n'
    ) % (ver, arch, installed_kb, DEB_DEPENDS)
    md5sums = ''.join('%s  %s\n' % (hashlib.md5(c).hexdigest(), p)
                      for p, (c, _) in sorted(files.items()) if isinstance(c, bytes))
    control_tar = tar_gz([('', None, 0o755),
                          ('control', control.encode('utf-8'), 0o644),
                          ('md5sums', md5sums.encode('utf-8'), 0o644),
                          ('postinst', read_lf(os.path.join(PACKAGING, 'linux', 'postinst')), 0o755),
                          ('postrm', read_lf(os.path.join(PACKAGING, 'linux', 'postrm')), 0o755)])

    deb = os.path.join(DIST, 'gitui_%s_%s.deb' % (ver, arch))
    with open(deb, 'wb') as f:
        f.write(ar([('debian-binary', b'2.0\n'),
                    ('control.tar.gz', control_tar),
                    ('data.tar.gz', tar_gz(entries))]))
    return deb


# ---------------------------------------------------------------- macOS .app

def icns(png):
    """An .icns holding the 256 px PNG as-is ('ic08' accepts PNG data)."""
    entry = b'ic08' + struct.pack('>I', 8 + len(png)) + png
    return b'icns' + struct.pack('>I', 8 + len(entry)) + entry


def zip_entry(zf, path, data, mode):
    info = zipfile.ZipInfo(path, date_time=time.localtime()[:6])
    info.create_system = 3  # Unix, so external_attr carries the permissions
    info.external_attr = (0o100000 | mode) << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    zf.writestr(info, data)


def build_mac(ver, arch):
    out = publish(TARGETS['mac-' + arch])
    archive = os.path.join(DIST, 'GitUi-%s-macos-%s.zip' % (ver, arch))
    app = 'GitUi.app/Contents/'
    with open(os.path.join(PACKAGING, 'macos', 'Info.plist'), encoding='utf-8') as f:
        plist = f.read().replace('\r\n', '\n').replace('{VERSION}', ver)
    with open(os.path.join(ASSETS, 'gitui-256.png'), 'rb') as f:
        icon = icns(f.read())
    installer = 'Install Git UI.command'
    with zipfile.ZipFile(archive, 'w') as zf:
        zip_entry(zf, installer, read_lf(os.path.join(PACKAGING, 'macos', installer)), 0o755)
        zip_entry(zf, app + 'Info.plist', plist.encode('utf-8'), 0o644)
        zip_entry(zf, app + 'Resources/gitui.icns', icon, 0o644)
        for rel in walk(out):
            with open(os.path.join(out, rel), 'rb') as f:
                zip_entry(zf, app + 'MacOS/' + rel, f.read(), 0o755 if is_executable(rel) else 0o644)
    return archive


def main():
    wanted = sys.argv[1:] or list(TARGETS)
    unknown = [t for t in wanted if t not in TARGETS]
    if unknown:
        sys.exit('Unknown target(s): %s. Known: %s' % (', '.join(unknown), ', '.join(TARGETS)))
    ver = version()
    os.makedirs(DIST, exist_ok=True)
    built = []
    for target in wanted:
        if target == 'win':
            built.append(build_win(ver))
        elif target.startswith('deb-'):
            built.append(build_deb(ver, target[4:]))
        else:
            built.append(build_mac(ver, target[4:]))
    print('\nBuilt:')
    for path in built:
        print('  %s (%.1f MB)' % (os.path.relpath(path, ROOT), os.path.getsize(path) / 1e6))


if __name__ == '__main__':
    main()
