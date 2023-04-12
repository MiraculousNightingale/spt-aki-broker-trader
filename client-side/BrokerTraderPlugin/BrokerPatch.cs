﻿using Aki.Reflection.Patching;
using BrokerTraderPlugin;
using EFT.InventoryLogic;
using EFT.UI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aki.Common.Http;

//using ItemPrice = TraderClass.GStruct219; // now use BrokerTraderPlugin.Reflections.ItemPrice instead of generic struct reference
//using CurrencyHelper = GClass2182; // old was GClass2179 // now use BrokerTraderPlugin.Reflections.CurrencyHelper instead of generic class reference

using Aki.Common.Utils;

using static BrokerTraderPlugin.PriceManager;
using TMPro;
using System.Text.RegularExpressions;
using Comfort.Common;
using HarmonyLib;
using System;
using BrokerTraderPlugin.Reflections;
using UnityEngine;
using static UnityEngine.RemoteConfigSettingsHelper;
using UnityEngine.UIElements;

namespace BrokerPatch
{
    //  Pull the TraderClass enumerable from EFT.UI.MerchantsList
    public class PatchMerchantsList : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(MerchantsList).GetMethod("Show", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(MerchantsList __instance, IEnumerable<TraderClass> tradersList, ISession session)
        {
            // Get supported TraderClass instancess to work with.
            try
            {
                // - Can also be used to filter for Unlocked traders, but the second variant seems to work fine so far.
                //TradersList = tradersList.Where((trader) =>
                //{
                //    if (!session.Profile.TradersInfo.ContainsKey(trader.Id)) return false;
                //    return SupportedTraderIds.Contains(trader.Id) && session.Profile.TradersInfo[trader.Id].Unlocked;
                //});
                TradersList = tradersList.Where((trader) => SupportedTraderIds.Contains(trader.Id) && trader.Info.Unlocked);
                //foreach (var trader in TradersList)
                //{
                //    Logger.LogError($"TRADER LISTING: {trader.LocalizedName}");
                //}
                //Session = Traverse.Create(__instance).Fields().Select(fName => AccessTools.Field(typeof(MerchantsList), fName)).FirstOrDefault(field => field.FieldType == typeof(ISession)).GetValue(__instance) as ISession;
                //Session = typeof(MerchantsList).GetField("ginterface128_0", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance) as ISession;
                Session = session; // session is actually one of the args, bruh
                BackendCfg = Singleton<BackendConfigSettingsClass>.Instance;
                if (Session == null) throw new Exception("Session is null.");
            }
            catch (Exception ex)
            {
                var msg = $"{PluginInfo.PLUGIN_GUID} error! Threw an exception during MerchantsList patch, perhaps due to version incompatibility. Exception message: {ex.Message}";
                Logger.LogError(msg);
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Infinite);
            }
        }
    }
    //  Patch price calculation method in TraderClass
    public class PatchGetUserItemPrice : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            //TraderClass - GetUserItemPrice - WORKS! use postfix patch 
            //TradingItemView - SetPrice -> WORKS! use prefix patch
            return typeof(TraderClass).GetMethod("GetUserItemPrice", BindingFlags.Instance | BindingFlags.Public);
        }

        // Use prefix patch to save already miserable Tarkov UI performance.
        // The old postfix implementation is kept commented as a back-up/reference.
        [PatchPrefix]
        private static bool PatchPrefix(ref TraderClass __instance, Item item, ref object __result)
        {
            // Only affect the Broker
            if (__instance.Id == BROKER_TRADER_ID)
            {
                try
                {
                    __result = GetBestItemPrice(item);
                    return false;
                }
                catch (Exception ex)
                {
                    var msg = $"{PluginInfo.PLUGIN_GUID} error! Threw an exception during GetUserItemPrice patch, perhaps due to version incompatibility. Exception message: {ex.Message}";
                    Logger.LogError(msg);
                    NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Infinite);
                }
            }
            return true;
        }

        //[PatchPostfix]
        //private static void PatchPostfix(ref TraderClass __instance, Item item, ref object __result)
        //{
        //    // Only affect the Broker
        //    if (__instance.Id == BROKER_TRADER_ID)
        //    {
        //        if (__result != null)
        //        {
        //            try
        //            {
        //                __result = GetBestItemPrice(item);
        //            }
        //            catch (Exception ex)
        //            {
        //                var msg = $"{PluginInfo.PLUGIN_GUID} error! Threw an exception during GetUserItemPrice patch, perhaps due to version incompatibility. Exception message: {ex.Message}";
        //                Logger.LogError(msg);
        //                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Infinite);
        //            }
        //        }
        //    }
        //}
    }
    //  Change how total transaction sum is generated when selling to trader.
    public class PatchEquivalentSum : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // method_10 assigns a sprite and text value to "_equivalentSum" when selling items
            // method_10 is still the same for 3.5.5. Identifying by CIL instructions could be used but that a pretty unnecessary stretch.

            // The original method_10 has several properties to distinct it:
            // * GetMethodBody().MaxStackSize == 4
            // * LocalVariables.Count = 2 
            // * One(first) of the variables is an "ItemPrice" generic structure (in 3.5.5. a TraderClass.GStruct219).
            // For now dynamically reaching this method is possible by simply checking if it has a variable of generic "ItemPrice" structure type.
            // Later this might be changed for more precision. IL code checks are still a last resort.
            var method = AccessTools.GetDeclaredMethods(typeof(TraderDealScreen)).Where(method => method.GetMethodBody().LocalVariables.Any(variable => variable.LocalType == ItemPrice.structType)).FirstOrDefault();
            if (method == null) throw new Exception("PatchEquivalentSum. Couldn't find the method by reflection.");
            return method;
        }

        [PatchPostfix]
        private static void PatchPostfix(TraderDealScreen __instance)
        {
            try
            {
                //var trader = typeof(TraderDealScreen).GetField("gclass1949_0", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance) as TraderClass; // has to be gclass1952_0
                // Search for trader fiels by type instead of a generic name.
                var trader = Traverse.Create(__instance).Fields().Select(fName => AccessTools.Field(__instance.GetType(), fName)).FirstOrDefault(field => field.FieldType == typeof(TraderClass) && !field.IsPublic).GetValue(__instance) as TraderClass;
                if (trader == null) throw new Exception("TraderDealScreen. Found trader field is null.");
                var _equivalentSumValue = typeof(TraderDealScreen).GetField("_equivalentSumValue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance) as TextMeshProUGUI;
                if (trader.Id == BROKER_TRADER_ID)
                {
                    // source is a list of ItemPrices, reference ItemPriceReflection.
                    var source = trader.CurrentAssortment.SellingStash.Containers.First().Items.Select(GetBestItemPrice).Where(itemPrice => itemPrice != null).ToList();
                    if (!source.Any()) _equivalentSumValue.text = "";
                    else
                    {
                        var groupByCurrency = source.GroupBy(ItemPrice.GetCurrencyId).Select(currencyGroup => new
                        {
                            CurrencyId = currencyGroup.Key,
                            Amount = currencyGroup.Sum(ItemPrice.GetAmount),
                        });
                        // Rouble amount has to be always first. Since Broker's main currency is RUB.
                        _equivalentSumValue.text = groupByCurrency.Where(group => group.CurrencyId == CurrencyHelper.ROUBLE_ID).Select(group => group.Amount).FirstOrDefault().ToString();
                        foreach (var currency in groupByCurrency.Where(group => group.CurrencyId != CurrencyHelper.ROUBLE_ID))
                        {
                            _equivalentSumValue.text += $" + {CurrencyHelper.GetCurrencyCharById(currency.CurrencyId)} {currency.Amount}";
                        }
                    }
                }
                Regex regex = new Regex("\\B(?=(\\d{3})+(?!\\d))");
                _equivalentSumValue.text = regex.Replace(_equivalentSumValue.text, " ");
            }
            catch (Exception ex)
            {
                var msg = $"{PluginInfo.PLUGIN_GUID} error! Threw an exception during EquivalentSum patch, perhaps due to version incompatibility. Exception message: {ex.Message}";
                Logger.LogError(msg);
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Infinite);
            }
        }
    }
    //  Before showing the trader screen refresh ragfair prices. This fixes calculated ragfair tax inconsistency if you didn't open ragfair menu yet.
    public class PatchRefreshRagfairOnTraderScreenShow : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // method_10 assigns a sprite and text value to "_equivalentSum" when selling items
            return typeof(TraderDealScreen).GetMethod("Show", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static void PatchPrefix(TraderClass trader)
        {
            try
            {
                if (trader.Id == BROKER_TRADER_ID && PriceManager.ModConfig.UseRagfair)
                {
                    Session.RagFair.RefreshItemPrices();
                }
            }
            catch (Exception ex)
            {
                var msg = $"{PluginInfo.PLUGIN_GUID} error! Threw an exception during RefreshRagfairOnTraderScreenShow patch, perhaps due to version incompatibility. Exception message: {ex.Message}";
                Logger.LogError(msg);
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Infinite);
            }
        }
    }
    //  Send accurate client item data to server when user pressed "DEAL!" on the trade screen.
    public class PatchSendDataOnDealButtonPress : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Might be NonPublic, unknown. If won't work, try TraderAssortmentControllerClass method Sell().
            return typeof(TraderAssortmentControllerClass).GetMethod("Sell", BindingFlags.Instance | BindingFlags.Public);
        }

        // Prefer prefix patch to make sure that the request is sent in time. (Although it's probably sync)
        [PatchPrefix]
        private static void PatchPrefix(TraderAssortmentControllerClass __instance)
        {
            try
            {
                var trader = Traverse.Create(__instance).Fields().Select(fName => AccessTools.Field(__instance.GetType(), fName)).FirstOrDefault(field => field.FieldType == typeof(TraderClass) && !field.IsPublic && field.IsInitOnly).GetValue(__instance) as TraderClass;
                if (trader == null) throw new Exception("TraderAssortmentControllerClass. Found trader field is null.");
                //var trader = __instance.GetType().GetField("gclass1949_0", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance) as TraderClass;
                if (trader.Id == BROKER_TRADER_ID)
                {
                    // Both are probably be identical, but use lower for consistency with source code.
                    // var soldItems = __instance.SellingStash.Containers.First().Items; 
                    var soldItems = __instance.SellingTableGrid.ContainedItems.Keys.ToList();
                    if (soldItems.Count > 0)
                    {
                        var itemsSellData = soldItems.Select(GetBrokerItemSellData);
                        // Send sell data to server
                        Dictionary<string, BrokerItemSellData> sellData = itemsSellData.ToDictionary(data => data.ItemId);
                        RequestHandler.PostJson(Routes.PostSoldItemsData, Json.Serialize(sellData));
                        // Show notifications for reputation increments.
                        if (PriceManager.ModConfig.UseNotifications)
                        {
                            var groupByTrader = sellData.Select(entry => entry.Value).GroupBy(data => data.TraderId);
                            Regex regex = new Regex("\\B(?=(\\d{3})+(?!\\d))"); // format thousands with spaces
                            //int traderNameSpacing = Math.Max(groupByTrader.Max(group => $"{group.Key} Nickname".Localized().Length), "Flea Market".Length);
                            string ragfairLocale = "";
                            foreach (var word in "RAG FAIR".Localized().Split(' '))
                            {
                                ragfairLocale += char.ToUpper(word[0]) + word.Substring(1) + " ";
                            }
                            ragfairLocale.Trim();
                            const string messageInitVal = "\nReputation changes:\n\n";
                            string message = messageInitVal;
                            foreach (var group in groupByTrader.Where(group => group.Key != BROKER_TRADER_ID))
                            {
                                int totalPrice = group.Sum(data => data.Price);
                                string currencyChar = CurrencyHelper.GetCurrencyChar(TradersList.First(trader => trader.Id == group.First().TraderId).Settings.Currency);
                                message += $"    \u2022    {$"{group.Key} Nickname".Localized()}:    + {currencyChar} {regex.Replace(totalPrice.ToString(), " ")}\n\n";
                                //message += string.Format("{0,-" + traderNameSpacing + "}:   + {1} {2}\n\n", $"{group.Key} Nickname".Localized(), currencyChar, regex.Replace(totalPrice.ToString(), " "));
                            }
                            // For Broker Trader - show flea rep increment
                            foreach (var group in groupByTrader.Where(group => group.Key == BROKER_TRADER_ID))
                            {
                                int totalPrice = group.Where(item => !CurrencyHelper.IsCurrencyId(soldItems.First(soldItem => soldItem.Id == item.ItemId).TemplateId)).Sum(item => item.Price);
                                if (totalPrice < 1) break; // if no "non-currency" items just break out of the loop
                                string currencyChar = CurrencyHelper.GetCurrencyChar(ECurrencyType.RUB);
                                string repIncStr = (totalPrice * RagfairSellRepGain).ToString();
                                // add a space 2 digits after the floating point for better contextual readability
                                message += $"    \u2022    {ragfairLocale}:    +{repIncStr.Insert(repIncStr.IndexOf('.') + 2 + 1, " ")}\n\n";
                                //message += string.Format("{0,-" + traderNameSpacing + "}:   +{1}\n\n", "Flea Market", repIncStr.Insert(repIncStr.IndexOf('.') + 2 + 1, " "));
                            }
                            if (message != messageInitVal)
                            {
                                NotificationManagerClass.DisplayMessageNotification(
                                    message,
                                    ModNotificationDuration,
                                    EFT.Communications.ENotificationIconType.RagFair,
                                    Color.white
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = $"{PluginInfo.PLUGIN_GUID} error! Threw an exception during SendDataOnDealButtonPress patch, perhaps due to version incompatibility. Exception message: {ex.Message}";
                Logger.LogError(msg);
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Infinite);
            }
        }

    }

}
