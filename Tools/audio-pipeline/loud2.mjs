// 🐛 修正：integrated LUFS 的 gating block 是 400 ms ⇒ 對 <0.4s 的 clip 一律回 -70，
//    那是**量測代理答非所問**，不是 clip 壞掉。改用兩個與長度無關的指標：
//      ① 全檔 RMS（ffmpeg astats Overall RMS_level）
//      ② momentary LUFS 的最大值（M max，窗口 400ms 但取最大而非 gated integrated）
//    打擊音效的「聽起來多大聲」其實更接近 ①/② 而不是 integrated。
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
const ROOT = 'D:/Unity Project/CharacterController/Assets/Audio/Combat';
const cap = a => { const r = spawnSync('ffmpeg', a, { encoding: 'utf8', maxBuffer: 1 << 26 }); return (r.stdout||'')+(r.stderr||''); };

const files=[];(function w(d){for(const e of fs.readdirSync(d,{withFileTypes:true})){const p=path.join(d,e.name);
 if(e.isDirectory())w(p);else if(e.name.endsWith('.wav'))files.push(p);}})(ROOT);

const rows=[];
for(const f of files){
  const o = cap(['-v','error','-i',f,'-af','astats=metadata=1:reset=0,ametadata=print:file=-','-f','null','-']);
  const rms=[...o.matchAll(/Overall[\s\S]*?RMS_level=(-?[\d.]+)/g)].map(m=>parseFloat(m[1]));
  const allRms=[...o.matchAll(/RMS_level=(-?[\d.]+)/g)].map(m=>parseFloat(m[1])).filter(Number.isFinite);
  const pk=[...o.matchAll(/Peak_level=(-?[\d.]+)/g)].map(m=>parseFloat(m[1])).filter(Number.isFinite);
  const e=cap(['-v','info','-i',f,'-af','ebur128=peak=true','-f','null','-']);
  const mVals=[...e.matchAll(/M:\s*(-?[\d.]+)/g)].map(m=>parseFloat(m[1])).filter(v=>v>-70);
  rows.push({
    ev:path.relative(ROOT,path.dirname(f)).split(path.sep).join('/'),
    name:path.basename(f),
    rms: rms.length?rms[rms.length-1]:(allRms.length?Math.max(...allRms):null),
    peak: pk.length?Math.max(...pk):null,
    mMax: mVals.length?Math.max(...mVals):null,
  });
}
fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/_meta/adopted-loudness.json',JSON.stringify(rows,null,1));

const by={};for(const r of rows)(by[r.ev]=by[r.ev]||[]).push(r);
console.log('event                   n   RMS range(dB)   spread   M-max range     spread');
for(const k of Object.keys(by).sort()){
  const g=by[k];
  const rs=g.map(x=>x.rms).filter(Number.isFinite);
  const ms=g.map(x=>x.mMax).filter(Number.isFinite);
  const rsp=rs.length?Math.max(...rs)-Math.min(...rs):0;
  const msp=ms.length?Math.max(...ms)-Math.min(...ms):0;
  console.log(k.padEnd(24)+String(g.length).padStart(2)+
   '  '+(rs.length?Math.min(...rs).toFixed(1)+'..'+Math.max(...rs).toFixed(1):'-').padStart(14)+
   '  '+rsp.toFixed(1).padStart(5)+(rsp>6?' ⚠️':'   ')+
   '  '+(ms.length?Math.min(...ms).toFixed(1)+'..'+Math.max(...ms).toFixed(1):'-').padStart(14)+
   '  '+msp.toFixed(1).padStart(5)+(msp>6?' ⚠️':''));
}
