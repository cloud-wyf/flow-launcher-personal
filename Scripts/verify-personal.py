from pathlib import Path
import json
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
catalog = json.loads((root / 'PersonalPlugins/plugins.json').read_text(encoding='utf-8'))
ids = set()
assemblies = set()
for item in catalog['plugins']:
    folder = root / 'PersonalPlugins' / item['assembly']
    metadata = json.loads((folder / 'plugin.json').read_text(encoding='utf-8'))
    assert metadata['ID'] == item['id'] and metadata['Version'] == item['version']
    assert metadata['ExecuteFileName'] == item['assembly'] + '.dll'
    assert item['id'] not in ids and item['assembly'] not in assemblies
    assert item['id'] != item.get('replaces')
    ids.add(item['id'])
    assemblies.add(item['assembly'])
    project = (folder / (item['assembly'] + '.csproj')).read_text(encoding='utf-8-sig')
    assert 'HintPath>D:' not in project and 'Program Files' not in project
assert '948d4e4b11de51519ea9ff8a4bb8b4e4' in (root / 'Flow.Launcher/ViewModel/ResultPresentation.cs').read_text(encoding='utf-8-sig')
assert 'cloud-wyf/flow-launcher-personal' in (root / 'Flow.Launcher/Properties/Settings.Designer.cs').read_text(encoding='utf-8-sig')
assert 'taskkill' not in (root / 'Flow.Launcher/Flow.Launcher.csproj').read_text(encoding='utf-8-sig')
assert json.loads((root / 'Plugins/Flow.Launcher.Plugin.BrowserBookmark/plugin.json').read_text(encoding='utf-8-sig'))['Disabled']
tracked = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z']).decode().split('\0')
assert not any('/UserData/' in path or path.startswith('UserData/') for path in tracked if path)
print('PASS: 插件身份隔离、来源关联、更新地址、工程引用和配置排除检查通过。')
