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

    public sealed partial class ExchangeBinanceDEXAPI : ExchangeAPI
    {
        public override string BaseUrl { get; set; } = "https://testnet-dex.binance.org/api/v1/time";
        public override string BaseUrlWebSocket { get; set; } = "wss://testnet-dex.binance.org/api";

        public ExchangeBinanceDEXAPI()
        {
            // give binance plenty of room to accept requests
            RequestWindow = TimeSpan.FromMinutes(15.0);
            NonceStyle = NonceStyle.UnixMilliseconds;
            NonceOffset = TimeSpan.FromSeconds(10.0);
            MarketSymbolSeparator = string.Empty;
            WebSocketOrderBookType = WebSocketOrderBookType.DeltasOnly;
        }

    }
}
