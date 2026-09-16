// 解析由 curl 抓下來的 zip 片段（tail ＋ central directory）。
// 只做解析，不連網——連網交給 curl（node 的 fetch 會被 CDN 擋成 403）。
import fs from 'node:fs';

const [, , mode, ...rest] = process.argv;

function findEOCD(buf) {
  for (let i = buf.length - 4; i >= 0; i--) {
    if (buf[i] === 0x50 && buf[i + 1] === 0x4b && buf[i + 2] === 0x06 && buf[i + 3] === 0x06) {
      return {
        kind: 'zip64',
        entries: Number(buf.readBigUInt64LE(i + 32)),
        cdSize: Number(buf.readBigUInt64LE(i + 40)),
        cdOffset: Number(buf.readBigUInt64LE(i + 48)),
      };
    }
  }
  for (let i = buf.length - 22; i >= 0; i--) {
    if (buf[i] === 0x50 && buf[i + 1] === 0x4b && buf[i + 2] === 0x05 && buf[i + 3] === 0x06) {
      return {
        kind: 'classic',
        entries: buf.readUInt16LE(i + 10),
        cdSize: buf.readUInt32LE(i + 12),
        cdOffset: buf.readUInt32LE(i + 16),
      };
    }
  }
  throw new Error('EOCD not found');
}

if (mode === 'eocd') {
  const buf = fs.readFileSync(rest[0]);
  const tailStart = Number(rest[1]);
  const e = findEOCD(buf);
  // 印成 shell 可以 eval 的形式
  console.log(`CD_OFFSET=${e.cdOffset}`);
  console.log(`CD_SIZE=${e.cdSize}`);
  console.log(`ENTRIES=${e.entries}`);
  console.log(`KIND=${e.kind}`);
  console.log(`TAILSTART=${tailStart}`);
} else if (mode === 'cd') {
  const cd = fs.readFileSync(rest[0]);
  const part = rest[1];
  const out = [];
  let p = 0;
  while (p + 46 <= cd.length) {
    if (cd.readUInt32LE(p) !== 0x02014b50) break;
    const method = cd.readUInt16LE(p + 10);
    let compSize = cd.readUInt32LE(p + 20);
    let uncompSize = cd.readUInt32LE(p + 24);
    const nameLen = cd.readUInt16LE(p + 28);
    const extraLen = cd.readUInt16LE(p + 30);
    const cmtLen = cd.readUInt16LE(p + 32);
    let localOffset = cd.readUInt32LE(p + 42);
    const name = cd.slice(p + 46, p + 46 + nameLen).toString('utf8');

    // zip64 extra field：欄位為 0xFFFFFFFF 時真值在 extra 裡，**順序固定**
    // （uncompressed → compressed → local header offset），只有被標成 0xFFFFFFFF 的才出現。
    if (uncompSize === 0xffffffff || compSize === 0xffffffff || localOffset === 0xffffffff) {
      let q = p + 46 + nameLen;
      const endExtra = q + extraLen;
      while (q + 4 <= endExtra) {
        const id = cd.readUInt16LE(q);
        const sz = cd.readUInt16LE(q + 2);
        if (id === 0x0001) {
          let r = q + 4;
          if (uncompSize === 0xffffffff) { uncompSize = Number(cd.readBigUInt64LE(r)); r += 8; }
          if (compSize === 0xffffffff) { compSize = Number(cd.readBigUInt64LE(r)); r += 8; }
          if (localOffset === 0xffffffff) { localOffset = Number(cd.readBigUInt64LE(r)); r += 8; }
        }
        q += 4 + sz;
      }
    }

    out.push({ name, method, compSize, uncompSize, localOffset });
    p += 46 + nameLen + extraLen + cmtLen;
  }

  fs.writeFileSync(
    `D:/Unity Project/_AudioResearch/Combat/_meta/part${part}-filelist.json`,
    JSON.stringify(out, null, 1)
  );

  const top = new Map();
  for (const f of out) {
    const t = f.name.split('/')[0];
    const e = top.get(t) || { n: 0, bytes: 0 };
    if (!f.name.endsWith('/')) { e.n++; e.bytes += f.uncompSize; }
    top.set(t, e);
  }
  console.log(`=== part${part}  entries=${out.length} ===`);
  for (const [k, v] of [...top].sort()) {
    if (v.n === 0) continue;
    console.log(`${(v.bytes / 2 ** 20).toFixed(0).padStart(7)} MB ${String(v.n).padStart(4)}f  ${k}`);
  }
}
