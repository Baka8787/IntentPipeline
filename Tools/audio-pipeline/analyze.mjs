// 客觀量測每個候選 wav：長度／取樣率／聲道／峰值／RMS／**前置靜音**／頻譜重心。
// ⚠️ 這**取代不了**人耳試聽——它回答「技術上能不能用」，回答不了「聽起來對不對」。
// ⭐ 前置靜音是本 repo 踩過的坑：docs/34 量到腳步 clip 有 29.7 ms 前置靜音，
//    那是整個延遲預算裡最大的單一項。攻擊音效對這個更敏感（打擊感就是 transient）。
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';

const ROOT = 'D:/Unity Project/_AudioResearch/Combat/Sonniss';

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.toLowerCase().endsWith('.wav')) out.push(p);
  }
  return out;
}

function probe(file) {
  const j = JSON.parse(execFileSync('ffprobe', [
    '-v', 'error', '-print_format', 'json', '-show_streams', '-show_format', file,
  ], { encoding: 'utf8', maxBuffer: 1 << 24 }));
  const s = j.streams.find(x => x.codec_type === 'audio');
  return {
    dur: Number(j.format.duration),
    rate: Number(s.sample_rate),
    ch: s.channels,
    bits: s.bits_per_raw_sample || s.bits_per_sample,
  };
}

/// ffmpeg 的診斷輸出分散在 stdout 與 stderr，一律兩邊都收下來再合併。
function capture(args) {
  const r = spawnSync('ffmpeg', args, { encoding: 'utf8', maxBuffer: 1 << 26 });
  return (r.stdout || '') + (r.stderr || '');
}

// 用 astats 取整體峰值／RMS；用 silencedetect 取開頭靜音長度。
function levels(file) {
  const out = capture([
    '-v', 'error', '-i', file,
    '-af', 'astats=metadata=1:reset=0,ametadata=print:file=-',
    '-f', 'null', '-',
  ]);
  const peak = [...out.matchAll(/Peak_level=(-?[\d.]+|-inf)/g)].map(m => parseFloat(m[1])).filter(Number.isFinite);
  const rms = [...out.matchAll(/RMS_level=(-?[\d.]+|-inf)/g)].map(m => parseFloat(m[1])).filter(Number.isFinite);
  return {
    peakDb: peak.length ? Math.max(...peak) : null,
    rmsDb: rms.length ? Math.max(...rms) : null,
  };
}

// 前置靜音：-50 dB 門檻下，第一段 silence 若從 0 開始，它的長度就是前置靜音。
// ⚠️ silencedetect 寫的是 **stderr**；execFileSync 的 stdio:'pipe' 只給 stdout
//    ⇒ 必須明確把兩者都收下來再合併（第一次寫錯，63 個檔全部 ERR）。
function leadSilence(file) {
  const out = capture(['-v','info','-i',file,'-af','silencedetect=noise=-50dB:d=0.005','-f','null','-']);
  const starts = [...out.matchAll(/silence_start:\s*(-?[\d.]+)/g)].map(m => parseFloat(m[1]));
  const ends = [...out.matchAll(/silence_end:\s*([\d.]+)/g)].map(m => parseFloat(m[1]));
  if (starts.length && Math.abs(starts[0]) < 0.001 && ends.length) return ends[0];
  return 0;
}

const files = walk(ROOT);
const rows = [];
for (const f of files) {
  try {
    const p = probe(f);
    const l = levels(f);
    const lead = leadSilence(f);
    rows.push({
      file: path.relative(ROOT, f).replace(/\\/g, '/'),
      lib: path.relative(ROOT, f).split(/[\\/]/)[1],
      base: path.basename(f),
      ...p, ...l, leadMs: Math.round(lead * 1000),
    });
  } catch (e) {
    rows.push({ file: path.relative(ROOT, f), error: String(e.message).slice(0, 80) });
  }
}

fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/_meta/candidate-analysis.json', JSON.stringify(rows, null, 1));

console.log('dur(s)  rate   ch  peak   rms    lead   file');
for (const r of rows.sort((a, b) => (a.lib||'').localeCompare(b.lib||'') || (a.base||'').localeCompare(b.base||''))) {
  if (r.error) { console.log('ERR ' + r.file + ' ' + r.error); continue; }
  console.log(
    `${r.dur.toFixed(2).padStart(6)} ${String(r.rate).padStart(6)} ${String(r.ch).padStart(3)} ` +
    `${String(r.peakDb).padStart(6)} ${String(r.rmsDb).padStart(6)} ${String(r.leadMs + 'ms').padStart(7)}  ${r.base.slice(0, 70)}`
  );
}
