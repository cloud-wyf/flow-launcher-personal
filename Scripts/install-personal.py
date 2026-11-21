from pathlib import Path
import argparse
import copy
import csv
import datetime
import hashlib
import io
import json
import shutil
import subprocess


def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inside(root, path):
    path = path.resolve()
    if not path.is_relative_to(root.resolve()):
        raise RuntimeError('路径超出安装目录: ' + str(path))
    return path


def rewrite_records(value, replacements):
    changed = False
    if isinstance(value, dict):
        for key, child in list(value.items()):
            if key == 'PluginID' and isinstance(child, str) and child in replacements:
                value[key] = replacements[child]
                changed = True
            else:
                changed = rewrite_records(child, replacements) or changed
    elif isinstance(value, list):
        for child in value:
            changed = rewrite_records(child, replacements) or changed
    return changed


def migrations(app, catalog):
    data = app / 'UserData'
    edits = {}
    settings_root = data / 'Settings'
    main_path = settings_root / 'Settings.json'
    replacements = {item['replaces']: item['id'] for item in catalog['plugins'] if 'replaces' in item}
    if main_path.exists():
        settings = json.loads(main_path.read_text(encoding='utf-8-sig'))
        original = copy.deepcopy(settings)
        plugins = settings.setdefault('PluginSettings', {}).setdefault('Plugins', {})
        for item in catalog['plugins']:
            previous = plugins.get(item.get('replaces'))
            if previous is not None:
                if item['id'] not in plugins:
                    current = copy.deepcopy(previous)
                    current.update(ID=item['id'], Name=item['name'], Version=item['version'])
                    plugins[item['id']] = current
                previous['Disabled'] = True
        settings['AutoUpdates'] = False
        if settings != original:
            edits[main_path] = encode(settings)
    for path in settings_root.rglob('*.json'):
        if path == main_path:
            continue
        value = json.loads(path.read_text(encoding='utf-8-sig'))
        if rewrite_records(value, replacements):
            edits[path] = encode(value)
    for item in catalog['plugins']:
        destination = settings_root / 'Plugins' / item['assembly']
        if 'legacyAssembly' in item:
            legacy = settings_root / 'Plugins' / item['legacyAssembly']
            for path in legacy.rglob('*'):
                if path.is_file():
                    target = destination / path.relative_to(legacy)
                    if not target.exists():
                        edits[target] = path.read_bytes()
        if item['assembly'] == 'Flow.Launcher.Plugin.NoteContent':
            target = destination / 'Settings.json'
            if not target.exists():
                for metadata_path in (data / 'Plugins').glob('*/plugin.json'):
                    metadata = json.loads(metadata_path.read_text(encoding='utf-8-sig'))
                    legacy = metadata_path.parent / 'Settings.json'
                    if metadata.get('ID') == item['id'] and legacy.exists():
                        edits[target] = legacy.read_bytes()
                        break
    return edits


def ensure_stopped():
    result = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq Flow.Launcher.exe', '/FO', 'CSV', '/NH'],
                            capture_output=True, text=True, errors='replace', check=True)
    if any(row and row[0].lower() == 'flow.launcher.exe' for row in csv.reader(io.StringIO(result.stdout))):
        raise RuntimeError('请先从托盘正常退出 Flow；安装不会强制结束进程。')


def payloads(app, bundle, catalog):
    manifest = json.loads((bundle / 'personal-manifest.json').read_text(encoding='utf-8'))
    plugin_folders = {}
    for item in catalog['plugins']:
        candidates = []
        for path in (app / 'UserData/Plugins').glob('*/plugin.json'):
            metadata = json.loads(path.read_text(encoding='utf-8-sig'))
            if metadata.get('ID') == item['id']:
                candidates.append(path.parent)
        if len(candidates) > 1:
            raise RuntimeError('已有多个相同 ID 的插件，停止安装: ' + item['name'])
        plugin_folders[item['assembly']] = candidates[0] if candidates else (
            app / 'UserData/Plugins' / (item['assembly'] + '-' + item['version']))
    edits = {}
    for item in manifest['files']:
        relative = Path(item['path'])
        source = inside(bundle, bundle / relative)
        if sha(source) != item['sha256']:
            raise RuntimeError('安装包哈希不匹配: ' + item['path'])
        target = app / relative
        if relative.parts[:2] == ('UserData', 'Plugins'):
            assembly = relative.parts[2]
            target = plugin_folders[assembly].joinpath(*relative.parts[3:])
        elif relative.parts[0] == 'Plugins' and (app / 'Flow.Launcher.dll').exists():
            # Existing third-party/default plugin binaries are preserved during migration.
            continue
        target = inside(app, target)
        if not target.exists() or sha(target) != item['sha256']:
            edits[target] = source.read_bytes()
    return edits


def apply(app, edits, backup):
    backup.mkdir(parents=True, exist_ok=False)
    originals = {}
    for path in edits:
        path = inside(app, path)
        original = path.read_bytes() if path.exists() else None
        originals[path] = original
        if original is not None:
            saved = backup / path.relative_to(app)
            saved.parent.mkdir(parents=True, exist_ok=True)
            saved.write_bytes(original)
    (backup / 'restore-manifest.json').write_bytes(encode({
        'app': str(app), 'files': [{'path': p.relative_to(app).as_posix(), 'existed': data is not None}
                                for p, data in originals.items()]}))
    written = []
    try:
        for path, data in edits.items():
            written.append(path)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            if path.read_bytes() != data:
                raise RuntimeError('安装文件校验失败: ' + str(path))
    except Exception:
        for path in reversed(written):
            original = originals[path]
            if original is None:
                if path.exists():
                    inside(app, path).unlink()
            else:
                path.write_bytes(original)
        raise
    return {'app': str(app), 'backup': str(backup), 'status': 'installed',
            'files': [{'path': p.relative_to(app).as_posix(), 'sha256': sha(p)} for p in edits]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--app', required=True, type=Path)
    parser.add_argument('--bundle', required=True, type=Path)
    parser.add_argument('--plan', action='store_true')
    args = parser.parse_args()
    app, bundle = args.app.resolve(), args.bundle.resolve()
    if app == bundle or app.is_relative_to(bundle) or bundle.is_relative_to(app):
        raise RuntimeError('安装包与安装目录必须分离。')
    version = app / 'Flow.Launcher.dll'
    if version.exists():
        literal = str(version).replace("'", "''")
        result = subprocess.run(['powershell', '-NoProfile', '-Command',
                                "[Diagnostics.FileVersionInfo]::GetVersionInfo('" + literal + "').ProductVersion"],
                               capture_output=True, text=True, check=True)
        if result.stdout.strip().split('.')[:3] != ['2', '1', '4']:
            raise RuntimeError('本次迁移只支持 v2.1.4，其他版本需先合并上游源码。')
    catalog = json.loads((bundle / 'personal-plugins.json').read_text(encoding='utf-8'))
    edits = payloads(app, bundle, catalog)
    config_edits = migrations(app, catalog)
    edits.update(config_edits)
    if args.plan:
        print(json.dumps({'filesToWrite': len(edits), 'configurationFilesToMigrate': len(config_edits)}, ensure_ascii=False))
        return
    ensure_stopped()
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup = app.parent / 'LocalPatches' / ('personal-migration-' + stamp)
    status = apply(app, edits, backup)
    (backup / 'installation-status.json').write_bytes(encode(status))
    print(json.dumps({'status': status['status'], 'backup': str(backup), 'writtenFiles': len(edits),
                      'migratedConfigurations': len(config_edits)}, ensure_ascii=False))


if __name__ == '__main__':
    main()
