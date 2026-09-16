import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve('D:/Unity Project/_AudioResearch/Combat');
const TYPES = { '.html': 'text/html; charset=utf-8', '.wav': 'audio/wav', '.json': 'application/json' };

http.createServer((req, res) => {
  const rel = decodeURIComponent(req.url.split('?')[0]).replace(/^\/+/, '') || 'audition.html';
  const f = path.resolve(ROOT, rel);
  // ⚠️ 必須用 path.resolve 後比對，不能拿字面量 ROOT 比——Windows 上 path.join 會產出反斜線，
  //    與寫成正斜線的 ROOT 永遠不相等 ⇒ 每個檔案都被自己的守門擋成 403（實際踩到）。
  if (!f.startsWith(ROOT)) { res.writeHead(403); return res.end('forbidden'); }
  fs.readFile(f, (e, d) => {
    if (e) { res.writeHead(404); return res.end('not found: ' + rel); }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(f).toLowerCase()] || 'application/octet-stream' });
    res.end(d);
  });
}).listen(8731, () => console.log('http://localhost:8731/'));
