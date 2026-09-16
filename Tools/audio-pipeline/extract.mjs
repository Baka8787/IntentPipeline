// 從 Sonniss 的 zip **只抽出指定的 library**，用 HTTP Range 逐檔抓 + 本地 inflate。
// ⭐ 為什麼這樣做：使用者要求「不要因為素材存在就全部 import」「優先只下載有用的分卷」。
//    Range 讓我們做得比「分卷」更細——只抓真正要聽的那 ~45 個檔（約 316 MB），
//    而不是 6.45 GB 的整包。
// ⚠️ 連網一律交給 curl：node 內建 fetch 會被 CDN 擋成 403（已實測）。
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { execFileSync } from 'node:child_process';

const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36';
const REF = 'https://gdc.sonniss.com/';
const OUT = 'D:/Unity Project/_AudioResearch/Combat/Sonniss';
const META = 'D:/Unity Project/_AudioResearch/Combat/_meta';

// part → 想要的頂層資料夾（library）前綴
const WANT = {
  1: [
    '344 Audio - Anime Fight Voices Vol. 1',
    '344 Audio - Anime Fight Voices Vol. 2',
    '344 Audio - Cinematic Fight Vol. 1',
    '344 Audio - Historical Weapons Vol. 2',
    '344 Audio - Elemental Palette Designed Vol. 1',
  ],
  2: [
    'Alexander Kopeikin - 100 kHz Designed Ice',
    'Alexander Kopeikin - Emotion and Magic',
    'Cinematic Sound Design - Colossal Impacts',
    'Cinematic Sound Design - Ultra Transitions & Impacts',
    'Cinematic Sound Design - Cartoon Impacts',
    'David Dumais Audio - Melee Weapons Sound Effects Pack 2',
    'Epic Stock Media - Anime Game',
    'Epic Stock Media - Elemental Mutation Whooshes and Impacts',
    'Epic Stock Media - Fantasy Game 2 - Sound Kit for Enchanted Realms',
    'Epic Stock Media - HD Game Materials',
    'Epic Stock Media - Humanoid Creatures Vol 4 - Monstrous and Undead Creature Vocalization Sound Sets',
  ],
  5: [
    'SoundBits - Vox Hominis - Human Effort Voices',
    'SoundBits - Vox Bestiae - Source Elements',
  ],
};

function fetchRange(url, start, end, outFile) {
  execFileSync('curl', [
    '-sf', '--retry', '3', '-A', UA, '-e', REF,
    '-H', `Range: bytes=${start}-${end}`, url, '-o', outFile,
  ], { stdio: ['ignore', 'ignore', 'inherit'] });
  return fs.readFileSync(outFile);
}

let grandTotal = 0;
const manifest = [];

for (const part of Object.keys(WANT)) {
  const url = `https://downloads.sonniss.com/Sonniss.com-GDC2026-GameAudioBundle${part}of5.zip`;
  const list = JSON.parse(fs.readFileSync(`${META}/part${part}-filelist.json`, 'utf8'));
  const wanted = list.filter(
    f => !f.name.endsWith('/') && WANT[part].some(w => f.name.startsWith(w + '/'))
  );

  console.log(`\n=== part${part}: ${wanted.length} 檔，壓縮後 ${(wanted.reduce((a, f) => a + f.compSize, 0) / 2 ** 20).toFixed(0)} MB ===`);

  for (const f of wanted) {
    const dest = path.join(OUT, `part${part}`, f.name);
    if (fs.existsSync(dest) && fs.statSync(dest).size === f.uncompSize) {
      console.log(`  skip  ${path.basename(f.name)}`);
      continue;
    }
    fs.mkdirSync(path.dirname(dest), { recursive: true });

    // local header：30 bytes 固定 + nameLen + extraLen（extraLen 可能與 central 不同，必須讀本地的）
    const headEnd = f.localOffset + 30 + 65535;
    const head = fetchRange(url, f.localOffset, Math.min(headEnd, f.localOffset + 30 + 4096), '_h.bin');
    if (head.readUInt32LE(0) !== 0x04034b50) { console.log(`  ⛔ bad local header: ${f.name}`); continue; }
    const nameLen = head.readUInt16LE(26);
    const extraLen = head.readUInt16LE(28);
    const dataStart = f.localOffset + 30 + nameLen + extraLen;

    const comp = fetchRange(url, dataStart, dataStart + f.compSize - 1, '_d.bin');
    const raw = f.method === 8 ? zlib.inflateRawSync(comp) : comp;
    if (raw.length !== f.uncompSize) console.log(`  ⚠️ size mismatch ${f.name}: ${raw.length} vs ${f.uncompSize}`);
    fs.writeFileSync(dest, raw);

    grandTotal += f.compSize;
    manifest.push({ part: Number(part), name: f.name, bytes: raw.length });
    console.log(`  ✅ ${(raw.length / 2 ** 20).toFixed(1).padStart(6)} MB  ${path.basename(f.name)}`);
  }
}

fs.writeFileSync(`${META}/sonniss-extracted.json`, JSON.stringify(manifest, null, 1));
console.log(`\n下載量（壓縮）：${(grandTotal / 2 ** 20).toFixed(0)} MB，共 ${manifest.length} 檔`);
