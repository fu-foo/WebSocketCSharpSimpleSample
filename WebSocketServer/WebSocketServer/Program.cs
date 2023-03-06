using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.ComponentModel;

namespace WebSocketServer
{
	internal class Program : IDisposable
	{
		// WebSocket
		private static WebSocket webSocket;

		// Main
		static void Main(string[] args)
		{
			Console.WriteLine("開始:CTRL+Cで停止してください");

			// WebSocket作成＋受信
			var nowait1 = CreateWebSocketServerAndReceive();

			// 送信
			var nowait2 = Send();

			// CTRL+Cを押すまで止めない用
			using (var manualResetEvent = new ManualResetEvent(false))
			{
				manualResetEvent.WaitOne();
			}
		}

		// Ctrl+C で止めるときにお行儀よく切断
		public async void Dispose()
		{
			if (webSocket != null)
			{
				Console.WriteLine("WebSocketサーバーからの切断");
				await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "WebSocketサーバーからの切断", CancellationToken.None);
				webSocket.Dispose();
			}
		}

		// WebSocket接続待ち作成
		static async Task CreateWebSocketServerAndReceive()
		{
			// ポート8080でWebSocket受付
			var httpListener = new HttpListener();
			httpListener.Prefixes.Add("http://+:8080/");
			httpListener.Start();

			Console.WriteLine("WebSocket受付開始");

			while (true)
			{
				var httpListenerContext = await httpListener.GetContextAsync();
				
				// WebSocketではない場合はNG
				if (!httpListenerContext.Request.IsWebSocketRequest)
				{
					Console.WriteLine("WebSocketでの接続ではありません");
					httpListenerContext.Response.StatusCode = 400;	// bad request
					httpListenerContext.Response.Close();
					return;
				}
				// Accept
				var taskHttpListenerContext = await httpListenerContext.AcceptWebSocketAsync(null);

				// 今回は１接続のみ意識、既に接続があれば既存WebSocketいったん破棄。
				if (webSocket != null)
				{
					webSocket.Dispose();
				}
				// WebSocket作成
				webSocket = taskHttpListenerContext.WebSocket;

				Console.WriteLine("WebSocket接続");
				Console.Write(">");

				// 受信
				var nowait = Receive();
			}
		}

		// 受信
		static async Task Receive()
		{
			//情報取得待ちループ
			while (true)
			{
				// WebSocketが切れていれば受信ループはおしまい
				if (webSocket == null || (webSocket != null && webSocket.State != WebSocketState.Open))
				{
					Console.WriteLine("WebSocket接続がありません");
					Console.Write(">");
					return;
				}

				// 受信
				var buffer = new byte[1024];	// とりあえず1024バイト
				var byteArray = new ArraySegment<byte>(buffer);
				var ret = await webSocket.ReceiveAsync(byteArray, CancellationToken.None);

				//エンドポイントCloseの場合、処理を中断
				if (ret.MessageType == WebSocketMessageType.Close)
				{
					Console.WriteLine("クライアントから切断されました");
					Console.Write(">");
					await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "クライアントから切断されました", CancellationToken.None);
					return;
				}

				// 今回は簡易チャットなのでテキスト以外は扱わない
				if (ret.MessageType != WebSocketMessageType.Text)
				{
					Console.WriteLine("テキストデータではありません");
					Console.Write(">");
					await webSocket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "テキストデータではありません", CancellationToken.None);
					return;
				}

				// 受信内容取得
				int size = ret.Count;
				while (!ret.EndOfMessage)
				{
					if (buffer.Length <= size)
					{
						Console.WriteLine("バッファーオーバー");
						await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "バッファーオーバー", CancellationToken.None);
						return;
					}
					byteArray = new ArraySegment<byte>(buffer, size, buffer.Length - size);
					ret = await webSocket.ReceiveAsync(byteArray, CancellationToken.None);

					size += ret.Count;
				}

				// 受信内容のバイト列を文字列に変換・出力
				var message = Encoding.UTF8.GetString(buffer, 0, size);
				Console.WriteLine($"\r\n[{DateTime.Now.ToString()}][受信]{message}");
				Console.Write(">");
			}
		}

		// 送信
		static async Task Send()
		{
			while (true)
			{
				// 入力待ち
				string line = Console.ReadLine();

				// 接続がなければ入力待ちに戻る
				if (webSocket == null || (webSocket != null && webSocket.State != WebSocketState.Open))
				{
					Console.WriteLine("WebSocket接続がありません");
					Console.Write(">");
					continue;
				}

				// 文字列
				var buffer = Encoding.UTF8.GetBytes(line);
				var byteArray = new ArraySegment<byte>(buffer);

				// 文字列→バイト列に変換して送信
				await webSocket.SendAsync(byteArray, WebSocketMessageType.Text, true, CancellationToken.None);
				Console.WriteLine($"[{DateTime.Now.ToString()}][送信]{line}");
				Console.Write(">");
			}
		}
	}
}
