// 把候選母帶切成 game-ready 的一次性 clip。
//
// 每個輸出都做四件事：
//  ① 依靜音切出單一 take（母帶常是「一個檔 20 個 take」）
//  ② **砍掉前置靜音**——本 repo 踩過的坑（docs/34：腳步 clip 29.7 ms 前置靜音是延遲預算最大單項）。
//     攻擊音效對這個更敏感：打擊感就是 transient，晚到 0.5 秒等於沒有。
//  ③ 轉成 **48 kHz / mono / 16-bit PCM**——與既有腳步素材同規格。
//     mono 是 3D 音效的必要條件：立體聲檔在 Unity 裡不會被正確空間化。
//  ④ peak 正規化到 -1 dBFS，讓同一個池裡的變化版本音量一致
//     （AudioDefinitionSO 的 volumeJitter 才是負責「聽起來不機械」的那一層，
//      素材本身的音量差異只會變成噪音）。
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const SRC = 'D:/Unity Project/_AudioResearch/Combat/Sonniss';
const OUT = 'D:/Unity Project/_AudioResearch/Combat/Cut';
const META = 'D:/Unity Project/_AudioResearch/Combat/_meta';

const segments = JSON.parse(fs.readFileSync(`${META}/segments.json`, 'utf8'));

// 事件 → [母帶檔名片段, 用途註記, 最多取幾個 take]
// ⚠️ 這是**依檔名與量測的提案**，不是試聽結果。見 audition.html。
const PLAN = [
  ['Melee/Swing', 'SWSH_SWING IMPACTS', '重武器揮擊到悶響命中（21 takes）', 21],
  ['Melee/Swing', 'METLFric_SWING SCRAPE', '長刃揮擊刮擦', 4],
  ['Melee/Hit', 'FGHTImpt_4 x Punch', '拳擊身體命中（4 takes）', 4],
  ['Melee/Hit', 'WEAPBlnt_Spear And Stick Impact', '木質武器命中（16 takes）', 16],
  ['Melee/Hit', 'WEAPSwrd_Sword Slide Cuts', '金屬刀刃命中（6 takes）', 6],
  ['Melee/Hit', 'METLImpt_METAL SWING HIT', '揮擊到金屬身體命中＋殘響尾', 2],
  ['Melee/Hit', 'FGHTImpt_Combat Punch Impact', '輕拳命中', 2],

  ['Fireball/Cast', 'MAGAngl_Magic Light Spell', '魔法詠唱（明亮、tonal）', 2],
  ['Fireball/Cast', 'DSGNStngr_Power Up Bright', '蓄力上升', 2],
  ['Fireball/Release', 'FIREWhsh_Whoosh Fire Deep Growl', '火焰 whoosh（發射）', 2],
  ['Fireball/Release', 'Woosh Debris', 'whoosh ＋ 碎屑層', 2],
  ['Fireball/Release', 'Woosh Sweep Slide Infographics Basic', '短促 whoosh（疊層用）', 2],
  ['Fireball/Impact', 'EXPLDsgn_Explosion Small Blast', '小爆炸（也標記為 Enemy Death）', 2],
  ['Fireball/Impact', 'Impact Hit Rapid Chord Reverb', '衝擊＋殘響', 2],
  ['Fireball/Impact', 'Impact Cut Sweep', '衝擊切掃', 2],
  ['Fireball/Impact', 'Transition Frantic Shaker Snap', '短促尖銳 transient（疊在低頻上）', 2],

  ['Ice/Cast', 'GLASMvmt_Whoosh Glass Crystal', '玻璃／水晶碎片 whoosh', 2],
  ['Ice/Cast', 'ICEFric_Dry Ice High Metal Squeal', '乾冰尖鳴（結晶感）', 2],
  ['Ice/Spawn', 'ice, surface cracking, fissure', '冰面裂開（冰刺冒出）', 2],
  ['Ice/Spawn', 'ice, crack, ice block snapping', '冰塊斷裂', 3],
  ['Ice/Impact', 'ice, block of ice crushed', '冰塊被壓碎（重）', 2],
  ['Ice/Impact', 'ICEFric_Dry Ice Squeak Metal', '乾冰摩擦（短）', 3],
  ['Ice/Impact', 'ELECMisc_Impact Electric Tonal', '電性衝擊（低頻層）', 2],

  ['Character/Hurt', 'VOXMale_Male Adventurer, Active Yell', '男性受擊喊叫（短）', 2],
  ['Character/Hurt', "VOXMale_Warrior Opponent Attack, 'Ai yah'", '男性受擊', 2],
  ['Character/Hurt', 'VOXFem_Anime, Schoolgirl Fight Exhale', '女性吐氣', 2],
  ['Character/Hurt', 'VOXFem_Anime, Warrior Elf Princess Aggressive Yell', '女性喊叫', 2],
  ['Character/Hurt', 'HMNBrth_Construction Kit Male Screeching Breath', '痛苦吸氣（敵人用）', 2],
  ['Character/Hurt', 'CREAEthr_Ethereal Entity Grim Pain', '非人痛苦（敵人用）', 2],
  ['Character/Death', 'VOXReac_Construction Kit Male Flutter Death Vocal', '死亡吐息', 2],
  ['Character/Death', 'CREAMnstr_Designed Sea Beast Creature Pain', '怪物痛苦長嚎（死亡用）', 2],
  ['Character/Exertion', 'CREAHmn_Violent Humanoid Creature Exhale Short', '短促出力', 2],
  ['Character/Exertion', 'CREAHmn_Designed Orc Male Attack Long Heavy', '出力攻擊', 2],

  ['Jump/Takeoff', 'HMNBrth_Panting Male', '喘息／出力（7 takes，起跳用）', 7],
  ['Jump/Takeoff', 'VOXFem_Anime, Adult Warrior Breaths', '吐氣', 2],
];

function slug(s) {
  return s.replace(/[^\w\s-]/g, '').replace(/\s+/g, '_').replace(/_+/g, '_').slice(0, 44);
}

fs.rmSync(OUT, { recursive: true, force: true });

const manifest = [];
for (const [event, needle, note, maxTakes] of PLAN) {
  const hit = segments.find(s => s.base.toLowerCase().includes(needle.toLowerCase()));
  if (!hit) { console.log(`⛔ 找不到母帶：${needle}`); continue; }

  const src = path.join(SRC, hit.file);
  const dir = path.join(OUT, event);
  fs.mkdirSync(dir, { recursive: true });

  // take 太長的排除：一次性音效不該超過 6 秒（drone／ambience 不是我們要的）
  const takes = hit.segs.filter(s => s.end - s.start <= 6).slice(0, maxTakes);
  if (takes.length === 0) { console.log(`⚠️ ${needle}：沒有 ≤6s 的 take（可能是 drone／ambience）`); continue; }

  let n = 0;
  for (const t of takes) {
    n++;
    const outFile = path.join(dir, `${slug(hit.base.replace(/\.wav$/i, ''))}_${String(n).padStart(2, '0')}.wav`);
    // 共用的切段＋砍頭＋降規格鏈。
    // ⛔ 刻意**不用** dynaudnorm／loudnorm／compressor：它們會壓掉 transient，
    //    而打擊感就是 transient。只做「整體乘一個常數」的 peak 正規化。
    const trimChain = 'silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0:detection=peak';
    const cutArgs = ['-ss', String(Math.max(0, t.start - 0.005)), '-to', String(t.end + 0.05), '-i', src];

    // 第一趟：量切完之後的真實峰值
    const probe = spawnSync('ffmpeg', [
      '-v', 'info', '-y', ...cutArgs, '-af', `${trimChain},volumedetect`,
      '-ac', '1', '-ar', '48000', '-f', 'null', '-',
    ], { encoding: 'utf8', maxBuffer: 1 << 26 });
    const pk = ((probe.stdout || '') + (probe.stderr || '')).match(/max_volume:\s*(-?[\d.]+) dB/);
    // 目標 -1 dBFS；量不到就不調整（寧可音量不完美，也不要靜默寫出壞資料）
    const gainDb = pk ? (-1 - parseFloat(pk[1])) : 0;

    const r = spawnSync('ffmpeg', [
      '-v', 'error', '-y', ...cutArgs,
      '-af', `${trimChain},volume=${gainDb.toFixed(2)}dB`,
      '-ac', '1', '-ar', '48000', '-sample_fmt', 's16',
      outFile,
    ], { encoding: 'utf8' });
    if (r.status !== 0) { console.log(`  ⛔ ffmpeg 失敗 ${path.basename(outFile)}: ${(r.stderr || '').slice(0, 120)}`); continue; }
    manifest.push({ event, src: hit.file, out: path.relative(OUT, outFile).replace(/\\/g, '/'), note });
  }
  console.log(`${String(takes.length).padStart(3)} → ${event.padEnd(20)} ${note}`);
}

fs.writeFileSync(`${META}/cut-manifest.json`, JSON.stringify(manifest, null, 1));
console.log(`\n輸出 ${manifest.length} 個 clip 到 ${OUT}`);
