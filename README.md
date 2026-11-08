# Bridge Just

動く橋をタップで止め、足場をつないで進むワンタップゲームです。橋を止めた位置に応じて成功・PERFECT・失敗が決まります。スコアとベストスコア、PERFECTコンボ、背景の切り替え、Retryがあります。

## 起動方法

使用するEditorはUnity 6.3 LTSです。URP、uGUI（TextMesh Proを含む）、Input Systemなどは、Unityが `Packages/manifest.json` から復元します。

1. Unity Hubで、このフォルダーをUnity 6.3 LTS（`6000.3.24f1`）のプロジェクトとして開きます。
2. パッケージの復元とアセットのインポートが終わったら、Gameビューを縦長の `9:16` に設定します。`Assets/Scenes/Start Scene.unity` を開き、Playを押します。
3. タイトル画面の **START** で `Game Scene` へ進みます。動く橋をタップで止めます。EditorではGameビューでマウスを使えます。

## 素材と利用条件

素材の出典、同梱ライセンス、許諾の確認状況は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) に記載しています。

未使用だったEmojiOneのサンプル素材は `Assets` から削除済みです。TextMesh Proの既定フォントはそのまま利用できます。

BGM 2曲と効果音ラボのMP3 3件は、対応する `.meta` とともにリポジトリの登録対象から除外しています。通常成功音には同梱のKenney音源を設定しており、BGMなしで起動する構成です。

個人用コピーでは、イベント用のMAX／AppLovin・Adjustと関連設定を取り除いています。終了後案内の要旨では、必要なプロジェクトの手元への保存が示されています。ソースの公開と素材の元ファイルの再配布には、個別の許諾確認が必要です。ソースコードの公開ライセンスは未設定です。
