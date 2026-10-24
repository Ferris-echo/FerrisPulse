import os
import glob

def ensure_bom(path):
    with open(path, 'rb') as f:
        data = f.read()
    changed = False
    if b'\r\r\n' in data:
        data = data.replace(b'\r\r\n', b'\r\n')
        changed = True
    if not data.startswith(b'\xef\xbb\xbf'):
        data = b'\xef\xbb\xbf' + data
        changed = True
    if changed:
        with open(path, 'wb') as f:
            f.write(data)
        print('Normalized and added BOM to: ' + path)

for root, _, files in os.walk('src'):
    for file in files:
        if file.endswith('.cs'):
            ensure_bom(os.path.join(root, file))

for root, _, files in os.walk('tests'):
    for file in files:
        if file.endswith('.ps1'):
            ensure_bom(os.path.join(root, file))

for root, _, files in os.walk('tools'):
    for file in files:
        if file.endswith('.ps1'):
            ensure_bom(os.path.join(root, file))
