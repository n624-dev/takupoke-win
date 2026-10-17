# 今回の検証失敗の記録

## W01 ローカルの全参照履歴検査で作者規則違反（2026-10-18）

- bb4250aに正式名称表示の未コミット変更を載せたLinux checkoutで、
  `python3 -B scripts/check-public-tree.py` がnoreply以外のcommit identityとしてexit1。
  Core301・Integration1161・一覧14件は成功済み。新規Actionsは開始していない。
- 対処前に観測事実を記録。検査は `git log --all` を対象にする。
  最新配布branchへの切替で主担当の差分だけを保存したstashがローカルに存在する。
  違反参照の種類と既存履歴を照合し、製品・検査条件・remote履歴は変更しない。
- 違反した2commitは主担当が今回作成した単一stashの本体とindexだけだった。
  remoteの製品履歴には該当せず、適用済み3ファイルの差分は別の所有patchにも保存済み。
  repoローカルの作者設定をnoreplyへ設定し、適用済みの所有stash参照だけを片付けて再検査する。
  検査・既存commitの書換えは不要。今回の配布sourceは10447の製品を継承する。
