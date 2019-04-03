using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExchangeSharp
{

    public sealed partial class ExchangeDeribitAPI : ExchangeAPI
    {
        public override string BaseUrl { get; set; } = "https://www.deribit.com";
        public override string BaseUrlWebSocket { get; set; } = "wss://www.deribit.com/ws/api/v2/";

        private int WebsocketId = 0;
        public ExchangeDeribitAPI()
        {
            // give binance plenty of room to accept requests
            RequestWindow = TimeSpan.FromMinutes(15.0);
            NonceStyle = NonceStyle.UnixMilliseconds;
            NonceOffset = TimeSpan.FromSeconds(10.0);
            MarketSymbolSeparator = string.Empty;
            WebSocketOrderBookType = WebSocketOrderBookType.DeltasOnly;
            RequestContentType = "application/json";
        }


        protected override async Task<IEnumerable<string>> OnGetMarketSymbolsAsync()
        {
            var m = await GetMarketSymbolsMetadataAsync();
            return m.Select(x => x.MarketSymbol);
        }

        protected override async Task<IEnumerable<ExchangeMarket>> OnGetMarketSymbolsMetadataAsync()
        {
            List<ExchangeMarket> markets = new List<ExchangeMarket>();
            JToken obj = await MakeJsonRequestAsync<JToken>("/api/v1/public/getinstruments");
            foreach (JToken marketSymbolToken in obj)
            {
                if (marketSymbolToken["kind"].ToStringInvariant() == "future")
                {
                    var market = new ExchangeMarket
                    {
                        MarketSymbol = marketSymbolToken["instrumentName"].ToStringUpperInvariant(),
                        IsActive = marketSymbolToken["isActive"].ToStringInvariant().EqualsWithOption("true"),
                        QuoteCurrency = marketSymbolToken["currency"].ToStringUpperInvariant(),
                        BaseCurrency = marketSymbolToken["baseCurrency"].ToStringUpperInvariant(),
                        MarginEnabled = true,
                    };
                    try
                    {
                        market.PriceStepSize = marketSymbolToken["tickSize"].ConvertInvariant<decimal>();
                        market.MaxPrice = marketSymbolToken["maxPrice"].ConvertInvariant<decimal>();
                        //market.MinPrice = symbol["minPrice"].ConvertInvariant<decimal>();

                        market.MaxTradeSize = marketSymbolToken["maxOrderQty"].ConvertInvariant<decimal>();
                        market.MinTradeSize = marketSymbolToken["minTradeSize"].ConvertInvariant<decimal>();
                        //market.QuantityStepSize = symbol["stepSize"].ConvertInvariant<decimal>();
                    }
                    catch
                    {

                    }
                    markets.Add(market);
                }
            }
            return markets;
        }

        protected override async Task<ExchangeOrderBook> OnGetOrderBookAsync(string marketSymbol, int maxCount = 100)
        {
            Dictionary<string, object> Payload = new Dictionary<string, object>();
            Payload["jsonrpc"] = "2.0";
            Payload["id"] = WebsocketId++;
            Payload["method"] = "public/get_order_book";
            Dictionary<string, object> Params = new Dictionary<string, object>();
            Payload["params"] = Params;
            Params["instrument_name"] = marketSymbol;
            Params["depth"] = 50;
            JToken token = await MakeJsonRequestAsync<JToken>("/api/v2", payload:Payload,requestMethod:"POST");
            return ExchangeAPIExtensions.ParseOrderBookFromJTokenArrayDictionaries(token, asks: "asks", bids: "bids", price: 0, amount:1, sequence: "change_id", maxCount: maxCount);
        }
        public override async Task<ExchangeOrderBook> GetOrderBookAsync(string marketSymbol, int maxCount = 100)
        {
            //marketSymbol = NormalizeMarketSymbol(marketSymbol);
            return await Cache.CacheMethod(MethodCachePolicy, async () => await OnGetOrderBookAsync(marketSymbol, maxCount), nameof(GetOrderBookAsync), nameof(marketSymbol), marketSymbol, nameof(maxCount), maxCount);
        }

        protected override async Task ProcessRequestAsync(IHttpWebRequest request, Dictionary<string, object> payload)
        {
            if (CanMakeAuthenticatedRequest(payload))
            {
                // convert nonce to long, trim off milliseconds
                var nonce = payload["nonce"].ConvertInvariant<long>();
                payload.Remove("nonce");
                var msg = CryptoUtility.GetJsonForPayload(payload);
                var sign = $"{request.Method}{request.RequestUri.AbsolutePath}{request.RequestUri.Query}{nonce}{msg}";
                string signature = CryptoUtility.SHA256Sign(sign, CryptoUtility.ToUnsecureBytesUTF8(PrivateApiKey));

                request.AddHeader("api-expires", nonce.ToStringInvariant());
                request.AddHeader("api-key", PublicApiKey.ToUnsecureString());
                request.AddHeader("api-signature", signature);

                await CryptoUtility.WritePayloadJsonToRequestAsync(request, payload);
            }
            else
            {
                await CryptoUtility.WritePayloadJsonToRequestAsync(request, payload);
            }
        }

        protected override IWebSocket OnGetOrderBookWebSocket(Action<ExchangeOrderBook> callback, int maxCount = 20, params string[] marketSymbols)
        {
            if (marketSymbols == null || marketSymbols.Length == 0)
            {
                marketSymbols = GetMarketSymbolsAsync().Sync().ToArray();
            }
            return ConnectWebSocket(string.Empty, (_socket, msg) =>
            {
                // parse the message over here
                string json = msg.ToStringFromUTF8();
                //Console.WriteLine(json);
                JToken token = JToken.Parse(json);
                if (token["params"] != null)
                {
                    JToken Params = token["params"];
                    JToken Data = Params["data"];

                    var book = new ExchangeOrderBook();
                    book.MarketSymbol = Data["instrument_name"].ToStringInvariant();
                    book.SequenceId = Data["change_id"].ConvertInvariant<long>();
                    JArray bids = (JArray) Data["bids"];
                    JArray asks = (JArray) Data["asks"];
                    int new_bids = 0;
                    int change_bids = 0;
                    int delete_bids = 0;
                    int new_asks = 0;
                    int change_asks = 0;
                    int delete_asks = 0;
                    if (bids != null)
                    {
                        foreach (var bid in bids)
                        {
                            string action = bid[0].ToStringInvariant();
                            switch (action)
                            {
                                case "new":
                                    new_bids++;
                                    break;
                                case "delete":
                                    delete_bids++;
                                    break;
                                case "change":
                                    change_bids++;
                                    break;
                                default:
                                    //Console.WriteLine("Unknown bid action:"+action);
                                    break;
                            }
                            decimal price = bid[1].ConvertInvariant<decimal>();
                            decimal size = bid[2].ConvertInvariant<decimal>();
                            var depth = new ExchangeOrderPrice { Price = price, Amount = size };
                            book.Bids[depth.Price] = depth;
                        }
                    }

                    if (asks != null)
                    {
                        foreach (var ask in asks)
                        {
                            string action = ask[0].ToStringInvariant();
                            switch (action)
                            {
                                case "new":
                                    new_asks++;
                                    break;
                                case "delete":
                                    delete_asks++;
                                    break;
                                case "change":
                                    change_asks++;
                                    break;
                                default:
                                    //Console.WriteLine("Unknown ask action:" + action);
                                    break;
                            }
                            decimal price = ask[1].ConvertInvariant<decimal>();
                            decimal size = ask[2].ConvertInvariant<decimal>();
                            var depth = new ExchangeOrderPrice { Price = price, Amount = size };
                            book.Asks[depth.Price] = depth;
                        }
                    }
                    if (!string.IsNullOrEmpty(book.MarketSymbol))
                    {
                        //Console.WriteLine("Asks:"+book.Asks.Count+":Bids:"+book.Bids.Count);
                        //Console.WriteLine("A_New:"+new_asks+":A_Change:"+change_asks+":A_Delete:"+delete_asks);
                        //Console.WriteLine("B_New:" + new_bids + ":B_Change:" + change_bids + ":B_Delete:" + delete_bids);
                        callback(book);
                    }
                }
                return Task.CompletedTask;
            }, async (_socket) =>
            {
                Console.WriteLine("Socket connected Deribit");
                // do the subscription over here
                // we should create a json message
                Dictionary<string, object> Request = new Dictionary<string, object>();
                // we now use the version 2 api
                //{
                //"jsonrpc": "2.0",
                //"id": 5984,
                //"method": "public/subscribe",
                //"params": {
                //    "channels": [
                //    "book.BTC-PERPETUAL.raw"
                //        ]
                //    }
                //}
                Request["jsonrpc"] = "2.0";
                Request["id"] = WebsocketId++;
                Request["method"] = "public/subscribe";
                Dictionary<string,object> Params = new Dictionary<string, object>();
                List<string> Channels = new List<string>();
                for (int i = 0; i < marketSymbols.Length; i++)
                {
                    Channels.Add("book." + marketSymbols[i] + ".raw");
                }
                Params["channels"] = Channels;
                Request["params"] = Params;
                await _socket.SendMessageAsync(Request);
            }, async (_socket) =>
                {
                    await Task.Run(() =>
                    {
                        Console.WriteLine("Socket disconnected deribit unimplmented");
                    });
                });
        }

        protected override IWebSocket OnGetTradesWebSocket(Action<KeyValuePair<string, ExchangeTrade>> callback, params string[] marketSymbols)
        {
            /*
            {
              "e": "trade",     // Event type
              "E": 123456789,   // Event time
              "s": "BNBBTC",    // Symbol
              "t": 12345,       // Trade ID
              "p": "0.001",     // Price
              "q": "100",       // Quantity
              "b": 88,          // Buyer order Id
              "a": 50,          // Seller order Id
              "T": 123456785,   // Trade time
              "m": true,        // Is the buyer the market maker?
              "M": true         // Ignore.
            }
            */

            if (marketSymbols == null || marketSymbols.Length == 0)
            {
                marketSymbols = GetMarketSymbolsAsync().Sync().ToArray();
            }
            return ConnectWebSocket(string.Empty, (_socket, msg) =>
            {
                string json = msg.ToStringFromUTF8();
                JToken token = JToken.Parse(json);
                JToken Params = (JToken) token["params"];
                if (Params != null)
                {
                    JArray data = (JArray)Params["data"];
                    if (data != null)
                    {
                        foreach (var trade in data)
                        {
                            JToken td = (JToken) trade;
                            string tradeSymbol = td["instrument_name"].ToStringInvariant();
                            callback(new KeyValuePair<string, ExchangeTrade>(tradeSymbol, td.ParseTrade("amount", "price", "direction", "timestamp", TimestampType.UnixMilliseconds, "trade_id", "buy")));

                        }
                    }
                }
                return Task.CompletedTask;

            }, async (_socket) =>
            {
                // subscribe when we are connected
                Dictionary<string, object> Request = new Dictionary<string, object>();
                Request["jsonrpc"] = "2.0";
                Request["id"] = WebsocketId++;
                Request["method"] = "public/subscribe";
                Dictionary<string, object> Params = new Dictionary<string, object>();
                List<string> Channels = new List<string>();
                for (int i = 0; i < marketSymbols.Length; i++)
                {
                    Channels.Add("trades." + marketSymbols[i] + ".raw");
                }
                Params["channels"] = Channels;
                Request["params"] = Params;
                await _socket.SendMessageAsync(Request);
            }, async (_socket) =>
            {
                await Task.Run(() => { Console.WriteLine("Socket disconnected deribit unimplmented"); });
            });
        }
    }
}
