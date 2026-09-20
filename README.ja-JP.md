# Zircon-Godot — Legend of Mir 3

[English](README.md) · [简体中文](README.zh-CN.md)

Zircon-Godot は『Legend of Mir 3』のクロスプラットフォーム版クライアント／サーバープロジェクトです。オリジナルの C# サーバールールとプロトコルを再利用し、クライアントを Godot/C# で再構築しています。

このリポジトリは [Suprcode/Zircon](https://github.com/Suprcode/Zircon) の fork です。現在は Godot クライアントから互換サーバーへ接続する構成を中心に、ローカル動作、リモートサーバー、Web 対応を進めています。

## 現在の状態

`ServerCore` は Linux のヘッドレスモードで動作し、既定で TCP `7000` を待ち受けます。`GodotClient` は接続、ログイン、キャラクター選択、ゲーム開始、オリジナルの `.Zl` と `.map` の読み込み、マップ、オブジェクト、移動、戦闘、NPC、仲間、インベントリ、スキル、照明、天候、主要 UI の描画に対応しています。原版との一致度は継続的に改善中です。

| 分野 | 状態 |
|---|---|
| `ServerLibrary`、`LibraryCore`、`ServerCore` の Linux 動作 | 利用可能、継続保守 |
| ログイン、キャラクター選択、ゲーム開始 | 利用可能 |
| Godot のマップと `.Zl` 描画 | 利用可能、継続調整 |
| 移動、戦闘、NPC、仲間、インベントリ、スキル | 統合済み、継続改善 |
| リモートサーバー | 対応。先にサーバーを検証 |
| Web クライアント | Mir3-Research で試作研究中 |

## 環境と起動

必要なものは .NET 10 SDK、Godot 4.x .NET（`godot-mono`）、オリジナルの `.Zl`、`.map`、`System.db`、サウンドなどです。開発用ランタイムは `/home/tetsuya/mir3ei` に置かれ、大容量データは Git に複製しません。

リポジトリのルートでビルドします。

```bash
dotnet restore ServerCore/ServerCore.csproj
dotnet build GodotClient/ZirconClient.csproj
```

ローカル環境全体を起動します。

```bash
cd /home/tetsuya/mir3ei
./login_game.sh
```

`all` を付けると古いプロセスを整理して再ビルド・再起動します。

```bash
./login_game.sh all
```

通常の接続先は `127.0.0.1:7000` です。テストアカウントは開発検証専用であり、本番の認証情報をリポジトリ、ログ、スクリーンショットに保存しないでください。

ランチャー自身が起動したサーバーは、Godot クライアント終了時に同時に停止します。起動前から動作していたサーバーは停止しません。

## ディレクトリ

```text
ServerLibrary/   サーバーのルール、ワールド状態、ゲームプレイ
ServerCore/      Linux ヘッドレスサーバー
LibraryCore/     共有モデル、MirDB、パケット、TCP 接続
GodotClient/     Godot/C# クライアント
Client/          原版 Windows クライアント（参照用）
RenderingCore/   原版レンダリング部品（参照用）
LibraryEditor/   ライブラリ・リソースツール
BotRunner/       自動テストとゲームプレイ支援
docs/            監査、引き継ぎ、コードベース文書
screenshots/     クライアント実行時の画像
```

## スクリーンショット

![仲間と戦闘](screenshots/gameplay_companion_combat.jpg)

![ネットワークデバッグとゲーム画面](screenshots/gameplay_network_debug.jpg)

![ウィンドウ表示での戦闘](screenshots/gameplay_windowed_combat.png)

## 文書と開発ルール

- [`docs/handoffs/`](docs/handoffs/)：クライアント／サーバーの引き継ぎ資料
- [`docs/codebase/`](docs/codebase/)：プロトコル、マップ、戦闘、モンスター、アイテム、基盤資料
- [`docs/notes/`](docs/notes/)：設計判断と検証記録
- [`docs/REMOTE_SERVER_AND_CLIENT_SETUP.md`](docs/REMOTE_SERVER_AND_CLIENT_SETUP.md)：リモート構築手順
- [Mir3-Research](../Mir3-Research)：原版解析、リソースデコード、マップ監査、Web 研究ツール

ログイン、ゲーム開始、マップ、描画、リソース変換、インデックス規約に関わる変更は、コンパイルだけでなく実際の動作検証を行います。コミットメッセージは既存の中国語形式に合わせます。
