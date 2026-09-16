import fs from 'node:fs';
import path from 'node:path';
const META = 'D:/Unity Project/_AudioResearch/Combat/_meta';
const SEL = 'D:/Unity Project/_AudioResearch/Combat/Selected';
const cut = JSON.parse(fs.readFileSync(`${META}/cut-manifest.json`, 'utf8'));
const sel = JSON.parse(fs.readFileSync(`${META}/selection.json`, 'utf8'));

const bySrcOut = new Map(cut.map(c => [c.out, c]));
const lines = [
  '# Selected clips — 來源與授權',
  '',
  '全部素材來自 **Sonniss #GameAudioGDC Bundle GDC 2026**（官方下載，無 mirror）。',
  '授權：License v2.0（Effective 27 Aug 2026）—— royalty-free、可商用、不需 attribution。',
  '⛔ 不得再散布為「音效」、不得原樣販售、**不得用於訓練 AI**。完整條款見 `../_meta/LICENSE-sonniss-gdc2026.md`。',
  '',
  '處理：依靜音切出單一 take → 砍前置靜音 → 48 kHz / mono / 16-bit PCM → peak 正規化至 -1 dBFS。',
  '⛔ 未使用 compressor／loudnorm（會壓掉 transient，而打擊感就是 transient）。',
  '',
];
for (const s of sel) {
  lines.push(`## ${s.event}  （選 ${s.picked} / 可用 ${s.available}）`, '', `> ${s.why}`, '');
  lines.push('| clip | 來源母帶 | library |', '|---|---|---|');
  for (const f of s.files) {
    const c = bySrcOut.get(`${s.event}/${f}`);
    const src = c ? c.src : '(?)';
    const lib = src.split('/')[1] || '';
    lines.push(`| \`${f}\` | \`${path.basename(src)}\` | ${lib} |`);
  }
  lines.push('');
}
fs.writeFileSync(`${SEL}/SOURCES.md`, lines.join('\n'));
console.log('wrote SOURCES.md');
