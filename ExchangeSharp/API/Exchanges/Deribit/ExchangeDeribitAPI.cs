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
        public override string BaseUrl { get; set; } = "https://www.deribit.com/api/v1/";
        public override string BaseUrlWebSocket { get; set; } = "wss://www.deribit.com/ws/api/v1/";

        public ExchangeDeribitAPI()
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
