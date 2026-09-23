"""验证 zip 里 index.js 的字段兼容逻辑是否到位"""
import zipfile

ZIP = r'C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype\serverless\fc\chinago-feedback.zip'

with zipfile.ZipFile(ZIP) as z:
    names = z.namelist()
    print(f"Total entries: {len(names)}")
    print(f"\nTop-level (first 10):")
    for n in names[:10]:
        info = z.getinfo(n)
        print(f"  {n}  ({info.file_size:,} B)")
    print(f"\nnode_modules count: {sum(1 for n in names if n.startswith('node_modules/'))}")

    # 读 index.js 内容，检查关键修复
    with z.open('index.js') as f:
        content = f.read().decode('utf-8')

print('\n--- 关键修复点验证 ---')
checks = [
    ('PascalCase 兼容', 'body.FeedbackId'),
    ('camelCase 兼容', 'body.feedbackId'),
    ('submittedAtUtc 兼容', 'body.SubmittedAtUtc'),
    ('normalized 输出', 'normalized,'),
    ('Buffer 解析', 'Buffer.isBuffer(event)'),
    ('body 兼容', 'evt.rawBody'),
]
for label, key in checks:
    found = '✅' if key in content else '❌'
    print(f'  {found} {label}  (contains "{key}")')
