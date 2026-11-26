import os
import glob

def ensure_bom(path):
    with open(path, 'rb') as f:
        data = f.read()
    orig = data
    while data.startswith(b'\xef\xbb\xbf'):
        data = data[3:]
    data = data.replace(b'\r\r\n', b'\n').replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')
    data = b'\xef\xbb\xbf' + data
    if data != orig:
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

for root, _, files in os.walk('docs'):
    for file in files:
        if file.endswith('.md'):
            ensure_bom(os.path.join(root, file))

for file in glob.glob('*.md'):
    ensure_bom(file)
