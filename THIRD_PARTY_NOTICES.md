# 素材の出典とライセンス

「未確認」は、対象の版・入手経路または利用許諾を裏付ける資料が不足していることを示します。リポジトリへの登録候補と、素材ごとの公開許可は別に確認します。

## GitHubで再配布可能な素材

| 素材 | 出典・条件 | 使用箇所・同梱文書 |
| --- | --- | --- |
| Kenney UI Packの画像・効果音・フォント | CC0。クレジット任意。[Kenney UI Pack](https://kenney.nl/assets/ui-pack) | Game SceneのUIに参照あり。[License.txt](Assets/UI/image/kenney_ui-pack/License.txt) |
| Kenney Shape Characters | CC0。クレジット任意。[Kenney Shape Characters](https://kenney.nl/assets/shape-characters) | Game SceneのキャラクターとCircle ChildのPrefab。[License.txt](Assets/UI/image/kenney_shape-characters/License.txt) |
| Kenney Interface Sounds | CC0。クレジット任意。`Assets/Audio` の `Start and Retry.ogg` は `select_005.ogg`、`Failed.ogg` は `error_003.ogg`、`Perfect_001.ogg` から `Perfect_004.ogg` は `confirmation_001.ogg` から `confirmation_004.ogg` とそれぞれバイト単位で一致します。[Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds) | Game Sceneの開始・通常成功・Retry・失敗・PERFECT音。[License.txt](Assets/UI/Font/Rainbow%20Font/kenney_interface-sounds/License.txt) |
| Fredoka可変フォント | Fredoka Project Authors、TTF内の版は2.001。Google Fontsの配布TTFとバイト単位で一致。SIL Open Font License 1.1。[Google Fonts](https://fonts.google.com/specimen/Fredoka) | Game SceneのUI用フォント。[Fredoka-OFL.txt](Assets/UI/Font/Fredoka-OFL.txt) |
| PixelMplus12 Regular | itouhiroとM+ FONTS PROJECT。M+ FONT LICENSE。[PixelMplus](https://github.com/itouhiro/PixelMplus) | 日本語表示用の同梱フォント。保存済みシーン・Prefab・アセットにTTFへの参照は見つかりませんでした。[SOURCE.md](Assets/UI/Font/PixelMplus/SOURCE.md)・[LICENSE_E.txt](Assets/UI/Font/PixelMplus/LICENSE_E.txt) |
| Liberation Sans | Google Corporation、Red Hat。SIL Open Font License 1.1。TextMesh ProのEssential Resourcesに由来します。[UnityのTMP資料](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/manual/index.html) | TMP既定フォントとSDF素材。[LiberationSans - OFL.txt](Assets/TextMesh%20Pro/Fonts/LiberationSans%20-%20OFL.txt) |

ゲームへの組み込みと元ファイルの再配布には、各行に示したライセンスが適用されます。OFLフォントの再配布には著作権表示とライセンス本文が必要です。PixelMplusは同梱の出典とライセンスを維持します。

Unityのパッケージ依存は `Packages/manifest.json` と `Packages/packages-lock.json` に記録されています。Unityが復元する各パッケージの `LICENSE.md` と、該当する第三者通知を参照してください。[Unity Companion License](https://unity.com/legal/licenses/unity-companion-license)

## 元ファイルの再配布が制限される素材

| 素材 | ゲームや制作物での利用 | GitHubへの登録 |
| --- | --- | --- |
| `Mini_mini_Kawaii_Pop.mp3` | ゲームの背景音として利用可。利用者が音源を容易に取得・複製できない形式が必要です。[蒲鉾さちこ氏の公式曲ページ](https://opentracks.com/bgm/detail/22881)・[OpenTracks音源利用ライセンス](https://opentracks.com/help/articles/license/) | 元MP3の再配布禁止。MP3と `.meta` を除外済み。 |
| `New_Train.mp3` | ファイル名と長さが[MFPの公式曲](https://opentracks.com/bgm/detail/22689)に一致します。入手経路・公式音源との同一性は未確認です。同曲ならOpenTracksの条件が適用されます。 | MP3と `.meta` を除外済み。 |
| 効果音ラボのMP3 3件 | 操作・演出音としての利用可。[効果音ラボ利用規約](https://soundeffect-lab.info/agreement/) | 元MP3の再配布禁止。MP3と `.meta` を除外済み。 |
| `mockup-non-branded-android-smartphone-transparent.png` | Mobile FIRSTの公式配布PNGとバイト単位で一致。個人・商用の制作物で利用可。保存済みシーン・Prefab・設定からの参照はありません。[公式配布ページ](https://www.webmobilefirst.com/en/mockups/non-branded-android-smartphone/)・[Mockup license](https://www.webmobilefirst.com/en/license-agreement/#mockup-license) | 元画像ファイルの再配布禁止。PNGと `.meta` を除外済み。 |

## 出典・許諾が未確認の素材

| 素材 | 確認結果 |
| --- | --- |
| `Assets/UI/AI/` のPNG 44件 | タイトル、背景、足場、橋、HUD、文字画像としてシーン・アセットに直接または間接の参照があります。元プロジェクトの作業記録では既存画像として使用。生成サービス・生成履歴・第三者入力素材・許諾は未確認です。 |
| `Assets/UI/Generated/WorldProgress/` | 元プロジェクトの実装報告に、`CircleMask.png` と `RoundedPanel.png` を単色の図形としてローカル生成した記録があります。外部素材の出典は記録されていません。円マスクはGame Sceneの円形プレビューに使用。角丸パネルは保存済みの参照がなく、公開時に外す候補です。プロジェクト由来の権利は下記のイベント条件に従います。 |
| `Assets/UI/Generated/AIBoard/` など | 作業記録によると、AIBoardの派生Spriteは元PNGを参照し、透明余白を切り出すUnityアセットです。元の `Assets/UI/AI/` の画像の許諾確認が必要です。 |
| `Assets/icon-1024.png` | プロジェクトのアプリアイコン。イベント版の広告制作報告には既存画像としての使用と提出物とのハッシュ一致が記録されています。制作元・生成方法・元画像の許諾は未確認です。 |

## 削除済みのEmojiOneサンプル

以前の `Assets` にあったPNGアトラス、JSON、Sprite Asset、帰属表示は、Unity uGUI 2.0.0のTMP Essential Resourcesに由来します。公開CDNと照合して3.x系の絵柄を確認しました。アトラス全体の正確な版と再配布許諾は未確認です。[UnityのTMP変更履歴](https://docs.unity.cn/Packages/com.unity.textmeshpro%402.0/changelog/CHANGELOG.html)・[EmojiOne 3.xの旧利用条件](https://d2gx6z0drfblcq.cloudfront.net/license-free.pdf)

ゲームでの使用は確認されなかったため、TMP Settingsの既定Sprite Assetを未設定にし、上記4ファイルと各 `.meta` を `Assets` から削除しました。共有のTMP Settings、既定フォント、フォントのフォールバック設定は維持しています。削除後の検証コピーで、既定フォントを使ったTMP文字描画と基本Playを確認しました。

## イベント由来のソース・素材

ユーザー提供の終了後案内の要旨では、イベント用SDK・設定の除去、個人用アプリIDへの変更、必要なプロジェクトの手元への保存、個人公開時の素材・ライブラリ条件の確認が示されています。手元での継続開発はこの案内の範囲で扱います。

公開の募集案内ではイベントでの提出・ストア配信を確認できます。コードの権利帰属、外部リポジトリでのソース公開、ポートフォリオへのソース・画像・動画の掲載、イベント由来の元素材の再配布については、参加時の合意文書または主催者の確認が必要です。第三者素材には各配布元の条件が適用されます。[Casual Game JAMの公式募集案内](https://www.cyberagent.co.jp/careers/students/event/detail/id=33809)
