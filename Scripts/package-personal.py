from pathlib import Path
import hashlib
import datetime
import json
import re
import shutil
import urllib.request
import zipfile

root = Path(__file__).resolve().parents[1]
artifacts = root / '.artifacts'
output = root / 'Output/Release'
publish = artifacts / 'publish'
bundle = artifacts / 'bundle'
if bundle.exists():
    bundle = artifacts / ('bundle-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
packages = artifacts / 'packages'
catalog = json.loads((root / 'PersonalPlugins/plugins.json').read_text(encoding='utf-8'))
version = re.search(r'AssemblyInformationalVersion\("([^"]+)"\)',
                    (root / 'SolutionAssemblyInfo.cs').read_text(encoding='utf-8-sig')).group(1)
rg_version = '15.2.0'
rg_name = 'ripgrep-' + rg_version + '-x86_64-pc-windows-msvc'
rg_hash = '71b2fef860abe467217a538ff31de02f5258807c0129f771846f87bd029aafc5'
archive = artifacts / 'downloads' / (rg_name + '.zip')
archive.parent.mkdir(parents=True, exist_ok=True)
if not archive.exists():
    url = 'https://github.com/BurntSushi/ripgrep/releases/download/' + rg_version + '/' + archive.name
    with urllib.request.urlopen(url, timeout=60) as response:
        archive.write_bytes(response.read())
if hashlib.sha256(archive.read_bytes()).hexdigest() != rg_hash:
    raise RuntimeError('ripgrep 归档哈希不匹配；未使用未经校验的文件。')
if not (publish / 'Flow.Launcher.exe').exists():
    raise RuntimeError('请先执行 Scripts/build-personal.ps1。')
shutil.copytree(publish, bundle)
shutil.copytree(output / 'Plugins', bundle / 'Plugins', dirs_exist_ok=True)
for item in catalog['plugins']:
    source = output / 'PersonalPlugins' / item['assembly']
    target = bundle / 'UserData/Plugins' / item['assembly']
    shutil.copytree(source, target, dirs_exist_ok=True)
    if item['assembly'] == 'Flow.Launcher.Plugin.NoteContent':
        with zipfile.ZipFile(archive) as rg:
            rg_binary = rg.read(rg_name + '/rg.exe')
            (target / 'rg.exe').write_bytes(rg_binary)
            (archive.parent / 'rg.exe').write_bytes(rg_binary)
            for name in rg.namelist():
                if name.endswith(('COPYING', 'LICENSE-MIT', 'UNLICENSE')):
                    license_path = target / 'licenses' / Path(name).name
                    license_path.parent.mkdir(exist_ok=True)
                    license_path.write_bytes(rg.read(name))
    metadata = json.loads((target / 'plugin.json').read_text(encoding='utf-8-sig'))
    if metadata['ID'] != item['id']:
        raise RuntimeError('插件构建身份不匹配: ' + item['name'])
    if (target / 'Settings.json').exists():
        raise RuntimeError('个人插件包不应含用户配置。')
shutil.copy2(root / 'LICENSE', bundle / 'LICENSE')
shutil.copy2(root / 'README.personal.md', bundle / 'README.personal.md')
shutil.copy2(root / 'PersonalPlugins/plugins.json', bundle / 'personal-plugins.json')
manifest = {'version': version, 'files': [
    {'path': path.relative_to(bundle).as_posix(), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    for path in sorted(bundle.rglob('*')) if path.is_file()
]}
(bundle / 'personal-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(artifacts / 'latest-bundle.json').write_text(json.dumps({'bundle': str(bundle), 'version': version}, ensure_ascii=False) + '\n', encoding='utf-8')
packages.mkdir(parents=True, exist_ok=True)
for item in catalog['plugins']:
    plugin_dir = bundle / 'UserData/Plugins' / item['assembly']
    shutil.make_archive(str(packages / (item['assembly'] + '-' + item['version'])), 'zip', plugin_dir)
shutil.make_archive(str(packages / ('Flow-Launcher-Personal-' + version)), 'zip', bundle)
print(json.dumps({'version': version, 'bundle': str(bundle), 'files': len(manifest['files']),
                  'packages': [path.name for path in packages.glob('*.zip')]}, ensure_ascii=False))
