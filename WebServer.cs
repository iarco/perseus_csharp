using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public static class Extensions {
	// Расширения, которые симулируют работу аналогов из Java - чтобы не переписывать тонну кода

	public static string[] Split(string source, string separator) {
		return source.Split(new string[] { separator }, StringSplitOptions.None);
	}

	public static string[] Split(string source, string separator, int count) {
		return source.Split(new string[] { separator }, count, StringSplitOptions.None);
	}
}

public class WebServer {

	static class HttpCommonVersion {
		public const string HTTP10 = "HTTP/1.0";
		public const string HTTP11 = "HTTP/1.1";
	}
	
	class HttpCommonHeaders {
		public Dictionary<string, string> headers = new Dictionary<string,string>();
		
		public HttpCommonHeaders(string request) {
			// Сюда передаются заголовки (и это может быть пустая строка вообще)
			if(request.Trim().Length > 0) {
				string[] splitRequest = Extensions.Split(request, "\r\n");

				if(splitRequest.Length > 0) {
					for (int i = 0; i < splitRequest.Length; i++) {
						string[] pair = Extensions.Split(splitRequest[i], ":", 2);
						this.headers.Add(pair[0].Trim(), pair.Length > 1 ? pair[1].Trim() : string.Empty);
					}
				}				
			}			
		}
		
		public new string ToString() {
			string returnValue = string.Empty;

			foreach (string key in this.headers.Keys)
			{
				returnValue += string.Format("{0}: {1}\r\n", key, this.headers[key]);
			}
						
			return returnValue;
		}
	}
	
	static class HttpCommonContentTypes {
		public static Dictionary<string, string> contentTypes = new Dictionary<string,string>() 
		{
			{"htm",		"text/html"		},
			{"html",	"text/html"		},
			{"txt",		"text/plain"	},
			{"gif",		"image/gif"		},
			{"png",		"image/png"		},
			{"jpg",		"image/jpeg"	},
			{"jpeg",	"image/jpeg"	}
		};		
	}
	
	
	enum HttpRequestMethod {
		Get,
		Unsupported
		// Может быть в будущем и будем обрабатывать что-то кроме GET
	}
	
	class HttpRequestURI {
		public string path = "/";
		public Dictionary<string, string> query = new Dictionary<string,string>();
		
		public HttpRequestURI(string uri) {
			// Сюда передается что-то вроде /folder/test.html?param=value&param2=value2			
			// И пустая строка тут тоже может быть
			
			if(uri.Length > 0) {
				string rawQuery = string.Empty;

				string[] split1 = Extensions.Split(uri, "?", 2);
				this.path = split1[0];
				if(split1.Length > 1) { rawQuery = split1[1]; }

				// Теперь нужно преобразовать RawQuery в набор значений
				string[] pairs = Extensions.Split(rawQuery, "&");
				if(pairs.Length > 0) {
					for (int i = 0; i < pairs.Length; i++) {
						string[] pair = Extensions.Split(pairs[i], "=", 2);
						this.query.Add(pair[0], pair.Length > 1 ? pair[1] : string.Empty);
					}
				}		
			}
		}
	}	

	class HttpRequest {
		// В этих объектах храним формализованные значения
		public HttpRequestMethod method = HttpRequestMethod.Unsupported;
		public HttpRequestURI requestURI = new HttpRequestURI("/");
		public string httpVersion;
		
		// В этих объектах мы храним переданные значения (для toString)
		private string _method;
		private string _URI;
		private string _httpVersion;

		public HttpRequest(string requestText) {
			
			// Сюда передается "GET / HTTP/1.1" (пустой строки тут быть не может)
			string[] splitRequest = Extensions.Split(requestText, " ", 3);
			
			_method = splitRequest[0];
			switch(_method.ToLower())
			{
				case "get":	this.method = HttpRequestMethod.Get; break;
				default:	this.method = HttpRequestMethod.Unsupported; break;
			}
			
			if(splitRequest.Length > 1)
			{
				_URI = splitRequest[1];
				this.requestURI = new HttpRequestURI(_URI);
			}
			
			if(splitRequest.Length > 2)
			{
				_httpVersion = splitRequest[2];
				switch(_httpVersion.ToUpper())
				{
					case HttpCommonVersion.HTTP10: this.httpVersion = HttpCommonVersion.HTTP10; break;
					case HttpCommonVersion.HTTP11: this.httpVersion = HttpCommonVersion.HTTP11; break;
				}
			}
		}
		
		public new string ToString()
		{
			return string.Format("{0} {1} {2}", _method, _URI, _httpVersion);
		}
	}
	
	
	static class HttpResponseStatusCodes {
		public const string HTTP200OK = "200 OK";
		public const string HTTP302Redirect = "302 Found";
		public const string HTTP400BadRequest = "400 Bad Request";
		public const string HTTP401Unauthorized = "401 Unauthorized";
		public const string HTTP404NotFound = "404 Not Found";
		public const string HTTP500InternalServerError = "500 Internal Server Error";
		
		public static string CreateStatusLine(string httpVersion, string statusCode) {
			return httpVersion + " " + statusCode;
		}
	}
	
	
	class PerseusHttpRequest {
		public HttpRequest request = null;
		public HttpCommonHeaders headers = null;
		
		public PerseusHttpRequest(string requestText) {
			string[] splitRequest = Extensions.Split(requestText, "\r\n", 2);
			
			// Тут может быть как 2 штуки (если есть запрос и заголовки)
			// Либо вообще 1 штука (только запрос)
			
			this.request = new HttpRequest(splitRequest[0]);
			this.headers = splitRequest.Length > 1 ? new HttpCommonHeaders(splitRequest[1]) : new HttpCommonHeaders(string.Empty);
		}
	}
	
	class PerseusHttpResponse {
		public string statusLine;
		public HttpCommonHeaders headers;
		public byte[] data;
		
		public PerseusHttpResponse(string statusLine, HttpCommonHeaders headers, byte[] data) {
			this.statusLine = statusLine;
			this.headers = headers;

			this.data = new byte[data.Length];
			data.CopyTo(this.data, 0);
		}
		
		/* public new string ToString() {
			return string.format("%s\r\n%s\r\n%s", this.statusLine, this.headers.ToString(), this.data.Length.ToString() + " bytes");
		}*/
		
		public byte[] ToBytes() {
			byte[] byteStatusLine = Encoding.ASCII.GetBytes(this.statusLine + "\r\n");
			byte[] byteHeaders = Encoding.ASCII.GetBytes(this.headers.ToString() + "\r\n");
			byte[] byteToSend = ConcatenateBytes(byteStatusLine, byteHeaders, this.data);

			return byteToSend;
		}
		
		private byte[] ConcatenateBytes(byte[] first, byte[] second, byte[] third) {
			List<byte> returnValue = new List<byte>(first.Length + second.Length + third.Length);
			int offset = 0;

			returnValue.InsertRange(offset, first);
			offset += first.Length;

			returnValue.InsertRange(offset, second);
			offset += second.Length;

			returnValue.InsertRange(offset, third);

			return returnValue.ToArray();
		}
	}

	
	class PerseusWebThread {
		private Socket clientSocket = null;
		private string name = string.Empty;
	
	    public PerseusWebThread(Socket sock) {
			this.clientSocket = sock;
		}
			
		public void ProcessRequest() {
			this.name = Thread.CurrentThread.Name;

			try
			{
				byte[] readBuffer = new byte[8192];
				int readCount = clientSocket.Receive(readBuffer);

				// -1 	- Конец потока, ничего не делаем
				// 0 	- Ничего не прочиталось, ничего не делаем
				
				if(readCount > 0)
				{
					// Теперь надо собрать строчку и проанализировать, что получилось
					// TODO: Тут строчка делается с подрезанием массива по readCount
					string requestString = Encoding.ASCII.GetString(readBuffer);
					PerseusHttpRequest request = new PerseusHttpRequest(requestString);
					PerseusHttpResponse response = null;
					
					// request.request.method = HttpRequestMethod.Unsupported;
					
					// В зависимости от метода подготавливаем ответ
					switch(request.request.method)
					{
						case HttpRequestMethod.Get:			response = CreateGetResponse(request); break;
						case HttpRequestMethod.Unsupported: response = CreateUnsupportedResponse(request); break;
					}
					
					// Ставим подпись :)
					response.headers.headers.Add("Server", "Perseus");
					
					// Теперь высылаем его браузеру
					SendToClient(request, response);
				}
			}
			catch (Exception e)
			{
				// Ошибка чтения или обработки запроса
				Console.WriteLine("{0}: Error on processing: {1}\r\n", this.name, e.Message);
			}
						
			try
			{
				clientSocket.Close();
			}
			catch // (Exception e)
			{
				// 
			}
        }
		
		private PerseusHttpResponse CreateGetResponse(PerseusHttpRequest request) {
			string statusLine = string.Empty;
			HttpCommonHeaders headers = new HttpCommonHeaders(string.Empty);
			byte[] content = new byte[0];
		
			// Запрос всегда приходит с разделителем / и всегда начинается с него
						
			// Надо заменить / в запросе на разделитель (платформа)
			string preparedPath = request.request.requestURI.path.Replace("/", Path.DirectorySeparatorChar.ToString());
			
			// Теперь этот путь нужно приделать к нашему пути . (у него на конце нет /)
			string completePath = string.Empty;
			bool fileError = false;
			
			try {
				completePath = Path.GetFullPath(".") + preparedPath;
			}
			catch { // (Exception e) {
				fileError = true;
			}
			
			if (fileError) {
				// Ошибка получения имени файла
				
				statusLine = HttpResponseStatusCodes.CreateStatusLine(request.request.httpVersion, HttpResponseStatusCodes.HTTP500InternalServerError);
			} else {
				// Нет, ошибки получения файла не было
				
				// File requestedFile = new File(completePath);
				bool directoryRequested = request.request.requestURI.path.EndsWith("/");
				bool itemExists = File.Exists(completePath) || Directory.Exists(completePath);

				if(itemExists) {
					// Да, что-то есть, только надо понять - это файл или папка

					if(Directory.Exists(completePath)) {
						// То, что существует, папка

						if(directoryRequested) {
							// Пользователь запрашивал папку, выдаем ее
							statusLine = HttpResponseStatusCodes.CreateStatusLine(request.request.httpVersion, HttpResponseStatusCodes.HTTP200OK);
							content = CreateGetResponseFolder(completePath, headers);
						} else {
							// Пользователь запрашивал файл, редирект на папку
							statusLine = HttpResponseStatusCodes.CreateStatusLine(request.request.httpVersion, HttpResponseStatusCodes.HTTP302Redirect);
							headers.headers.Add("Location", request.request.requestURI.path + "/");

							// HACK: Мы не поддерживаем параметры, т.е. их получаем, но не передаем далее
						}
					} else {
						// То, что существует, файл
						if(!directoryRequested) {
							// Пользователь запрашивал файл, выдаем файл
							statusLine = HttpResponseStatusCodes.CreateStatusLine(request.request.httpVersion, HttpResponseStatusCodes.HTTP200OK);
							content = CreateGetResponseFile(completePath, headers);
						} else {
							// Пользователь запрашивал папку - 404
							statusLine = HttpResponseStatusCodes.CreateStatusLine(request.request.httpVersion, HttpResponseStatusCodes.HTTP404NotFound);
						}
					}
				} else {
					// Нет ни файла, ни папки - это 404
					statusLine = HttpResponseStatusCodes.CreateStatusLine(request.request.httpVersion, HttpResponseStatusCodes.HTTP404NotFound);
				}				
			}
			
			// Правила обработки запросов
		
			// Если на конце нет /, то пользователь запрашивает файл
			// Если есть файл, то читаем файл
			// Если такого файла нет, надо проверить, а есть ли такая папка?
			// Если есть папка, то редирект на адрес + /
			// Если папки нет, то 404

			// Если на конце есть /, то пользователь запрашивает папку
			// Если в папке есть index.html или index.htm, то вернем их
			// Если нет этих файлов, то вернем листинг папки

			return new PerseusHttpResponse(statusLine, headers, content);
			
			// HACK: Возможно, это крайне порочная архитектура - высылать отсюда объект целиком в памяти - это приведет к адовому ее расходу
		}
		
		private byte[] CreateGetResponseFile(string file, HttpCommonHeaders headers) {
			byte[] returnValue = new byte[0];

			FileStream fs = null;
			try {
				fs = new FileStream(file, FileMode.Open, FileAccess.Read);
				returnValue = new byte[(int)fs.Length];
				fs.Read(returnValue, 0, (int)fs.Length);
			}
			catch (Exception e) {
				
				Console.WriteLine("{0}: File read error: {1}\r\n", this.name, file);
			}
			
			// Content-Type
			string fileName = Path.GetFileName(file);
			int indexOfDot = fileName.IndexOf(".");
			string extension = indexOfDot > -1 ? fileName.Substring(indexOfDot + 1) : "html";
			extension = HttpCommonContentTypes.contentTypes.ContainsKey(extension) ? extension : "html";
			headers.headers.Add("Content-Type", HttpCommonContentTypes.contentTypes[extension]);
			
			headers.headers.Add("Content-Lenght", returnValue.Length.ToString());
						
			return returnValue;
		}
		
		private byte[] CreateGetResponseFolder(string folder, HttpCommonHeaders headers) {
			// HACK: Нам насрать на index.html или index.htm
			
			string returnValue;

			string returnHTMLTemplate = "<html><head><title>{0}</title></head><body><h1>{0}</h1>{1}</body></html>";
			string returnFileListing = string.Empty;
			string returnFileListingTemplate = "<a href=\"{0}\">{0}</a><br />\r\n";
			
			// Возвращаем листинг папки
			string[] directories = Directory.GetDirectories(folder);
			string[] files = Directory.GetFiles(folder);

			if((directories.Length + files.Length) > 0) {
				// Да, в папке есть какие-то файлы или папки
				foreach (string dir in directories)	{
					string name = Path.GetFileName(dir) + "/";
					returnFileListing += string.Format(returnFileListingTemplate, name);
				}

				foreach (string file in files) {
					string name = Path.GetFileName(file);
					returnFileListing += string.Format(returnFileListingTemplate, name);
				}
				
			} else {
				// Нет, папка пустая
				
				returnFileListing = "Empty folder";
			}

			// Вот тут надо добавить HTML-обрамление
			returnValue = string.Format(returnHTMLTemplate, Path.GetFileName(Path.GetDirectoryName(folder)), returnFileListing);
			
			// Добавляем content-type
			headers.headers.Add("Content-type", HttpCommonContentTypes.contentTypes["html"]);
			
			return Encoding.ASCII.GetBytes(returnValue);
		}
		
		private PerseusHttpResponse CreateUnsupportedResponse(PerseusHttpRequest request) {	
			string statusLine = HttpResponseStatusCodes.CreateStatusLine(request.request.httpVersion, HttpResponseStatusCodes.HTTP400BadRequest);
			HttpCommonHeaders headers = new HttpCommonHeaders(string.Empty);		

			return new PerseusHttpResponse(statusLine, headers, new byte[0]);			
		}
		
		private void SendToClient(PerseusHttpRequest request, PerseusHttpResponse response) {
			byte[] byteData = response.ToBytes();
			
			// HACK: Возможно для больших объемов тут нужно будет делать буферизацию чтения/отправки
			// И объект тогда будет другой, вместо DataOutputStream
			
			try {
				if (clientSocket.Connected) {
					
					if (byteData.Length != clientSocket.Send(byteData, SocketFlags.None)) {
						// Отправилось неправильное количество байт
						// Протоколируем неправильную отправку
						Console.WriteLine("{0}: Error on .send (wrong count)", this.name);
					} else {
						// Все нормально отправилось
						// Протоколируем
						Console.WriteLine("{0}: Sent {1} bytes, {2} [{3}]", this.name, byteData.Length, response.statusLine, request.request.requestURI.path);
					}
				} else {
					// Клиент отвалился
					Console.WriteLine("{0}: Error on .send (client disconnected)", this.name);
				}
			}
			catch (Exception e)
			{
				// Что-то не так с сокетом
				// Протоколируем ошибку сокета
				Console.WriteLine("{0}: Socket exception on .send: {1}\r\n", this.name, e.Message);
			}
		}		
	}

	
	static class PerseusServer {
		private const int WEB_PORT = 8090;
		private const int QUEUE_LENGTH = 100;
		private const int TIMEOUT_RESTART = 60;
		private static DateTime lastRestart = new DateTime(1970, 1, 1);
	
		public static void Start() {
			
			// accept может выдавать 4 исключения
			
			IPEndPoint webEndPoint = new IPEndPoint(IPAddress.Any, WEB_PORT);
			Socket webSocket = null;
		
			while(true) {
				try { 
					Socket acceptedSocket = webSocket.Accept();
					IPEndPoint endPoint = (IPEndPoint)acceptedSocket.RemoteEndPoint;
					string userID = endPoint.Address + ":" + endPoint.Port;
															
					// Инициализируем поток
					PerseusWebThread pwt = new PerseusWebThread(acceptedSocket);
					Thread webThread = new Thread(new ThreadStart(pwt.ProcessRequest));
					webThread.Name = "WWW / " + userID;
					webThread.IsBackground = true;
					webThread.Start();
				}
				
				catch (SocketException se) {
					Console.WriteLine("Socket exception on .accept: " + se.Message);
				} 
				catch (Exception e) {
					Console.WriteLine("General exception on .accept: " + e.Message);
						
					// Это не ошибка, на самом деле, но ладно
						
					// Определяем, прошло ли уже время для рестарта?
					TimeSpan tmpSpan = DateTime.Now - lastRestart;
					if (tmpSpan.TotalSeconds > TIMEOUT_RESTART)	{
						// Выполняем рестарт
						lastRestart = DateTime.Now;
							
						// Если сейчас в webSocket что-то есть, то это надо зачистить
						if(webSocket != null) {							
							try { 
								webSocket.Shutdown(SocketShutdown.Both);
							} catch (Exception closeException) {
								Console.WriteLine("General exception on .Shutdown: " + closeException.Message);
							}

							// .Close может генерировать исключение!
							try {
								webSocket.Close();
							} catch (Exception e3) {
								// Ошибка закрытия...
								Console.WriteLine("General exception on .Close: " + e3.Message);
							}
						}
							
						// Выполняем создание сокета
						try {
							webSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
							webSocket.Bind(webEndPoint);
							webSocket.Listen(QUEUE_LENGTH);
						}
						catch (Exception createException) {
							Console.WriteLine("General exception on creating: " + createException.Message);
						}
					}
				}
			}
		}
	}
	
    public static void main(String[] args) {
		PerseusServer.Start();
    }	
}