﻿using CryptoMarketClient.Binance;
using CryptoMarketClient.Bittrex;
using CryptoMarketClient.Common;
using CryptoMarketClient.Exchanges.Base;
using CryptoMarketClient.Exchanges.Bitmex;
using DevExpress.Utils;
using DevExpress.Utils.Serializing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using WebSocket4Net;
using Microsoft.AspNet.SignalR.Client;
using System.Threading;

namespace CryptoMarketClient {
    public abstract class Exchange : IXtraSerializable {
        public static List<Exchange> Registered { get; } = new List<Exchange>();
        public static List<Exchange> Connected { get; } = new List<Exchange>();


        static Exchange() {
            Registered.Add(PoloniexExchange.Default);
            Registered.Add(BittrexExchange.Default);
            Registered.Add(BinanceExchange.Default);
            Registered.Add(BitmexExchange.Default);

            foreach(Exchange exchange in Registered)
                exchange.Load();
        }

        public static Color AskColor {
            get { return Color.Red; }
        }

        public static Color BidColor {
            get { return Color.Green; }
        }

        public DateTime LastWebSocketRecvTime { get; set; }
        public abstract bool AllowCandleStickIncrementalUpdate { get; }
        public abstract bool ObtainExchangeSettings();

        Ticker btcUsdtTicker;
        public Ticker BtcUsdtTicker {
            get {
                if(btcUsdtTicker == null && Tickers.Count > 0) {
                    btcUsdtTicker = Tickers.FirstOrDefault(t => t.BaseCurrency == "USDT" && t.MarketCurrency == "BTC");
                    if(btcUsdtTicker == null)
                        btcUsdtTicker = Tickers.FirstOrDefault(t => t.BaseCurrency == "usdt" && t.MarketCurrency == "btc");
                }
                return btcUsdtTicker;
            }
        }

        public static int OrderBookDepth { get; set; }
        public static bool AllowTradeHistory { get; set; }
        public bool IsConnected {
            get { return Connected.Contains(this); }
            protected set {
                if(IsConnected == value)
                    return;
                if(value) {
                    if(IsConnected)
                        return;
                    Connected.Add(this);
                }
                else {
                    if(!IsConnected)
                        Connected.Remove(this);
                }
                OnIsConnectedChanged();
            }
        }
        protected virtual void OnIsConnectedChanged() {
        }

        public abstract bool SupportWebSocket(WebSocketType type);

        static string Text { get { return "Yes, man is mortal, but that would be only half the trouble. The worst of it is that he's sometimes unexpectedly mortal—there's the trick!"; } }

        public bool LoadTickers() {
            if(GetTickersInfo()) {
                foreach(Ticker ticker in Tickers) {
                    ticker.Load();
                }
                return true;
            }
            return false;
        }
        public abstract bool GetTickersInfo();
        public abstract bool UpdateTickersInfo();

        [XtraSerializableProperty(XtraSerializationVisibility.Collection, true, false, true)]
        public List<AccountInfo> Accounts { get; } = new List<AccountInfo>();

        public List<CurrencyInfoBase> Currencies { get; } = new List<CurrencyInfoBase>();

        protected object XtraCreateAccountsItem(XtraItemEventArgs e) {
            return new AccountInfo();
        }

        protected void XtraSetIndexAccountsItem(XtraSetItemIndexEventArgs e) {
            Accounts.Add((AccountInfo)e.Item.Value);
        }

        public List<Ticker> Tickers { get; } = new List<Ticker>();

        public event TickerUpdateEventHandler TickerUpdate;
        public event EventHandler TickersUpdate;
        protected void RaiseTickerUpdate(Ticker t) {
            TickerUpdateEventArgs e = new TickerUpdateEventArgs() { Ticker = t };
            if(TickerUpdate != null)
                TickerUpdate(this, e);
            t.RaiseChanged();
        }
        protected void RaiseTickersUpdate() {
            if(TickersUpdate != null)
                TickersUpdate(this, EventArgs.Empty);
        }

        [XtraSerializableProperty(XtraSerializationVisibility.Collection, true, false, true)]
        public List<PinnedTickerInfo> PinnedTickers { get; set; } = new List<PinnedTickerInfo>();
        PinnedTickerInfo XtraCreatePinnedTickersItem(XtraItemEventArgs e) {
            return new PinnedTickerInfo();
        }
        void XtraSetIndexPinnedTickersItem(XtraSetItemIndexEventArgs e) {
            if(e.NewIndex == -1) {
                PinnedTickers.Add((PinnedTickerInfo)e.Item.Value);
                return;
            }
            PinnedTickers.Insert(e.NewIndex, (PinnedTickerInfo)e.Item.Value);
        }

        protected byte[] OpenedOrdersData { get; set; }
        protected internal bool IsOpenedOrdersChanged(byte[] newBytes) {
            if(newBytes == null)
                return false;
            if(OpenedOrdersData == null || OpenedOrdersData.Length != newBytes.Length) {
                OpenedOrdersData = newBytes;
                return true;
            }
            for(int i = 0; i < newBytes.Length; i++) {
                if(OpenedOrdersData[i] != newBytes[i])
                    return true;
            }
            return false;
        }

        AccountInfo defaultAccount;
        public AccountInfo DefaultAccount {
            get { return defaultAccount; }
            set {
                if(DefaultAccount == value)
                    return;
                defaultAccount = value;
                OnDefaultAccountChanged();
            }
        }
        void OnDefaultAccountChanged() {
            foreach(AccountInfo a in Accounts) {
                a.Default = a == DefaultAccount;
            }
            Save();
        }

        public abstract ExchangeType Type { get; }
        public string Name { get { return Type.ToString(); } }

        #region Settings
        public void Save() {
            SaveLayoutToXml(Name + ".xml");
        }
        public string TickersDirectory { get { return "\\Tickers\\" + Name; } }
        public void Load() {
            string dir = TickersDirectory;
            if(!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if(!File.Exists(Name + ".xml"))
                return;
            RestoreLayoutFromXml(Name + ".xml");
        }

        void IXtraSerializable.OnEndDeserializing(string restoredVersion) {
        }

        void IXtraSerializable.OnEndSerializing() {

        }

        void IXtraSerializable.OnStartDeserializing(LayoutAllowEventArgs e) {

        }

        void IXtraSerializable.OnStartSerializing() {
        }

        protected XtraObjectInfo[] GetXtraObjectInfo() {
            ArrayList result = new ArrayList();
            result.Add(new XtraObjectInfo("Exchange", this));
            return (XtraObjectInfo[])result.ToArray(typeof(XtraObjectInfo));
        }
        protected virtual bool SaveLayoutCore(XtraSerializer serializer, object path) {
            System.IO.Stream stream = path as System.IO.Stream;
            if(stream != null)
                return serializer.SerializeObjects(GetXtraObjectInfo(), stream, this.GetType().Name);
            else
                return serializer.SerializeObjects(GetXtraObjectInfo(), path.ToString(), this.GetType().Name);
        }
        protected virtual void RestoreLayoutCore(XtraSerializer serializer, object path) {
            System.IO.Stream stream = path as System.IO.Stream;
            if(stream != null)
                serializer.DeserializeObjects(GetXtraObjectInfo(), stream, this.GetType().Name);
            else
                serializer.DeserializeObjects(GetXtraObjectInfo(), path.ToString(), this.GetType().Name);
        }
        //layout
        public virtual void SaveLayoutToXml(string xmlFile) {
            SaveLayoutCore(new XmlXtraSerializer(), xmlFile);
        }
        public virtual void RestoreLayoutFromXml(string xmlFile) {
            RestoreLayoutCore(new XmlXtraSerializer(), xmlFile);
            foreach(AccountInfo info in Accounts) {
                info.Exchange = Exchange.Registered.FirstOrDefault(e => e.Type == info.Type);
            }
            UpdateDefaultAccount();
        }
        #endregion

        #region Encryption
        private string Encrypt(string toEncrypt, bool useHashing) {
            if(toEncrypt == null)
                return null;

            byte[] keyArray;
            byte[] toEncryptArray = UTF8Encoding.UTF8.GetBytes(toEncrypt);

            //System.Configuration.AppSettingsReader settingsReader = new AppSettingsReader();
            // Get the key from config file

            string key = Text;
            //System.Windows.Forms.MessageBox.Show(key);
            //If hashing use get hashcode regards to your key
            if(useHashing) {
                MD5CryptoServiceProvider hashmd5 = new MD5CryptoServiceProvider();
                keyArray = hashmd5.ComputeHash(UTF8Encoding.UTF8.GetBytes(key));
                //Always release the resources and flush data
                // of the Cryptographic service provide. Best Practice

                hashmd5.Clear();
            }
            else
                keyArray = UTF8Encoding.UTF8.GetBytes(key);

            TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
            //set the secret key for the tripleDES algorithm
            tdes.Key = keyArray;
            //mode of operation. there are other 4 modes.
            //We choose ECB(Electronic code Book)
            tdes.Mode = CipherMode.ECB;
            //padding mode(if any extra byte added)

            tdes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cTransform = tdes.CreateEncryptor();
            //transform the specified region of bytes array to resultArray
            byte[] resultArray =
              cTransform.TransformFinalBlock(toEncryptArray, 0,
              toEncryptArray.Length);
            //Release resources held by TripleDes Encryptor
            tdes.Clear();
            //Return the encrypted data into unreadable string format
            return Convert.ToBase64String(resultArray, 0, resultArray.Length);
        }

        private string Decrypt(string cipherString, bool useHashing) {
            if(string.IsNullOrEmpty(cipherString))
                return string.Empty;
            byte[] keyArray;
            //get the byte code of the string

            byte[] toEncryptArray = Convert.FromBase64String(cipherString);

            string key = Text;

            if(useHashing) {
                //if hashing was used get the hash code with regards to your key
                MD5CryptoServiceProvider hashmd5 = new MD5CryptoServiceProvider();
                keyArray = hashmd5.ComputeHash(UTF8Encoding.UTF8.GetBytes(key));
                //release any resource held by the MD5CryptoServiceProvider

                hashmd5.Clear();
            }
            else {
                //if hashing was not implemented get the byte code of the key
                keyArray = UTF8Encoding.UTF8.GetBytes(key);
            }

            TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
            //set the secret key for the tripleDES algorithm
            tdes.Key = keyArray;
            //mode of operation. there are other 4 modes. 
            //We choose ECB(Electronic code Book)

            tdes.Mode = CipherMode.ECB;
            //padding mode(if any extra byte added)
            tdes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cTransform = tdes.CreateDecryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(
                                 toEncryptArray, 0, toEncryptArray.Length);
            //Release resources held by TripleDes Encryptor                
            tdes.Clear();
            //return the Clear decrypted TEXT
            return UTF8Encoding.UTF8.GetString(resultArray);
        }
        #endregion

        protected string GetDownloadString(Ticker ticker, string address) {
            try {
                return ticker.DownloadString(address);
            }
            catch(Exception e) {
                Console.WriteLine("WebClient exception = " + e.ToString());
                return string.Empty;
            }
        }

        protected MyWebClient[] WebClientBuffer { get; } = new MyWebClient[32];
        protected int CurrentClientIndex { get; set; }
        public MyWebClient GetWebClient() {
            //for(int i = 0; i < WebClientBuffer.Length; i++) {
            //    if(WebClientBuffer[CurrentClientIndex] == null)
            //        WebClientBuffer[CurrentClientIndex] = new MyWebClient();
            //    if(!WebClientBuffer[CurrentClientIndex].IsBusy)
            //        return WebClientBuffer[CurrentClientIndex];
            //    CurrentClientIndex++;
            //    if(CurrentClientIndex >= WebClientBuffer.Length)
            //        CurrentClientIndex = 0;
            //}
            return new MyWebClient();
        }
        protected virtual int WebSocketCheckTimerInterval { get { return 5000; } }
        protected virtual int WebSocketAllowedDelayInterval { get { return 5000; } }
        protected virtual int OrderBookAllowedDelayInterval { get { return 30000; } }

        System.Threading.Timer webSocketCheckTimer;
        protected System.Threading.Timer WebSocketCheckTimer {
            get {
                if(webSocketCheckTimer == null)
                    webSocketCheckTimer = new System.Threading.Timer(new System.Threading.TimerCallback(OnWebSocketCheckTimer), this, 30000, WebSocketCheckTimerInterval);
                return webSocketCheckTimer;
            }
        }

        protected void StartSocketTimer() {
            object item = WebSocketCheckTimer;
        }

        void CheckSocketConnectionInfoDelay(List<SocketConnectionInfo> sockets) {
            foreach(SocketConnectionInfo info in sockets)
                CheckConnection(info);
        }

        void CheckConnection(SocketConnectionInfo info) {
            if(info.Reconnecting)
                return;
            if(!info.IsOpening && !info.IsOpened) {
                info.Open();
                return;
            }          
            if(info.State == SocketConnectionState.Error) {
                info.Open();
                return;
            }
            if((DateTime.Now - info.LastActiveTime).TotalMilliseconds >= WebSocketAllowedDelayInterval) {
                OnConnectionLost(info);
            }
            if(info.Ticker != null) {
                if(info.Type == SocketSubscribeType.OrderBook && 
                    info.Ticker.OrderBook.LastUpdateTime != DateTime.MinValue && (DateTime.Now - info.Ticker.OrderBook.LastUpdateTime).TotalMilliseconds >= OrderBookAllowedDelayInterval) {
                    OnConnectionLost(info);
                    return;
                }
            }
            foreach(var s in info.Subscribtions) {
                if(s.Ticker == null)
                    continue;
                if(s.Type == SocketSubscribeType.OrderBook || !s.Ticker.IsListeningOrderBook)
                    continue;
                if(s.Ticker.OrderBook.LastUpdateTime != DateTime.MinValue && (DateTime.Now - s.Ticker.OrderBook.LastUpdateTime).TotalMilliseconds >= OrderBookAllowedDelayInterval)
                    OnOrderBookConnectionLost(info, s);
            }
        }

        protected virtual void OnWebSocketCheckTimer(object state) {
            if(TickersSocket != null)
                CheckConnection(TickersSocket);
            CheckSocketConnectionInfoDelay(OrderBookSockets);
            CheckSocketConnectionInfoDelay(TradeHistorySockets);
            CheckSocketConnectionInfoDelay(KlineSockets);
        }

        protected virtual void OnOrderBookConnectionLost(SocketConnectionInfo info, WebSocketSubscribeInfo s) {
            Telemetry.Default.TrackEvent(LogType.Error, this, s.Ticker, "subscription connection lost", s.Type.ToString());
            Reconnect(info);
        }

        protected virtual void OnConnectionLost(SocketConnectionInfo info) {
            Telemetry.Default.TrackEvent(LogType.Error, this, info.Ticker, "ticker socket connection lost", info.Type.ToString());
            if(info.Subscribtions.Count > 0) {
                foreach(var item in info.Subscribtions) {
                    Telemetry.Default.TrackEvent(LogType.Error, this, item.Ticker, "socket channel subscribtion lost", info.Type.ToString());
                }
            }
            Reconnect(info);
        }

        public void Reconnect(SocketConnectionInfo info) {
            info.Reconnect();
        }

        public void Reconnect() {
            if(TickersSocket != null)
                TickersSocket.Reconnect();
            foreach(var item in OrderBookSockets)
                item.Reconnect();
            foreach(var item in TradeHistorySockets)
                item.Reconnect();
            foreach(var item in KlineSockets)
                item.Reconnect();
        }

        protected virtual void OnConnectionLost(WebSocket webSocket) {
            Telemetry.Default.TrackEvent(LogType.Error, this, (Ticker)null, "main socket connection lost", "");
            TickersSocket.Reconnect();
        }

        protected Stopwatch Timer { get; } = new Stopwatch();
        protected List<RateLimit> RequestRate { get; set; }
        protected List<RateLimit> OrderRate { get; set; }
        public bool IsInitialized { get; set; }
        protected void CheckRequestRateLimits() {
            if(RequestRate == null)
                return;
            foreach(RateLimit limit in RequestRate)
                limit.CheckAllow();
        }
        protected void CheckOrderRateLimits() {
            if(OrderRate == null)
                return;
            foreach(RateLimit limit in OrderRate)
                limit.CheckAllow();
        }

        public static DateTime FromUnixTime(long unixTime) {
            return epoch.AddMilliseconds(unixTime).ToLocalTime();
        }
        protected static readonly DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        protected string GetDownloadString(string address) {
            try {
                CheckRequestRateLimits();
                return GetWebClient().DownloadString(address);
            }
            catch(Exception e) {
                WebException we = e as WebException;
                if(we != null && we.Message.Contains("418") || we.Message.Contains("429"))
                    IsInitialized = false;
                Telemetry.Default.TrackException(we);
                return null;
            }
        }
        public static Int32 ToUnixTimestamp(DateTime time) {
            return (Int32)(time.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }
        public DateTime FromUnixTimestamp(Int64 time) {
            return new DateTime(1970, 1, 1).AddSeconds(time);
        }
        protected byte[] GetDownloadBytes(string address) {
            try {
                CheckRequestRateLimits();
                return GetWebClient().DownloadData(address);
            }
            catch(Exception e) {
                WebException we = e as WebException;
                if(we != null && we.Message.Contains("418") || we.Message.Contains("429"))
                    IsInitialized = false;
                Telemetry.Default.TrackException(e);
                return null;
            }
        }
        public bool IsTickerPinned(Ticker tickerBase) {
            return PinnedTickers.FirstOrDefault(p => p.BaseCurrency == tickerBase.BaseCurrency && p.MarketCurrency == tickerBase.MarketCurrency) != null;
        }
        public Ticker GetTicker(PinnedTickerInfo info) {
            return Tickers.FirstOrDefault(t => t.BaseCurrency == info.BaseCurrency && t.MarketCurrency == info.MarketCurrency);
        }
        public List<OpenedOrderInfo> UpdateAllOpenedOrders() {
            List<OpenedOrderInfo> list = new List<OpenedOrderInfo>();
            foreach(AccountInfo account in Accounts) {
                if(!account.Active)
                    continue;
                UpdateOpenedOrders(account);
                list.AddRange(account.OpenedOrders);
            }
            return list;
        }
        public bool GetAllAccountsDeposites() {
            bool res = true;
            foreach(AccountInfo account in Accounts) {
                if(!account.Active)
                    continue;
                res &= GetDeposites(account);
            }
            return res;
        }
        public abstract bool UpdateOrderBook(Ticker tickerBase);
        public abstract bool UpdateArbitrageOrderBook(Ticker tickerBase, int depth);
        public abstract bool ProcessOrderBook(Ticker tickerBase, string text);
        public abstract bool UpdateTicker(Ticker tickerBase);
        public abstract bool UpdateTrades(Ticker tickerBase);
        public abstract List<TradeInfoItem> GetTrades(Ticker ticker, DateTime starTime);
        public abstract bool UpdateOpenedOrders(AccountInfo account, Ticker ticker);
        public bool UpdateOpenedOrders(AccountInfo account) { return UpdateOpenedOrders(account, null); }
        public abstract bool UpdateCurrencies();
        public abstract bool UpdateBalances(AccountInfo info);
        public abstract bool GetBalance(AccountInfo info, string currency);
        public abstract string CreateDeposit(AccountInfo account, string currency);
        public abstract bool GetDeposites(AccountInfo account);
        public abstract TradingResult Buy(AccountInfo account, Ticker ticker, double rate, double amount);
        public abstract TradingResult Sell(AccountInfo account, Ticker ticker, double rate, double amount);
        public abstract bool Cancel(AccountInfo account, string orderId);
        public virtual BindingList<CandleStickData> GetCandleStickData(Ticker ticker, int candleStickPeriodMin, DateTime start, long periodInSeconds) {
            return new BindingList<CandleStickData>();
        }
        public abstract bool Withdraw(AccountInfo account, string currency, string adress, string paymentId, double amount);
        public abstract bool UpdateAccountTrades(AccountInfo account, Ticker ticker);
        public bool UpdateAccountTrades(AccountInfo account) { return UpdateAccountTrades(account, null); }

        public bool UpdateDefaultAccountBalances() { return UpdateBalances(DefaultAccount); }
        public bool UpdateAllAccountsBalances() {
            bool res = true;
            foreach(AccountInfo account in Accounts) {
                if(!account.Active)
                    continue;
                res &= UpdateBalances(account);
            }
            return res;
        }
        public bool UpdateAllAccountsTrades() {
            bool res = true;
            foreach(AccountInfo account in Accounts) {
                if(!account.Active)
                    continue;
                res &= UpdateAccountTrades(account);
            }
            return res;
        }
        public List<TradeInfoItem> GetAllAccountTrades() {
            List<TradeInfoItem> res = new List<TradeInfoItem>();
            foreach(AccountInfo account in Accounts) {
                if(!account.Active)
                    continue;
                res.AddRange(account.MyTrades);
            }
            return res;
        }
        public string CheckCreateDeposit(AccountInfo account, string currency) {
            BalanceBase b = account.Balances.FirstOrDefault(bb => bb.Currency == currency);
            if(!string.IsNullOrEmpty(b.DepositAddress))
                return b.DepositAddress;
            return CreateDeposit(account, currency);
        }
        
        public abstract List<CandleStickIntervalInfo> GetAllowedCandleStickIntervals();
        
        protected List<SocketConnectionInfo> OrderBookSockets { get; } = new List<SocketConnectionInfo>();
        protected List<SocketConnectionInfo> TradeHistorySockets { get; } = new List<SocketConnectionInfo>();
        protected List<SocketConnectionInfo> KlineSockets { get; } = new List<SocketConnectionInfo>();

        protected virtual string GetOrderBookSocketAddress(Ticker ticker) { return string.Empty; }
        protected virtual string GetTradeSocketAddress(Ticker ticker) { return string.Empty; }
        protected virtual string GetKlineSocketAddress(Ticker ticker) { return string.Empty; }

        public SocketConnectionState GetOrderBookSocketState(Ticker ticker) {
            SocketConnectionInfo info = GetConnectionInfo(ticker, OrderBookSockets);
            return info == null ? SocketConnectionState.Disconnected : info.State;
        }

        protected SocketConnectionInfo GetConnectionInfo(Ticker ticker, CandleStickIntervalInfo info, List<SocketConnectionInfo> sockets) {
            foreach(SocketConnectionInfo i in sockets) {
                if(i.Ticker == ticker && i.KlineInfo.Interval == info.Interval) {
                    return i;
                }
            }
            return null;
        }

        protected SocketConnectionInfo GetConnectionInfo(Ticker ticker, List<SocketConnectionInfo> sockets) {
            foreach(SocketConnectionInfo info in sockets) {
                if(info.Ticker == ticker) {
                    return info;
                }
            }
            return null;
        }

        protected SocketConnectionInfo CreateOrderBookWebSocket(Ticker ticker) {
            return new SocketConnectionInfo(this, ticker, GetOrderBookSocketAddress(ticker), SocketType.WebSocket, SocketSubscribeType.OrderBook);
        }

        protected internal virtual void OnOrderBookSocketMessageReceived(object sender, MessageReceivedEventArgs e) {

        }

        protected internal virtual void OnOrderBookSocketClosed(object sender, EventArgs e) {
            SocketConnectionInfo info = OrderBookSockets.FirstOrDefault(c => c.Key == sender);
            if(info != null) {
                info.State = SocketConnectionState.Disconnected;
                info.Ticker.IsOrderBookSubscribed = false;
                Telemetry.Default.TrackEvent(LogType.Log, this, info.Ticker, "order book socket closed", e.ToString());
            }
            else
                Telemetry.Default.TrackEvent(LogType.Log, this, (Ticker)null, "order book socket closed", e.ToString());
        }

        protected internal virtual void OnOrderBookSocketOpened(object sender, EventArgs e) {
            SocketConnectionInfo info = OrderBookSockets.FirstOrDefault(c => c.Key == sender);
            if(info != null) {
                info.State = SocketConnectionState.Connected;
                info.Ticker.UpdateOrderBook();
                info.Ticker.IsOrderBookSubscribed = true;
                Telemetry.Default.TrackEvent(LogType.Success, this, info.Ticker, "order book socket opened", "");
            }
            else
                Telemetry.Default.TrackEvent(LogType.Success, this, (Ticker)null, "order book socket opened", "");
        }

        protected internal virtual void OnOrderBookSocketError(object sender, SuperSocket.ClientEngine.ErrorEventArgs e) {
            SocketConnectionInfo info = OrderBookSockets.FirstOrDefault(c => c.Key == sender);
            if(info != null) {
                bool isReconnecting = info.Reconnecting;
                info.Reconnecting = false;
                info.State = SocketConnectionState.Error;
                info.LastError = e.Exception.Message;
                Telemetry.Default.TrackEvent(LogType.Error, this, info.Ticker, "order book socket error", e.Exception.Message);
                if(!isReconnecting)
                    Reconnect(info);
                else
                    info.LastActiveTime = DateTime.Now;
            }
            else
                Telemetry.Default.TrackEvent(LogType.Error, this, (Ticker)null, "order book socket error", e.Exception.Message);
        }

        protected SocketConnectionInfo CreateTradesWebSocket(Ticker ticker) {
            return new SocketConnectionInfo(this, ticker, GetTradeSocketAddress(ticker), SocketType.WebSocket, SocketSubscribeType.TradeHistory);
        }

        protected SocketConnectionInfo CreateKlineWebSocket(Ticker ticker) {
            return new SocketConnectionInfo(this, ticker, GetKlineSocketAddress(ticker), SocketType.WebSocket, SocketSubscribeType.Kline);
        }

        protected internal virtual void OnKlineSocketMessageReceived(object sender, MessageReceivedEventArgs e) { }

        protected internal virtual void OnKlineSocketClosed(object sender, EventArgs e) {
            SocketConnectionInfo info = KlineSockets.FirstOrDefault(c => c.Key == sender);
            info.State = SocketConnectionState.Disconnected;
            Telemetry.Default.TrackEvent(LogType.Log, this, info.Ticker, "kline socket closed", "");
        }

        protected internal virtual void OnKlineSocketOpened(object sender, EventArgs e) {
            SocketConnectionInfo info = KlineSockets.FirstOrDefault(c => c.Key == sender);
            info.State = SocketConnectionState.Connected;
            Telemetry.Default.TrackEvent(LogType.Success, this, info.Ticker, "kline socket opened", "");
        }

        protected internal virtual void OnKlineSocketError(object sender, SuperSocket.ClientEngine.ErrorEventArgs e) {
            SocketConnectionInfo info = KlineSockets.FirstOrDefault(c => c.Key == sender);
            if(info != null) {
                bool isReconnecting = info.Reconnecting;
                info.Reconnecting = false;
                info.State = SocketConnectionState.Error;
                info.LastError = e.Exception.Message;
                Telemetry.Default.TrackEvent(LogType.Error, this, info.Ticker, "kline socket error", e.Exception.Message);
                if(!isReconnecting)
                    info.Reconnect();
                else
                    info.LastActiveTime = DateTime.Now;
            }
            else
                Telemetry.Default.TrackEvent(LogType.Error, this, (Ticker)null, "kline socket error", e.Exception.Message);
        }

        protected internal virtual void OnTradeHistorySocketOpened(object sender, EventArgs e) {
            SocketConnectionInfo info = TradeHistorySockets.FirstOrDefault(c => c.Key == sender);
            if(info != null) {
                info.Ticker.TradeHistory.Clear();
                Telemetry.Default.TrackEvent(LogType.Success, this, info.Ticker, "trade history socket opened", e.ToString());
            }
            else
                Telemetry.Default.TrackEvent(LogType.Success, this, (Ticker)null, "trade history socket opened", e.ToString());
        }

        protected internal virtual void OnTradeHistorySocketError(object sender, SuperSocket.ClientEngine.ErrorEventArgs e) {
            SocketConnectionInfo info = TradeHistorySockets.FirstOrDefault(c => c.Key == sender);
            if(info != null) {
                bool isReconnecting = info.Reconnecting;
                info.Reconnecting = false;
                info.State = SocketConnectionState.Error;
                info.LastError = e.Exception.Message;
                Telemetry.Default.TrackEvent(LogType.Error, this, info.Ticker, "trade history socket error", e.Exception.Message);
                if(!isReconnecting)
                    info.Reconnect();
                info.LastActiveTime = DateTime.Now;
            }
            else
                Telemetry.Default.TrackEvent(LogType.Error, this, (Ticker)null, "trade history socket error", e.Exception.Message);
        }

        protected internal virtual void OnTradeHistorySocketMessageReceived(object sender, MessageReceivedEventArgs e) {
        }

        protected internal virtual void OnTradeHistorySocketClosed(object sender, EventArgs e) {
            SocketConnectionInfo info = TradeHistorySockets.FirstOrDefault(c => c.Key == sender);
            if(info != null) {
                Telemetry.Default.TrackEvent(LogType.Log, this, info.Ticker, "trade history socket closed", e.ToString());
            }
            else
                Telemetry.Default.TrackEvent(LogType.Log, this, (Ticker)null, "trade history socket closed", e.ToString());
        }

        public SocketConnectionState TickersSocketState {
            get { return TickersSocket == null ? SocketConnectionState.Disconnected : TickersSocket.State; }
            set {
                if(TickersSocket == null)
                    return;
                TickersSocket.State = value;
            }
        }
        public SocketConnectionInfo TickersSocket { get; protected set; }
        //public WebSocket WebSocket { get; private set; }
        public abstract string BaseWebSocketAdress { get; }

        public delegate bool WaitCondition();
        protected bool WaitUntil(long msToWait, WaitCondition condition) {
            Stopwatch w = new Stopwatch();
            w.Start();
            while(!condition() && w.ElapsedMilliseconds < msToWait) {
                Thread.Sleep(100);
                //Application.DoEvents();
            }
            return condition();
        }

        protected virtual SocketConnectionInfo CreateTickersSocket() {
            return new SocketConnectionInfo(this, null, BaseWebSocketAdress, SocketType.WebSocket, SocketSubscribeType.Tickers);
        }

        public virtual void StartListenTickersStream() {
            TickersSocket = CreateTickersSocket();
            TickersSocket.Open();
        }

        public virtual void StopListenTickersStream() {
            Telemetry.Default.TrackEvent(LogType.Log, this, (Ticker)null, "stop listening tickers stream", "");
            if(TickersSocket == null)
                return;
            TickersSocket.Close();
            TickersSocket = null;
        }

        protected internal virtual void OnTickersSocketMessageReceived(object sender, MessageReceivedEventArgs e) {
            if(TickersSocket == null)
                return;
            TickersSocket.State = SocketConnectionState.Connected;
            LastWebSocketRecvTime = DateTime.Now;
        }

        protected internal virtual void OnTickersSocketClosed(object sender, EventArgs e) {
            if(TickersSocket == null)
                return;
            TickersSocket.State = SocketConnectionState.Disconnected;
            Telemetry.Default.TrackEvent(LogType.Log, this, (Ticker)null, "tickers socket closed", "");
        }

        protected internal virtual void OnTickersSocketOpened(object sender, EventArgs e) {
            TickersSocket.State = SocketConnectionState.Connected;
            Telemetry.Default.TrackEvent(LogType.Success, this, (Ticker)null, "tickers socket opened", "");
            //foreach(Ticker ticker in SubscribedTickers)
            //    StartListenTickerStream(ticker);
        }

        protected internal virtual void OnTickersSocketError(object sender, SuperSocket.ClientEngine.ErrorEventArgs e) {
            bool isReconnecting = TickersSocket.Reconnecting;
            TickersSocket.Reconnecting = false;
            TickersSocket.State = SocketConnectionState.Error;
            TickersSocket.LastError = e.Exception.Message;
            Telemetry.Default.TrackEvent(LogType.Error, this, (Ticker)null, "tickers socket error", e.Exception.Message);
            if(!isReconnecting)
                TickersSocket.Reconnect();
            else
                TickersSocket.LastActiveTime = DateTime.Now; // skip 10 second...
        }

        public bool Connect() {
            if(IsConnected)
                return true;
            if(!ObtainExchangeSettings())
                return false;
            if(!LoadTickers())
                return false;
            if(!UpdateTickersInfo())
                return false;
            StartSocketTimer();
            IsConnected = true;
            return true;
        }
        //public abstract Form CreateAccountForm();
        public abstract void OnAccountRemoved(AccountInfo info);
        public virtual void StartListenTickerStream(Ticker ticker) {
            StartListenOrderBook(ticker);
            StartListenTradeHistory(ticker);
            StartListenKline(ticker);
        }
        public override string ToString() {
            return Type.ToString();
        }
        public virtual void StopListenTickerStream(Ticker ticker) {
            StopListenOrderBook(ticker);
            StopListenTradeHistory(ticker);
            StopListenKline(ticker);
        }

        public virtual void StopListenKline(Ticker ticker) {
            SocketConnectionInfo info = GetConnectionInfo(ticker, KlineSockets);
            if(info != null)
                info.Close();
        }

        public virtual void StopListenOrderBook(Ticker ticker) {
            SocketConnectionInfo info = GetConnectionInfo(ticker, OrderBookSockets);
            if(info != null)
                info.Close();
        }

        public virtual void StopListenTradeHistory(Ticker ticker) {
            SocketConnectionInfo info = GetConnectionInfo(ticker, TradeHistorySockets);
            if(info != null)
                info.Close();
        }

        protected virtual void StartListenOrderBookCore(Ticker ticker) {
            SocketConnectionInfo info = CreateOrderBookWebSocket(ticker);
            OrderBookSockets.Add(info);
            info.Open();
        }

        protected virtual void StartListenKlineCore(Ticker ticker) {
            SocketConnectionInfo info = CreateKlineWebSocket(ticker);
            KlineSockets.Add(info);
            info.Open();
        }

        protected virtual void StartListenTradeHistoryCore(Ticker ticker) {
            SocketConnectionInfo info = CreateTradesWebSocket(ticker);
            TradeHistorySockets.Add(info);
            info.Open();
        }

        public virtual void StartListenOrderBook(Ticker ticker) {
            if(IsListeningOrderBook(ticker))
                return;
            StartListenOrderBookCore(ticker);
        }

        public virtual void StartListenTradeHistory(Ticker ticker) {
            if(IsListeningTradeHistory(ticker))
                return;
            StartListenTradeHistoryCore(ticker);
        }

        public virtual void StartListenKline(Ticker ticker) {
            if(IsListeningKline(ticker))
                return;
            StartListenKlineCore(ticker);
        }

        IncrementalUpdateQueue updates;
        public IncrementalUpdateQueue Updates {
            get {
                if(updates == null)
                    updates = new IncrementalUpdateQueue(CreateIncrementalUpdateDataProvider());
                return updates;
            }
        }

        protected internal abstract IIncrementalUpdateDataProvider CreateIncrementalUpdateDataProvider();

        protected internal virtual void OnSignalSocketError(Exception e) {
            TickersSocket.State = SocketConnectionState.Error;
            Telemetry.Default.TrackEvent(LogType.Error, this, (Ticker)null, "on socket error", e.Message);
            TickersSocket.Reconnect();
        }

        protected internal virtual void OnSignalConnectionClosed() {
            TickersSocket.State = SocketConnectionState.Disconnected;
            Telemetry.Default.TrackEvent(LogType.Log, this, (Ticker)null, "connection closed", "");
        }

        protected internal virtual void OnSignalStateChanged(StateChange e) {
            LogManager.Default.AddMessage(LogType.Log, this, (Ticker)null, "socket state changed", e.NewState.ToString());
            if(e.NewState == ConnectionState.Connected) {
                TickersSocket.State = SocketConnectionState.Connected;
                TickersSocket.UpdateSubscribtions();
            }
            else if(e.NewState == ConnectionState.Connecting)
                TickersSocket.State = SocketConnectionState.Connecting;
            else if(e.NewState == ConnectionState.Disconnected)
                TickersSocket.State = SocketConnectionState.Disconnected;
            else if(e.NewState == ConnectionState.Reconnecting)
                TickersSocket.State = SocketConnectionState.Connecting;
        }

        protected internal virtual void OnSignalReceived(string s) {

        }

        protected void OnIncrementalUpdateRecv(IncrementalUpdateQueue updates) {
            if(updates.CanApply) {
                updates.Apply();
                TickersSocketState = SocketConnectionState.Connected;
                // apply
            }
            if(updates.TooLongQueue) {
                TickersSocketState = SocketConnectionState.TooLongQue;
                // call snapshot
            }
        }
        public void UpdateDefaultAccount() {
            DefaultAccount = Accounts.FirstOrDefault(a => a.Default);
            Save();
        }
        public List<BalanceBase> GetAllBalances() {
            List<BalanceBase> res = new List<BalanceBase>();
            foreach(AccountInfo account in Accounts) {
                if(account.Active)
                    res.AddRange(account.Balances);
            }
            return res;
        }

        public List<OpenedOrderInfo> GetAllOpenedOrders() {
            List<OpenedOrderInfo> res = new List<OpenedOrderInfo>();
            foreach(AccountInfo account in Accounts) {
                if(account.Active)
                    res.AddRange(account.OpenedOrders);
            }
            return res;
        }

        public static Exchange Get(ExchangeType exchange) {
            return Registered.FirstOrDefault(e => e.Type == exchange);
        }
        public Ticker Ticker(string tickerName) {
            return Tickers.FirstOrDefault(t => t.Name == tickerName);
        }
        public Ticker Ticker(string baseCurrency, string marketCurrency) {
            return Tickers.FirstOrDefault(t => t.BaseCurrency == baseCurrency && t.MarketCurrency == marketCurrency);
        }
        public virtual void StopListenStreams() {
            StopListenTickersStream();

            while(KlineSockets.Count > 0)
                StopListenKline(KlineSockets.First().Ticker);
            while(OrderBookSockets.Count > 0)
                StopListenTickerStream(OrderBookSockets.First().Ticker);
            while(TradeHistorySockets.Count > 0)
                StopListenTickerStream(TradeHistorySockets.First().Ticker);
        }
        public void Disconnect() {
            IsConnected = false;
            StopListenStreams();
        }
        
        protected virtual bool IsListeningOrderBook(Ticker ticker) {
            if(GetConnectionInfo(ticker, OrderBookSockets) != null)
                return true;
            return false;
        }

        protected virtual bool IsListeningTradeHistory(Ticker ticker) {
            if(GetConnectionInfo(ticker, TradeHistorySockets) != null)
                return true;
            return false;
        }

        protected virtual bool IsListeningKline(Ticker ticker) {
            if(GetConnectionInfo(ticker, KlineSockets) != null)
                return true;
            return false;
        }

        List<CandleStickIntervalInfo> allowedCandleStickIntervals;
        public List<CandleStickIntervalInfo> AllowedCandleStickIntervals {
            get {
                if(allowedCandleStickIntervals == null)
                    allowedCandleStickIntervals = GetAllowedCandleStickIntervals();
                return allowedCandleStickIntervals;
            }
        }

        public static Color CandleStickColor { get { return BidColor; } }
        public static Color CandleStickReductionColor { get { return AskColor; } }
    }

    public class CandleStickIntervalInfo {
        public string Text { get; set; }
        public string Command { get; set; }
        public TimeSpan Interval { get; set; }
    }

    public enum WebSocketType {
        Tickers,
        Ticker
    }

    public enum SocketConnectionState {
        None,
        Connecting,
        Connected,
        Disconnecting,
        Disconnected,
        DelayRecv,
        Error,
        TooLongQue
    }
}
