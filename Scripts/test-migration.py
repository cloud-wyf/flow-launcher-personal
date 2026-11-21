from pathlib import Path
import importlib.util
import json
import uuid

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('installer', root / 'Scripts/install-personal.py')
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)
fixture = root / '.artifacts/migration-tests' / uuid.uuid4().hex
app = fixture / 'app'
settings = app / 'UserData/Settings'
settings.mkdir(parents=True)
catalog = json.loads((root / 'PersonalPlugins/plugins.json').read_text(encoding='utf-8'))
bookmark, note = catalog['plugins']
old_id, new_id = bookmark['replaces'], bookmark['id']
config = {'AutoUpdates': True, 'PluginSettings': {'Plugins': {old_id: {
    'ID': old_id, 'Name': 'Browser Bookmarks', 'Version': '2.1.4',
    'ActionKeywords': ['*', 'b'], 'Priority': 17, 'Disabled': False}}}, 'Unrelated': 'preserve'}
(settings / 'Settings.json').write_bytes(installer.encode(config))
legacy = settings / 'Plugins' / bookmark['legacyAssembly']
legacy.mkdir(parents=True)
tags = {'https://example.invalid': ['工作', '云服务']}
(legacy / 'BookmarkTags.json').write_bytes(installer.encode(tags))
(legacy / 'Settings.json').write_bytes(installer.encode({'EnableChrome': True}))
records = {'records': {'a': [{'PluginID': old_id, 'Title': 'Alpha'}, {'PluginID': 'other'}]}}
(settings / 'TopMostRecord.json').write_bytes(installer.encode(records))
note_dir = app / 'UserData/Plugins/NoteContent-1.0.0'
note_dir.mkdir(parents=True)
(note_dir / 'plugin.json').write_bytes(installer.encode({'ID': note['id']}))
note_config = {'SearchDirectories': ['D:/Example'], 'FileGlobs': ['*.md'], 'MaxResults': 200, 'TimeoutSeconds': 10}
(note_dir / 'Settings.json').write_bytes(installer.encode(note_config))
edits = installer.migrations(app, catalog)
assert (settings / 'Settings.json').read_bytes() == installer.encode(config), '计划不能写入配置'
status = installer.apply(app, edits, fixture / 'backup')
migrated = json.loads((settings / 'Settings.json').read_text(encoding='utf-8'))
assert migrated['PluginSettings']['Plugins'][old_id]['Disabled']
assert migrated['PluginSettings']['Plugins'][new_id]['ActionKeywords'] == ['*', 'b']
assert migrated['PluginSettings']['Plugins'][new_id]['Priority'] == 17
assert not migrated['PluginSettings']['Plugins'][new_id]['Disabled']
assert migrated['Unrelated'] == 'preserve' and not migrated['AutoUpdates']
assert json.loads((settings / 'Plugins' / bookmark['assembly'] / 'BookmarkTags.json').read_text(encoding='utf-8')) == tags
assert json.loads((settings / 'Plugins' / note['assembly'] / 'Settings.json').read_text(encoding='utf-8')) == note_config
assert json.loads((settings / 'TopMostRecord.json').read_text(encoding='utf-8'))['records']['a'][0]['PluginID'] == new_id
assert json.loads((settings / 'TopMostRecord.json').read_text(encoding='utf-8'))['records']['a'][1]['PluginID'] == 'other'
assert installer.migrations(app, catalog) == {}, '重复执行必须保持新配置不变'
before = (settings / 'Settings.json').read_bytes()
blocked = app / 'blocked'
blocked.write_text('file', encoding='utf-8')
try:
    installer.apply(app, {settings / 'Settings.json': b'changed', blocked / 'child': b'cannot-write'}, fixture / 'rollback-backup')
    raise AssertionError('应该触发失败')
except FileExistsError:
    pass
assert (settings / 'Settings.json').read_bytes() == before, '失败必须恢复原配置'
try:
    installer.inside(app, app / '..' / 'outside')
    raise AssertionError('应该拒绝越界路径')
except RuntimeError:
    pass
print('PASS: 关键词和优先级迁移、标签保留、旧插件禁用、置顶映射、正文设置迁移、幂等和失败回滚。')
