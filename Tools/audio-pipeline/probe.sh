#!/bin/bash
set -e
UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36"
REF="https://gdc.sonniss.com/"
P="$1"
U="https://downloads.sonniss.com/Sonniss.com-GDC2026-GameAudioBundle${P}of5.zip"

# ⚠️ 2026-09-17 踩過：TOTAL 抓不到時 TAILSTART 變負數 ⇒ Range 標頭失效 ⇒ 伺服器回 200 整包，
#    結果是「本來只想讀 1 MB 目錄，卻開始下載 1.2 GB」。所以這裡硬性驗證。
TOTAL=$(curl -sI --retry 3 -A "$UA" -e "$REF" "$U" | tr -d '\r' | awk 'tolower($1)=="content-length:"{print $2}')
if ! [[ "$TOTAL" =~ ^[0-9]+$ ]] || [ "$TOTAL" -lt 2000000 ]; then
  echo "part$P: 取不到合理的 Content-Length（得到 '$TOTAL'）⇒ 中止，不做 Range 請求" >&2
  exit 1
fi

TAILSTART=$(( TOTAL - 1048576 ))
curl -sf -A "$UA" -e "$REF" -H "Range: bytes=${TAILSTART}-$((TOTAL-1))" "$U" -o "tail${P}.bin"
# 再驗一次：檔案必須就是 1 MB，否則代表 Range 沒被遵守
SZ=$(stat -c%s "tail${P}.bin")
if [ "$SZ" -ne 1048576 ]; then echo "part$P: Range 未被遵守（拿到 ${SZ} bytes）⇒ 中止" >&2; rm -f "tail${P}.bin"; exit 1; fi

eval "$(node zipparse.mjs eocd "tail${P}.bin" "$TAILSTART")"
curl -sf -A "$UA" -e "$REF" -H "Range: bytes=${CD_OFFSET}-$((CD_OFFSET+CD_SIZE-1))" "$U" -o "cd${P}.bin"
node zipparse.mjs cd "cd${P}.bin" "$P"
