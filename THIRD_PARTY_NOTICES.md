# 素材の出典とライセンス

この文書は、素材ごとの出典、適用条件、未確認事項を記録します。

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

## Unityから取り込んだリソース

`Assets/TextMesh Pro/` に同梱しているシェーダー、マテリアル、設定などの取り込み元は、Unity uGUI (`com.unity.ugui`) **2.0.0** の `Package Resources/TMP Essential Resources.unitypackage` です。配布アーカイブと現行ファイルを照合しました。Unity提供のリソースは、Unity Companion Licenseの条件に従って、有効なUnityエンジンライセンスで制作したコンテンツに関連して配布できます。同ライセンス第5条に従い、公式のライセンス本文とパッケージの `LICENSE.md` に記載された著作権表示を同梱しています。Liberation Sansには、上表のSIL Open Font Licenseが別途適用されます。 [TMPの同梱ライセンスと著作権表示](LICENSES/TMP-Essential-Resources.md) · [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license)

Unityが復元するパッケージの依存関係は `Packages/manifest.json` と `Packages/packages-lock.json` に記録しています。各パッケージには、その配布物に含まれるライセンスと第三者通知が適用されます。

## 元ファイルの再配布が制限される素材

| 素材 | ゲームや制作物での利用 | GitHubへの登録 |
| --- | --- | --- |
| `Mini_mini_Kawaii_Pop.mp3` | ゲームの背景音として利用可。利用者が音源を容易に取得・複製できない形式が必要です。[蒲鉾さちこ氏の公式曲ページ](https://opentracks.com/bgm/detail/22881)・[OpenTracks音源利用ライセンス](https://opentracks.com/help/articles/license/) | 元MP3の再配布禁止。MP3と `.meta` を除外済み。 |
| `New_Train.mp3` | ファイル名と長さが[MFPの公式曲](https://opentracks.com/bgm/detail/22689)に一致します。入手経路・公式音源との同一性は未確認です。同曲ならOpenTracksの条件が適用されます。 | MP3と `.meta` を除外済み。 |
| 効果音ラボのMP3 3件 | 操作・演出音としての利用可。[効果音ラボ利用規約](https://soundeffect-lab.info/agreement/) | 元MP3の再配布禁止。MP3と `.meta` を除外済み。 |
| `mockup-non-branded-android-smartphone-transparent.png` | Mobile FIRSTの公式配布PNGとバイト単位で一致。個人・商用の制作物で利用可。保存済みシーン・Prefab・設定からの参照はありません。[公式配布ページ](https://www.webmobilefirst.com/en/mockups/non-branded-android-smartphone/)・[Mockup license](https://www.webmobilefirst.com/en/license-agreement/#mockup-license) | 元画像ファイルの再配布禁止。PNGと `.meta` を除外済み。 |

## 画像の制作元と利用条件

対象は `Assets/UI/AI/` のPNG 44件、数式で生成したPNG 2件、アプリアイコン1件の計47件です。

| 制作元の確認方法 | 件数 | 対象 |
| --- | ---: | --- |
| ChatGPT生成をC2PAで検証 | 15 | `Assets/UI/AI/AI Board/` の10件、`Assets/UI/AI/START SCENE Image/` の3件、`Assets/UI/AI/scaffold.png`、`Assets/UI/AI/Font/ゲームオーバー グロッシーキャンディロゴ.png` |
| ユーザー本人の申告による制作元確認（ChatGPT生成） | 30 | `Assets/UI/AI/` 直下の背景5件、`Assets/UI/AI/AI Bridge/` の4件、`Assets/UI/AI/Font/` のGAME OVERロゴを除くRainbow Fontの文字画像20件、`Assets/icon-1024.png` |
| プロジェクト内の数式で生成 | 2 | `Assets/UI/Generated/WorldProgress/` の `CircleMask.png` と `RoundedPanel.png`。外部画像を入力せず、白い円と角丸矩形を生成しています。 |

C2PAの15件は、埋め込み来歴の署名と現行PNG本体のハッシュを照合し、ChatGPTによる生成を確認しました。申告による30件は、同等の署名付き来歴を確認できていません。`Assets/UI/Generated/AIBoard/` のSprite 10件は元PNGの透明余白を切り出すUnityアセットです。公開条件は参照元のPNGに従い、47件の集計には含めていません。 [ChatGPT生成画像のC2PAについて](https://help.openai.com/en/articles/8912793-c2pa-in-chatgpt-images)

OpenAIの個人向け利用規約（2026年1月1日発効）では、OpenAIと利用者の間で、法令で認められる範囲において出力は利用者に帰属します。規約、適用ポリシー、第三者の権利を遵守する範囲で、ゲームへの組み込みや、元PNG・加工PNGの公開が可能です。生成画像にOpenAIのクレジットを付けることは公式画像ガイド上は任意です。AI生成であることは、共有先に適用される公開ポリシーに従い、本書でも明示しています。 [OpenAI利用規約](https://openai.com/ja-JP/policies/terms-of-use/) · [共有と公開に関するポリシー](https://openai.com/ja-JP/policies/sharing-publication-policy/) · [公式画像ガイド](https://learn.chatgpt.com/docs/image-generation)

ChatGPT生成の45件について、生成時に第三者の画像、ロゴ、文字素材などを入力したかは**未確認**です。第三者素材を使っていた場合は、入力と加工に必要な権利に加え、出力に残る第三者素材を公開リポジトリで再配布できるかを個別に確認します。C2PAとユーザー申告は制作元の根拠であり、第三者素材の許諾は個別の条件に従います。

## 削除済みのEmojiOneサンプル

以前の `Assets` にあったPNGアトラス、JSON、Sprite Asset、帰属表示は、Unity uGUI 2.0.0のTMP Essential Resourcesに由来します。公開CDNと照合して3.x系の絵柄を確認しました。アトラス全体の正確な版と再配布許諾は未確認です。[UnityのTMP変更履歴](https://docs.unity.cn/Packages/com.unity.textmeshpro%402.0/changelog/CHANGELOG.html)・[EmojiOne 3.xの旧利用条件](https://d2gx6z0drfblcq.cloudfront.net/license-free.pdf)

ゲームでの使用は確認されなかったため、TMP Settingsの既定Sprite Assetを未設定にし、上記4ファイルと各 `.meta` を `Assets` から削除しました。共有のTMP Settings、既定フォント、フォントのフォールバック設定は維持しています。削除後の検証コピーで、既定フォントを使ったTMP文字描画と基本Playを確認しました。

## イベント由来のソース・素材

プロジェクト所有者がCasual Game JAM運営に確認し、イベント由来ソースコードの公開許可を得ています。第三者素材の再配布には、本書に記載した素材ごとの条件が適用されます。ゲーム全体を対象とするライセンスと、ソースコードの再利用を許諾するライセンスは設定していません。
