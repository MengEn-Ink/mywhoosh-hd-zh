from pathlib import Path
import json,struct,hashlib,zipfile
root=Path(__file__).resolve().parent
folder=root/'screenshots'
report=json.loads((folder/'render-report.json').read_text(encoding='utf-8'))
assert len(report)==10
hashes=set()
for r in report:
    p=folder/r['file']; data=p.read_bytes()
    assert data[:8]==b'\x89PNG\r\n\x1a\n'
    assert struct.unpack('>II',data[16:24])==(1600,1000)
    assert r['textBoundsWarnings']==[]
    assert r['gameOperations'] is False
    hashes.add(hashlib.sha256(data).hexdigest())
assert len(hashes)==10,'Duplicated screenshots'
archive=root/'MyWhoosh-十套全新布局预览.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(folder.glob('*.png')): z.write(p,p.name)
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    assert len(z.namelist())==11
print(json.dumps({'verified':True,'layouts':10,'overview':1,'all_unique':True,'size':'1600x1000','text_bounds_warnings':0,'archive':str(archive)},ensure_ascii=False))
