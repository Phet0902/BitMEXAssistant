﻿using BitMEX;
using CsvHelper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WebSocketSharp;

namespace BitMEXAssistant
{
    public partial class Bot : Form
    {
        #region Class Properties
        string APIKey = "";
        string APISecret = "";
        BitMEXApi bitmex;
        List<Instrument> ActiveInstruments = new List<Instrument>();
        Instrument ActiveInstrument = new Instrument();
        string Timeframe = "1m";
        bool RealNetwork = false;

        int DCACounter = 0;
        int DCAContractsPer = 0;
        int DCAHours = 0;
        int DCAMinutes = 0;
        int DCASeconds = 0;
        int DCATimes = 0;
        string DCASide = "Buy";

        WebSocket ws;
        Dictionary<string, decimal> Prices = new Dictionary<string, decimal>();
        //List<Alert> Alerts = new List<Alert>();

        public static string Version = "0.0.16";

        string LimitNowBuyOrderId = "";
        decimal LimitNowBuyOrderPrice = 0;
        string LimitNowSellOrderId = "";
        decimal LimitNowSellOrderPrice = 0;
        List<OrderBook> OrderBookTopAsks = new List<OrderBook>();
        List<OrderBook> OrderBookTopBids = new List<OrderBook>();
        #endregion

        public Bot()
        {
            InitializeComponent();
        }

        #region Bot Form Events

        private void Bot_Load(object sender, EventArgs e)
        {

            APIInfo Login = new APIInfo();
            while (!Login.APIValid)
            {
                Login.ShowDialog();
            }

            if (Login.APIValid)
            {
                InitializeDropdownsAndSettings();
                InitializeAPI();

                InitializePostAPIDropdownsAndSettings();


                InitializeSymbolInformation();
                InitializeWebSocket();
                InitializeDependentSymbolInformation();

                tmrClientUpdates.Start(); // Start our client update timer
            }
        }

        private void Bot_FormClosing(object sender, FormClosingEventArgs e)
        {
            ws.Close(); // Make sure our websocket is closed.
        }
        #endregion

        #region Initialization
        private void InitializeWebSocket()
        {
            if (Properties.Settings.Default.Network == "Real")
            {
                ws = new WebSocket("wss://www.bitmex.com/realtime");
            }
            else
            {
                ws = new WebSocket("wss://testnet.bitmex.com/realtime");
            }

            ws.OnMessage += (sender, e) =>
            {
                try
                {
                    JObject Message = JObject.Parse(e.Data);
                    if (Message.ContainsKey("table"))
                    {
                        if ((string)Message["table"] == "trade")
                        {
                            if (Message.ContainsKey("data"))
                            {
                                JArray TD = (JArray)Message["data"];
                                if (TD.Any())
                                {
                                    decimal Price = (decimal)TD.Children().LastOrDefault()["price"];
                                    string Symbol = (string)TD.Children().LastOrDefault()["symbol"];
                                    Prices[Symbol] = Price;
                                }
                            }
                        }
                        else if ((string)Message["table"] == "orderBook10")
                        {
                            if (Message.ContainsKey("data"))
                            {
                                JArray TD = (JArray)Message["data"];
                                if (TD.Any())
                                {
                                    JArray TDBids = (JArray)TD[0]["bids"];
                                    if (TDBids.Any())
                                    {
                                        List<OrderBook> OB = new List<OrderBook>();
                                        foreach (JArray i in TDBids)
                                        {
                                            OrderBook OBI = new OrderBook();
                                            OBI.Price = (decimal)i[0];
                                            OBI.Size = (int)i[1];
                                            OB.Add(OBI);
                                        }

                                        OrderBookTopBids = OB;
                                    }

                                    JArray TDAsks = (JArray)TD[0]["asks"];
                                    if (TDAsks.Any())
                                    {
                                        List<OrderBook> OB = new List<OrderBook>();
                                        foreach (JArray i in TDAsks)
                                        {
                                            OrderBook OBI = new OrderBook();
                                            OBI.Price = (decimal)i[0];
                                            OBI.Size = (int)i[1];
                                            OB.Add(OBI);
                                        }

                                        OrderBookTopAsks = OB;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //MessageBox.Show(ex.Message);
                }
            };

            ws.Connect();

            // Assemble our price dictionary
            foreach (Instrument i in ActiveInstruments)
            {
                ws.Send("{\"op\": \"subscribe\", \"args\": [\"trade:" + i.Symbol + "\"]}");
            }

        }

        private void InitializeSymbolSpecificData(bool FirstLoad = false)
        {
            if (!FirstLoad)
            {
                // Unsubscribe from old orderbook
                ws.Send("{\"op\": \"unsubscribe\", \"args\": [\"orderBook10:" + ActiveInstrument.Symbol + "\"]}");
                OrderBookTopAsks = new List<OrderBook>();
                OrderBookTopBids = new List<OrderBook>();

                ActiveInstrument = bitmex.GetInstrument(((Instrument)ddlSymbol.SelectedItem).Symbol)[0];
            }

            // Subscribe to new orderbook
            ws.Send("{\"op\": \"subscribe\", \"args\": [\"orderBook10:" + ActiveInstrument.Symbol + "\"]}");
            UpdatePositionInfo();
            UpdateFormsForTickSize(ActiveInstrument.TickSize, ActiveInstrument.DecimalPlacesInTickSize);

        }

        private void InitializeDropdownsAndSettings()
        {
            // Network/API Settings
            RealNetwork = (Properties.Settings.Default.Network == "Real"); // Set the bool = true if the setting is real network, false if test
            if (RealNetwork)
            {
                APIKey = Properties.Settings.Default.APIKey;
                APISecret = Properties.Settings.Default.APISecret;
            }
            else
            {
                APIKey = Properties.Settings.Default.TestAPIKey;
                APISecret = Properties.Settings.Default.TestAPISecret;
            }


            ddlCandleTimes.SelectedIndex = 0;

            // Spread Settings
            nudSpreadBuyOrderCount.Value = Properties.Settings.Default.SpreadBuyOrders;
            nudSpreadSellOrderCount.Value = Properties.Settings.Default.SpreadSellOrders;
            nudSpreadBuyValueApart.Value = Properties.Settings.Default.SpreadBuyValueApart;
            nudSpreadSellValueApart.Value = Properties.Settings.Default.SpreadSellValueApart;
            nudSpreadBuyContractsEach.Value = Properties.Settings.Default.SpreadBuyContractsEach;
            nudSpreadSellContractsEach.Value = Properties.Settings.Default.SpreadSellContractsEach;
            chkSpreadBuyReduceOnly.Checked = Properties.Settings.Default.SpreadBuyReduceOnly;
            chkSpreadSellReduceOnly.Checked = Properties.Settings.Default.SpreadSellReduceOnly;
            chkSpreadyBuyPostOnly.Checked = Properties.Settings.Default.SpreadBuyPostOnly;
            chkSpreadSellPostOnly.Checked = Properties.Settings.Default.SpreadSellPostOnly;
            chkSpreadBuyExecute.Checked = Properties.Settings.Default.SpreadBuyExecute;
            chkSpreadSellExecute.Checked = Properties.Settings.Default.SpreadSellExecute;
            chkSpreadCancelWhileOrdering.Checked = Properties.Settings.Default.SpreadCancelBeforeOrdering;


            // DCA Settings
            nudDCAContracts.Value = Properties.Settings.Default.DCAContracts;
            nudDCAHours.Value = Properties.Settings.Default.DCAHours;
            nudDCAMinutes.Value = Properties.Settings.Default.DCAMinutes;
            nudDCASeconds.Value = Properties.Settings.Default.DCASeconds;
            nudDCATimes.Value = Properties.Settings.Default.DCATimes;
            chkDCAReduceOnly.Checked = Properties.Settings.Default.DCAReduceOnly;

            // Setting Tab Settings
            chkSettingOverloadRetry.Checked = Properties.Settings.Default.OverloadRetry;
            nudSettingsOverloadRetryAttempts.Value = Properties.Settings.Default.OverloadRetryAttempts;
            nudSettingsRetryWaitTime.Value = Properties.Settings.Default.RetryAttemptWaitTime;

            // Manual Ordering Settings
            chkManualMarketBuyReduceOnly.Checked = Properties.Settings.Default.ManualMarketReduceOnly;
            //nudManualMarketBuyContracts.Value = Properties.Settings.Default.ManualMarketContracts; // Moved this to Post API settings, because intrument data is required.
            nudManualLimitContracts.Value = Properties.Settings.Default.ManualLimitContracts;
            nudManualLimitPrice.Value = Properties.Settings.Default.ManualLimitPrice;
            chkManualLimitReduceOnly.Checked = Properties.Settings.Default.ManualLimitReduceOnly;
            chkManualLimitPostOnly.Checked = Properties.Settings.Default.ManualLimitPostOnly;
            chkManualLimitCancelWhileOrdering.Checked = Properties.Settings.Default.ManualLimitCancelOpenOrders;
            chkManualLimitHiddenOrder.Checked = Properties.Settings.Default.ManualLimitHiddenOrder;
            nudManualLimitPercentModifier1.Value = Properties.Settings.Default.ManualLimitPercentModifier1;
            nudManualLimitPercentModifier2.Value = Properties.Settings.Default.ManualLimitPercentModifier2;
            nudManualLimitPercentModifier3.Value = Properties.Settings.Default.ManualLimitPercentModifier3;
            nudManualLimitPercentModifier4.Value = Properties.Settings.Default.ManualLimitPercentModifier4;
            chkManualLimitPercentModifierUseCurrentPrice.Checked = Properties.Settings.Default.ManualLimitPercentModifierUseCurrentPrice;

            // Limit Now
            nudLimitNowBuyContracts.Value = Properties.Settings.Default.LimitNowBuyContracts;
            nudLimitNowBuyTicksFromCenter.Value = Properties.Settings.Default.LimitNowBuyTicksFromCenter;
            nudLimitNowBuyDelay.Value = Properties.Settings.Default.LimitNowBuyDelay;
            chkLimitNowBuyContinue.Checked = Properties.Settings.Default.LimitNowBuyContinue;
            tmrLimitNowBuy.Interval = Properties.Settings.Default.LimitNowBuyDelay;
            tmrLimitNowSell.Interval = Properties.Settings.Default.LimitNowBuyDelay;
            nudLimitNowSellContracts.Value = Properties.Settings.Default.LimitNowSellContracts;
            nudLimitNowSellTicksFromCenter.Value = Properties.Settings.Default.LimitNowSellTicksFromCenter;
            nudLimitNowSellDelay.Value = Properties.Settings.Default.LimitNowSellDelay;
            chkLimitNowSellContinue.Checked = Properties.Settings.Default.LimitNowSellContinue;
            ddlLimitNowBuyMethod.SelectedItem = Properties.Settings.Default.LimitNowBuyMethod;
            ddlLimitNowSellMethod.SelectedItem = Properties.Settings.Default.LimitNowSellMethod;


            // Update other client items...
            lblVersion.Text = "v" + Version;
            UpdateDateAndTime();
        }

        private void InitializePostAPIDropdownsAndSettings()
        {
            // Manual Ordering Settings
            nudManualMarketBuyContracts.Value = Properties.Settings.Default.ManualMarketContracts;
        }

        private void InitializeAPI()
        {
            try
            {
                bitmex = new BitMEXApi(APIKey, APISecret, RealNetwork);

                // Show users what network they are on.
                if (RealNetwork)
                {
                    lblNetwork.Text = "Real";
                }
                else
                {
                    lblNetwork.Text = "Test";
                }

                // Get our balance
                UpdateBalance();

                // Start our HeartBeat
                Heartbeat.Start();
            }
            catch (Exception ex)
            {
            }
        }

        private void InitializeSymbolInformation()
        {
            ActiveInstruments = bitmex.GetActiveInstruments().OrderByDescending(a => a.Volume24H).ToList();
            // Assemble our price dictionary
            foreach (Instrument i in ActiveInstruments)
            {
                Prices.Add(i.Symbol, 0); // just setting up the item, 0 is fine here.
            }
        }

        private void InitializeDependentSymbolInformation()
        {
            ddlSymbol.DataSource = ActiveInstruments;
            ddlSymbol.DisplayMember = "Symbol";
            ddlSymbol.SelectedIndex = 0;
            ActiveInstrument = ActiveInstruments[0];

            UpdatePositionInfo();
            InitializeSymbolSpecificData(true);
        }
        #endregion

        #region General Tools

        private void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        private void pbxYouTubeSubscribe_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.youtube.com/BigBits?sub_confirmation=1");
        }

        private void lblDonate_Click(object sender, EventArgs e)
        {
            TabControl.SelectTab("tabDonate");
        }

        private void UpdatePrice()
        {
            nudCurrentPrice.Value = Prices[ActiveInstrument.Symbol];
        }

        private void tmrClientUpdates_Tick(object sender, EventArgs e)
        {
            UpdatePrice();
            UpdateManualMarketBuyButtons();  // Update our buy buttons on manual market buys
            //TriggerAlerts();
        }
        #endregion

        #region Symbol And Time Frame Tools
        private void ddlSymbol_SelectedIndexChanged(object sender, EventArgs e)
        {

            InitializeSymbolSpecificData();


        }

        private void UpdateFormsForTickSize(decimal TickSize, int Decimals)
        {
            nudPositionLimitPrice.DecimalPlaces = Decimals;
            nudPositionLimitPrice.Increment = TickSize;

            nudSpreadBuyValueApart.DecimalPlaces = Decimals;
            nudSpreadBuyValueApart.Increment = TickSize;

            nudSpreadSellValueApart.DecimalPlaces = Decimals;
            nudSpreadSellValueApart.Increment = TickSize;

            nudCurrentPrice.DecimalPlaces = Decimals;
            nudCurrentPrice.Increment = TickSize;
            nudCurrentPrice.Controls[0].Enabled = false;

            nudManualLimitPrice.DecimalPlaces = Decimals;
            nudManualLimitPrice.Increment = TickSize;
        }

        private void ddlCandleTimes_SelectedIndexChanged(object sender, EventArgs e)
        {
            Timeframe = ddlCandleTimes.SelectedItem.ToString();
        }

        private void UpdateBalance()
        {
            lblBalance.Text = "Balance: " + bitmex.GetAccountBalance().ToString();
        }

        private void Heartbeat_Tick(object sender, EventArgs e)
        {
            if (DateTime.UtcNow.Second == 0)
            {
                //Update our balance each minute
                UpdateBalance();
                UpdatePositionInfo();
            }

            // Update the time every second.
            UpdateDateAndTime();


        }

        private void UpdateDateAndTime()
        {
            lblUTCTime.Text = DateTime.UtcNow.ToShortDateString() + " " + DateTime.UtcNow.ToLongTimeString();
        }
        #endregion

        #region DCA
        private void UpdateDCASummary()
        {
            DCAContractsPer = Convert.ToInt32(nudDCAContracts.Value);
            DCAHours = Convert.ToInt32(nudDCAHours.Value);
            DCAMinutes = Convert.ToInt32(nudDCAMinutes.Value);
            DCASeconds = Convert.ToInt32(nudDCASeconds.Value);
            DCATimes = Convert.ToInt32(nudDCATimes.Value);

            DateTime Start = DateTime.UtcNow;
            DateTime End = new DateTime();
            if (chkDCAExecuteImmediately.Checked)
            {
                End = DateTime.UtcNow.AddHours(DCAHours * (DCATimes - 1)).AddMinutes(DCAMinutes * (DCATimes - 1)).AddSeconds(DCASeconds * (DCATimes - 1));
            }
            else
            {
                End = DateTime.UtcNow.AddHours(DCAHours * DCATimes).AddMinutes(DCAMinutes * DCATimes).AddSeconds(DCASeconds * DCATimes);
            }
            TimeSpan Duration = End - Start;

            if (Duration.TotalMinutes < 1.0)
            {
                lblDCASummary.Text = (DCAContractsPer * DCATimes).ToString() + " Contracts over " + DCATimes.ToString() + " orders during a total of " + Duration.Seconds.ToString() + " seconds.";
            }
            else if (Duration.TotalHours < 1.0)
            {
                lblDCASummary.Text = (DCAContractsPer * DCATimes).ToString() + " Contracts over " + DCATimes.ToString() + " orders during a total of " + Duration.Minutes.ToString() + " minutes " + Duration.Seconds.ToString() + " seconds.";
            }
            else
            {
                lblDCASummary.Text = (DCAContractsPer * DCATimes).ToString() + " Contracts over " + DCATimes.ToString() + " orders during a total of " + ((int)Math.Floor(Duration.TotalHours)).ToString() + " hours " + Duration.Minutes.ToString() + " minutes " + Duration.Seconds.ToString() + " seconds.";
            }



        }

        private void nudDCAContracts_ValueChanged(object sender, EventArgs e)
        {
            DCAContractsPer = Convert.ToInt32(nudDCAContracts.Value);
            Properties.Settings.Default.DCAContracts = DCAContractsPer;
            SaveSettings();
            UpdateDCASummary();
        }

        private void nudDCAHours_ValueChanged(object sender, EventArgs e)
        {
            DCAHours = Convert.ToInt32(nudDCAHours.Value);
            Properties.Settings.Default.DCAHours = DCAHours;
            SaveSettings();
            UpdateDCASummary();
        }

        private void nudDCAMinutes_ValueChanged(object sender, EventArgs e)
        {
            DCAMinutes = Convert.ToInt32(nudDCAMinutes.Value);
            Properties.Settings.Default.DCAMinutes = DCAMinutes;
            SaveSettings();
            UpdateDCASummary();
        }

        private void nudDCASeconds_ValueChanged(object sender, EventArgs e)
        {
            DCASeconds = Convert.ToInt32(nudDCASeconds.Value);
            Properties.Settings.Default.DCASeconds = DCASeconds;
            SaveSettings();
            UpdateDCASummary();
        }

        private void nudDCATimes_ValueChanged(object sender, EventArgs e)
        {
            DCATimes = Convert.ToInt32(nudDCATimes.Value);
            Properties.Settings.Default.DCATimes = DCATimes;
            SaveSettings();
            UpdateDCASummary();
        }

        private void chkDCAReduceOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.DCAReduceOnly = chkDCAReduceOnly.Checked;
            SaveSettings();
        }

        private void chkDCAExecuteImmediately_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.DCAExecuteImmediately = chkDCAExecuteImmediately.Checked;
            SaveSettings();
            UpdateDCASummary();
        }

        private void btnDCABuy_Click(object sender, EventArgs e)
        {
            UpdateDCASummary(); // Makes sure our variables are current.

            DCASide = "Buy";

            tmrDCA.Interval = (DCASeconds * 1000) + (DCAMinutes * 60 * 1000) + (DCAHours * 60 * 60 * 1000); // Must multiply by 1000, because timers operate in milliseconds.
            tmrDCA.Start(); // Start the timer.
            pgbDCA.Value = 0;
            LockDCA();
            if (chkDCAExecuteImmediately.Checked)
            {
                DCAAction();
            }
        }

        private void btnDCASell_Click(object sender, EventArgs e)
        {
            UpdateDCASummary(); // Makes sure our variables are current.

            DCASide = "Sell";

            tmrDCA.Interval = (DCASeconds * 1000) + (DCAMinutes * 60 * 1000) + (DCAHours * 60 * 60 * 1000); // Must multiply by 1000, because timers operate in milliseconds.
            tmrDCA.Start(); // Start the timer.
            pgbDCA.Value = 0;
            LockDCA();
            if (chkDCAExecuteImmediately.Checked)
            {
                DCAAction();
            }
        }

        private void btnDCAStop_Click(object sender, EventArgs e)
        {
            DCACounter = 0;
            pgbDCA.Value = 0;
            tmrDCA.Stop();
            LockDCA(false);
        }

        private void tmrDCA_Tick(object sender, EventArgs e)
        {
            DCAAction();
        }

        private void DCAAction()
        {
            DCACounter++;
            bitmex.MarketOrder(ActiveInstrument.Symbol, DCASide, DCAContractsPer, chkDCAReduceOnly.Checked);

            double Percent = ((double)DCACounter / (double)DCATimes) * 100;
            pgbDCA.Value = Convert.ToInt32(Math.Round(Percent));

            if (DCACounter == DCATimes)
            {
                DCACounter = 0;
                tmrDCA.Stop();
                pgbDCA.Value = 0;
                LockDCA(false);

            }
        }

        private void LockDCA(bool Lock = true)
        {
            nudDCAContracts.Enabled = !Lock;
            nudDCAHours.Enabled = !Lock;
            nudDCAMinutes.Enabled = !Lock;
            nudDCASeconds.Enabled = !Lock;
            nudDCATimes.Enabled = !Lock;
            pgbDCA.Visible = Lock;
            btnDCABuy.Visible = !Lock;
            btnDCASell.Visible = !Lock;
            btnDCAStop.Visible = Lock;
            chkDCAReduceOnly.Enabled = !Lock;
            chkDCAExecuteImmediately.Enabled = !Lock;
        }

        #endregion

        #region Position Manager
        private void UpdatePositionInfo()
        {
            nudPositionLimitPrice.Increment = ActiveInstrument.TickSize;
            List<Position> Positions = bitmex.GetOpenPositions(ActiveInstrument.Symbol);
            if (Positions.Any())
            {
                gbxPosition.Visible = true;
                txtPositionSize.Text = Positions[0].CurrentQty.ToString();
                txtPositionEntryPrice.Text = Positions[0].AvgEntryPrice.ToString();
                txtPositionMarkPrice.Text = Positions[0].MarkPrice.ToString();
                txtPositionLiquidation.Text = Positions[0].LiquidationPrice.ToString();
                txtPositionMargin.Text = Positions[0].Leverage.ToString();
                txtPositionUnrealizedPnL.Text = Positions[0].UsefulUnrealisedPnl.ToString();
                txtPositionUnrealizedPnLPercent.Text = Positions[0].UnrealisedPnlPcnt.ToString() + "%";
                if (nudPositionLimitPrice.Value == 0m) // Only updates when default value is present
                {
                    nudPositionLimitPrice.Value = Convert.ToDecimal(((int)Math.Floor((double)Positions[0].MarkPrice)).ToString() + ".0");
                }

            }
            else
            {
                gbxPosition.Visible = false;
            }

        }

        private void btnPositionMarketClose_Click(object sender, EventArgs e)
        {
            UpdatePositionInfo(); // Make sure info is up to date as possible.

            int Size = Convert.ToInt32(txtPositionSize.Text);
            string Side = "Buy";

            if (Size < 0) // We are short
            {
                Side = "Buy";
                Size = (int)Math.Abs((decimal)Size); // Makes sure size is positive number
            }
            else if (Size > 0)
            {
                Side = "Sell";
                Size = (int)Math.Abs((decimal)Size); // Makes sure size is positive number
            }
            bitmex.MarketOrder(ActiveInstrument.Symbol, Side, Size, true);
            UpdatePositionInfo(); // Update our position information again.
        }

        private void btnPositionLimitClose_Click(object sender, EventArgs e)
        {
            try
            {
                decimal Price = nudPositionLimitPrice.Value;

                // We have entered a valid price
                int Size = Convert.ToInt32(txtPositionSize.Text);
                string Side = "Buy";

                if (Size < 0) // We are short
                {
                    Side = "Buy";
                    Size = (int)Math.Abs((decimal)Size); // Makes sure size is positive number
                }
                else if (Size > 0)
                {
                    Side = "Sell";
                    Size = (int)Math.Abs((decimal)Size); // Makes sure size is positive number
                }
                bitmex.LimitOrder(ActiveInstrument.Symbol, Side, Size, Price, true);

                UpdatePositionInfo(); // Make sure info is up to date as possible.



            }
            catch (Exception ex)
            {

            }

        }

        private void btnPositionMargin_Click(object sender, EventArgs e)
        {
            bitmex.ChangeMargin(ActiveInstrument.Symbol, nudPositionMargin.Value);
            UpdatePositionInfo();
        }
        #endregion

        #region Settings Tab
        private void chkSettingOverloadRetry_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.OverloadRetry = chkSettingOverloadRetry.Checked;
            SaveSettings();
        }

        private void nudSettingsOverloadRetryAttempts_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.OverloadRetryAttempts = (int)nudSettingsOverloadRetryAttempts.Value;
            SaveSettings();
        }

        private void nudSettingsRetryWaitTime_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.RetryAttemptWaitTime = (int)nudSettingsRetryWaitTime.Value;
            SaveSettings();
        }
        #endregion

        #region Spread Orders

        private void nudSpreadBuyOrderCount_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadBuyOrders = (int)nudSpreadBuyOrderCount.Value;
            SaveSettings();
        }

        private void nudSpreadSellOrderCount_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadSellOrders = (int)nudSpreadSellOrderCount.Value;
            SaveSettings();
        }

        private void nudSpreadBuyValueApart_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadBuyValueApart = nudSpreadBuyValueApart.Value;
            SaveSettings();
        }

        private void nudSpreadSellValueApart_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadSellValueApart = nudSpreadSellValueApart.Value;
            SaveSettings();
        }

        private void nudSpreadBuyContractsEach_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadBuyContractsEach = (int)nudSpreadBuyContractsEach.Value;
            SaveSettings();
        }

        private void nudSpreadSellContractsEach_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadSellContractsEach = (int)nudSpreadSellContractsEach.Value;
            SaveSettings();
        }

        private void chkSpreadBuyReduceOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadBuyReduceOnly = chkSpreadBuyReduceOnly.Checked;
            SaveSettings();
        }

        private void chkSpreadSellReduceOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadSellReduceOnly = chkSpreadSellReduceOnly.Checked;
            SaveSettings();
        }

        private void chkSpreadyBuyPostOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadBuyPostOnly = chkSpreadyBuyPostOnly.Checked;
            SaveSettings();
        }

        private void chkSpreadSellPostOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadSellPostOnly = chkSpreadSellPostOnly.Checked;
            SaveSettings();
        }

        private void chkSpreadBuyExecute_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadBuyExecute = chkSpreadBuyExecute.Checked;
            SaveSettings();
        }

        private void chkSpreadSellExecute_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadSellExecute = chkSpreadSellExecute.Checked;
            SaveSettings();
        }

        private void chkSpreadCancelWhileOrdering_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SpreadCancelBeforeOrdering = chkSpreadCancelWhileOrdering.Checked;
            SaveSettings();
        }

        private void btnSpreadCloseAllOpenOrders_Click(object sender, EventArgs e)
        {
            bitmex.CancelAllOpenOrders(ActiveInstrument.Symbol);
        }

        private void btnSpreadPlaceOrders_Click(object sender, EventArgs e)
        {
            // do our logic for creating a bulk order to submit
            List<Order> BulkOrders = new List<Order>();

            // Step 1, see if we need to cancel all open orders and do it if so
            if (chkSpreadCancelWhileOrdering.Checked)
            {
                // Cancel all open orders
                bitmex.CancelAllOpenOrders(ActiveInstrument.Symbol);
            }

            // Step 2, check to see if we even need to bother building buy or sell orders
            // Step 3, if we do, respectively create each individual order necessary based on settings logic
            decimal CurrentPrice = bitmex.GetCurrentPrice(ActiveInstrument.Symbol);

            if (chkSpreadBuyExecute.Checked)
            {
                // build our buy orders
                for (int i = 1; i <= (int)nudSpreadBuyOrderCount.Value; i++)
                {
                    Order o = new Order();
                    o.Side = "Buy";
                    o.OrderQty = (int?)nudSpreadBuyContractsEach.Value;
                    o.Symbol = ActiveInstrument.Symbol;
                    o.Price = (double?)(CurrentPrice - (nudSpreadBuyValueApart.Value * i));
                    if (chkSpreadBuyReduceOnly.Checked && chkSpreadyBuyPostOnly.Checked)
                    {
                        o.ExecInst = "ReduceOnly,ParticipateDoNotInitiate";
                    }
                    else if (!chkSpreadBuyReduceOnly.Checked && chkSpreadyBuyPostOnly.Checked)
                    {
                        o.ExecInst = "ParticipateDoNotInitiate";
                    }
                    else if (chkSpreadBuyReduceOnly.Checked && !chkSpreadyBuyPostOnly.Checked)
                    {
                        o.ExecInst = "ReduceOnly";
                    }
                    BulkOrders.Add(o);
                }
            }
            if (chkSpreadSellExecute.Checked)
            {
                // build our sell orders
                for (int i = 1; i <= (int)nudSpreadSellOrderCount.Value; i++)
                {
                    Order o = new Order();
                    o.Side = "Sell";
                    o.OrderQty = (int?)nudSpreadSellContractsEach.Value;
                    o.Symbol = ActiveInstrument.Symbol;
                    o.Price = (double?)(CurrentPrice + (nudSpreadSellValueApart.Value * i));
                    if (chkSpreadSellReduceOnly.Checked && chkSpreadSellPostOnly.Checked)
                    {
                        o.ExecInst = "ReduceOnly,ParticipateDoNotInitiate";
                    }
                    else if (!chkSpreadSellReduceOnly.Checked && chkSpreadSellPostOnly.Checked)
                    {
                        o.ExecInst = "ParticipateDoNotInitiate";
                    }
                    else if (chkSpreadSellReduceOnly.Checked && !chkSpreadSellPostOnly.Checked)
                    {
                        o.ExecInst = "ReduceOnly";
                    }
                    BulkOrders.Add(o);
                }
            }

            // Step 4, call the bulk order submit button
            string BulkOrderString = BuildBulkOrder(BulkOrders);
            bitmex.BulkOrder(BulkOrderString);

        }

        private string BuildBulkOrder(List<Order> Orders, bool Amend = false)
        {
            StringBuilder str = new StringBuilder();

            str.Append("[");

            int i = 1;
            foreach (Order o in Orders)
            {
                if (i > 1)
                {
                    str.Append(", ");
                }
                str.Append("{");
                if (Amend == true)
                {
                    str.Append("\"orderID\": \"" + o.OrderId.ToString() + "\", ");
                }
                str.Append("\"orderQty\": " + o.OrderQty.ToString() + ", \"price\": " + o.Price.ToString() + ", \"side\": \"" + o.Side + "\", \"symbol\": \"" + o.Symbol + "\"");
                if (o.ExecInst.Trim() != "")
                {
                    str.Append(", \"execInst\": \"" + o.ExecInst + "\"");
                }
                str.Append("}");
                i++;
            }

            str.Append("]");

            return str.ToString();
        }

        #endregion

        #region Export Candles
        private void ExportCandleData()
        {
            // First see if we have the file we want where we want it. To do that, we need to get the filepath to our app folder in my documents
            string path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); // We are working in My Documents.
            if (!Directory.Exists(path + "\\BitMEXAssistant"))
            {
                // If our Kizashi Logs folder doesn't exist, create it.
                Directory.CreateDirectory(path + "\\BitMEXAssistant");
            }

            // Optionally, you could loop through all symbols and timeframes to get all files at once here
            string ourfilepath = Path.Combine(path, "BitMEXAssistant", "Assistant" + ActiveInstrument.Symbol + Timeframe + "CandleHistory.csv");
            // Get candle info, and Account balance
            if (!File.Exists(ourfilepath))
            {
                // If our files doesn't exist, we'll creat it now with the stream writer
                using (StreamWriter write = new StreamWriter(ourfilepath))
                {
                    CsvWriter csw = new CsvWriter(write);

                    csw.WriteHeader<SimpleCandle>(); // writes the csv header for this class
                    csw.NextRecord();

                    // loop through all candles, add those items to the csv while we are getting 500 candles (full datasets)
                    List<SimpleCandle> Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe).Where(a => a.Trades > 0).ToList();
                    while (Candles.Count > 0)
                    {

                        csw.WriteRecords(Candles);

                        // Get candles with a start time of the last candle plus 1 min
                        switch (Timeframe)
                        {
                            case "1m":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(1)).Where(a => a.Trades > 0).ToList();
                                break;
                            case "5m":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(5)).Where(a => a.Trades > 0).ToList();
                                break;
                            case "1h":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddHours(1)).Where(a => a.Trades > 0).ToList();
                                break;
                            case "1d":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddDays(1)).Where(a => a.Trades > 0).ToList();
                                break;
                            default:
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(1)).Where(a => a.Trades > 0).ToList();
                                break;
                        }

                        // Lets sleep for a bit, 5 seconds, don't want to get rate limited
                        Thread.Sleep(2500);
                    }

                }
            }
            else
            {
                // our file exists, let read existing contents, add them back in, with the new candles.
                string ourtemppath = Path.Combine(path, "BitMEXAssistant", "Assistant" + ActiveInstrument.Symbol + Timeframe + "CandleHistory.csv");
                // Open our file, and append data to it.
                using (StreamReader reader = new StreamReader(ourfilepath))
                {
                    using (StreamWriter write = new StreamWriter(ourtemppath))
                    {
                        CsvWriter csw = new CsvWriter(write);
                        CsvReader csr = new CsvReader(reader);

                        // Recreate existing records, then add new ones.
                        List<SimpleCandle> records = csr.GetRecords<SimpleCandle>().ToList();

                        csw.WriteRecords(records);

                        // Now to any new since the most recent record
                        List<SimpleCandle> Candles = new List<SimpleCandle>();
                        // Get candles with a start time of the last candle plus 1 min
                        switch (Timeframe)
                        {
                            case "1m":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, records.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(1)).Where(a => a.Trades > 0).ToList();
                                break;
                            case "5m":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, records.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(5)).Where(a => a.Trades > 0).ToList();
                                break;
                            case "1h":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, records.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddHours(1)).Where(a => a.Trades > 0).ToList();
                                break;
                            case "1d":
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, records.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddDays(1)).Where(a => a.Trades > 0).ToList();
                                break;
                            default:
                                Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, records.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(1)).Where(a => a.Trades > 0).ToList();
                                break;
                        }

                        // loop through all candles, add those items to the csv while we are getting 500 candles (full datasets)

                        while (Candles.Count > 0)
                        {

                            csw.WriteRecords(Candles);

                            // Get candles with a start time of the last candle plus 1 min
                            switch (Timeframe)
                            {
                                case "1m":
                                    Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(1)).Where(a => a.Trades > 0).ToList();
                                    break;
                                case "5m":
                                    Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(5)).Where(a => a.Trades > 0).ToList();
                                    break;
                                case "1h":
                                    Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddHours(1)).Where(a => a.Trades > 0).ToList();
                                    break;
                                case "1d":
                                    Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddDays(1)).Where(a => a.Trades > 0).ToList();
                                    break;
                                default:
                                    Candles = GetSimpleCandles(ActiveInstrument.Symbol, Timeframe, Candles.OrderByDescending(a => a.TimeStamp).FirstOrDefault().TimeStamp.AddMinutes(1)).Where(a => a.Trades > 0).ToList();
                                    break;
                            }

                            // Lets sleep for a bit, 5 seconds, don't want to get rate limited
                            Thread.Sleep(2500);
                        }

                    }
                }

                File.Delete(ourfilepath);
                File.Copy(ourtemppath, ourfilepath);
                File.Delete(ourtemppath);
            }


        }

        private List<SimpleCandle> GetSimpleCandles(string Symbol, string Timeframe, DateTime Start = new DateTime())
        {
            List<SimpleCandle> Candles = new List<SimpleCandle>();
            if (Start != new DateTime())
            {
                Candles = bitmex.GetCandleHistory(Symbol, Timeframe, Start);
            }
            else
            {
                Candles = bitmex.GetCandleHistory(Symbol, Timeframe);
            }

            return Candles;
        }

        private void btnExportCandles_Click(object sender, EventArgs e)
        {
            ExportCandleData();
        }
        #endregion

        #region Manual Ordering

        private void UpdateManualMarketBuyButtons()
        {
            btnManualMarketBuy.Text = "Market Buy" + Environment.NewLine + ((int)nudManualMarketBuyContracts.Value).ToString() + " @" + nudCurrentPrice.Value.ToString("F" + ActiveInstrument.DecimalPlacesInTickSize.ToString());
            btnManualMarketSell.Text = "Market Sell" + Environment.NewLine + ((int)nudManualMarketBuyContracts.Value).ToString() + " @" + nudCurrentPrice.Value.ToString("F" + ActiveInstrument.DecimalPlacesInTickSize.ToString());
        }

        private void btnManualMarketBuy_Click(object sender, EventArgs e)
        {
            bitmex.MarketOrder(ActiveInstrument.Symbol, "Buy", (int)nudManualMarketBuyContracts.Value, chkManualMarketBuyReduceOnly.Checked);
        }

        private void btnManualMarketSell_Click(object sender, EventArgs e)
        {
            bitmex.MarketOrder(ActiveInstrument.Symbol, "Sell", (int)nudManualMarketBuyContracts.Value, chkManualMarketBuyReduceOnly.Checked);
        }

        private void nudManualMarketBuyContracts_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualMarketContracts = (int)nudManualMarketBuyContracts.Value;
            SaveSettings();
            UpdateManualMarketBuyButtons();
        }

        private void chkManualMarketBuyReduceOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualMarketReduceOnly = chkManualMarketBuyReduceOnly.Checked;
            SaveSettings();
        }

        private void btnManualLimitSetCurrentPrice_Click(object sender, EventArgs e)
        {
            nudManualLimitPrice.Value = Prices[ActiveInstrument.Symbol];
        }

        private void nudManualLimitContracts_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitContracts = (int)nudManualLimitContracts.Value;
            SaveSettings();
        }

        private void nudManualLimitPrice_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitPrice = nudManualLimitPrice.Value;
            SaveSettings();
        }

        private void chkManualLimitReduceOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitReduceOnly = chkManualLimitReduceOnly.Checked;
            SaveSettings();
        }

        private void chkManualLimitPostOnly_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitPostOnly = chkManualLimitPostOnly.Checked;
            SaveSettings();
        }

        private void chkManualLimitCancelWhileOrdering_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitCancelOpenOrders = chkManualLimitCancelWhileOrdering.Checked;
            SaveSettings();
        }

        private void btnManualLimitBuy_Click(object sender, EventArgs e)
        {
            if (chkManualLimitCancelWhileOrdering.Checked)
            {
                bitmex.CancelAllOpenOrders(ActiveInstrument.Symbol);
            }
            bitmex.LimitOrder(ActiveInstrument.Symbol, "Buy", (int)nudManualLimitContracts.Value, nudManualLimitPrice.Value, chkManualLimitReduceOnly.Checked, chkManualLimitPostOnly.Checked, chkManualLimitHiddenOrder.Checked);

        }

        private void btnManualLimitSell_Click(object sender, EventArgs e)
        {
            if (chkManualLimitCancelWhileOrdering.Checked)
            {
                bitmex.CancelAllOpenOrders(ActiveInstrument.Symbol);
            }
            bitmex.LimitOrder(ActiveInstrument.Symbol, "Sell", (int)nudManualLimitContracts.Value, nudManualLimitPrice.Value, chkManualLimitReduceOnly.Checked, chkManualLimitPostOnly.Checked, chkManualLimitHiddenOrder.Checked);

        }

        private void chkManualLimitHiddenOrder_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitHiddenOrder = chkManualLimitHiddenOrder.Checked;
            SaveSettings();
        }

        private void nudManualLimitPercentModifier1_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitPercentModifier1 = nudManualLimitPercentModifier1.Value;
            SaveSettings();
        }

        private void nudManualLimitPercentModifier2_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitPercentModifier2 = nudManualLimitPercentModifier2.Value;
            SaveSettings();
        }

        private void nudManualLimitPercentModifier3_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitPercentModifier3 = nudManualLimitPercentModifier3.Value;
            SaveSettings();
        }

        private void nudManualLimitPercentModifier4_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitPercentModifier4 = nudManualLimitPercentModifier4.Value;
            SaveSettings();
        }

        private decimal PercentageChange(bool Increase, decimal Base, decimal Change, decimal TickSize)
        {
            decimal Result = 0;
            decimal Adjustment = Base * Change;

            if (Increase)
            {
                // increase
                Result = Base + Adjustment;

            }
            else
            {
                // decrease
                Result = Base - Adjustment;
            }

            decimal Remainder = Result % TickSize;
            Result = Result - Remainder; // Remove any remainder to avoid issues.

            return Result;
        }

        private void UpdateManualLimitPriceFromPercentModifier(bool Increase, decimal Change)
        {
            Change = Change / 100; // Values are shown as %s, so must divide by 100

            if (chkManualLimitPercentModifierUseCurrentPrice.Checked)
            {
                nudManualLimitPrice.Value = PercentageChange(Increase, nudCurrentPrice.Value, Change, ActiveInstrument.TickSize);
            }
            else
            {
                nudManualLimitPrice.Value = PercentageChange(Increase, nudManualLimitPrice.Value, Change, ActiveInstrument.TickSize);
            }
        }

        private void btnManualLimitPercentModifier1Down_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(false, nudManualLimitPercentModifier1.Value);
        }

        private void btnManualLimitPercentModifier1Up_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(true, nudManualLimitPercentModifier1.Value);
        }

        private void btnManualLimitPercentModifier2Down_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(false, nudManualLimitPercentModifier2.Value);
        }

        private void btnManualLimitPercentModifier2Up_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(true, nudManualLimitPercentModifier2.Value);
        }

        private void btnManualLimitPercentModifier3Down_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(false, nudManualLimitPercentModifier3.Value);
        }

        private void btnManualLimitPercentModifier3Up_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(true, nudManualLimitPercentModifier3.Value);
        }

        private void btnManualLimitPercentModifier4Down_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(false, nudManualLimitPercentModifier4.Value);
        }

        private void btnManualLimitPercentModifier4Up_Click(object sender, EventArgs e)
        {
            UpdateManualLimitPriceFromPercentModifier(true, nudManualLimitPercentModifier4.Value);
        }

        private void chkManualLimitPercentModifierUseCurrentPrice_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ManualLimitPercentModifierUseCurrentPrice = chkManualLimitPercentModifierUseCurrentPrice.Checked;
            SaveSettings();
        }

        private void btnManualLimitCancelOpenOrders_Click(object sender, EventArgs e)
        {
            bitmex.CancelAllOpenOrders(ActiveInstrument.Symbol);
        }

        #endregion

        #region Limit Now
        private void nudLimitNowBuyContracts_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowBuyContracts = (int)nudLimitNowBuyContracts.Value;
            SaveSettings();
        }

        private void btnLimitNowBuy_Click(object sender, EventArgs e)
        {
            LimitNowStartBuying();
        }

        private void btnLimitNowSell_Click(object sender, EventArgs e)
        {
            LimitNowStartSelling();
        }

        private void tmrLimitNowBuy_Tick(object sender, EventArgs e)
        {
            List<Order> LimitNowOrderResult = LimitNowAmendBuying();

            // Timer should stop if there are no orders left to amend.
            if (LimitNowOrderResult.Any())
            {
                if (LimitNowOrderResult.FirstOrDefault().OrdStatus == "Filled")
                {
                    LimitNowStopBuying();

                }
            }
            else
            {
                // Order no longer available, stop it
                LimitNowStopBuying();
            }
        }

        private List<Order> LimitNowStartBuying()
        {
            // Initial order
            decimal Price = LimitNowGetOrderPrice("Buy");
            List<Order> LimitNowOrderResult = bitmex.LimitNowOrder(ActiveInstrument.Symbol, "Buy", (int)nudLimitNowBuyContracts.Value, Price, false, true, false);
            btnLimitNowBuyCancel.Visible = true;
            btnLimitNowBuy.Visible = false;

            if (LimitNowOrderResult.Any())
            {
                // Start buy timer
                LimitNowBuyOrderId = LimitNowOrderResult.FirstOrDefault().OrderId;
                LimitNowBuyOrderPrice = Price;
                tmrLimitNowBuy.Start();
            }

            return LimitNowOrderResult;
        }

        private List<Order> LimitNowAmendBuying()
        {
            decimal Price = LimitNowGetOrderPrice("Buy");
            int Contracts = (int)nudLimitNowBuyContracts.Value;
            List<Order> LimitNowOrderResult = new List<Order>();
            if (Price != 0)
            {
                LimitNowOrderResult = bitmex.LimitNowAmendOrder(LimitNowBuyOrderId, Price, Contracts);
                LimitNowBuyOrderPrice = Price;
            }
            else
            {
                LimitNowOrderResult.Add(new Order());
            }
            return LimitNowOrderResult;


        }

        private List<Order> LimitNowAmendSelling()
        {
            decimal Price = LimitNowGetOrderPrice("Sell");
            int Contracts = (int)nudLimitNowSellContracts.Value;
            List<Order> LimitNowOrderResult = new List<Order>();



            if (Price != 0)
            {
                LimitNowOrderResult = bitmex.LimitNowAmendOrder(LimitNowSellOrderId, Price, Contracts);
                LimitNowSellOrderPrice = Price;
            }
            else
            {
                LimitNowOrderResult.Add(new Order());
            }
            return LimitNowOrderResult;

        }

        private void tmrLimitNowSell_Tick(object sender, EventArgs e)
        {
            List<Order> LimitNowOrderResult = LimitNowAmendSelling();

            // Timer should stop if there are no orders left to amend.
            if (LimitNowOrderResult.Any())
            {
                if (LimitNowOrderResult.FirstOrDefault().OrdStatus == "Filled")
                {
                    LimitNowStopSelling();

                }
            }
            else
            {
                // Order no longer available, stop it
                LimitNowStopSelling();
            }
        }

        private List<Order> LimitNowStartSelling()
        {

            // Initial order
            decimal Price = LimitNowGetOrderPrice("Sell");
            List <Order> LimitNowOrderResult = bitmex.LimitNowOrder(ActiveInstrument.Symbol, "Sell", (int)nudLimitNowSellContracts.Value, Price, false, true, false);
            btnLimitNowSellCancel.Visible = true;
            btnLimitNowSell.Visible = false;

            if (LimitNowOrderResult.Any())
            {
                // Start buy timer
                LimitNowSellOrderId = LimitNowOrderResult.FirstOrDefault().OrderId;
                LimitNowSellOrderPrice = Price;
                tmrLimitNowSell.Start();
            }

            return LimitNowOrderResult;
        }

        private void nudLimitNowBuyTicksFromCenter_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowBuyTicksFromCenter = (int)nudLimitNowBuyTicksFromCenter.Value;
            SaveSettings();
        }

        private void nudLimitNowBuyDelay_ValueChanged(object sender, EventArgs e)
        {
            tmrLimitNowBuy.Interval = (int)nudLimitNowBuyDelay.Value;

            Properties.Settings.Default.LimitNowBuyDelay = (int)nudLimitNowBuyDelay.Value;
            SaveSettings();
        }

        private void btnLimitNowBuyCancel_Click(object sender, EventArgs e)
        {
            bitmex.CancelOrder(LimitNowBuyOrderId);
            chkLimitNowBuyContinue.Checked = false;
            LimitNowStopBuying();
        }

        private void btnLimitNowSellCancel_Click(object sender, EventArgs e)
        {
            bitmex.CancelOrder(LimitNowSellOrderId);
            chkLimitNowSellContinue.Checked = false;
            LimitNowStopSelling();
        }

        private void LimitNowStopBuying()
        {
            tmrLimitNowBuy.Stop();

            btnLimitNowBuyCancel.Visible = false;
            btnLimitNowBuy.Visible = true;
            LimitNowBuyOrderId = "";

            if (chkLimitNowBuyContinue.Checked)
            {
                LimitNowStartBuying();
            }
        }

        private void LimitNowStopSelling()
        {
            tmrLimitNowSell.Stop();

            btnLimitNowSellCancel.Visible = false;
            btnLimitNowSell.Visible = true;
            LimitNowSellOrderId = "";

            if (chkLimitNowSellContinue.Checked)
            {
                LimitNowStartSelling();
            }
        }

        private void chkLimitNowBuyContinue_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowBuyContinue = chkLimitNowBuyContinue.Checked;
            SaveSettings();
        }

        private void nudLimitNowSellContracts_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowSellContracts = (int)nudLimitNowSellContracts.Value;
            SaveSettings();
        }

        private void nudLimitNowSellTicksFromCenter_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowSellTicksFromCenter = (int)nudLimitNowSellTicksFromCenter.Value;
            SaveSettings();
        }

        private void nudLimitNowSellDelay_ValueChanged(object sender, EventArgs e)
        {
            tmrLimitNowSell.Interval = (int)nudLimitNowSellDelay.Value;

            Properties.Settings.Default.LimitNowSellDelay = (int)nudLimitNowSellDelay.Value;
            SaveSettings();
        }

        private void chkLimitNowSellContinue_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowSellContinue = chkLimitNowSellContinue.Checked;
            SaveSettings();
        }

        private void ddlLimitNowBuyMethod_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowBuyMethod = (string)ddlLimitNowBuyMethod.SelectedItem;
            SaveSettings();
            if ((string)ddlLimitNowBuyMethod.SelectedItem == "Best Price")
            {
                nudLimitNowBuyTicksFromCenter.Enabled = false;
            }
            else if ((string)ddlLimitNowBuyMethod.SelectedItem == "Quick Fill")
            {
                nudLimitNowBuyTicksFromCenter.Enabled = true;
            }

        }

        private void ddlLimitNowSellMethod_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LimitNowSellMethod = (string)ddlLimitNowSellMethod.SelectedItem;
            SaveSettings();
            if ((string)ddlLimitNowSellMethod.SelectedItem == "Best Price")
            {
                nudLimitNowSellTicksFromCenter.Enabled = false;
            }
            else if ((string)ddlLimitNowSellMethod.SelectedItem == "Quick Fill")
            {
                nudLimitNowSellTicksFromCenter.Enabled = true;
            }

        }

        private decimal LimitNowGetOrderPrice(string Side)
        {
            decimal Price = 0;
            try
            {
                switch (Side)
                {
                    case "Buy":
                        if ((string)ddlLimitNowBuyMethod.SelectedItem == "Best Price")
                        {
                            decimal LowestAsk = OrderBookTopAsks.OrderBy(a => a.Price).FirstOrDefault().Price;
                            decimal HighestBid = OrderBookTopBids.OrderByDescending(a => a.Price).FirstOrDefault().Price;
                            if (HighestBid != LimitNowBuyOrderPrice) // Our price isn't the highest in book
                            {
                                if (LowestAsk - HighestBid > ActiveInstrument.TickSize) // More than 1 tick size spread
                                {
                                    Price = HighestBid + ActiveInstrument.TickSize;
                                }
                                else
                                {
                                    Price = LowestAsk - ActiveInstrument.TickSize;
                                }
                            }

                        }
                        else if ((string)ddlLimitNowBuyMethod.SelectedItem == "Quick Fill")
                        {
                            Price = OrderBookTopAsks.OrderBy(a => a.Price).FirstOrDefault().Price - (ActiveInstrument.TickSize * nudLimitNowBuyTicksFromCenter.Value);
                        }
                        break;
                    case "Sell":
                        if ((string)ddlLimitNowBuyMethod.SelectedItem == "Best Price")
                        {
                            decimal LowestAsk = OrderBookTopAsks.OrderBy(a => a.Price).FirstOrDefault().Price;
                            decimal HighestBid = OrderBookTopBids.OrderByDescending(a => a.Price).FirstOrDefault().Price;
                            if (LowestAsk != LimitNowSellOrderPrice) // Our price isn't the highest in book
                            {
                                if (LowestAsk - HighestBid > ActiveInstrument.TickSize) // More than 1 tick size spread
                                {
                                    Price = LowestAsk - ActiveInstrument.TickSize;
                                }
                                else
                                {
                                    Price = HighestBid + ActiveInstrument.TickSize;
                                }
                            }
                        }
                        else if ((string)ddlLimitNowBuyMethod.SelectedItem == "Quick Fill")
                        {
                            Price = OrderBookTopBids.OrderByDescending(a => a.Price).FirstOrDefault().Price + (ActiveInstrument.TickSize * nudLimitNowSellTicksFromCenter.Value);
                        }
                        break;
                    default:
                        break;
                }
            }
            catch(Exception ex)
            {

            }
            
            return Price;
        }
        #endregion



        private void TriggerAlerts()
        {
            //if(Alerts.Where(a => a.Triggered == false).Any())
            //{

            //    foreach(Alert a in Alerts)
            //    {
            //        a.Triggered = true;
            //        switch (a.Side)
            //        {
            //            case "Above":
            //                if(Prices[a.Symbol] > a.Price)
            //                {
            //                    MessageBox.Show("Alert! " + a.Symbol + " price is now above " + a.Price.ToString() + ".");
            //                }
            //                break;
            //            case "Below":
            //                if (Prices[a.Symbol] < a.Price)
            //                {
            //                    MessageBox.Show("Alert! " + a.Symbol + " price is now below " + a.Price.ToString() + ".");
            //                }
            //                break;
            //        }



            //    }
            //}
        }

    }

    public class Alert
    {
        public string Symbol { get; set; }
        public string Side { get; set; }
        public decimal Price { get; set; }
        public bool Triggered { get; set; }
    }
}
