// 🐛 修我自己上一輪的錯：**peak 正規化 ≠ 響度一致**。
//
// 上一輪把每個 clip 峰值拉到 -1 dBFS 就收工。但峰值只描述「最大的那一個取樣點」，
// 不描述「聽起來多大聲」——一個密實的悶響與一個尖銳的 transient 峰值相同，
// 響度可以差十幾 dB。實測結果：
//     Melee/Hit   池內 RMS 差 **15.7 dB**
//     Character/Hurt  差 8.4 dB / M-max 差 9.0 dB
// ⇒ shuffle bag 每抽一次，音量就跳一次。**這會讓「近戰有沒有重量」根本無法被公平試聽**
//   （聽到的是 10 顆裡的哪一顆，比素材本身影響更大）。
//
// 修法：**池內響度對齊**，用「最響的 300 ms 窗口 RMS」當指標——
// 它與長度無關（避免 integrated LUFS 對 <0.4s clip 回 -70 的代理失配），
// 又比全檔 RMS 更貼近一次性音效的感知響度（長尾不會把指標拉低）。
//
// ⛔ 仍然只做「整體乘一個常數」，不用 compressor／limiter：打擊感就是 transient。
// ⚠️ 增益後若真峰值會超過 -1 dBFS，就**以峰值為準夾住**（寧可該顆略輕，不可削波）。
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const SEL = 'D:/Unity Project/_AudioResearch/Combat/Selected';
const WIN = 0.300; // 秒

function decode(file) {
  const r = spawnSync('ffmpeg', ['-v', 'error', '-i', file, '-f', 's16le', '-acodec', 'pcm_s16le', '-ac', '1', '-ar', '48000', '-'],
    { maxBuffer: 1 << 28 });
  const b = r.stdout;
  const n = Math.floor(b.length / 2);
  const a = new Float64Array(n);
  for (let i = 0; i < n; i++) a[i] = b.readInt16LE(i * 2) / 32768;
  return a;
}

/** 最響的 WIN 秒窗口 RMS（dBFS），以及全檔真峰值（dBFS）。 */
function metrics(samples) {
  const sr = 48000;
  const w = Math.min(samples.length, Math.round(WIN * sr));
  let sum = 0;
  for (let i = 0; i < w; i++) sum += samples[i] * samples[i];
  let best = sum;
  for (let i = w; i < samples.length; i++) {
    sum += samples[i] * samples[i] - samples[i - w] * samples[i - w];
    if (sum > best) best = sum;
  }
  const rms = Math.sqrt(best / w);
  let peak = 0;
  for (let i = 0; i < samples.length; i++) { const v = Math.abs(samples[i]); if (v > peak) peak = v; }
  return {
    winRmsDb: rms > 0 ? 20 * Math.log10(rms) : -120,
    peakDb: peak > 0 ? 20 * Math.log10(peak) : -120,
  };
}

const events = [];
(function w(d) {
  const ents = fs.readdirSync(d, { withFileTypes: true });
  if (ents.some(e => e.isFile() && e.name.endsWith('.wav'))) events.push(d);
  for (const e of ents) if (e.isDirectory()) w(path.join(d, e.name));
})(SEL);

const report = [];
for (const dir of events) {
  const files = fs.readdirSync(dir).filter(f => f.endsWith('.wav'));
  const m = files.map(f => ({ f, p: path.join(dir, f), ...metrics(decode(path.join(dir, f))) }));

  // 目標＝池內中位數（不用平均，避免一顆離群值把整池拖走）
  const sorted = m.map(x => x.winRmsDb).slice().sort((a, b) => a - b);
  const target = sorted[Math.floor(sorted.length / 2)];

  const ev = path.relative(SEL, dir).split(path.sep).join('/');
  const before = Math.max(...m.map(x => x.winRmsDb)) - Math.min(...m.map(x => x.winRmsDb));
  const rows = [];

  for (const x of m) {
    let gain = target - x.winRmsDb;
    const headroom = -1.0 - x.peakDb;          // 還能推多少才碰到 -1 dBFS
    const clamped = Math.min(gain, headroom);  // 不許削波
    const applied = clamped;

    if (Math.abs(applied) < 0.25) { rows.push({ f: x.f, gain: 0, note: 'ok' }); continue; }

    const tmp = x.p + '.tmp.wav';
    const r = spawnSync('ffmpeg', ['-v', 'error', '-y', '-i', x.p,
      '-af', `volume=${applied.toFixed(2)}dB`, '-ac', '1', '-ar', '48000', '-sample_fmt', 's16', tmp],
      { encoding: 'utf8' });
    if (r.status !== 0) { rows.push({ f: x.f, gain: 0, note: 'ffmpeg failed' }); continue; }
    fs.renameSync(tmp, x.p);
    rows.push({ f: x.f, gain: +applied.toFixed(2), note: clamped < gain - 0.25 ? 'peak-limited' : '' });
  }

  const after = m.map(x => ({ ...metrics(decode(x.p)) }));
  const spreadAfter = Math.max(...after.map(a => a.winRmsDb)) - Math.min(...after.map(a => a.winRmsDb));
  report.push({ event: ev, target: +target.toFixed(1), before: +before.toFixed(1), after: +spreadAfter.toFixed(1), rows });
  console.log(`${ev.padEnd(22)} target ${target.toFixed(1).padStart(6)} dB   spread ${before.toFixed(1).padStart(5)} → ${spreadAfter.toFixed(1).padStart(5)} dB` +
    (rows.some(r => r.note === 'peak-limited') ? '   (部分被峰值夾住)' : ''));
}
fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/_meta/pool-loudness-match.json', JSON.stringify(report, null, 1));
