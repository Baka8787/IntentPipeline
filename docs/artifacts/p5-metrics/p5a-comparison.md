# P5 probe comparison

before = 056edca,6bcdf6e,a6d91ff · after = 305c5b8a39,p5a-wip

Values: min / median / max (n). OVERLAP = ranges overlap ⇒ not evidence of a change.


## AttackAndHurt

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.932 / 0.932 / 0.932 (n=5) | 0.932 / 0.932 / 0.932 (n=8) | SAME |
| inBandFrames | 1119 / 1119 / 1119 (n=5) | 1119 / 1119 / 1119 (n=8) | SAME |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=8) | SAME |
| firstValidFrame | 81 / 81 / 81 (n=5) | 81 / 81 / 81 (n=8) | SAME |
| meanDistance | 1.506 / 1.506 / 1.56 (n=5) | 1.506 / 1.521 / 1.56 (n=8) |  |
| travelled | 13.698 / 14.894 / 14.894 (n=5) | 13.698 / 14.67 / 14.945 (n=8) |  |
| metersPerValidSecond | 0.734 / 0.799 / 0.799 (n=5) | 0.734 / 0.787 / 0.801 (n=8) | OVERLAP |
| attacks | 5 / 5 / 5 (n=5) | 5 / 5 / 5 (n=8) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=8) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=8) | SAME |
| allowedMoveFrames | 531 / 579 / 579 (n=5) | 531 / 569 / 583 (n=8) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=8) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=8) | SAME |
| modeSwitches | 1 / 3 / 5 (n=5) | 1 / 4 / 5 (n=8) |  |
| strafeFlips | 7 / 7 / 7 (n=5) | 7 / 7 / 7 (n=8) | SAME |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=8) | SAME |
| stuckFrames | 153 / 153 / 192 (n=5) | 0 / 0 / 0 (n=8) |  |
| stuckInActionHurt | 89 / 89 / 131 (n=5) | 0 / 0 / 0 (n=8) | BETTER |
| actionFrames | 197 / 197 / 245 (n=5) | 197 / 207 / 245 (n=8) |  |
| hurtFrames | 417 / 417 / 417 (n=5) | 417 / 417 / 417 (n=8) | SAME |
| strafeFrames | 937 / 994 / 1007 (n=3) | 937 / 971 / 1007 (n=8) |  |
| retreatFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=8) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=8) | SAME |
| stuckAfterNonExecuted | — | 0 / 0 / 0 (n=3) | n/a |

## Corner

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.854 / 0.854 / 0.854 (n=5) | 0.854 / 0.854 / 0.854 (n=5) | SAME |
| inBandFrames | 1025 / 1025 / 1025 (n=5) | 1025 / 1025 / 1025 (n=5) | SAME |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 175 / 175 / 175 (n=5) | 175 / 175 / 175 (n=5) | SAME |
| meanDistance | 1.923 / 1.923 / 1.923 (n=5) | 1.923 / 1.923 / 1.923 (n=5) | SAME |
| travelled | 24.225 / 24.225 / 24.225 (n=5) | 24.225 / 24.225 / 24.225 (n=5) | SAME |
| metersPerValidSecond | 1.418 / 1.418 / 1.418 (n=5) | 1.418 / 1.418 / 1.418 (n=5) | SAME |
| attacks | 5 / 5 / 5 (n=5) | 5 / 5 / 5 (n=5) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 951 / 951 / 951 (n=5) | 951 / 951 / 951 (n=5) | SAME |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 13 / 13 / 13 (n=5) | 13 / 13 / 13 (n=5) | SAME |
| strafeFlips | 14 / 14 / 14 (n=5) | 14 / 14 / 14 (n=5) | SAME |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 54 / 54 / 54 (n=5) | 0 / 0 / 0 (n=5) |  |
| stuckInActionHurt | 28 / 28 / 28 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| actionFrames | 245 / 245 / 245 (n=5) | 245 / 245 / 245 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 783 / 783 / 783 (n=3) | 783 / 783 / 783 (n=5) | SAME |
| retreatFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |

## CornerPinned

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.474 / 0.5 / 0.517 (n=6) | 0.474 / 0.505 / 0.512 (n=8) | OVERLAP |
| inBandFrames | 569 / 599.5 / 621 (n=6) | 569 / 606 / 614 (n=8) | OVERLAP |
| inBandBlockedFrames | 0 / 0 / 0 (n=6) | 0 / 0 / 0 (n=8) | SAME |
| firstValidFrame | 0 / 0 / 0 (n=6) | 0 / 0 / 0 (n=8) | SAME |
| meanDistance | 1.256 / 1.27 / 1.287 (n=6) | 1.229 / 1.27 / 1.287 (n=8) |  |
| travelled | 5.495 / 9.124 / 10.689 (n=6) | 5.495 / 8.554 / 10.631 (n=8) |  |
| metersPerValidSecond | 0.543 / 0.921 / 1.121 (n=6) | 0.543 / 0.847 / 1.121 (n=8) | OVERLAP |
| attacks | 5 / 6 / 6 (n=6) | 5 / 6 / 6 (n=8) |  |
| attacksNotValid | 0 / 0 / 0 (n=6) | 0 / 0 / 0 (n=8) | SAME |
| noProgressFrames | 359 / 374.5 / 399 (n=6) | 359 / 374 / 399 (n=8) | OVERLAP |
| allowedMoveFrames | 547 / 693 / 759 (n=6) | 547 / 665 / 759 (n=8) |  |
| recoverFrames | 0 / 0 / 0 (n=6) | 0 / 0 / 0 (n=8) | SAME |
| strafeBlockedFrames | 157 / 271 / 463 (n=6) | 157 / 298 / 463 (n=8) | OVERLAP |
| modeSwitches | 13 / 25 / 33 (n=6) | 13 / 23 / 33 (n=8) |  |
| strafeFlips | 6 / 7 / 8 (n=6) | 5 / 6 / 8 (n=8) |  |
| pathRequests | 7 / 7 / 7 (n=6) | 7 / 7 / 7 (n=8) | SAME |
| stuckFrames | 474 / 500 / 511 (n=6) | 368 / 368 / 368 (n=8) |  |
| stuckInActionHurt | 106 / 123.5 / 156 (n=6) | 2 / 2 / 2 (n=8) | BETTER |
| actionFrames | 245 / 294 / 294 (n=6) | 245 / 262 / 294 (n=8) |  |
| hurtFrames | 0 / 0 / 0 (n=6) | 0 / 0 / 0 (n=8) | SAME |
| strafeFrames | 210 / 328.5 / 501 (n=6) | 210 / 354 / 501 (n=8) |  |
| retreatFrames | 613 / 663.5 / 748 (n=6) | 613 / 654 / 748 (n=8) |  |
| retreatNoProgressFrames | 359 / 371 / 399 (n=6) | 359 / 370 / 399 (n=8) | OVERLAP |
| stuckAfterNonExecuted | — | 0 / 0 / 0 (n=3) | n/a |

## CornerPinned_v1

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.955 / 0.955 / 0.955 (n=3) | — | n/a |
| inBandFrames | 1146 / 1146 / 1146 (n=3) | — | n/a |
| inBandBlockedFrames | 0 / 0 / 0 (n=3) | — | n/a |
| firstValidFrame | 0 / 0 / 0 (n=3) | — | n/a |
| meanDistance | 1.423 / 1.423 / 1.423 (n=3) | — | n/a |
| travelled | 4.265 / 4.265 / 4.265 (n=3) | — | n/a |
| metersPerValidSecond | 0.223 / 0.223 / 0.223 (n=3) | — | n/a |
| attacks | 6 / 6 / 6 (n=3) | — | n/a |
| attacksNotValid | 0 / 0 / 0 (n=3) | — | n/a |
| noProgressFrames | 0 / 0 / 0 (n=3) | — | n/a |
| allowedMoveFrames | 134 / 134 / 134 (n=3) | — | n/a |
| recoverFrames | 0 / 0 / 0 (n=3) | — | n/a |
| strafeBlockedFrames | 1012 / 1012 / 1012 (n=3) | — | n/a |
| modeSwitches | 11 / 11 / 11 (n=3) | — | n/a |
| strafeFlips | 9 / 9 / 10 (n=3) | — | n/a |
| pathRequests | 3 / 3 / 3 (n=3) | — | n/a |
| stuckFrames | 15 / 15 / 16 (n=3) | — | n/a |
| stuckInActionHurt | 8 / 8 / 9 (n=3) | — | n/a |
| actionFrames | 294 / 294 / 294 (n=3) | — | n/a |
| hurtFrames | 0 / 0 / 0 (n=3) | — | n/a |
| strafeFrames | 1056 / 1056 / 1056 (n=3) | — | n/a |
| retreatFrames | 72 / 72 / 72 (n=3) | — | n/a |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | — | n/a |

## Kiting

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | -1 / -1 / -1 (n=5) | -1 / -1 / -1 (n=5) | SAME |
| meanDistance | 4.698 / 4.698 / 4.699 (n=5) | 4.698 / 4.698 / 4.699 (n=5) |  |
| travelled | 31.337 / 31.34 / 31.359 (n=5) | 31.336 / 31.346 / 31.358 (n=5) |  |
| attacks | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 1181 / 1181 / 1181 (n=5) | 1181 / 1181 / 1185 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFlips | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| pathRequests | 94 / 94 / 94 (n=5) | 94 / 94 / 94 (n=5) | SAME |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
| retreatFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |

## MovingSlow

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.768 / 0.8 / 0.819 (n=5) | 0.8 / 0.821 / 0.842 (n=5) | OVERLAP |
| inBandFrames | 1152 / 1200 / 1229 (n=5) | 1200 / 1231 / 1263 (n=5) | OVERLAP |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 153 / 153 / 153 (n=5) | 153 / 153 / 153 (n=5) | SAME |
| meanDistance | 1.893 / 1.914 / 1.937 (n=5) | 1.874 / 1.889 / 1.914 (n=5) |  |
| travelled | 30.802 / 31.237 / 31.312 (n=5) | 30.658 / 31.119 / 31.237 (n=5) |  |
| metersPerValidSecond | 1.504 / 1.562 / 1.631 (n=5) | 1.456 / 1.517 / 1.562 (n=5) | OVERLAP |
| attacks | 6 / 6 / 7 (n=5) | 6 / 6 / 7 (n=5) |  |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 1181 / 1197 / 1197 (n=5) | 1177 / 1196 / 1197 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 39 / 44 / 44 (n=5) | 39 / 44 / 44 (n=5) |  |
| strafeFlips | 7 / 8 / 9 (n=5) | 8 / 9 / 9 (n=5) |  |
| pathRequests | 79 / 79 / 79 (n=5) | 79 / 79 / 79 (n=5) | SAME |
| stuckFrames | 56 / 64 / 75 (n=5) | 0 / 0 / 0 (n=5) |  |
| stuckInActionHurt | 27 / 30 / 40 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| actionFrames | 294 / 294 / 310 (n=5) | 294 / 294 / 314 (n=5) |  |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 495 / 521 / 568 (n=3) | 521 / 616 / 637 (n=5) |  |
| retreatFrames | 0 / 10 / 10 (n=3) | 10 / 10 / 20 (n=5) |  |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |

## OpenStatic

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.932 / 0.932 / 0.932 (n=5) | 0.932 / 0.932 / 0.932 (n=5) | SAME |
| inBandFrames | 1119 / 1119 / 1119 (n=5) | 1119 / 1119 / 1119 (n=5) | SAME |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 81 / 81 / 81 (n=5) | 81 / 81 / 81 (n=5) | SAME |
| meanDistance | 1.517 / 1.559 / 1.59 (n=5) | 1.537 / 1.56 / 1.578 (n=5) |  |
| travelled | 22.611 / 23.492 / 23.901 (n=5) | 22.611 / 23.826 / 23.901 (n=5) |  |
| metersPerValidSecond | 1.212 / 1.26 / 1.282 (n=5) | 1.212 / 1.278 / 1.282 (n=5) | OVERLAP |
| attacks | 5 / 6 / 6 (n=5) | 5 / 5 / 6 (n=5) |  |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 898 / 934 / 947 (n=5) | 898 / 947 / 951 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 5 / 7 / 9 (n=5) | 5 / 7 / 9 (n=5) |  |
| strafeFlips | 7 / 7 / 8 (n=5) | 7 / 7 / 8 (n=5) |  |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 52 / 64 / 78 (n=5) | 0 / 0 / 0 (n=5) |  |
| stuckInActionHurt | 34 / 36 / 45 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| actionFrames | 245 / 263 / 294 (n=5) | 245 / 245 / 294 (n=5) |  |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 911 / 1012 / 1025 (n=3) | 911 / 961 / 975 (n=5) |  |
| retreatFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |

## Pillar

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.772 / 0.772 / 0.772 (n=5) | 0.772 / 0.772 / 0.772 (n=5) | SAME |
| inBandFrames | 1015 / 1015 / 1015 (n=5) | 1015 / 1015 / 1015 (n=5) | SAME |
| inBandBlockedFrames | 88 / 88 / 88 (n=5) | 88 / 88 / 88 (n=5) | SAME |
| firstValidFrame | 273 / 273 / 273 (n=5) | 273 / 273 / 273 (n=5) | SAME |
| meanDistance | 1.709 / 1.747 / 1.773 (n=5) | 1.709 / 1.747 / 1.747 (n=5) |  |
| travelled | 22.519 / 22.557 / 22.584 (n=5) | 22.519 / 22.557 / 22.557 (n=5) |  |
| metersPerValidSecond | 1.458 / 1.46 / 1.462 (n=5) | 1.458 / 1.46 / 1.46 (n=5) | OVERLAP |
| attacks | 5 / 5 / 5 (n=5) | 5 / 5 / 5 (n=5) | SAME |
| attacksNotValid | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 946 / 946 / 946 (n=5) | 946 / 946 / 946 (n=5) | SAME |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 5 / 7 / 7 (n=5) | 5 / 5 / 7 (n=5) |  |
| strafeFlips | 7 / 7 / 7 (n=5) | 7 / 7 / 7 (n=5) | SAME |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 57 / 64 / 74 (n=5) | 0 / 0 / 0 (n=5) |  |
| stuckInActionHurt | 33 / 35 / 42 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| actionFrames | 245 / 245 / 245 (n=5) | 245 / 245 / 245 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 858 / 858 / 896 (n=3) | 858 / 858 / 898 (n=5) |  |
| retreatFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |

## ThinWall

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.762 / 0.771 / 0.771 (n=5) | 0.762 / 0.771 / 0.771 (n=5) | OVERLAP |
| inBandFrames | 939 / 950 / 950 (n=5) | 939 / 950 / 950 (n=5) | OVERLAP |
| inBandBlockedFrames | 25 / 25 / 25 (n=5) | 25 / 25 / 25 (n=5) | SAME |
| firstValidFrame | 275 / 275 / 275 (n=5) | 275 / 275 / 275 (n=5) | SAME |
| meanDistance | 1.756 / 1.797 / 1.804 (n=5) | 1.752 / 1.797 / 1.803 (n=5) |  |
| travelled | 23.118 / 24.085 / 24.193 (n=5) | 23.761 / 24.15 / 24.193 (n=5) |  |
| metersPerValidSecond | 1.5 / 1.562 / 1.585 (n=5) | 1.541 / 1.569 / 1.585 (n=5) | OVERLAP |
| attacks | 4 / 5 / 5 (n=5) | 4 / 4 / 5 (n=5) |  |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 957 / 999 / 1000 (n=5) | 986 / 1000 / 1000 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 7 / 7 / 9 (n=5) | 7 / 7 / 9 (n=5) |  |
| strafeFlips | 6 / 6 / 7 (n=5) | 6 / 7 / 7 (n=5) |  |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 40 / 44 / 54 (n=5) | 0 / 0 / 0 (n=5) |  |
| stuckInActionHurt | 25 / 29 / 32 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| actionFrames | 196 / 198 / 240 (n=5) | 196 / 196 / 211 (n=5) |  |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 772 / 811 / 825 (n=3) | 811 / 825 / 825 (n=5) |  |
| retreatFrames | 0 / 0 / 17 (n=3) | 0 / 0 / 17 (n=5) |  |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |

## Unreachable

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | -1 / -1 / -1 (n=5) | -1 / -1 / -1 (n=5) | SAME |
| meanDistance | 3.726 / 3.726 / 3.726 (n=5) | 3.863 / 3.863 / 3.863 (n=5) |  |
| travelled | 8.225 / 8.225 / 8.225 (n=5) | 8.1 / 8.1 / 8.1 (n=5) |  |
| attacks | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 804 / 804 / 804 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| allowedMoveFrames | 1195 / 1195 / 1195 (n=5) | 387 / 387 / 387 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFlips | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| pathRequests | 1 / 1 / 1 (n=5) | 2 / 2 / 2 (n=5) | WORSE |
| stuckFrames | 709 / 709 / 709 (n=5) | 0 / 0 / 0 (n=5) |  |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
| retreatFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=3) | 0 / 0 / 0 (n=5) | SAME |
