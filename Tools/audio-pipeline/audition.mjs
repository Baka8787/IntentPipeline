// 產生「情境試聽」頁：用 Web Audio API 以**遊戲裡真實的時間與相對電平**排程多個 clip，
// 而不是一次聽一個孤立的 wav。
//
// ⭐ 為什麼非這樣不可（使用者 #1）：近戰的 swing / hit / hurt 幾乎**同時**發生
//    （Melee release 在 0.6 × 0.4 = 0.24 s，命中窗同幀開，Hurt 慢一幀 ≈16 ms）。
//    孤立聽每一顆都可能「沒問題」，疊起來才會發現互相吃掉。
import fs from 'node:fs';
import path from 'node:path';

const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
const enc = s => s.split('/').map(encodeURIComponent).join('/');

const SEL = 'D:/Unity Project/_AudioResearch/Combat/Selected';
const CUT = 'D:/Unity Project/_AudioResearch/Combat/Cut';
const META = 'D:/Unity Project/_AudioResearch/Combat/_meta';

const match = JSON.parse(fs.readFileSync(`${META}/pool-loudness-match.json`, 'utf8'));
const target = Object.fromEntries(match.map(m => [m.event, m.target]));

// SO 上的 volume（WireClips.cs）
const VOL = {
  'Melee/Swing': 0.65, 'Melee/Hit': 0.95, 'Character/Hurt': 0.80, 'Character/Death': 0.90,
  'Fireball/Cast': 0.70, 'Fireball/Release': 0.85, 'Fireball/Impact': 0.90,
  'Ice/Cast': 0.70, 'Ice/Spawn': 0.85, 'Ice/Impact': 0.90,
  'Jump/Takeoff': 0.45, 'Character/Exertion': 0.80,
};

// 情境：[標題, 說明, [[事件, 起始秒, 標註]…]]
const SCENES = [
  ['近戰：揮擊 → 命中 → 受擊', {
    why: '⚠️ <b>這三個幾乎同時發生，不是先後</b>。Melee release 在 0.6×0.4 = <b>0.24 s</b>，命中窗同幀開啟，Hurt 慢一幀（≈16 ms）。',
    watch: '① Melee_Hit 聽不聽得到（它比 Enemy_Hurt 低 8.3 dB）② 會不會糊成一團 ③ 有沒有「兩次撞擊」的感覺——Melee_Swing 用的母帶叫 <b>SWING <u>IMPACTS</u> … To Thud Impact</b>，它自己就含一次撞擊',
    parts: [['Melee/Swing', 0, '揮擊（含母帶自帶的 thud）'], ['Melee/Hit', 0.02, '命中'], ['Character/Hurt', 0.036, '敵人受擊']],
  }],
  ['近戰：只有揮擊（沒打中）', {
    why: '每次揮擊都會響，打不中時只有這一顆。',
    watch: '單獨聽時它像不像「揮空」？若它自帶 thud，揮空也會有撞擊聲 ⇒ 那就不對。',
    parts: [['Melee/Swing', 0, '揮擊']],
  }],
  ['火球：Cast → Release → 命中', {
    why: 'Cast 在按下的瞬間，Release 在 1.2×0.35 = <b>0.42 s</b> 之後。命中時間取決於距離（3 m ÷ 15 m/s ≈ 0.2 s）。',
    watch: '① Cast 與 Release 是兩聲還是糊成一聲 ② 聽起來像<b>法術</b>還是像 UI／科技音效 ③ 命中有沒有份量',
    parts: [['Fireball/Cast', 0, 'Cast'], ['Fireball/Release', 0.42, 'Release'], ['Fireball/Impact', 0.62, '命中'], ['Character/Hurt', 0.636, '敵人受擊']],
  }],
  ['冰刺：Cast → Spawn ＋ 地面爆發', {
    why: 'Cast 在按下瞬間，Release/Spawn 在 1.367×0.35 = <b>0.48 s</b>。<b>Ice_Release（角色身上）與 Ice_Impact（身前落點）同時播</b>。',
    watch: '① 兩個同時播會不會糊 ② 有沒有「冰」的身份，還是只是 generic impact／科技音',
    parts: [['Ice/Cast', 0, 'Cast'], ['Ice/Spawn', 0.48, 'Spawn（角色）'], ['Ice/Impact', 0.48, '地面爆發（落點）'], ['Character/Hurt', 0.50, '敵人受擊']],
  }],
  ['敵人死亡', {
    why: '致死那一幀只有 Death，<b>不會</b>同時有 Hurt（ADR-009 D2 保證）。',
    watch: '① 有沒有「結束感」② 目前 Enemy_Death 只有 2 顆、Player_Death 只有 <b>1 顆</b>',
    parts: [['Melee/Swing', 0, '揮擊'], ['Melee/Hit', 0.02, '命中'], ['Character/Death', 0.036, '死亡']],
  }],
  ['起跳', {
    why: '每次離地都響——<b>包括走下平台邊緣</b>（JustLeftGround 是「離地」不是「起跳」）。',
    watch: '會不會太吵、太頻繁、太出戲。目前刻意壓到 −22.2 dB（全場第二低）。',
    parts: [['Jump/Takeoff', 0, '起跳出力']],
  }],
];

function listDir(root) {
  const out = {};
  (function w(d) {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) w(p);
      else if (e.name.endsWith('.wav')) {
        const ev = path.relative(root, path.dirname(p)).split(path.sep).join('/');
        (out[ev] = out[ev] || []).push(path.relative(root, p).split(path.sep).join('/'));
      }
    }
  })(root);
  return out;
}
const sel = listDir(SEL), cut = listDir(CUT);

// wav → data URI（只做 Selected；Cut 的備援維持檔案參照）
const dataUri = new Map();
for (const ev of Object.keys(sel)) {
  for (const rel of sel[ev]) {
    const abs = path.join(SEL, rel);
    dataUri.set('Selected/' + enc(rel), 'data:audio/wav;base64,' + fs.readFileSync(abs).toString('base64'));
  }
}


// 每個事件的最終增益 = SO volume（池內響度已在 loudnorm-pool 對齊過）
const EVENTS = Object.keys(sel).sort();
const data = {
  pools: Object.fromEntries(EVENTS.map(ev => [ev, {
    sel: sel[ev].map(r => dataUri.get('Selected/' + enc(r))),
    selNames: sel[ev].map(r => path.basename(r)),
    reserve: (cut[ev] || []).filter(r => !sel[ev].some(s => path.basename(s) === path.basename(r))).map(r => 'Cut/' + enc(r)),
    vol: VOL[ev] ?? 0.8,
    target: target[ev] ?? null,
  }])),
  scenes: SCENES.map(([title, s]) => ({ title, why: s.why, watch: s.watch, parts: s.parts })),
};

const VERDICTS = ['Keep', 'Needs tuning', 'Replace', 'Needs extra layer'];

let scenesHtml = '';
data.scenes.forEach((s, i) => {
  scenesHtml += `<article class="scene"><h3>${esc(s.title)}</h3>
<p class="why">${s.why}</p>
<p class="watch"><b>聽什麼：</b>${s.watch}</p>
<div class="timeline">${s.parts.map(p => `<span class="ev" data-ev="${esc(p[0])}"><i>${p[1]}s</i> ${esc(p[2])}</span>`).join('<span class="arrow">+</span>')}</div>
<div class="row"><button class="play" data-scene="${i}">▶ 播放情境</button>
<button class="reroll" data-scene="${i}">🎲 換一組 clip</button>
<span class="picked" id="picked${i}"></span></div></article>`;
});

let poolsHtml = '';
for (const ev of EVENTS) {
  const p = data.pools[ev];
  poolsHtml += `<article class="pool"><h3>${esc(ev)}
<span class="meta">${p.sel.length} 選用${p.reserve.length ? ` · ${p.reserve.length} 備援` : ''} · 池目標 ${p.target} dB · vol ${p.vol}</span></h3>
<div class="verdict" data-ev="${esc(ev)}">${VERDICTS.map(v => `<label><input type="radio" name="v_${esc(ev)}" value="${v}"> ${v}</label>`).join('')}
<input class="note" data-ev="${esc(ev)}" placeholder="備註（可留空）"></div>
<div class="clips">`;
  p.sel.forEach((f, i) => { poolsHtml += `<div class="clip"><audio controls preload="none" data-pool="${esc(ev)}" data-i="${i}"></audio><span>${esc(p.selNames[i])}</span></div>`; });
  poolsHtml += '</div>';
  if (p.reserve.length) {
    poolsHtml += `<details><summary>備援 ${p.reserve.length} 個（在 Cut/，未進 Unity）</summary><div class="clips">`;
    for (const f of p.reserve) poolsHtml += `<div class="clip"><audio controls preload="none" src="${f}"></audio><span>${esc(path.basename(decodeURIComponent(f)))}</span></div>`;
    poolsHtml += '</div></details>';
  }
  poolsHtml += '</article>';
}

const html = `<!DOCTYPE html><html lang="zh-Hant"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Combat SFX Verdict</title><style>
:root{--bg:#faf9f7;--fg:#1d1c1a;--mut:#6b6864;--line:#e3e0db;--card:#fff;--acc:#b8562f;--ok:#3f7d4e}
@media(prefers-color-scheme:dark){:root:not([data-theme=light]){--bg:#17161a;--fg:#eceae6;--mut:#9a9691;--line:#302d33;--card:#1f1d22;--acc:#e08a5e;--ok:#6fbf84}}
:root[data-theme=dark]{--bg:#17161a;--fg:#eceae6;--mut:#9a9691;--line:#302d33;--card:#1f1d22;--acc:#e08a5e;--ok:#6fbf84}
*{box-sizing:border-box}
body{margin:0;padding:28px 16px 90px;background:var(--bg);color:var(--fg);
font:15px/1.65 ui-sans-serif,system-ui,"Noto Sans TC",sans-serif}
.wrap{max-width:900px;margin:0 auto}
h1{font-size:26px;margin:0 0 4px}h2{font-size:19px;margin:34px 0 12px;padding-bottom:6px;border-bottom:1px solid var(--line)}
h3{font-size:16px;margin:0 0 8px}
.sub{color:var(--mut);margin:0 0 20px}
.warn{background:var(--card);border:1px solid var(--line);border-left:3px solid var(--acc);padding:14px 16px;border-radius:8px;margin:0 0 20px}
.warn b{color:var(--acc)}
.scene,.pool{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:15px 17px;margin:0 0 14px}
.why,.watch{font-size:13px;color:var(--mut);margin:0 0 8px}
.watch b{color:var(--fg)}
.timeline{display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin:10px 0}
.ev{background:var(--bg);border:1px solid var(--line);border-radius:6px;padding:3px 9px;font-size:12px}
.ev i{color:var(--acc);font-style:normal;font-variant-numeric:tabular-nums}
.arrow{color:var(--mut);font-size:12px}
.row{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:10px}
button{font:inherit;font-size:13px;padding:7px 14px;border-radius:7px;border:1px solid var(--line);
background:var(--bg);color:var(--fg);cursor:pointer}
button:hover{border-color:var(--acc)}
button.play{background:var(--acc);color:#fff;border-color:var(--acc);font-weight:600}
.picked{font-size:11px;color:var(--mut);font-family:ui-monospace,monospace}
.meta{font-size:11px;font-weight:400;color:var(--mut)}
.verdict{display:flex;gap:12px;flex-wrap:wrap;align-items:center;margin:8px 0 12px;padding:9px 11px;
background:var(--bg);border:1px solid var(--line);border-radius:7px}
.verdict label{font-size:13px;cursor:pointer;display:flex;align-items:center;gap:5px}
.note{flex:1;min-width:150px;font:inherit;font-size:12px;padding:5px 8px;border-radius:5px;
border:1px solid var(--line);background:var(--card);color:var(--fg)}
.clips{display:grid;grid-template-columns:repeat(auto-fill,minmax(270px,1fr));gap:7px}
.clip{display:flex;flex-direction:column;gap:2px}
.clip span{font-size:10px;color:var(--mut);font-family:ui-monospace,monospace;word-break:break-all}
audio{height:32px;width:100%}
details{margin-top:10px}summary{cursor:pointer;color:var(--mut);font-size:13px}
#out{position:fixed;left:0;right:0;bottom:0;background:var(--card);border-top:1px solid var(--line);
padding:10px 16px;display:flex;gap:10px;align-items:center;justify-content:center}
#out button{background:var(--ok);color:#fff;border-color:var(--ok);font-weight:600}
textarea{width:100%;height:190px;font:12px ui-monospace,monospace;margin-top:10px;
border:1px solid var(--line);border-radius:7px;padding:10px;background:var(--bg);color:var(--fg)}
</style></head><body><div class="wrap">
<h1>戰鬥音效 — 情境試聽與驗收</h1>
<p class="sub">56 個 clip，池內響度已對齊（Melee/Hit 由 15.0 dB 落差收到 2.5 dB）。</p>

<div class="warn"><b>⚠️ 我聽不到聲音，所以這頁沒有任何我的判斷。</b>
Keep / Needs tuning / Replace / Needs extra layer 全部空著，等你填。
下方「情境試聽」用 <b>Web Audio 以遊戲裡真實的時間差與相對音量</b>排程多顆 clip
——<b>不要只聽單顆 wav</b>，那正是會誤判的地方。</div>

<h2>A. 情境試聽（先聽這個）</h2>
${scenesHtml}

<h2>B. 逐池試聽 ＋ 填驗收結論</h2>
${poolsHtml}

<textarea id="dump" placeholder="按下方「產生結論」後，把這裡的內容貼回對話"></textarea>
</div>
<div id="out"><button id="gen">產生結論</button><span class="picked">填完上面的單選後按這裡</span></div>
<script>
const DATA = ${JSON.stringify(data)};
let ctx = null;
const cache = new Map();

async function load(url){
  if(cache.has(url)) return cache.get(url);
  const r = await fetch(url);
  const b = await r.arrayBuffer();
  const buf = await ctx.decodeAudioData(b);
  cache.set(url, buf);
  return buf;
}
const pick = ev => { const p = DATA.pools[ev]; const i = Math.floor(Math.random()*p.sel.length); return {url:p.sel[i], name:p.selNames[i]}; };
const chosen = {};

async function playScene(i){
  ctx = ctx || new (window.AudioContext||window.webkitAudioContext)();
  await ctx.resume();
  const s = DATA.scenes[i];
  if(!chosen[i]) chosen[i] = s.parts.map(p => pick(p[0]));
  const t0 = ctx.currentTime + 0.12;
  const names = [];
  for(let k=0;k<s.parts.length;k++){
    const [ev, at] = s.parts[k];
    const {url, name} = chosen[i][k];
    names.push(name);
    try{
      const buf = await load(url);
      const src = ctx.createBufferSource(); src.buffer = buf;
      const g = ctx.createGain(); g.gain.value = DATA.pools[ev].vol;
      src.connect(g).connect(ctx.destination);
      src.start(t0 + at);
    }catch(e){ console.warn('load failed', url, e); }
  }
  document.getElementById('picked'+i).textContent = names.join('  +  ');
}

// <audio> 的 src 從 DATA 補上（避免同一份 base64 在檔案裡出現兩次）
document.querySelectorAll('audio[data-pool]').forEach(a => {
  a.src = DATA.pools[a.dataset.pool].sel[+a.dataset.i];
});

document.querySelectorAll('button.play').forEach(b =>
  b.addEventListener('click', () => playScene(+b.dataset.scene)));
document.querySelectorAll('button.reroll').forEach(b =>
  b.addEventListener('click', () => { const i=+b.dataset.scene; delete chosen[i]; playScene(i); }));

document.getElementById('gen').addEventListener('click', () => {
  const lines = ['## 戰鬥音效驗收結論', ''];
  for(const ev of Object.keys(DATA.pools)){
    const sel = document.querySelector('input[name="v_'+ev.replace(/"/g,'')+'"]:checked');
    const note = document.querySelector('.note[data-ev="'+ev+'"]');
    lines.push('- **' + ev + '**: ' + (sel ? sel.value : '(未填)') +
      (note && note.value.trim() ? ' — ' + note.value.trim() : ''));
  }
  document.getElementById('dump').value = lines.join('\\n');
  document.getElementById('dump').scrollIntoView({behavior:'smooth'});
  document.getElementById('dump').select();
});
</script></body></html>`;

fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/audition.html', html);
console.log('wrote audition.html — scenes=' + data.scenes.length + ' pools=' + EVENTS.length);
