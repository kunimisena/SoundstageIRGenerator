"""Rebuild the checked-in bilingual offline guide; Python standard library only."""
from pathlib import Path
import html,re
root=Path(__file__).resolve().parents[1]
def inline(s):
 s=html.escape(s)
 s=re.sub(r'`([^`]+)`',r'<code>\1</code>',s)
 s=re.sub(r'\*\*([^*]+)\*\*',r'<strong>\1</strong>',s)
 s=re.sub(r'\[([^\]]+)\]\((https?://[^)]+)\)',r'<a href="\2">\1</a>',s)
 # Local Markdown references remain descriptive; navigation is provided by this page.
 s=re.sub(r'\[([^\]]+)\]\([^)]+\)',r'\1',s)
 return s

def render(s):
 out=[];block=[];table=False;code=False;math=False
 for line in s.splitlines():
  if line.startswith('```'):
   if code:out.append('<pre>'+html.escape('\n'.join(block))+'</pre>');block=[]
   code=not code;continue
  if code:block.append(line);continue
  if line in (r'\[',r'\]'):
   if math:out.append('<pre class="formula">'+html.escape('\n'.join(block))+'</pre>');block=[]
   math=not math;continue
  if math:block.append(line);continue
  if line.startswith('|'):
   if not table:out.append('<div class="table"><table>');table=True
   cells=line.strip('|').split('|')
   if all(re.fullmatch(r'\s*:?-+:?\s*',c) for c in cells):continue
   out.append('<tr>'+''.join('<td>'+inline(c.strip())+'</td>' for c in cells)+'</tr>');continue
  if table:out.append('</table></div>');table=False
  if not line.strip():continue
  m=re.match(r'^(#{1,3}) (.+)',line)
  if m:out.append(f'<h{len(m[1])}>{inline(m[2])}</h{len(m[1])}>')
  elif line.startswith('- '):out.append('<p class="bullet">• '+inline(line[2:])+'</p>')
  else:out.append('<p>'+inline(line)+'</p>')
 if table:out.append('</table></div>')
 return '\n'.join(out)
sections=[('zh-guide','使用指南','zh-CN/guide.md'),('zh-design','设计哲学','zh-CN/design.md'),('en-guide','User guide','en/guide.md'),('en-design','Design philosophy','en/design.md')]
nav=''.join(f'<button onclick="show(\'{i}\')">{label}</button>' for i,label,_ in sections)
body=''.join(f'<article id="{i}"'+('' if j==0 else ' hidden')+'>'+render((root/'docs'/file).read_text(encoding='utf-8'))+'</article>' for j,(i,label,file) in enumerate(sections))
page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Statistical Field Studio · Guide</title><style>
:root{color-scheme:light}body{margin:0;background:#f6f0e1;color:#39352d;font:16px/1.75 system-ui,"Microsoft YaHei",sans-serif}header{background:#eee7d8;padding:16px max(20px,calc((100vw - 1020px)/2));border-bottom:1px solid #324656}header b{letter-spacing:.12em;color:#426b71}nav{display:flex;gap:8px;flex-wrap:wrap;margin-top:10px}button{padding:9px 16px;border:1px solid #405768;background:#e6decc;border-radius:5px;color:#39352d;cursor:pointer}button:hover{background:#dbe1d8}main{max-width:1020px;margin:auto;padding:24px}h1{font-size:30px}h2{margin-top:2em;color:#39352d}h3{color:#404040}a{color:#426b71}p{max-width:90ch}code,pre{font-family:Consolas,monospace;background:#eee7d8;border-radius:4px}code{padding:1px 4px}pre{padding:15px;overflow:auto;white-space:pre-wrap;font-size:14px}.table{overflow:auto}table{width:100%;border-collapse:collapse;font-size:14px}td{padding:9px;border:1px solid #ddd}tr:first-child{background:#e6decc;font-weight:600}.bullet{margin:.4em 0}footer{padding:25px;color:#606060;border-top:1px solid #324656}article[hidden]{display:none}
</style><header><b>STATISTICAL FIELD STUDIO</b><div>4.9.3 · 离线双语指南 / Offline bilingual guide</div><nav>'''+nav+'''</nav></header><main>'''+body+'''</main><footer>Original code and documentation: MIT / 自有代码与文档：MIT。FABIAN attribution: FABIAN-NOTICE.txt beside the application.</footer><script>function show(id){document.querySelectorAll('article').forEach(a=>a.hidden=a.id!==id);document.documentElement.lang=id.startsWith('en')?'en':'zh-CN';window.scrollTo(0,0);location.hash=id;}if(document.getElementById(location.hash.slice(1)))show(location.hash.slice(1));</script></html>'''
(root/'docs/guide.html').write_text(page,encoding='utf-8')
print('Offline guide generated.')
