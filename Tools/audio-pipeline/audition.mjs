import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
const SEL = 'D:/Unity Project/_AudioResearch/Combat/Selected';
const CUT = 'D:/Unity Project/_AudioResearch/Combat/Cut';

// 事件 → 在遊戲裡什麼時候會聽到（從 docs/37 §3 抄過來，讓試聽時有情境）
const CONTEXT = {
  'Fireball/Cast': '按下 Slot2 的瞬間（抬手）。之後 0.42 秒才會 Release。',
  'Fireball/Release': '火球真的射出去的那一刻，從角色身上播。',
  'Fireball/Impact': '火球打到東西，**在接觸點**播（可能離角色很遠）。',
  'Ice/Cast': '按下 Slot3 的瞬間（抬手）。',
  'Ice/Spawn': '施法完成，從角色身上播。與 Ice/Impact 同時發生。',
  'Ice/Impact': '冰刺從地上冒出來，**在身前 0.4–4.4 m 的落點**播。',
  'Melee/Swing': '每次揮擊**必定**會響。三段連擊 = 三聲。',
  'Melee/Hit': '只有真的打到敵人才響，**在被打者身上**播。',
  'Character/Hurt': '受到未致死傷害。玩家與敵人用不同的資產。',
  'Character/Death': '由活轉死的那一幀。',
  'Character/Exertion': '（目前未接線，備援素材）',
  'Jump/Takeoff': '每次離地都會響——**包括走下平台邊緣**（已知近似）。太吵的話先降音量。',
};

function info(f) {
  try {
    const j = JSON.parse(execFileSync('ffprobe', ['-v','error','-print_format','json','-show_format','-show_streams', f], { encoding: 'utf8' }));
    return { dur: Number(j.format.duration).toFixed(2) };
  } catch { return { dur: '?' }; }
}

function listDir(root) {
  const out = {};
  (function w(d) {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) w(p);
      else if (e.name.endsWith('.wav')) {
        const ev = path.relative(root, path.dirname(p)).split(path.sep).join('/');
        (out[ev] = out[ev] || []).push({ name: e.name, rel: path.relative(root, p).split(path.sep).join('/'), ...info(p) });
      }
    }
  })(root);
  return out;
}

const sel = listDir(SEL);
const cut = listDir(CUT);

const esc = s => s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
const enc = s => s.split('/').map(encodeURIComponent).join('/');

let body = '';
for (const ev of Object.keys(sel).sort()) {
  const picked = sel[ev];
  const all = cut[ev] || [];
  const reserve = all.filter(a => !picked.some(p => p.name === a.name));
  body += `<section><h2>${esc(ev)} <span class="n">${picked.length} 選用${reserve.length ? ` · ${reserve.length} 備援` : ''}</span></h2>`;
  body += `<p class="ctx">${esc(CONTEXT[ev] || '')}</p>`;
  body += '<table><tbody>';
  for (const c of picked) {
    body += `<tr><td class="d">${c.dur}s</td><td><audio controls preload="none" src="Selected/${enc(c.rel)}"></audio></td><td class="f">${esc(c.name)}</td></tr>`;
  }
  body += '</tbody></table>';
  if (reserve.length) {
    body += `<details><summary>備援 ${reserve.length} 個（在 Cut/ 裡，未進 Unity）</summary><table><tbody>`;
    for (const c of reserve) {
      body += `<tr><td class="d">${c.dur}s</td><td><audio controls preload="none" src="Cut/${enc(c.rel)}"></audio></td><td class="f">${esc(c.name)}</td></tr>`;
    }
    body += '</tbody></table></details>';
  }
  body += '</section>';
}

const html = `<!DOCTYPE html><html lang="zh-Hant"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Combat SFX Audition</title><style>
:root{--bg:#faf9f7;--fg:#1d1c1a;--mut:#6b6864;--line:#e3e0db;--card:#fff;--acc:#b8562f}
@media(prefers-color-scheme:dark){:root:not([data-theme=light]){--bg:#17161a;--fg:#eceae6;--mut:#9a9691;--line:#302d33;--card:#1f1d22;--acc:#e08a5e}}
:root[data-theme=dark]{--bg:#17161a;--fg:#eceae6;--mut:#9a9691;--line:#302d33;--card:#1f1d22;--acc:#e08a5e}
*{box-sizing:border-box}
body{margin:0;padding:32px 16px 80px;background:var(--bg);color:var(--fg);
font:15px/1.6 ui-sans-serif,system-ui,"Noto Sans TC",sans-serif}
.wrap{max-width:900px;margin:0 auto}
h1{font-size:26px;margin:0 0 4px}
.sub{color:var(--mut);margin:0 0 24px}
.warn{background:var(--card);border:1px solid var(--line);border-left:3px solid var(--acc);
padding:14px 16px;border-radius:8px;margin:0 0 28px}
.warn b{color:var(--acc)}
section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px 18px;margin:0 0 16px}
h2{font-size:17px;margin:0 0 6px;display:flex;align-items:baseline;gap:10px;flex-wrap:wrap}
.n{font-size:12px;font-weight:400;color:var(--mut)}
.ctx{color:var(--mut);font-size:13px;margin:0 0 12px}
table{width:100%;border-collapse:collapse}
td{padding:5px 6px;border-bottom:1px solid var(--line);vertical-align:middle}
tr:last-child td{border-bottom:0}
.d{color:var(--mut);font-size:12px;white-space:nowrap;width:1%;font-variant-numeric:tabular-nums}
.f{font-size:11px;color:var(--mut);word-break:break-all;font-family:ui-monospace,monospace}
audio{height:32px;width:260px;max-width:100%;display:block}
details{margin-top:10px}summary{cursor:pointer;color:var(--mut);font-size:13px}
@media(max-width:620px){audio{width:100%}.f{display:none}}
</style></head><body><div class="wrap">
<h1>戰鬥音效試聽</h1>
<p class="sub">56 個選用 clip，全部 48 kHz / mono / 16-bit，已砍前置靜音、峰值正規化到 −1 dBFS。</p>
<div class="warn"><b>⚠️ 這份選材沒有經過耳朵。</b> 我無法試聽音訊——依據只有客觀量測（長度／前置靜音／峰值）、
檔名語意（UCS 前綴），以及與專案已知事實的匹配（角色持劍 ⇒ 排除木棍撞擊）。
<b>真正的「符合 demo 的視覺與動畫重量」只有你聽得出來。</b>
覺得哪個不對就記下來，換成備援、或告訴我換一個母帶重切。</div>
${body}
</div></body></html>`;

fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/audition.html', html);
console.log('wrote audition.html  events=' + Object.keys(sel).length);
