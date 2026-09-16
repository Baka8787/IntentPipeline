// 預測「在遊戲裡實際會有多大聲」＝ 池目標響度 ＋ 20log10(SO volume)。
// ⚠️ 這回答的是**相對電平**，不是「聽起來對不對」。它的用途是讓試聽時
//    不會把「某個事件被另一個蓋掉」誤判成「素材不好」。
import fs from 'node:fs';
const match = JSON.parse(fs.readFileSync('D:/Unity Project/_AudioResearch/Combat/_meta/pool-loudness-match.json','utf8'));
const t = Object.fromEntries(match.map(m=>[m.event,m.target]));

// SO 上目前的 volume（WireClips.cs 設定的值）
const DEF = [
  ['Melee_Swing',        'Melee/Swing',       0.65, '每次揮擊必響'],
  ['Melee_Hit',          'Melee/Hit',         0.95, '只有打中才響'],
  ['Player_Hurt',        'Character/Hurt',    0.80, '玩家受擊'],
  ['Enemy_Hurt',         'Character/Hurt',    0.80, '敵人受擊'],
  ['Player_Death',       'Character/Death',   0.90, ''],
  ['Enemy_Death',        'Character/Death',   0.90, '＋疊 EXPLDsgn'],
  ['Fireball_Cast',      'Fireball/Cast',     0.70, ''],
  ['Fireball_Release',   'Fireball/Release',  0.85, ''],
  ['Fireball_Impact',    'Fireball/Impact',   0.90, ''],
  ['Ice_Cast',           'Ice/Cast',          0.70, ''],
  ['Ice_Release',        'Ice/Spawn',         0.85, ''],
  ['Ice_Impact',         'Ice/Impact',        0.90, ''],
  ['Player_JumpTakeoff', 'Jump/Takeoff',      0.45, '每次跳都聽到'],
];

const rows = DEF.map(([n,ev,v,note])=>({n,ev,v,note,eff:+(t[ev]+20*Math.log10(v)).toFixed(1)}));
rows.sort((a,b)=>b.eff-a.eff);
console.log('定義                  池目標    ×vol     預測電平   備註');
for(const r of rows) console.log(
  r.n.padEnd(21)+String(t[r.ev]).padStart(6)+'   '+String(r.v).padStart(4)+'   '+String(r.eff).padStart(7)+' dB   '+r.note);

console.log('\n=== 同時發生的組合（#6：會不會互相吃掉／爆掉）===');
const g = Object.fromEntries(rows.map(r=>[r.n,r.eff]));
const combos = [
  ['近戰打中敵人', ['Melee_Swing','Melee_Hit','Enemy_Hurt']],
  ['近戰打死敵人', ['Melee_Swing','Melee_Hit','Enemy_Death']],
  ['冰刺施放',     ['Ice_Release','Ice_Impact']],
  ['冰刺打中',     ['Ice_Release','Ice_Impact','Enemy_Hurt']],
  ['火球命中敵人', ['Fireball_Impact','Enemy_Hurt']],
  ['玩家被打',     ['Player_Hurt']],
];
for(const [label,parts] of combos){
  const lv = parts.map(p=>g[p]);
  // 不相關訊號功率相加
  const sum = 10*Math.log10(lv.reduce((a,v)=>a+Math.pow(10,v/10),0));
  const loudest = Math.max(...lv);
  const quietest = Math.min(...lv);
  console.log(
    label.padEnd(15)+parts.map((p,i)=>p+'('+lv[i]+')').join(' + ').padEnd(62)+
    ' 合計 '+sum.toFixed(1).padStart(6)+' dB   最大差 '+(loudest-quietest).toFixed(1).padStart(4)+' dB'+
    ((loudest-quietest)>8?'  ⚠️ 弱的那個會被蓋掉':''));
}
