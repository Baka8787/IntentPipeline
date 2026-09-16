// 數出每個候選檔裡有幾個**獨立的一次性事件**（take）。
//
// ⭐ 為什麼這件事重要：我原本從 track list 推論「每個 library 只有 2–4 個檔 ⇒ 湊不出 shuffle 池」。
//    量完才發現那個推論**錯在把「檔案數」當成「素材數」**——production library 的慣例是
//    「一個檔裡放 20 個 take」。SWSH_SWING IMPACTS 一個檔就有 61.8 秒。
//    ⇒ 檔案數不是素材數，切開才知道。
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const ROOT = 'D:/Unity Project/_AudioResearch/Combat/Sonniss';

function capture(args) {
  const r = spawnSync('ffmpeg', args, { encoding: 'utf8', maxBuffer: 1 << 26 });
  return (r.stdout || '') + (r.stderr || '');
}

/** 以靜音切段，回傳 [{start,end}]。noise/minSil 可調，因為不同素材的底噪差很多。 */
function segments(file, noiseDb, minSil) {
  const out = capture(['-v', 'info', '-i', file,
    '-af', `silencedetect=noise=${noiseDb}dB:d=${minSil}`, '-f', 'null', '-']);
  const dur = parseFloat((out.match(/Duration:\s*(\d+):(\d+):([\d.]+)/) || [])
    .slice(1).reduce((a, v, i) => a + Number(v) * [3600, 60, 1][i], 0)) || 0;

  const silences = [];
  const re = /silence_start:\s*(-?[\d.]+)[\s\S]*?silence_end:\s*([\d.]+)/g;
  let m;
  while ((m = re.exec(out))) silences.push({ s: parseFloat(m[1]), e: parseFloat(m[2]) });

  const segs = [];
  let cursor = 0;
  for (const s of silences) {
    if (s.s > cursor + 0.02) segs.push({ start: cursor, end: s.s });
    cursor = s.e;
  }
  if (dur - cursor > 0.02) segs.push({ start: cursor, end: dur });
  return segs.filter(s => s.end - s.start >= 0.08); // < 80 ms 視為雜訊不是 take
}

const targets = process.argv.slice(2);
const files = [];
(function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.toLowerCase().endsWith('.wav')) files.push(p);
  }
})(ROOT);

const report = [];
for (const f of files) {
  const base = path.basename(f);
  if (targets.length && !targets.some(t => base.toLowerCase().includes(t.toLowerCase()))) continue;
  const segs = segments(f, -45, 0.25);
  const durs = segs.map(s => +(s.end - s.start).toFixed(2));
  report.push({ file: path.relative(ROOT, f).replace(/\\/g, '/'), base, takes: segs.length, segs });
  console.log(`${String(segs.length).padStart(3)} takes  [${durs.slice(0, 12).join(', ')}${durs.length > 12 ? ', …' : ''}]  ${base.slice(0, 62)}`);
}
fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/_meta/segments.json', JSON.stringify(report, null, 1));
