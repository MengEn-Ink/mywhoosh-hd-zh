"""Reproduce the supplement from a caller-selected cache copy. No game writes."""
import argparse, hashlib, json, pathlib
parser = argparse.ArgumentParser()
parser.add_argument('cache', type=pathlib.Path, help='Readable Cycling cache copy')
parser.add_argument('output', type=pathlib.Path, help='New supplement output; never overwritten')
args = parser.parse_args()
rows = []
for p in args.cache.rglob('*.json'):
    a = json.loads(p.read_bytes())
    if a.get('Name') != '15min Varied Tempo #1':
        continue
    desc = next(v for k, v in a.items() if k.lower() == 'description')
    rows.append({'Name': a['Name'], 'Description': desc, 'ChineseName': '15分钟变踏频节奏训练 #1',
                 'ChineseDescription': '今天在第3区（节奏区）训练中变化踏频和功率。在15分钟的训练区间内观察心率，留意踏频变化时心率的变化。\n\n今天的强度位于该训练区的较低端，额外的训练负荷来自目标踏频要求。\n\n【中文要点已按英文核对；以下保留英文原文】\n' + desc})
assert len(rows) == 3 and len({(r['Name'], r['Description']) for r in rows}) == 3
assert all('15' in r['ChineseDescription'] and '第3区' in r['ChineseDescription'] for r in rows)
assert args.output.parent.is_dir(), 'Output parent must already exist'
assert not args.output.exists(), 'Do not overwrite existing supplemental resource'
# Stable across filesystem enumeration orders.
rows.sort(key=lambda row: row['Description'])
data = (json.dumps(rows, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
with args.output.open('xb') as f:
    f.write(data)
print('SUPPLEMENT_SHA256=' + hashlib.sha256(data).hexdigest().upper())
