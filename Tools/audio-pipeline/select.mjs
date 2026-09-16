// 從 Cut/ 挑進 Selected/。
//
// ⚠️⚠️ **重要聲明：以下選擇不是「試聽後」的結果——我沒有耳朵。**
// 依據只有三種，全部可查核：
//   ① 客觀量測（長度／前置靜音／峰值／取樣率）
//   ② 檔名與 library 的語意（UCS 前綴：SWSH=swoosh, FGHTImpt=fight impact, CREA=creature…）
//   ③ **與本專案已知事實的匹配**（角色持劍 ⇒ 木棍撞擊不match；冰刺是一列地面爆發 ⇒ 要裂開不要水花）
// 真正的「符合 demo 視覺與動畫重量」只能由人在 Play 裡判斷。見 audition.html。
import fs from 'node:fs';
import path from 'node:path';

const CUT = 'D:/Unity Project/_AudioResearch/Combat/Cut';
const SEL = 'D:/Unity Project/_AudioResearch/Combat/Selected';

// event → { take: [檔名片段…], max, why }
// 「take」是有序偏好；不足 max 時就全收。
const CHOICE = {
  'Melee/Swing': {
    prefer: ['SWSH_SWING_IMPACTS', 'METLFric_SWING_SCRAPE'],
    max: 9,
    why: '21 個同源 take ⇒ 池內一致性最好。取 8 個 ＋ 1 個長刃刮擦做變化。',
  },
  'Melee/Hit': {
    // ⛔ 刻意排除 WEAPBlnt（矛／木棍）：角色持劍（slash1 ＋ WeaponSocket），木棍撞擊語意不符。
    prefer: ['FGHTImpt_4_x_Punch', 'WEAPSwrd_Sword_Slide_Cuts', 'METLImpt_METAL_SWING_HIT', 'FGHTImpt_Combat_Punch'],
    max: 10,
    why: '拳擊身體 ＋ 金屬刀刃命中。⛔ 排除 16 個木棍撞擊——角色持劍，語意不符（留在 Cut/ 當備援）。',
  },
  'Fireball/Cast': { prefer: [], max: 10, why: '全收（只有 2 個）。' },
  'Fireball/Release': { prefer: [], max: 10, why: '全收（只有 3 個）。' },
  'Fireball/Impact': { prefer: [], max: 10, why: '全收（只有 4 個）。' },
  'Ice/Cast': { prefer: [], max: 10, why: '全收（只有 2 個）。' },
  'Ice/Spawn': { prefer: [], max: 10, why: '全收（只有 3 個）。' },
  'Ice/Impact': { prefer: [], max: 10, why: '全收（只有 5 個）。' },
  'Character/Hurt': { prefer: [], max: 10, why: '全收（6 個：男 2／女 2／非人 2）。玩家與敵人在 Unity 端分派。' },
  'Character/Death': { prefer: [], max: 10, why: '全收（2 個）。⚠️ 這是最薄的一項。' },
  'Character/Exertion': { prefer: [], max: 10, why: '全收（2 個）。' },
  'Jump/Takeoff': { prefer: [], max: 8, why: '7 個同源喘息 ＋ 1 個吐氣。' },
};

fs.rmSync(SEL, { recursive: true, force: true });
const report = [];

for (const [event, cfg] of Object.entries(CHOICE)) {
  const srcDir = path.join(CUT, event);
  if (!fs.existsSync(srcDir)) { console.log(`⛔ 沒有 ${event}`); continue; }
  const all = fs.readdirSync(srcDir).filter(f => f.endsWith('.wav'));

  let ordered;
  if (cfg.prefer.length === 0) {
    ordered = all.slice();
  } else {
    ordered = [];
    for (const p of cfg.prefer) {
      for (const f of all) if (f.startsWith(p) && !ordered.includes(f)) ordered.push(f);
    }
  }
  const picked = ordered.slice(0, cfg.max);

  const destDir = path.join(SEL, event);
  fs.mkdirSync(destDir, { recursive: true });
  for (const f of picked) fs.copyFileSync(path.join(srcDir, f), path.join(destDir, f));

  report.push({ event, picked: picked.length, available: all.length, why: cfg.why, files: picked });
  console.log(`${String(picked.length).padStart(2)}/${String(all.length).padEnd(3)} ${event.padEnd(20)} ${cfg.why}`);
}

fs.writeFileSync('D:/Unity Project/_AudioResearch/Combat/_meta/selection.json', JSON.stringify(report, null, 1));
console.log('\n總計 ' + report.reduce((a, r) => a + r.picked, 0) + ' 個 clip 進 Selected/');
