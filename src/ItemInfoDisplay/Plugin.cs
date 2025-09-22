using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Zorro.UI.Effects;

namespace ItemInfoDisplay;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    private static GUIManager guiManager;
    private static TextMeshProUGUI itemInfoDisplayTextMesh;
    private static Dictionary<string, string> effectColors = new Dictionary<string, string>();
    private static float lastKnownSinceItemAttach;
    private static bool hasChanged;
    private static ConfigEntry<float> configFontSize;
    private static ConfigEntry<float> configOutlineWidth;
    private static ConfigEntry<float> configLineSpacing;
    private static ConfigEntry<float> configSizeDeltaX;
    private static ConfigEntry<float> configForceUpdateTime;

    private void Awake()
    {
        Log = Logger;
        InitEffectColors(effectColors);
        lastKnownSinceItemAttach = 0f;
        hasChanged = true;
        //翻譯謝謝
        configFontSize = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "字體大小", 20f, "調整物品描述文字的字體大小。");
        configOutlineWidth = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "輪廓寬度", 0.08f, "調整物品描述文字的輪廓寬度。");
        configLineSpacing = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "行距", -35f, "調整物品描述文字的行距。");
        configSizeDeltaX = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "大小變化X", 550f, "調整物品描述文字容器的水平長度。增加會將文字向左移動，減少會將文字向右移動。");
        configForceUpdateTime = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "強制更新時間", 1f, "調整物品強制更新的時間（秒）。");
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayUpdatePatch));
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayEquipPatch));
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayFinishCookingPatch));
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayReduceUsesRPCPatch));
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private static class ItemInfoDisplayUpdatePatch 
    {
        [HarmonyPatch(typeof(CharacterItems), "Update")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayUpdate(CharacterItems __instance)
        {
            try
            {
                if (guiManager == null)
                {
                    AddDisplayObject();
                }
                else
                {
                    if (Character.observedCharacter.data.currentItem != null)
                    {
                        if (hasChanged)
                        {
                            hasChanged = false;
                            ProcessItemGameObject();
                        }
                        else if (Mathf.Abs(Character.observedCharacter.data.sinceItemAttach - lastKnownSinceItemAttach) >= configForceUpdateTime.Value)
                        {
                            hasChanged = true;
                            lastKnownSinceItemAttach = Character.observedCharacter.data.sinceItemAttach;
                        }

                        if (!itemInfoDisplayTextMesh.gameObject.activeSelf)
                        {
                            itemInfoDisplayTextMesh.gameObject.SetActive(true);
                        }
                    }
                    else
                    {
                        if (itemInfoDisplayTextMesh.gameObject.activeSelf) 
                        {
                            itemInfoDisplayTextMesh.gameObject.SetActive(false);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    private static class ItemInfoDisplayEquipPatch
    {
        [HarmonyPatch(typeof(CharacterItems), "Equip")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayEquip(CharacterItems __instance)
        {
            try
            {
                if (Character.ReferenceEquals(Character.observedCharacter, __instance.character))
                {
                    hasChanged = true;
                }
            }
            catch (Exception e)
            {
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    private static class ItemInfoDisplayFinishCookingPatch
    {
        [HarmonyPatch(typeof(ItemCooking), "FinishCooking")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayFinishCooking(ItemCooking __instance)
        {
            try
            {
                if (Character.ReferenceEquals(Character.observedCharacter, __instance.item.holderCharacter))
                {
                    hasChanged = true;
                }
            }
            catch (Exception e)
            {
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    private static class ItemInfoDisplayReduceUsesRPCPatch
    {
        [HarmonyPatch(typeof(Action_ReduceUses), "ReduceUsesRPC")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayReduceUsesRPC(Action_ReduceUses __instance)
        {
            try
            {
                if (Character.ReferenceEquals(Character.observedCharacter, __instance.character))
                {
                    hasChanged = true;
                }
            }
            catch (Exception e)
            {
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    private static void ProcessItemGameObject()
    {
        Item item = Character.observedCharacter.data.currentItem; // not sure why this broke after THE MESA update, made no changes (just rebuilt)
        GameObject itemGameObj = item.gameObject;
        Component[] itemComponents = itemGameObj.GetComponents(typeof(Component));
        bool isConsumable = false;
        string prefixStatus = "";
        string suffixWeight = "";
        string suffixUses = "";
        string suffixCooked = "";
        string suffixAfflictions = "";
        itemInfoDisplayTextMesh.text = "";

        if (Ascents.itemWeightModifier > 0)
        {
            suffixWeight += effectColors["Weight"] + ((item.carryWeight + Ascents.itemWeightModifier) * 2.5f).ToString("F1").Replace(".0", "") + " WEIGHT</color>";
        }
        else
        {
            suffixWeight += effectColors["Weight"] + (item.carryWeight * 2.5f).ToString("F1").Replace(".0", "") + " WEIGHT</color>";
        }

        if (itemGameObj.name.Equals("Bugle(Clone)"))
        {
            itemInfoDisplayTextMesh.text += "圍着隊友吹響它吧！\n";
        }
        else if (itemGameObj.name.Equals("Pirate Compass(Clone)"))
        {
            itemInfoDisplayTextMesh.text += effectColors["Injury"] + "指向</color>最近的行李箱\n";
        }
        else if (itemGameObj.name.Equals("Compass(Clone)"))
        {
            itemInfoDisplayTextMesh.text += effectColors["Injury"] + "指向</color>山頂\n";
        }
        else if (itemGameObj.name.Equals("Shell Big(Clone)"))
        {
            itemInfoDisplayTextMesh.text += "試試把它" + effectColors["Hunger"] + "丟向</color>椰子w\n";
        }

        for (int i = 0; i < itemComponents.Length; i++)
        {
            if (itemComponents[i].GetType() == typeof(ItemUseFeedback))
            {
                ItemUseFeedback itemUseFeedback = (ItemUseFeedback)itemComponents[i];
                if (itemUseFeedback.useAnimation.Equals("Eat") || itemUseFeedback.useAnimation.Equals("Drink") || itemUseFeedback.useAnimation.Equals("Heal"))
                {
                    isConsumable = true;
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_Consume))
            {
                isConsumable = true;
            }
            else if (itemComponents[i].GetType() == typeof(Action_RestoreHunger))
            {
                Action_RestoreHunger effect = (Action_RestoreHunger)itemComponents[i];
                prefixStatus += ProcessEffect((effect.restorationAmount * -1f), "Hunger");
            }
            else if (itemComponents[i].GetType() == typeof(Action_GiveExtraStamina))
            {
                Action_GiveExtraStamina effect = (Action_GiveExtraStamina)itemComponents[i];
                prefixStatus += ProcessEffect(effect.amount, "Extra Stamina");
            }
            else if (itemComponents[i].GetType() == typeof(Action_InflictPoison))
            {
                Action_InflictPoison effect = (Action_InflictPoison)itemComponents[i];
                prefixStatus += "AFTER " + effect.delay.ToString() + "s, " + ProcessEffectOverTime(effect.poisonPerSecond, 1f, effect.inflictionTime, "Poison");
            }
            else if (itemComponents[i].GetType() == typeof(Action_AddOrRemoveThorns))
            {
                Action_AddOrRemoveThorns effect = (Action_AddOrRemoveThorns)itemComponents[i];
                prefixStatus += ProcessEffect((effect.thornCount * 0.05f), "Thorns"); // TODO: Search for thorns amount per applied thorn
            }
            else if (itemComponents[i].GetType() == typeof(Action_ModifyStatus))
            {
                Action_ModifyStatus effect = (Action_ModifyStatus)itemComponents[i];
                prefixStatus += ProcessEffect(effect.changeAmount, effect.statusType.ToString());
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyMassAffliction))
            {
                Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)itemComponents[i];
                suffixAfflictions += "<#CCCCCC>NEARBY PLAYERS WILL RECEIVE:</color>\n";
                suffixAfflictions += ProcessAffliction(effect.affliction);
                if (effect.extraAfflictions.Length > 0)
                {
                    for (int j = 0; j < effect.extraAfflictions.Length; j++)
                    {
                        if (suffixAfflictions.EndsWith('\n'))
                        {
                            suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 1);
                        }
                        suffixAfflictions += ",\n" + ProcessAffliction(effect.extraAfflictions[j]);
                    }
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyAffliction))
            {
                Action_ApplyAffliction effect = (Action_ApplyAffliction)itemComponents[i];
                suffixAfflictions += ProcessAffliction(effect.affliction);
            }
            else if (itemComponents[i].GetType() == typeof(Action_ClearAllStatus))
            {
                Action_ClearAllStatus effect = (Action_ClearAllStatus)itemComponents[i];
                itemInfoDisplayTextMesh.text += effectColors["ItemInfoDisplayPositive"] + "CLEAR ALL STATUS</color>";
                if (effect.excludeCurse)
                {
                    itemInfoDisplayTextMesh.text += " EXCEPT " + effectColors["Curse"] + "CURSE</color>";
                }
                if (effect.otherExclusions.Count > 0)
                {
                    foreach (CharacterAfflictions.STATUSTYPE exclusion in effect.otherExclusions)
                    {
                        itemInfoDisplayTextMesh.text += ", " + effectColors[exclusion.ToString()] + exclusion.ToString().ToUpper() + "</color>";
                    }
                }
                itemInfoDisplayTextMesh.text = itemInfoDisplayTextMesh.text.Replace(", <#E13542>CRAB</color>", "") + "\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_ConsumeAndSpawn))
            {
                Action_ConsumeAndSpawn effect = (Action_ConsumeAndSpawn)itemComponents[i];
                if (effect.itemToSpawn.ToString().Contains("Peel"))
                {
                    itemInfoDisplayTextMesh.text += "<#CCCCCC>GAIN A PEEL WHEN EATEN</color>\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_ReduceUses))
            {
                OptionableIntItemData uses = (OptionableIntItemData)item.data.data[DataEntryKey.ItemUses];
                if (uses.HasData)
                {
                    if (uses.Value > 1)
                    {
                        suffixUses += "   " + uses.Value + " USES";
                    }
                }
            }
            else if (itemComponents[i].GetType() == typeof(Lantern))
            {
                Lantern lantern = (Lantern)itemComponents[i];
                if (itemGameObj.name.Equals("Torch(Clone)")){
                    itemInfoDisplayTextMesh.text += "可被點亮\n";
                }
                else {
                        suffixAfflictions += "<#CCCCCC>點亮時，附近的玩家會獲得：</color>\n";
                }

                if (itemGameObj.name.Equals("Lantern_Faerie(Clone)"))
                {
                    StatusField effect = itemGameObj.transform.Find("FaerieLantern/Light/Heat").GetComponent<StatusField>();
                    suffixAfflictions += ProcessEffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, effect.statusType.ToString());
                    foreach (StatusField.StatusFieldStatus status in effect.additionalStatuses)
                    {
                        if (suffixAfflictions.EndsWith('\n'))
                        {
                            suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 1);
                        }
                        suffixAfflictions += ",\n" + ProcessEffectOverTime(status.statusAmountPerSecond, 1f, lantern.startingFuel, status.statusType.ToString());
                    }
                }
                else if (itemGameObj.name.Equals("Lantern(Clone)"))
                {
                    StatusField effect = itemGameObj.transform.Find("GasLantern/Light/Heat").GetComponent<StatusField>();
                    suffixAfflictions += ProcessEffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, effect.statusType.ToString());
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_RaycastDart))
            {
                Action_RaycastDart effect = (Action_RaycastDart)itemComponents[i];
                isConsumable = true;
                suffixAfflictions += "<#CCCCCC>發射一支飛鏢，造成以下效果：</color>\n";
                for (int j = 0; j < effect.afflictionsOnHit.Length; j++)
                {
                    suffixAfflictions += ProcessAffliction(effect.afflictionsOnHit[j]);
                    if (suffixAfflictions.EndsWith('\n'))
                    {
                        suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 1);
                    }
                    suffixAfflictions += ",\n";
                }
                if (suffixAfflictions.EndsWith('\n'))
                {
                    suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 2);
                }
                suffixAfflictions += "\n";
            }
            else if (itemComponents[i].GetType() == typeof(MagicBugle))
            {
                itemInfoDisplayTextMesh.text += "當吹響友誼號角時，\n";
            }
            else if (itemComponents[i].GetType() == typeof(ClimbingSpikeComponent))
            {
                itemInfoDisplayTextMesh.text += "放置一個你可以抓住的岩釘\n來 " + effectColors["Extra Stamina"] + "恢復士氣</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Flare))
            {
                itemInfoDisplayTextMesh.text += "可被點亮\n";
            }
            else if (itemComponents[i].GetType() == typeof(Backpack))
            {
                itemInfoDisplayTextMesh.text += "放下背包才可放入物品\n";
            }
            else if (itemComponents[i].GetType() == typeof(BananaPeel))
            {
                itemInfoDisplayTextMesh.text += "踩到後會" + effectColors["Hunger"] + "滑倒</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(Constructable))
            {
                Constructable effect = (Constructable)itemComponents[i];
                if (effect.constructedPrefab.name.Equals("PortableStovetop_Placed"))
                {
                    itemInfoDisplayTextMesh.text += "放置" + effectColors["Injury"] + "攜帶式爐具</color>以提供" + effect.constructedPrefab.GetComponent<Campfire>().burnsFor.ToString() + "秒的溫暖\n";
                }
                else
                {
                    itemInfoDisplayTextMesh.text += "可被放置\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeSpool))
            {
                RopeSpool effect = (RopeSpool)itemComponents[i];
                if (effect.isAntiRope)
                {
                    itemInfoDisplayTextMesh.text += "放置一條向上漂浮的繩索\n";
                }
                else
                {
                    itemInfoDisplayTextMesh.text += "放置一條繩索\n";
                }
                itemInfoDisplayTextMesh.text += "綁繩結最短需" + (effect.minSegments / 4f).ToString("F2").Replace(".0", "") + "公尺長，最多" 
                    + (Rope.MaxSegments / 4f).ToString("F1").Replace(".0", "") + "公尺長\n";
                //using force update here for remaining length since Rope has no character distinction for Detach_Rpc() hook, maybe unless OK with any player triggering this
                if (configForceUpdateTime.Value <= 1f)
                {
                    suffixUses += "   剩餘" + (effect.RopeFuel / 4f).ToString("F2").Replace(".00", "") + "公尺";
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeShooter))
            {
                RopeShooter effect = (RopeShooter)itemComponents[i];
                // 發射一條繩索錨，並放置一條繩子 …
                itemInfoDisplayTextMesh.text += "發射一條";
                if (effect.ropeAnchorWithRopePref.name.Equals("RopeAnchorForRopeShooterAnti"))
                {
                    // 向上漂浮（上升繩）
                    itemInfoDisplayTextMesh.text += (effect.maxLength / 4f).ToString("F1").Replace(".0", "") + "公尺長" + effectColors["Cold"] + "向上漂浮</color>的繩子\n";
                }
                else
                {
                    // 一般下降繩
                    itemInfoDisplayTextMesh.text += (effect.maxLength / 4f).ToString("F1").Replace(".0", "") + "公尺長的繩子\n";
                }
                itemInfoDisplayTextMesh.text += (effect.maxLength / 4f).ToString("F1").Replace(".0", "") + "公尺\n";
            }
            else if (itemComponents[i].GetType() == typeof(Antigrav))
            {
                Antigrav effect = (Antigrav)itemComponents[i];
                if (effect.intensity != 0f)
                {
                    suffixAfflictions += effectColors["Injury"] + "警告：</color><#CCCCCC>放下後會飛走</color>\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_Balloon))
            {
                suffixAfflictions += "可以綁在玩家身上\n";
            }
            else if (itemComponents[i].GetType() == typeof(VineShooter))
            {
                VineShooter effect = (VineShooter)itemComponents[i];
                itemInfoDisplayTextMesh.text += "從當前位置發射一條鏈條到你瞄準的地方\n最長可達"
                    + (effect.maxLength / (5f / 3f)).ToString("F1").Replace(".0", "") + "公尺\n";
            }
            else if (itemComponents[i].GetType() == typeof(ShelfShroom))
            {
                ShelfShroom effect = (ShelfShroom)itemComponents[i];
                if (effect.instantiateOnBreak.name.Equals("HealingPuffShroomSpawn"))
                {
                    GameObject effect1 = effect.instantiateOnBreak.transform.Find("VFX_SporeHealingExplo").gameObject;
                    AOE effect1AOE = effect1.GetComponent<AOE>();
                    GameObject effect2 = effect1.transform.Find("VFX_SporePoisonExplo").gameObject;
                    AOE effect2AOE = effect2.GetComponent<AOE>();
                    AOE[] effect2AOEs = effect2.GetComponents<AOE>();
                    TimeEvent effect2TimeEvent = effect2.GetComponent<TimeEvent>();
                    RemoveAfterSeconds effect2RemoveAfterSeconds = effect2.GetComponent<RemoveAfterSeconds>();
                    itemInfoDisplayTextMesh.text += effectColors["Hunger"] + "丟出</color>後會釋放出氣體，效果為：\n";
                    itemInfoDisplayTextMesh.text += ProcessEffect((Mathf.Round(effect1AOE.statusAmount * 0.9f * 40f) / 40f), effect1AOE.statusType.ToString()); // incorrect? calculates strangely so i somewhat manually adjusted the values
                    itemInfoDisplayTextMesh.text += ProcessEffectOverTime((Mathf.Round(effect2AOE.statusAmount * (1f / effect2TimeEvent.rate) * 40f) / 40f), 1f, effect2RemoveAfterSeconds.seconds, effect2AOE.statusType.ToString()); // incorrect?
                    if (effect2AOEs.Length > 1)
                    {
                        itemInfoDisplayTextMesh.text += ProcessEffectOverTime((Mathf.Round(effect2AOEs[1].statusAmount * (1f / effect2TimeEvent.rate) * 40f) / 40f), 1f, (effect2RemoveAfterSeconds.seconds + 1f), effect2AOEs[1].statusType.ToString()); // incorrect?
                    } // didn't handle dynamically because there were 2 poison removal AOEs but 1 doesn't seem to work or they are buggy in some way (probably time event rate)?
                }
                else if (effect.instantiateOnBreak.name.Equals("ShelfShroomSpawn"))
                {
                    itemInfoDisplayTextMesh.text += effectColors["Hunger"] + "丟出</color>後會生成一個平台\n";
                }
                else if (effect.instantiateOnBreak.name.Equals("BounceShroomSpawn"))
                {
                    itemInfoDisplayTextMesh.text += effectColors["Hunger"] + "丟出</color>後會生成一個彈跳墊\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(ScoutEffigy))
            {
                itemInfoDisplayTextMesh.text += effectColors["Extra Stamina"] + "復活</color>一名死亡的玩家\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Die))
            {
                itemInfoDisplayTextMesh.text += "用了就" + effectColors["Curse"] + "死翹翹</color>了\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_SpawnGuidebookPage))
            {
                isConsumable = true;
                itemInfoDisplayTextMesh.text += "可被開啟\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Guidebook))
            {
                itemInfoDisplayTextMesh.text += "可被閱讀\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_CallScoutmaster))
            {
                itemInfoDisplayTextMesh.text += effectColors["Injury"] + "使用後會打破規則0</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_MoraleBoost))
            {
                Action_MoraleBoost effect = (Action_MoraleBoost)itemComponents[i];
                if (effect.boostRadius < 0)
                {
                    itemInfoDisplayTextMesh.text += effectColors["ItemInfoDisplayPositive"] + "獲得</color> " + effectColors["Extra Stamina"] + (effect.baselineStaminaBoost * 100f).ToString("F1").Replace(".0", "") + " 點額外士氣</color>\n";
                }
                else if (effect.boostRadius > 0)
                {
                    itemInfoDisplayTextMesh.text += "<#CCCCCC>最近的玩家</color>" + effectColors["ItemInfoDisplayPositive"] + " 獲得</color> " + effectColors["Extra Stamina"] + (effect.baselineStaminaBoost * 100f).ToString("F1").Replace(".0", "") + " 點額外士氣</color>\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(Breakable))
            {
                itemInfoDisplayTextMesh.text += effectColors["Hunger"] + "丟出</color>後會裂開\n";
            }
            else if (itemComponents[i].GetType() == typeof(Bonkable))
            {
                itemInfoDisplayTextMesh.text += "被丟到頭的玩家會" + effectColors["Injury"] + "暈倒</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(MagicBean))
            {
                MagicBean effect = (MagicBean)itemComponents[i];
                itemInfoDisplayTextMesh.text += effectColors["Hunger"] + "丟出</color>後會種下垂直向上生長的藤蔓，\n最長可達"
                    + (effect.plantPrefab.maxLength / 2f).ToString("F1").Replace(".0", "") + "公尺或直到碰到東西為止\n";
            }
            else if (itemComponents[i].GetType() == typeof(BingBong))
            {
                itemInfoDisplayTextMesh.text += "兵幫航空的吉祥物\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Passport))
            {
                itemInfoDisplayTextMesh.text += "打開護照來自訂角色\n";
            }
            else if (itemComponents[i].GetType() == typeof(Actions_Binoculars))
            {
                itemInfoDisplayTextMesh.text += "用來看得更遠\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_WarpToRandomPlayer))
            {
                itemInfoDisplayTextMesh.text += "傳送到隨機玩家身邊\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_WarpToBiome))
            {
                Action_WarpToBiome effect = (Action_WarpToBiome)itemComponents[i];
                itemInfoDisplayTextMesh.text += "傳送到" + effect.segmentToWarpTo.ToString().ToUpper() + "\n";
            }
            else if (itemComponents[i].GetType() == typeof(Parasol))
            {
                itemInfoDisplayTextMesh.text += "打開遮陽傘來減緩下降速度\n";
            }
            else if (itemComponents[i].GetType() == typeof(Frisbee))
            {
                itemInfoDisplayTextMesh.text += "把它" + effectColors["Hunger"] + "丟出去</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_ConstructableScoutCannonScroll))
            {
                itemInfoDisplayTextMesh.text += "\n<#CCCCCC>放下後透過點燃導火線</color>\n來把大砲中的偵察兵發射出去\n";
                    //+ "\n<#CCCCCC>LIMITS GRAVITATIONAL ACCELERATION\n(PREVENTS OR LOWERS FALL DAMAGE)</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(Dynamite))
            {
                Dynamite effect = (Dynamite)itemComponents[i];
                itemInfoDisplayTextMesh.text += effectColors["Injury"] + "爆炸</color>造成最多" + effectColors["Injury"] 
                    + (effect.explosionPrefab.GetComponent<AOE>().statusAmount * 100f).ToString("F1").Replace(".0", "") + " 傷害</color>\n<#CCCCCC>拿着引爆會受到額外傷害</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(Scorpion))
            {
                Scorpion effect = (Scorpion)itemComponents[i]; // wanted to hide poison info if dead, but mob state does not immediately update on equip leading to visual bug
                if (configForceUpdateTime.Value <= 1f)
                {
                    float effectPoison = Mathf.Max(0.5f, (1f - item.holderCharacter.refs.afflictions.statusSum + 0.05f)) * 100f; // v.1.23.a BASED ON Scorpion.InflictAttack
                    itemInfoDisplayTextMesh.text += "如果蠍子活着的話會" + effectColors["Poison"] + "螫</color>你\n在" + effectColors["Heat"] + "烤熟</color>後會" + effectColors["Heat"] + "死掉</color>\n\n" 
                        + "<#CCCCCC>每被螫一次會持續</color>" + effectColors["Poison"] + effect.totalPoisonTime.ToString("F1").Replace(".0", "") + "秒</color>獲得"
                        + effectPoison.ToString("F1").Replace(".0", "") + " 點毒素</color>\n"
                        + "<#CCCCCC>(若目前十分健康會造成更多傷害)</color>\n" ;

                }
                else
                {
                    //Not translated
                    itemInfoDisplayTextMesh.text += "IF ALIVE, " + effectColors["Poison"] + "STINGS</color> YOU\n" + effectColors["Curse"] 
                        + "DIES</color> WHEN " + effectColors["Heat"] + "COOKED</color>\n\n" + "<#CCCCCC>NEXT STING WILL DEAL:</color>\nAT LEAST "
                        + effectColors["Poison"] + "50 POISON</color> OVER " + effect.totalPoisonTime.ToString("F1").Replace(".0", "") + "s\nAT MOST "
                        + effectColors["Poison"] + "105 POISON</color> OVER " + effect.totalPoisonTime.ToString("F1").Replace(".0", "")
                        + "s\n<#CCCCCC>(MORE DAMAGE IF HEALTHY)</color>\n"; // v.1.23.a THERE'S NO VARIABLE FOR POISON AMOUNT, CALCULATED IN Scorpion.InflictAttack
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_Spawn))
            {
                Action_Spawn effect = (Action_Spawn)itemComponents[i];
                if (effect.objectToSpawn.name.Equals("VFX_Sunscreen"))
                {
                    AOE effectAOE = effect.objectToSpawn.transform.Find("AOE").GetComponent<AOE>();
                    RemoveAfterSeconds effectTime = effect.objectToSpawn.transform.Find("AOE").GetComponent<RemoveAfterSeconds>();
                    itemInfoDisplayTextMesh.text += "<#CCCCCC>噴灑一個持續" + effectTime.seconds.ToString("F1").Replace(".0", "") + "秒</color>的霧氣，會造成以下效果：\n"
                        + ProcessAffliction(effectAOE.affliction);                }
            }
            else if (itemComponents[i].GetType() == typeof(CactusBall))
            {
                CactusBall effect = (CactusBall)itemComponents[i];
                itemInfoDisplayTextMesh.text += "需要至少用" + (effect.throwChargeRequirement * 100f).ToString("F1").Replace(".0", "") + "% 的力" + effectColors["Hunger"] + "丟出去</color>\n不然就會" + effectColors["Thorns"] + "黏</color>在你身上\n";
            }
            else if (itemComponents[i].GetType() == typeof(BingBongShieldWhileHolding))
            {
                itemInfoDisplayTextMesh.text += "<#CCCCCC>裝備時將會獲得：</color>\n" + effectColors["Shield"] + "護盾</color>（無敵狀態）\n";
            }
            else if (itemComponents[i].GetType() == typeof(ItemCooking))
            {
                ItemCooking itemCooking = (ItemCooking)itemComponents[i];
                if (itemCooking.wreckWhenCooked && (itemCooking.timesCookedLocal >= 1))
                {
                    suffixCooked += "\n" + effectColors["Curse"] + "因為被烤而壞掉</color>";
                }
                else if (itemCooking.wreckWhenCooked)
                {
                    suffixCooked += "\n" + effectColors["Curse"] + "拿去烤的話會壞掉</color>";
                }
                else if (itemCooking.timesCookedLocal >= ItemCooking.COOKING_MAX)
                {
                    suffixCooked += "   " + effectColors["Curse"] + itemCooking.timesCookedLocal.ToString() + "個烤過的\n不可被烤</color>";
                }
                else if (itemCooking.timesCookedLocal == 0)
                {
                    suffixCooked += "\n" + effectColors["Extra Stamina"] + "可以被烤</color>";
                }
                else if (itemCooking.timesCookedLocal == 1)
                {
                    suffixCooked += "   " + effectColors["Extra Stamina"] + itemCooking.timesCookedLocal.ToString() + "個烤過的</color>\n" + effectColors["Hunger"] + "可被烤</color>";
                }
                else if (itemCooking.timesCookedLocal == 2)
                {
                    suffixCooked += "   " + effectColors["Hunger"] + itemCooking.timesCookedLocal.ToString() + "個烤過的</color>\n" + effectColors["Injury"] + "可被烤</color>";
                }
                else if (itemCooking.timesCookedLocal == 3)
                {
                    suffixCooked += "   " + effectColors["Injury"] + itemCooking.timesCookedLocal.ToString() + "個烤過的</color>\n" + effectColors["Poison"] + "可被烤</color>";
                }
                else if (itemCooking.timesCookedLocal >= 4)
                {
                    suffixCooked += "   " + effectColors["Poison"] + itemCooking.timesCookedLocal.ToString() + "個烤過的\n可被烤</color>";
                }
            }
        }

        if ((prefixStatus.Length > 0) && isConsumable)
        {
            itemInfoDisplayTextMesh.text = prefixStatus + "\n" + itemInfoDisplayTextMesh.text;
        }
        if (suffixAfflictions.Length > 0)
        {
            itemInfoDisplayTextMesh.text += "\n" + suffixAfflictions;
        }
        itemInfoDisplayTextMesh.text += "\n" + suffixWeight + suffixUses + suffixCooked;
        itemInfoDisplayTextMesh.text = itemInfoDisplayTextMesh.text.Replace("\n\n\n", "\n\n");
    }

    private static string ProcessEffect(float amount, string effect)
    {
        string result = "";

        if (amount == 0)
        {
            return result;
        }
        else if (amount > 0)
        {
            if (effect.Equals("Extra Stamina"))
            {
                result += effectColors["ItemInfoDisplayPositive"];
            }
            else
            {
                result += effectColors["ItemInfoDisplayNegative"];
            }
            result += "獲得</color> ";
        }
        else if (amount < 0)
        {
            if (effect.Equals("Extra Stamina"))
            {
                result += effectColors["ItemInfoDisplayNegative"];
            }
            else
            {
                result += effectColors["ItemInfoDisplayPositive"];
            }
            result += "移除</color> ";
        }
        result += effectColors[effect] + (Mathf.Abs(amount) * 100f).ToString("F1").Replace(".0", "") + " " + effect.ToUpper() + "</color>\n";

        return result;
    }

    private static string ProcessEffectOverTime(float amountPerSecond, float rate, float time, string effect)
    {
        string result = "";

        if ((amountPerSecond == 0) || (time == 0))
        {
            return result;
        }
        else if (amountPerSecond > 0)
        {
            if (effect.Equals("Extra Stamina"))
            {
                result += effectColors["ItemInfoDisplayPositive"];
            }
            else
            {
                result += effectColors["ItemInfoDisplayNegative"];
            }
            result += "獲得</color> ";
        }
        else if (amountPerSecond < 0)
        {
            if (effect.Equals("Extra Stamina"))
            {
                result += effectColors["ItemInfoDisplayNegative"];
            }
            else
            {
                result += effectColors["ItemInfoDisplayPositive"];
            }
            result += "移除</color> ";
        }
        result += effectColors[effect] + ((Mathf.Abs(amountPerSecond) * time * (1 / rate)) * 100f).ToString("F1").Replace(".0", "") + " " + effect.ToUpper() + "</color> OVER " + time.ToString() + "s\n";

        return result;
    }

    private static string ProcessAffliction(Peak.Afflictions.Affliction affliction)
    {
        string result = "";

        if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.FasterBoi)
        {
            Peak.Afflictions.Affliction_FasterBoi effect = (Peak.Afflictions.Affliction_FasterBoi)affliction;
            result += effectColors["ItemInfoDisplayPositive"] + "獲得</color> " + (effect.totalTime + effect.climbDelay).ToString("F1").Replace(".0", "") + "秒的" 
                + effectColors["Extra Stamina"] + Mathf.Round(effect.moveSpeedMod * 100f).ToString("F1").Replace(".0", "") + "% 奔跑速度加成</color>，或獲得\n"
                + effectColors["ItemInfoDisplayPositive"] + "GAIN</color> " + effect.totalTime.ToString("F1").Replace(".0", "") + "秒的" + effectColors["Extra Stamina"] 
                + Mathf.Round(effect.climbSpeedMod * 100f).ToString("F1").Replace(".0", "") + "% 攀爬速度加成</color>\n" + effectColors["ItemInfoDisplayNegative"] 
                + "然後獲得</color> " + effectColors["Drowsy"] + (effect.drowsyOnEnd * 100f).ToString("F1").Replace(".0", "") + " 秒的昏睡效果</color>\n";
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.ClearAllStatus)
        {
            Peak.Afflictions.Affliction_ClearAllStatus effect = (Peak.Afflictions.Affliction_ClearAllStatus)affliction;
            result += effectColors["ItemInfoDisplayPositive"] + "清除所有狀態</color>";
            if (effect.excludeCurse)
            {
                result += "（除了" + effectColors["Curse"] + "詛咒</color>）";
            }
            result += "\n";
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.AddBonusStamina)
        {
            Peak.Afflictions.Affliction_AddBonusStamina effect = (Peak.Afflictions.Affliction_AddBonusStamina)affliction;
            result += effectColors["ItemInfoDisplayPositive"] + "獲得</color> " + effectColors["Extra Stamina"] + (effect.staminaAmount * 100f).ToString("F1").Replace(".0", "") + "點額外</color>\n";
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.InfiniteStamina)
        {
            Peak.Afflictions.Affliction_InfiniteStamina effect = (Peak.Afflictions.Affliction_InfiniteStamina)affliction;
            if (effect.climbDelay > 0)
            {
                result += effectColors["ItemInfoDisplayPositive"] + "獲得</color> " + (effect.totalTime + effect.climbDelay).ToString("F1").Replace(".0", "") + "秒的" 
                    + effectColors["Extra Stamina"] + "無限奔跑士氣</color>，或獲得 " + effect.totalTime.ToString("F1").Replace(".0", "") + "秒的" 
                    + effectColors["Extra Stamina"] + "無限攀爬士氣</color>\n";
            
            }
            else
            {
                result += effectColors["ItemInfoDisplayPositive"] + "獲得</color> " + (effect.totalTime).ToString("F1").Replace(".0", "") + "秒的" + effectColors["Extra Stamina"] + "無限士氣</color>\n";
            }
            if (effect.drowsyAffliction != null)
            {
                result += "然後獲得" + ProcessAffliction(effect.drowsyAffliction);
            }
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.AdjustStatus)
        {
            Peak.Afflictions.Affliction_AdjustStatus effect = (Peak.Afflictions.Affliction_AdjustStatus)affliction;
            if (effect.statusAmount > 0)
            {
                if (effect.Equals("Extra Stamina"))
                {
                    result += effectColors["ItemInfoDisplayPositive"];
                }
                else
                {
                    result += effectColors["ItemInfoDisplayNegative"];
                }
                result += "獲得</color> ";
            }
            else
            {
                if (effect.Equals("Extra Stamina"))
                {
                    result += effectColors["ItemInfoDisplayNegative"];
                }
                else
                {
                    result += effectColors["ItemInfoDisplayPositive"];
                }
                result += "移除</color> ";
            }
            result += effectColors[effect.statusType.ToString()] + (Mathf.Abs(effect.statusAmount) * 100f).ToString("F1").Replace(".0", "")
                + " " + effect.statusType.ToString().ToUpper() + "</color>\n";
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.DrowsyOverTime)
        {
            Peak.Afflictions.Affliction_AdjustDrowsyOverTime effect = (Peak.Afflictions.Affliction_AdjustDrowsyOverTime)affliction; // 1.6.a
            if (effect.statusPerSecond > 0)
            {
                result += effectColors["ItemInfoDisplayNegative"] + "獲得</color> ";
            }
            else
            {
                result += effectColors["ItemInfoDisplayPositive"] + "移除</color> ";
            }
            result += effectColors["Drowsy"] + (Mathf.Round((Mathf.Abs(effect.statusPerSecond) * effect.totalTime * 100f) * 0.4f) / 0.4f).ToString("F1").Replace(".0", "")
                + " 昏睡效果</color>" + effect.totalTime.ToString("F1").Replace(".0", "") + "秒\n";
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.ColdOverTime)
        {
            Peak.Afflictions.Affliction_AdjustColdOverTime effect = (Peak.Afflictions.Affliction_AdjustColdOverTime)affliction; // 1.6.a
            if (effect.statusPerSecond > 0)
            {
                result += effectColors["ItemInfoDisplayNegative"] + "獲得</color> ";
            }
            else
            {
                result += effectColors["ItemInfoDisplayPositive"] + "移除</color> ";
            }
            result += effectColors["Cold"] + (Mathf.Abs(effect.statusPerSecond) * effect.totalTime * 100f).ToString("F1").Replace(".0", "")
                + " 冷卻效果</color> " + effect.totalTime.ToString("F1").Replace(".0", "") + "秒\n";
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.Chaos)
        {
            result += effectColors["ItemInfoDisplayPositive"] + "清除所有狀態</color>, 然後隨機獲得\n" + effectColors["Hunger"] + "飢餓</color>, "
                + effectColors["Extra Stamina"] + "額外耐力</color>, " + effectColors["Injury"] + "受傷</color>,\n" + effectColors["Poison"] + "中毒</color>, "
                + effectColors["Cold"] + "寒冷</color>, " + effectColors["Hot"] + "炎熱</color>, " + effectColors["Drowsy"] + "昏睡</color>\n";
        }
        else if (affliction.GetAfflictionType() is Peak.Afflictions.Affliction.AfflictionType.Sunscreen)
        {
            Peak.Afflictions.Affliction_Sunscreen effect = (Peak.Afflictions.Affliction_Sunscreen)affliction;
            result += "慎防中暑！在台地的太陽下逗留超過" + effect.totalTime.ToString("F1").Replace(".0", "") + "秒會開始" + effectColors["Heat"] + "獲得炎熱效果</color>\n";
        }

        return result;
    }

    private static void AddDisplayObject()
    {
        GameObject guiManagerGameObj = GameObject.Find("GAME/GUIManager");
        guiManager = guiManagerGameObj.GetComponent<GUIManager>();
        TMPro.TMP_FontAsset font = guiManager.heroDayText.font;

        GameObject invGameObj = guiManagerGameObj.transform.Find("Canvas_HUD/Prompts/ItemPromptLayout").gameObject;
        GameObject itemInfoDisplayGameObj = new GameObject("ItemInfoDisplay");
        itemInfoDisplayGameObj.transform.SetParent(invGameObj.transform);
        itemInfoDisplayTextMesh = itemInfoDisplayGameObj.AddComponent<TextMeshProUGUI>();
        RectTransform itemInfoDisplayRect = itemInfoDisplayGameObj.GetComponent<RectTransform>();

        itemInfoDisplayRect.sizeDelta = new Vector2(configSizeDeltaX.Value, 0f); // Y is 0, otherwise moves other item prompts
        itemInfoDisplayTextMesh.font = font;
        itemInfoDisplayTextMesh.fontSize = configFontSize.Value;
        itemInfoDisplayTextMesh.alignment = TextAlignmentOptions.BottomLeft;
        itemInfoDisplayTextMesh.lineSpacing = configLineSpacing.Value;
        itemInfoDisplayTextMesh.text = "";
        itemInfoDisplayTextMesh.outlineWidth = configOutlineWidth.Value;
    }
    private static void InitEffectColors(Dictionary<string, string> dict)
    {
        dict.Add("Hunger", "<#FFBD16>");
        dict.Add("Extra Stamina", "<#BFEC1B>");
        dict.Add("Injury", "<#FF5300>");
        dict.Add("Crab", "<#E13542>");
        dict.Add("Poison", "<#A139FF>");
        dict.Add("Cold", "<#00BCFF>");
        dict.Add("Heat", "<#C80918>");
        dict.Add("Hot", "<#C80918>");
        dict.Add("Sleepy", "<#FF5CA4>");
        dict.Add("Drowsy", "<#FF5CA4>");
        dict.Add("Curse", "<#1B0043>");
        dict.Add("Weight", "<#A65A1C>");
        dict.Add("Thorns", "<#768E00>");
        dict.Add("Shield", "<#D48E00>");

        dict.Add("ItemInfoDisplayPositive", "<#DDFFDD>");
        dict.Add("ItemInfoDisplayNegative", "<#FFCCCC>");
    }
}