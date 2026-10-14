# P5 probe comparison

variants p5a → p5b · before = 5c08d068f8 · after = 5c08d068f8

Values: min / median / max (n). OVERLAP = ranges overlap ⇒ not evidence of a change.


## AttackAndHurt

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.85 / 0.884 / 0.884 (n=5) | 0.884 / 0.884 / 0.884 (n=5) | OVERLAP |
| inBandFrames | 1020 / 1061 / 1061 (n=5) | 1061 / 1061 / 1061 (n=5) | OVERLAP |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 139 / 139 / 139 (n=5) | 139 / 139 / 139 (n=5) | SAME |
| meanDistance | 1.521 / 1.56 / 1.56 (n=5) | 1.426 / 1.426 / 1.426 (n=5) |  |
| travelled | 13.698 / 13.698 / 14.67 (n=5) | 2.737 / 2.737 / 2.737 (n=5) |  |
| metersPerValidSecond | 0.775 / 0.775 / 0.863 (n=5) | 0.155 / 0.155 / 0.155 (n=5) | BETTER |
| attacks | 5 / 5 / 5 (n=5) | 5 / 5 / 5 (n=5) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 531 / 531 / 569 (n=5) | 91 / 91 / 91 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 1 / 1 / 5 (n=5) | 1 / 1 / 1 (n=5) |  |
| strafeFlips | 7 / 7 / 7 (n=5) | 0 / 0 / 0 (n=5) |  |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 207 / 245 / 245 (n=5) | 207 / 207 / 207 (n=5) |  |
| hurtFrames | 417 / 417 / 417 (n=5) | 417 / 417 / 417 (n=5) | SAME |
| strafeFrames | 959 / 1007 / 1007 (n=5) | 1007 / 1007 / 1007 (n=5) |  |
| retreatFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 1141 / 1141 / 1141 (n=5) |  |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 1010 / 1010 / 1010 (n=5) |  |
| slotChanges | 0 / 0 / 0 (n=5) | 1 / 1 / 1 (n=5) |  |

## Corner

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.812 / 0.813 / 0.813 (n=5) | 0.833 / 0.833 / 0.833 (n=5) | BETTER |
| inBandFrames | 975 / 976 / 976 (n=5) | 999 / 999 / 999 (n=5) | BETTER |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 190 / 190 / 190 (n=5) | 190 / 190 / 190 (n=5) | SAME |
| meanDistance | 1.913 / 1.913 / 1.923 (n=5) | 1.859 / 1.859 / 1.859 (n=5) |  |
| travelled | 24.225 / 24.254 / 24.254 (n=5) | 5.6 / 5.6 / 5.6 (n=5) |  |
| metersPerValidSecond | 1.491 / 1.491 / 1.491 (n=5) | 0.336 / 0.336 / 0.336 (n=5) | BETTER |
| attacks | 5 / 5 / 5 (n=5) | 5 / 5 / 5 (n=5) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 951 / 951 / 951 (n=5) | 198 / 198 / 198 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 13 / 15 / 15 (n=5) | 3 / 3 / 3 (n=5) |  |
| strafeFlips | 14 / 14 / 14 (n=5) | 0 / 0 / 0 (n=5) |  |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 245 / 245 / 245 (n=5) | 245 / 245 / 245 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 783 / 821 / 821 (n=5) | 929 / 929 / 929 (n=5) |  |
| retreatFrames | 0 / 0 / 0 (n=5) | 27 / 27 / 27 (n=5) |  |
| retreatNoProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 1047 / 1047 / 1047 (n=5) |  |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 948 / 948 / 948 (n=5) |  |
| slotChanges | 0 / 0 / 0 (n=5) | 1 / 1 / 1 (n=5) |  |

## CornerPinned

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.372 / 0.393 / 0.442 (n=5) | 0.834 / 0.838 / 0.838 (n=5) | BETTER |
| inBandFrames | 446 / 472 / 530 (n=5) | 1001 / 1005 / 1005 (n=5) | BETTER |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 2 / 2 / 2 (n=5) | 2 / 2 / 2 (n=5) | SAME |
| meanDistance | 1.233 / 1.26 / 1.27 (n=5) | 1.301 / 1.301 / 1.376 (n=5) |  |
| travelled | 6.641 / 9.694 / 10.631 (n=5) | 5.352 / 5.352 / 5.736 (n=5) |  |
| metersPerValidSecond | 0.752 / 1.232 / 1.43 (n=5) | 0.32 / 0.32 / 0.344 (n=5) | BETTER |
| attacks | 5 / 6 / 6 (n=5) | 6 / 6 / 6 (n=5) |  |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 354 / 399 / 399 (n=5) | 22 / 22 / 22 (n=5) | BETTER |
| allowedMoveFrames | 581 / 721 / 759 (n=5) | 228 / 228 / 245 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 157 / 244 / 371 (n=5) | 5 / 5 / 5 (n=5) | BETTER |
| modeSwitches | 17 / 27 / 33 (n=5) | 5 / 5 / 7 (n=5) |  |
| strafeFlips | 7 / 8 / 8 (n=5) | 2 / 2 / 2 (n=5) |  |
| pathRequests | 7 / 7 / 7 (n=5) | 7 / 7 / 7 (n=5) | SAME |
| stuckFrames | 368 / 368 / 368 (n=5) | 0 / 0 / 0 (n=5) |  |
| stuckInActionHurt | 2 / 2 / 2 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| actionFrames | 245 / 294 / 294 (n=5) | 273 / 294 / 294 (n=5) |  |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 210 / 303 / 455 (n=5) | 945 / 956 / 956 (n=5) |  |
| retreatFrames | 640 / 673 / 748 (n=5) | 229 / 229 / 240 (n=5) |  |
| retreatNoProgressFrames | 350 / 399 / 399 (n=5) | 22 / 22 / 22 (n=5) | BETTER |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 1018 / 1018 / 1018 (n=5) |  |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 894 / 894 / 894 (n=5) |  |
| slotChanges | 0 / 0 / 0 (n=5) | 3 / 3 / 3 (n=5) |  |

## Kiting

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | -1 / -1 / -1 (n=5) | -1 / -1 / -1 (n=5) | SAME |
| meanDistance | 4.694 / 4.695 / 4.695 (n=5) | 4.693 / 4.694 / 4.694 (n=5) |  |
| travelled | 31.348 / 31.359 / 31.363 (n=5) | 31.355 / 31.359 / 31.363 (n=5) |  |
| attacks | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 1181 / 1181 / 1181 (n=5) | 1181 / 1181 / 1181 (n=5) | SAME |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFlips | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| pathRequests | 96 / 96 / 96 (n=5) | 96 / 96 / 97 (n=5) | OVERLAP |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| retreatFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotChanges | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |

## MovingSlow

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.439 / 0.519 / 0.585 (n=7) | 0.519 / 0.585 / 0.599 (n=7) | OVERLAP |
| inBandFrames | 658 / 778 / 877 (n=7) | 778 / 877 / 898 (n=7) | OVERLAP |
| inBandBlockedFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| firstValidFrame | 166 / 166 / 166 (n=7) | 166 / 166 / 166 (n=7) | SAME |
| meanDistance | 1.889 / 1.937 / 1.964 (n=7) | 1.874 / 1.889 / 1.937 (n=7) |  |
| travelled | 30.07 / 31.237 / 31.312 (n=7) | 30.658 / 31.119 / 31.312 (n=7) |  |
| metersPerValidSecond | 2.129 / 2.415 / 2.742 (n=7) | 2.048 / 2.129 / 2.415 (n=7) | OVERLAP |
| attacks | 6 / 6 / 7 (n=7) | 6 / 6 / 7 (n=7) |  |
| attacksNotValid | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| allowedMoveFrames | 1151 / 1196 / 1198 (n=7) | 1177 / 1196 / 1201 (n=7) |  |
| recoverFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| modeSwitches | 38 / 39 / 44 (n=7) | 39 / 39 / 44 (n=7) |  |
| strafeFlips | 8 / 8 / 9 (n=7) | 8 / 8 / 9 (n=7) |  |
| pathRequests | 79 / 79 / 79 (n=7) | 79 / 79 / 79 (n=7) | SAME |
| stuckFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| actionFrames | 294 / 294 / 343 (n=7) | 294 / 294 / 314 (n=7) |  |
| hurtFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| strafeFrames | 414 / 495 / 637 (n=7) | 495 / 616 / 637 (n=7) |  |
| retreatFrames | 0 / 10 / 20 (n=7) | 0 / 10 / 20 (n=7) |  |
| retreatNoProgressFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| slotFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| slotHoldFrames | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |
| slotChanges | 0 / 0 / 0 (n=7) | 0 / 0 / 0 (n=7) | SAME |

## OpenStatic

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.902 / 0.912 / 0.913 (n=5) | 0.92 / 0.92 / 0.92 (n=5) | BETTER |
| inBandFrames | 1082 / 1094 / 1096 (n=5) | 1104 / 1104 / 1104 (n=5) | BETTER |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 96 / 96 / 96 (n=5) | 96 / 96 / 96 (n=5) | SAME |
| meanDistance | 1.543 / 1.578 / 1.59 (n=5) | 1.398 / 1.398 / 1.398 (n=5) |  |
| travelled | 22.638 / 23.492 / 23.901 (n=5) | 2.75 / 2.75 / 2.75 (n=5) |  |
| metersPerValidSecond | 1.255 / 1.286 / 1.316 (n=5) | 0.149 / 0.149 / 0.149 (n=5) | BETTER |
| attacks | 5 / 6 / 6 (n=5) | 5 / 6 / 6 (n=5) |  |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 902 / 934 / 947 (n=5) | 95 / 95 / 95 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 7 / 9 / 9 (n=5) | 1 / 1 / 1 (n=5) |  |
| strafeFlips | 7 / 7 / 8 (n=5) | 0 / 0 / 0 (n=5) |  |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 245 / 263 / 294 (n=5) | 245 / 294 / 294 (n=5) |  |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 911 / 964 / 1017 (n=5) | 1050 / 1050 / 1050 (n=5) |  |
| retreatFrames | 0 / 0 / 18 (n=5) | 0 / 0 / 0 (n=5) |  |
| retreatNoProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 1141 / 1141 / 1141 (n=5) |  |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 1053 / 1053 / 1053 (n=5) |  |
| slotChanges | 0 / 0 / 0 (n=5) | 1 / 1 / 1 (n=5) |  |

## Pillar

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.762 / 0.762 / 0.765 (n=5) | 0.816 / 0.816 / 0.816 (n=5) | BETTER |
| inBandFrames | 975 / 975 / 978 (n=5) | 979 / 979 / 979 (n=5) | BETTER |
| inBandBlockedFrames | 60 / 60 / 60 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| firstValidFrame | 273 / 273 / 273 (n=5) | 221 / 221 / 221 (n=5) | BETTER |
| meanDistance | 1.747 / 1.747 / 1.783 (n=5) | 1.745 / 1.745 / 1.745 (n=5) |  |
| travelled | 22.557 / 22.557 / 22.634 (n=5) | 4.908 / 4.908 / 4.908 (n=5) |  |
| metersPerValidSecond | 1.476 / 1.479 / 1.479 (n=5) | 0.301 / 0.301 / 0.301 (n=5) | BETTER |
| attacks | 5 / 5 / 5 (n=5) | 5 / 5 / 5 (n=5) | SAME |
| attacksNotValid | 1 / 1 / 1 (n=5) | 0 / 0 / 0 (n=5) | BETTER |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 946 / 946 / 946 (n=5) | 236 / 236 / 236 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 5 / 5 / 9 (n=5) | 1 / 1 / 1 (n=5) |  |
| strafeFlips | 7 / 7 / 7 (n=5) | 0 / 0 / 0 (n=5) |  |
| pathRequests | 1 / 1 / 1 (n=5) | 1 / 1 / 1 (n=5) | SAME |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 245 / 245 / 245 (n=5) | 245 / 245 / 245 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 843 / 858 / 858 (n=5) | 908 / 908 / 908 (n=5) |  |
| retreatFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 1052 / 1052 / 1052 (n=5) |  |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 907 / 907 / 907 (n=5) |  |
| slotChanges | 0 / 0 / 0 (n=5) | 1 / 1 / 1 (n=5) |  |

## ThinWall

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0.746 / 0.751 / 0.751 (n=5) | 0.739 / 0.739 / 0.739 (n=5) | WORSE |
| inBandFrames | 895 / 901 / 901 (n=5) | 887 / 887 / 887 (n=5) | WORSE |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | 290 / 290 / 290 (n=5) | 313 / 313 / 313 (n=5) | WORSE |
| meanDistance | 1.752 / 1.752 / 1.786 (n=5) | 1.775 / 1.775 / 1.775 (n=5) |  |
| travelled | 23.479 / 23.761 / 24.135 (n=5) | 6.81 / 6.81 / 6.811 (n=5) |  |
| metersPerValidSecond | 1.574 / 1.582 / 1.607 (n=5) | 0.461 / 0.461 / 0.461 (n=5) | BETTER |
| attacks | 4 / 5 / 5 (n=5) | 4 / 4 / 4 (n=5) |  |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 974 / 986 / 1000 (n=5) | 312 / 312 / 312 (n=5) |  |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 7 / 7 / 7 (n=5) | 1 / 1 / 1 (n=5) |  |
| strafeFlips | 6 / 7 / 8 (n=5) | 0 / 0 / 0 (n=5) |  |
| pathRequests | 1 / 1 / 1 (n=5) | 7 / 7 / 7 (n=5) | WORSE |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 196 / 211 / 223 (n=5) | 196 / 196 / 196 (n=5) |  |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 820 / 825 / 825 (n=5) | 821 / 821 / 821 (n=5) |  |
| retreatFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 1106 / 1106 / 1106 (n=5) |  |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 836 / 836 / 836 (n=5) |  |
| slotChanges | 0 / 0 / 0 (n=5) | 2 / 2 / 2 (n=5) |  |

## Unreachable

| metric | before | after | verdict |
|---|---|---|---|
| attackValidRatio | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| inBandBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| firstValidFrame | -1 / -1 / -1 (n=5) | -1 / -1 / -1 (n=5) | SAME |
| meanDistance | 3.863 / 3.863 / 3.863 (n=5) | 3.863 / 3.863 / 3.863 (n=5) | SAME |
| travelled | 8.1 / 8.1 / 8.1 (n=5) | 8.1 / 8.1 / 8.1 (n=5) | SAME |
| attacks | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| attacksNotValid | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| noProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| allowedMoveFrames | 387 / 387 / 387 (n=5) | 387 / 387 / 387 (n=5) | SAME |
| recoverFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeBlockedFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| modeSwitches | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFlips | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| pathRequests | 2 / 2 / 2 (n=5) | 2 / 2 / 2 (n=5) | SAME |
| stuckFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckInActionHurt | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| actionFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| hurtFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| strafeFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| retreatFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| retreatNoProgressFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| stuckAfterNonExecuted | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotHoldFrames | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
| slotChanges | 0 / 0 / 0 (n=5) | 0 / 0 / 0 (n=5) | SAME |
