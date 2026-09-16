// 量測**實際採用**的 56 個 clip：前置靜音 ＋ loudness（LUFS，不只峰值）。
// 這一項是使用者明確要求的檢查（#5），而且它本來就是量測問題不是聽感問題。
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const ROOT = 'D:/Unity Project/CharacterController/Assets/Audio/Combat';
const cap = a => { const r = spawnSync('ffmpeg', a, { encoding: 'utf8', maxBuffer: 1 << 26 }); return (r.stdout || '') + (r.stderr || ''); };

const files = [];
(function w(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) w(p); else if (e.name.endsWith('.wav')) files.push(p);
  }
})(ROOT);

const rows = [];
for (const f of files) {
  // 前置靜音：-50 dB（聽得見的門檻）與 -60 dB（嚴格）都量，避免單一門檻誤判
  const lead = th => {
    const o = cap(['-v','info','-i',f,'-af',`silencedetect=noise=${th}dB:d=0.003`,'-f','null','-']);
    const s = [...o.matchAll(/silence_start:\s*(-?[\d.]+)/g)].map(m=>parseFloat(m[1]));
    const e = [...o.matchAll(/silence_end:\s*([\d.]+)/g)].map(m=>parseFloat(m[1]));
    return (s.length && Math.abs(s[0]) < 0.002 && e.length) ? e[0] : 0;
  };
  const o = cap(['-v','info','-i',f,'-af','ebur128=peak=true','-f','null','-']);
  const I = (o.match(/I:\s*(-?[\d.]+)\s*LUFS/g) || []).pop();
  const LRA = (o.match(/LRA:\s*(-?[\d.]+)\s*LU/g) || []).pop();
  const TP = (o.match(/Peak:\s*(-?[\d.]+)\s*dBFS/g) || []).pop();
  const dur = parseFloat(cap(['-v','info','-i',f,'-f','null','-']).match(/Duration:\s*(\d+):(\d+):([\d.]+)/)?.slice(1).reduce((a,v,i)=>a+Number(v)*[3600,60,1][i],0) || 0);

  rows.push({
    ev: path.relative(ROOT, path.dirname(f)).split(path.sep).join('/'),
    name: path.basename(f),
    dur: +dur.toFixed(2),
    lead50: Math.round(lead(-50)*1000),
    lead60: Math.round(lead(-60)*1000),
    lufs: I ? parseFloat(I.match(/-?[\d.]+/)[0]) : null,
    lra: LRA ? parseFloat(LRA.match(/-?[\d.]+/)[0]) : null,
    tp: TP ? parseFloat(TP.match(/-?[\d.]+/)[0]) : null,
  });
}
fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/_meta/adopted-measure.json', JSON.stringify(rows,null,1));

const by = {};
for (const r of rows) (by[r.ev] = by[r.ev] || []).push(r);
console.log('event                 n   lead50(ms)      LUFS range        truePeak   worst-lead file');
for (const k of Object.keys(by).sort()) {
  const g = by[k];
  const leads = g.map(x=>x.lead50), lu = g.map(x=>x.lufs).filter(Number.isFinite), tp = g.map(x=>x.tp).filter(Number.isFinite);
  const worst = g.reduce((a,b)=>b.lead50>a.lead50?b:a);
  console.log(
    k.padEnd(22) + String(g.length).padStart(2) +
    '  ' + (Math.min(...leads)+'–'+Math.max(...leads)).padStart(10) +
    '  ' + (Math.min(...lu).toFixed(1)+'..'+Math.max(...lu).toFixed(1)).padStart(15) +
    '  ' + Math.max(...tp).toFixed(2).padStart(7) +
    '   ' + (worst.lead50>20 ? worst.name.slice(0,34) : '-')
  );
}
