using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Runtime.Serialization;
using Peak.Afflictions;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Zorro.UI.Effects;
using static GlobalStatusEffects;

namespace ItemInfoDisplay;

enum EffectColors {
    HUNGER = 0xFFBD16,
    EXTRA_STAMINA = 0xBFEC1B,
    INJURY = 0xFF5300,
    CRAB = 0xE13542,
    POISON = 0xA139FF,
    COLD = 0x00BCFF,
    HEAT = 0xC80918,
    SLEEPY = 0xFF5CA4,
    DROWSY = 0xFF5CA5,
    CURSE = 0x1B0043,
    WEIGHT = 0xA65A1C,
    THORNS = 0x768E00,
    SHIELD = 0xD48E00,
    ITEM_INFO_DISPLAY_POSITIVE = 0xDDFFDD,
    ITEM_INFO_DISPLAY_NEGATIVE = 0xFFCCCC
}

static class EffectColorMethods {
    public static string HexTag(this EffectColors eff) {
        // Substring is bcz of the alpha channel "FF" at the start of the hex
        return "<#" + eff.ToString("X").Substring(2) + ">";
    }
}

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin{
    internal static ManualLogSource Log { get; private set; } = null!;
    private static GUIManager guiManager;
    private static TextMeshProUGUI itemInfoDisplayTextMesh;
    private static float lastKnownSinceItemAttach;
    private static bool hasChanged;
    private static ConfigEntry<float> configFontSize;
    private static ConfigEntry<float> configOutlineWidth;
    private static ConfigEntry<float> configLineSpacing;
    private static ConfigEntry<float> configSizeDeltaX;
    private static ConfigEntry<float> configForceUpdateTime;

    private void Awake(){
        Log = Logger;
        lastKnownSinceItemAttach = 0f;
        hasChanged = true;

        configFontSize = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "字體大小", 20f, "調整物品描述文字的字體大小。");
        configOutlineWidth = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "輪廓寬度", 0.08f, "調整物品描述文字的輪廓寬度。");
        configLineSpacing = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "行距", -35f, "調整物品描述文字的行距。");
        configSizeDeltaX = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "大小變化X", 550f, "調整物品描述文字容器的水平長度。增加會將文字向左移動，減少會將文字向右移動。");
        configForceUpdateTime = ((BaseUnityPlugin)this).Config.Bind<float>("ItemInfoDisplay", "強制更新時間", 1f, "調整物品強制更新的時間（秒）。");
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayUpdatePatch));
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayEquipPatch));
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayFinishCookingPatch));
        Harmony.CreateAndPatchAll(typeof(ItemInfoDisplayReduceUsesRPCPatch));
        Log.LogInfo($"Plugin {Name} is loaded! (Modified Version by Vocaloid2048)");
    }

    /**
     * 目前有以下物品未處理：
     */
    private static void ProcessItemGameObject(){
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

        // Text Line Display: Weight
        // 展示：負重：$1 點
        suffixWeight += EffectColors.HUNGER.HexTag() + "負重：" + PrettyCount((item.carryWeight + Mathf.Min(Ascents.itemWeightModifier, 0)) * 2.5f, 1) + "點</color>\n";

        switch (itemGameObj.name) {
            case "Bugle(Clone)": 
                itemInfoDisplayTextMesh.text += "圍着隊友吹響它吧！\n" + EffectColorLocalName(EffectColors.HEAT) + "然後就會有人過來打扁你（誤</color>"; break;
            case "Pirate Compass(Clone)": 
                itemInfoDisplayTextMesh.text += EffectColors.INJURY.HexTag() + "指向</color>"+"最近的行李箱\n"; break;
            case "Compass(Clone)":
                itemInfoDisplayTextMesh.text += EffectColors.INJURY.HexTag() + "指向</color>"+"山頂\n"; break;
            case "Shell Big(Clone)":
                itemInfoDisplayTextMesh.text += "試試把它" + EffectColors.HUNGER.HexTag() + "丟向</color>"+"椰子\n"; break;
            case "Backpack(Clone)":
                itemInfoDisplayTextMesh.text += "放下背包才可放入物品\n"; break;
            case "Megaphone(Clone)":
                itemInfoDisplayTextMesh.text += "感覺聲音太小了嗎？可以試試用它來喊話哦~\n"+ EffectColors.HEAT.HexTag() + "我可沒有叫你對着隊友叫賣</color>\n"; break;
            case "AloeVera(Clone)":
                itemInfoDisplayTextMesh.text += "食用後可以減少 "+EffectColors.HEAT.HexTag()+ "50"+" 點"+EffectColorLocalName(EffectColors.HEAT) + "</color>\n"; break;
            case "Cure-All(Clone)":
                itemInfoDisplayTextMesh.text += "飲用後可以幫助消除多種負面狀態</color>\n"; break;
        }

        switch (itemGameObj.name.ToUpper()) {
            case string name when name.Contains("WINTERBERRY"):
                itemInfoDisplayTextMesh.text += "放心，這個能吃的（應該吧\n"; break;
            case string name when name.Contains("PRICKLEBERRY"):
                itemInfoDisplayTextMesh.text += "小心有刺！你必須靠其他玩家幫忙才能拔走身上的刺！\n"; break;
            case string name when name.Contains("SCORCHBERRY"):
                itemInfoDisplayTextMesh.text += EffectColorLocalName(EffectColors.HEAT) + "好辣！</color>\n"; break;
            case string name when name.Contains("SUNSCREEN"):
                itemInfoDisplayTextMesh.text += "為玩家提供 90 秒的防曬効果，留意最多只能用三次哦！\n"; break;
        }

        for (int i = 0; i < itemComponents.Length; i++) {
            // Check the type of each component and process accordingly
            switch (itemComponents[i]) {
                case ItemUseFeedback itemUseFeedback: {
                    isConsumable = (itemUseFeedback.useAnimation.Equals("Eat") || itemUseFeedback.useAnimation.Equals("Drink") || itemUseFeedback.useAnimation.Equals("Heal"));
                    break;
                }
                case Action_Consume effect: {
                    isConsumable = true; break;
                }
                case Action_RestoreHunger effect: {
                    prefixStatus += ProcessEffect((effect.restorationAmount * -1f), EffectColors.HUNGER) + "\n"; break;
                }
                case Action_GiveExtraStamina effect: {
                    prefixStatus += ProcessEffect(effect.amount, EffectColors.EXTRA_STAMINA) + "\n"; break;
                }
                case Action_InflictPoison effect: {
                    prefixStatus += "經過 " + effect.delay.ToString() + " 秒後，" + ProcessEffectOverTime(effect.poisonPerSecond, 1f, effect.inflictionTime, EffectColors.POISON) + "\n"; break;
                }
                case Action_AddOrRemoveThorns effect: {
                    // TODO: Search for thorns amount per applied thorn
                    prefixStatus += ProcessEffect((effect.thornCount * 0.05f), EffectColors.THORNS) + "\n"; break;
                }
                case Action_ModifyStatus effect: {
                    prefixStatus += ProcessEffect(effect.changeAmount, GetEffectColorsByStr(effect.statusType.ToString())) + "\n"; break;
                }
                case Action_ApplyMassAffliction effect: {
                    suffixAfflictions += "<#CCCCCC>附近的玩家將會收到：</color>\n";
                    suffixAfflictions += ProcessAffliction(effect.affliction);
                    if (effect.extraAfflictions.Length > 0) {
                        for (int j = 0; j < effect.extraAfflictions.Length; j++) {
                            if (suffixAfflictions.EndsWith('\n')) {
                                suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 1);
                            }
                            suffixAfflictions += ",\n" + ProcessAffliction(effect.extraAfflictions[j]);
                        }
                    }
                    break;
                }
                case Action_ApplyAffliction effect: {
                    suffixAfflictions += ProcessAffliction(effect.affliction); break;
                }
                case Action_ClearAllStatus effect: {
                    itemInfoDisplayTextMesh.text += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "清理所有狀態</color>";
                    itemInfoDisplayTextMesh.text += (effect.excludeCurse ? "（除了" + EffectColors.CURSE.HexTag() + "詛咒</color>\n" : "\n");
                    
                    if (effect.otherExclusions.Count > 0) {
                        foreach (CharacterAfflictions.STATUSTYPE exclusion in effect.otherExclusions) {
                            EffectColors eff = GetEffectColorsByStr(exclusion.ToString());
                            if(eff == EffectColors.CRAB) continue;
                               itemInfoDisplayTextMesh.text += "、" + eff.HexTag() + EffectColorLocalName(eff) + "</color>";
                        }
                        itemInfoDisplayTextMesh.text += "）";
                    }
                    break;
                }
                case Action_ConsumeAndSpawn effect: {
                    if (effect.itemToSpawn.ToString().Contains("Peel")) {
                        itemInfoDisplayTextMesh.text += "<#CCCCCC>食用後獲得果皮</color>\n";
                    }
                    break;
                }
                case Action_ReduceUses effect: {
                    OptionableIntItemData uses = (OptionableIntItemData)item.data.data[DataEntryKey.ItemUses];
                    if (uses.HasData && uses.Value > 1) { suffixUses += "剩餘 " + uses.Value + " 次使用次數"; }
                    break;
                }
                case Lantern lantern: {
                    if (itemGameObj.name.Equals("Torch(Clone)")){
                        itemInfoDisplayTextMesh.text += "可被點亮\n";
                    } else {
                        suffixAfflictions += "<#CCCCCC>點亮時，附近的玩家會獲得：</color>\n";
                    }

                    if (itemGameObj.name.Equals("Lantern_Faerie(Clone)")){
                        StatusField effect = itemGameObj.transform.Find("FaerieLantern/Light/Heat").GetComponent<StatusField>();
                        EffectColors eff = GetEffectColorsByStr(effect.statusType.ToString());
                        suffixAfflictions += ProcessEffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, eff);
                        foreach (StatusField.StatusFieldStatus status in effect.additionalStatuses){
                            if (suffixAfflictions.EndsWith('\n')){
                                suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 1);
                            }
                            suffixAfflictions += ",\n" + ProcessEffectOverTime(status.statusAmountPerSecond, 1f, lantern.startingFuel, eff);
                        }
                    } else if (itemGameObj.name.Equals("Lantern(Clone)")){
                        StatusField effect = itemGameObj.transform.Find("GasLantern/Light/Heat").GetComponent<StatusField>();
                        EffectColors eff = GetEffectColorsByStr(effect.statusType.ToString());
                        suffixAfflictions += ProcessEffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, eff);
                    }
                    break;
                }
                case Action_RaycastDart effect: {
                    isConsumable = true;
                    suffixAfflictions += "<#CCCCCC>發射一支飛鏢，造成以下效果：</color>\n";
                    for (int j = 0; j < effect.afflictionsOnHit.Length; j++){
                        suffixAfflictions += ProcessAffliction(effect.afflictionsOnHit[j]);
                        /*
                           if (suffixAfflictions.EndsWith('\n')){
                               suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 1);
                           }
                         */
                        suffixAfflictions += "，\n";
                    }
                    /*
                       if (suffixAfflictions.EndsWith('\n')){
                           suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 2);
                       }
                     */
                    suffixAfflictions += "\n";
                    break;
                }
                case MagicBugle effect: {
                    itemInfoDisplayTextMesh.text += "當吹響友誼號角時，\n"; break;
                }
                case ClimbingSpikeComponent effect: {
                    itemInfoDisplayTextMesh.text += "放置一個你可以抓住的岩釘\n來 " + EffectColors.EXTRA_STAMINA.HexTag() + "恢復體力</color>\n"; break;
                }
                case Action_Flare effect: {
                    itemInfoDisplayTextMesh.text += "可被點亮\n";break;
                }
                case Backpack effect: {
                    itemInfoDisplayTextMesh.text += "放下背包才可放入物品\n"; break;
                }
                case BananaPeel effect: {
                    itemInfoDisplayTextMesh.text += "踩到後會" + EffectColors.HUNGER.HexTag() + "滑倒</color>\n"; break;
                }
                case ScoutEffigy effect: {
                    itemInfoDisplayTextMesh.text += EffectColors.EXTRA_STAMINA.HexTag() + "復活</color>一名死亡的玩家\n"; break;
                }
                case Constructable effect: {
                    if (effect.constructedPrefab.name.Equals("PortableStovetop_Placed")){
                        itemInfoDisplayTextMesh.text += "放置" + EffectColors.INJURY.HexTag() + "攜帶式爐具</color>以提供" + PrettyCount(effect.constructedPrefab.GetComponent<Campfire>().burnsFor, 1) + "秒的溫暖\n";
                    }
                    else{
                        itemInfoDisplayTextMesh.text += "可被放置\n";
                    }
                    break;
                }
                case RopeSpool effect: {
                    itemInfoDisplayTextMesh.text += (effect.isAntiRope ? "放置一條向上漂浮的繩索\n" : "放置一條繩索\n");
                    
                    itemInfoDisplayTextMesh.text += "綁繩結最短需" + PrettyCount(effect.minSegments / 4f, 2) + "公尺長，最多" + PrettyCount(Rope.MaxSegments / 4f, 1) + "公尺長\n";
                    
                    //using force update here for remaining length since Rope has no character distinction for Detach_Rpc() hook, maybe unless OK with any player triggering this
                    if (configForceUpdateTime.Value <= 1f){
                        suffixUses += "   剩餘" + PrettyCount(effect.RopeFuel / 4f, 2) + "公尺";
                    }
                    break;
                }
                case RopeShooter effect: {
                    // 發射一條繩索錨，並放置一條繩子 …
                    itemInfoDisplayTextMesh.text += "發射一條";
                    itemInfoDisplayTextMesh.text += PrettyCount(effect.maxLength / 4f, 1) + "公尺長" + (effect.ropeAnchorWithRopePref.name.Equals("RopeAnchorForRopeShooterAnti") ? "向上漂浮</color>" : "") + "的繩子\n";
                    break;
                }
                case Antigrav effect: {
                    if(effect.intensity == 0f) break;
                    suffixAfflictions += EffectColors.INJURY.HexTag() + "警告：</color><#CCCCCC>放下後會飛走</color>\n";
                    break;
                }
                case Action_Balloon effect: {
                    suffixAfflictions += "可以綁在玩家身上\n";
                    break;
                }
                case VineShooter effect: {
                    itemInfoDisplayTextMesh.text += "從當前位置發射一條鏈條到你瞄準的地方\n最長可達"
                        + PrettyCount(effect.maxLength / (5f / 3f), 1) + "公尺\n";
                    break;
                }
                // 以下為 case.txt 內容的 switch-case 改寫
                case ShelfShroom effect: {
                    if (effect.instantiateOnBreak.name.Equals("HealingPuffShroomSpawn")) {
                        GameObject effect1 = effect.instantiateOnBreak.transform.Find("VFX_SporeHealingExplo").gameObject;
                        AOE effect1AOE = effect1.GetComponent<AOE>();
                        GameObject effect2 = effect1.transform.Find("VFX_SporePoisonExplo").gameObject;
                        AOE effect2AOE = effect2.GetComponent<AOE>();
                        AOE[] effect2AOEs = effect2.GetComponents<AOE>();
                        TimeEvent effect2TimeEvent = effect2.GetComponent<TimeEvent>();
                        RemoveAfterSeconds effect2RemoveAfterSeconds = effect2.GetComponent<RemoveAfterSeconds>();
                        itemInfoDisplayTextMesh.text += EffectColors.HUNGER.HexTag() + "丟出</color>後會釋放出氣體，效果為：\n";
                        itemInfoDisplayTextMesh.text += ProcessEffect(Mathf.Round(effect1AOE.statusAmount * 0.9f * 40f) / 40f, GetEffectColorsByStr(effect1AOE.statusType.ToString())) + "\n";
                        itemInfoDisplayTextMesh.text += ProcessEffectOverTime(Mathf.Round(effect2AOE.statusAmount * (1f / effect2TimeEvent.rate) * 40f) / 40f, 1f, effect2RemoveAfterSeconds.seconds, GetEffectColorsByStr(effect2AOE.statusType.ToString())) + "\n";
                        if (effect2AOEs.Length > 1) {
                            itemInfoDisplayTextMesh.text += ProcessEffectOverTime(Mathf.Round(effect2AOEs[1].statusAmount * (1f / effect2TimeEvent.rate) * 40f) / 40f, 1f, (effect2RemoveAfterSeconds.seconds + 1f), GetEffectColorsByStr(effect2AOEs[1].statusType.ToString())) + "\n";
                        }
                    } else if (effect.instantiateOnBreak.name.Equals("ShelfShroomSpawn")) {
                        itemInfoDisplayTextMesh.text += EffectColors.HUNGER.HexTag() + "丟出</color>後會生成一個平台\n";
                    } else if (effect.instantiateOnBreak.name.Equals("BounceShroomSpawn")) {
                        itemInfoDisplayTextMesh.text += EffectColors.HUNGER.HexTag() + "丟出</color>後會生成一個彈跳墊\n";
                    }
                    break;
                }
                case Action_Die effect: {
                    itemInfoDisplayTextMesh.text += "用了就" + EffectColors.CURSE.HexTag() + "死翹翹</color>了\n"; break;
                }
                case Action_SpawnGuidebookPage effect: {
                    isConsumable = true;
                    itemInfoDisplayTextMesh.text += "可被開啟\n"; break;
                }
                case Action_Guidebook effect: {
                    itemInfoDisplayTextMesh.text += "可被閱讀\n"; break;
                }
                case Action_CallScoutmaster effect: {
                    itemInfoDisplayTextMesh.text += EffectColors.INJURY.HexTag() + "使用後會打破規則0</color>\n"; break;
                }
                case Action_MoraleBoost effect: {
                    if (effect.boostRadius < 0) {
                        itemInfoDisplayTextMesh.text += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "獲得</color> " + EffectColors.EXTRA_STAMINA.HexTag() + PrettyCount(effect.baselineStaminaBoost * 100f, 1) + " 點額外士氣</color>\n";
                    } else if (effect.boostRadius > 0) {
                        itemInfoDisplayTextMesh.text += "<#CCCCCC>最近的玩家</color>" + EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "會獲得</color> " + EffectColors.EXTRA_STAMINA.HexTag() + PrettyCount(effect.baselineStaminaBoost * 100f, 1) + " 點額外士氣</color>\n";
                    }
                    break;
                }
                case Breakable effect: {
                    itemInfoDisplayTextMesh.text += EffectColors.HUNGER.HexTag() + "丟出</color>後會裂開\n"; break;
                }
                case Bonkable effect: {
                    itemInfoDisplayTextMesh.text += "被丟到頭的玩家會" + EffectColors.INJURY.HexTag() + "暈倒</color>\n"; break;
                }
                case MagicBean effect: {
                    itemInfoDisplayTextMesh.text += EffectColors.HUNGER.HexTag() + "丟出</color>後會種下垂直向上生長的藤蔓，\n最長可達"
                        + PrettyCount(effect.plantPrefab.maxLength / 2f, 1) + "公尺或直到碰到東西為止\n"; 
                    break;
                }
                case BingBong effect: {
                    itemInfoDisplayTextMesh.text += "兵幫航空的吉祥物\n"; break;
                }
                case Action_Passport effect: {
                    itemInfoDisplayTextMesh.text += "打開護照來自訂角色\n"; break;
                }
                case Actions_Binoculars effect: {
                    itemInfoDisplayTextMesh.text += "用來看得更遠\n"; break;
                }
                case Action_WarpToRandomPlayer effect: {
                    itemInfoDisplayTextMesh.text += "傳送到隨機玩家身邊\n"; break;
                }
                case Action_WarpToBiome effect: {
                    itemInfoDisplayTextMesh.text += "傳送到" + effect.segmentToWarpTo.ToString().ToUpper() + "\n"; break;
                }
                case Parasol effect: {
                    itemInfoDisplayTextMesh.text += "打開遮陽傘來減緩下降速度\n還能在台地避免太陽直射造成"+EffectColors.HEAT.HexTag()+EffectColorLocalName(EffectColors.HEAT)+"傷害\n"; break;
                }
                case Frisbee effect: {
                    itemInfoDisplayTextMesh.text += "把它" + EffectColors.HUNGER.HexTag() + "丟出去</color>\n"; break;
                }
                case Action_ConstructableScoutCannonScroll effect: {
                    itemInfoDisplayTextMesh.text += "\n<#CCCCCC>放下後透過點燃導火線</color>\n來把大砲中的偵察兵發射出去\n"; break;
                }
                case Dynamite effect: {
                    itemInfoDisplayTextMesh.text += EffectColors.INJURY.HexTag() + "爆炸</color>造成最多" + EffectColors.INJURY.HexTag()
                        + PrettyCount(effect.explosionPrefab.GetComponent<AOE>().statusAmount * 100f, 1) + " 傷害</color>\n<#CCCCCC>拿着引爆會受到額外傷害</color>\n"; 
                    break;
                }
                case Scorpion effect: {
                    if (configForceUpdateTime.Value <= 1f) {
                        float effectPoison = Mathf.Max(0.5f, (1f - item.holderCharacter.refs.afflictions.statusSum + 0.05f)) * 100f;
                        itemInfoDisplayTextMesh.text += "如果蠍子活着的話會" + EffectColors.POISON.HexTag() + "螫</color>你\n在" + EffectColors.HEAT.HexTag() + "烤熟</color>後會" + EffectColors.HEAT.HexTag() + "死掉</color>\n\n"
                            + "<#CCCCCC>每被螫一次會持續</color>" + EffectColors.POISON.HexTag() + PrettyCount(effect.totalPoisonTime, 1) + "秒</color>，共獲得"
                            + PrettyCount(effectPoison, 1) + " 點毒素</color>\n"
                            + "<#CCCCCC>(若目前十分健康會造成更多傷害)</color>\n";
                    } else {
                        itemInfoDisplayTextMesh.text += "IF ALIVE, " + EffectColors.POISON.HexTag() + "STINGS</color> YOU\n" + EffectColors.CURSE.HexTag()
                            + "DIES</color> WHEN " + EffectColors.HEAT.HexTag() + "COOKED</color>\n\n" + "<#CCCCCC>NEXT STING WILL DEAL:</color>\nAT LEAST "
                            + EffectColors.POISON.HexTag() + "50 POISON</color> OVER " + PrettyCount(effect.totalPoisonTime, 1) + "s\nAT MOST "
                            + EffectColors.POISON.HexTag() + "105 POISON</color> OVER " + PrettyCount(effect.totalPoisonTime, 1)
                            + "s\n<#CCCCCC>(MORE DAMAGE IF HEALTHY)</color>\n";
                    }
                    break;
                }
                case Action_Spawn effect: {
                    if (effect.objectToSpawn.name.Equals("VFX_Sunscreen")) {
                        AOE effectAOE = effect.objectToSpawn.transform.Find("AOE").GetComponent<AOE>();
                        RemoveAfterSeconds effectTime = effect.objectToSpawn.transform.Find("AOE").GetComponent<RemoveAfterSeconds>();
                        itemInfoDisplayTextMesh.text += "<#CCCCCC>噴灑一個持續" + PrettyCount(effectTime.seconds, 1) + "秒</color>的霧氣，會造成以下效果：\n"
                            + ProcessAffliction(effectAOE.affliction);
                    }
                    break;
                }
                case CactusBall effect: {
                    itemInfoDisplayTextMesh.text += "需要至少用" + PrettyCount(effect.throwChargeRequirement * 100f, 1) + "% 的力" + EffectColors.HUNGER.HexTag() + "丟出去</color>\n不然就會" + EffectColors.THORNS.HexTag() + "黏</color>在你身上\n";
                    break;
                }
                case BingBongShieldWhileHolding effect: {
                    itemInfoDisplayTextMesh.text += "<#CCCCCC>裝備時將會獲得：</color>\n" + EffectColors.SHIELD.HexTag() + "護盾</color>（無敵狀態）\n"; break;
                }
                case ItemCooking itemCooking: {
                    // 優先處理 wreckWhenCooked 狀態
                    if (itemCooking.wreckWhenCooked) {
                        suffixCooked += "\n" + EffectColors.CURSE.HexTag() + (itemCooking.timesCookedLocal >= 1 ? "因為被烤而壞掉</color>" : "拿去烤的話會壞掉</color>");
                        break;
                    }

                    // 依照 timesCookedLocal 狀態處理
                    switch (itemCooking.timesCookedLocal) {
                        case 0:
                            suffixCooked += "\n" + EffectColors.EXTRA_STAMINA.HexTag() + "可被烤</color>";
                            break;
                        case 1:
                            suffixCooked += "\n" + EffectColors.EXTRA_STAMINA.HexTag() + "烤過 1 次</color>\n - " + EffectColors.HUNGER.HexTag() + "可被烤</color>";
                            break;
                        case 2:
                            suffixCooked += "\n" + EffectColors.HUNGER.HexTag() + "烤過 2 次</color>\n - " + EffectColors.INJURY.HexTag() + "可被烤</color>";
                            break;
                        case 3:
                            suffixCooked += "\n" + EffectColors.INJURY.HexTag() + "烤過 3 次</color>\n - " + EffectColors.POISON.HexTag() + "可被烤</color>";
                            break;
                        default: {
                            if (itemCooking.timesCookedLocal >= ItemCooking.COOKING_MAX) {
                                suffixCooked += "\n" + EffectColors.CURSE.HexTag() + "烤過 " + itemCooking.timesCookedLocal.ToString() + " 次 - 不可被烤</color>";
                            } else if (itemCooking.timesCookedLocal >= 4) {
                                suffixCooked += "\n" + EffectColors.POISON.HexTag() + "烤過 " + itemCooking.timesCookedLocal.ToString() + " 次 - 可被烤</color>";
                            }
                            break;
                        }
                    }
                    break;
                }
            }
        }

        if ((prefixStatus.Length > 0) && isConsumable){
            itemInfoDisplayTextMesh.text = prefixStatus + itemInfoDisplayTextMesh.text;
        }
        if (suffixAfflictions.Length > 0){
            itemInfoDisplayTextMesh.text += "\n" + suffixAfflictions;
        }
        itemInfoDisplayTextMesh.text += "\n" + suffixWeight + suffixUses + suffixCooked;
        itemInfoDisplayTextMesh.text = itemInfoDisplayTextMesh.text.Replace("\n\n\n", "\n\n");
    }

    /**
     * Tool Functions/ Helper
     */

    private static class ItemInfoDisplayUpdatePatch {
        [HarmonyPatch(typeof(CharacterItems), "Update")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayUpdate(CharacterItems __instance){
            try{
                if (guiManager == null){
                    AddDisplayObject();
                }
                else{
                    if (Character.observedCharacter.data.currentItem != null){
                        if (hasChanged){
                            hasChanged = false;
                            ProcessItemGameObject();
                        }
                        else if (Mathf.Abs(Character.observedCharacter.data.sinceItemAttach - lastKnownSinceItemAttach) >= configForceUpdateTime.Value){
                            hasChanged = true;
                            lastKnownSinceItemAttach = Character.observedCharacter.data.sinceItemAttach;
                        }

                        if (!itemInfoDisplayTextMesh.gameObject.activeSelf){
                            itemInfoDisplayTextMesh.gameObject.SetActive(true);
                        }
                    }
                    else{
                        if (itemInfoDisplayTextMesh.gameObject.activeSelf) {
                            itemInfoDisplayTextMesh.gameObject.SetActive(false);
                        }
                    }
                }
            }
            catch (Exception e){
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    private static class ItemInfoDisplayEquipPatch{
        [HarmonyPatch(typeof(CharacterItems), "Equip")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayEquip(CharacterItems __instance){
            try{
                if (Character.ReferenceEquals(Character.observedCharacter, __instance.character)){
                    hasChanged = true;
                }
            }
            catch (Exception e){
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    private static class ItemInfoDisplayFinishCookingPatch{
        [HarmonyPatch(typeof(ItemCooking), "FinishCooking")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayFinishCooking(ItemCooking __instance){
            try{
                if (Character.ReferenceEquals(Character.observedCharacter, __instance.item.holderCharacter)){
                    hasChanged = true;
                }
            }
            catch (Exception e){
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    private static class ItemInfoDisplayReduceUsesRPCPatch{
        [HarmonyPatch(typeof(Action_ReduceUses), "ReduceUsesRPC")]
        [HarmonyPostfix]
        private static void ItemInfoDisplayReduceUsesRPC(Action_ReduceUses __instance){
            try{
                if (Character.ReferenceEquals(Character.observedCharacter, __instance.character)){
                    hasChanged = true;
                }
            }
            catch (Exception e){
                Log.LogError(e.Message + e.StackTrace);
            }
        }
    }

    /**
     * Process Effect
     * 展示：<獲得/移除> $1 點<效果本地化名字>
     */
    private static string ProcessEffect(float amount, EffectColors effect) {
        string result = "";
        if (amount == 0) return result;

        result += (effect == EffectColors.EXTRA_STAMINA ? EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() : EffectColors.ITEM_INFO_DISPLAY_NEGATIVE.HexTag());
        result += (amount > 0 ? "獲得</color> " : "移除</color> ");
        result += effect.HexTag() + PrettyCount(Mathf.Abs(amount) * 100f, 1) + " 點" + EffectColorLocalName(effect);

        return result;
    }


    /**
     * Process Effect Time
     * 展示：$1秒内合共獲得/移除 $1 點<效果本地化名字>
     */
    private static string ProcessEffectOverTime(float amountPerSecond, float rate, float time, EffectColors effect) {
        string result = "";
        if (amountPerSecond == 0 || time == 0) return result;

        result += time.ToString() +"秒内合共";
        result += (effect == EffectColors.EXTRA_STAMINA ? EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() : EffectColors.ITEM_INFO_DISPLAY_NEGATIVE.HexTag());
        result += (amountPerSecond > 0 ? "獲得</color> " : "移除</color> ");
        result += effect.HexTag() + PrettyCount(Mathf.Abs(amountPerSecond) * time * (1 / rate) * 100f, 1) + " 點" + EffectColorLocalName(effect);

        return result;
    }

    private static string ProcessAffliction(Affliction affliction) {
        string result = "";

        switch (affliction.GetAfflictionType()) {
            /**
             * 展示：獲得 $1 秒的$2% 奔跑速度加成，\n或者獲得 $3 秒的 $4% 攀爬速度加成\n然後獲得 $5 秒的昏睡效果
             */
            case Affliction.AfflictionType.FasterBoi: {
                    Affliction_FasterBoi effect = (Affliction_FasterBoi) affliction;
                    result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "獲得</color> " + PrettyCount(effect.totalTime + effect.climbDelay,1) + " 秒的 "
                  + EffectColors.EXTRA_STAMINA.HexTag() + PrettyCount(Mathf.Round(effect.moveSpeedMod * 100f),1) + "% 奔跑速度加成</color>，\n"
                  + EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "或者獲得</color> " + PrettyCount(effect.totalTime, 1) + " 秒的 " 
                  + EffectColors.EXTRA_STAMINA.HexTag() + PrettyCount(Mathf.Round(effect.climbSpeedMod * 100f),1) + "% 攀爬速度加成</color>\n" 
                  + EffectColors.ITEM_INFO_DISPLAY_NEGATIVE.HexTag() + "然後獲得</color> " + EffectColors.DROWSY.HexTag() + PrettyCount(effect.drowsyOnEnd * 100f,1) + " 秒的昏睡效果</color>\n";
                    break;
            }
            /**
             * 展示：清除所有狀態（除了詛咒）\n
             */
            case Affliction.AfflictionType.ClearAllStatus: {
                Affliction_ClearAllStatus effect = (Affliction_ClearAllStatus) affliction;
                result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "清除所有狀態</color>" + (effect.excludeCurse ? "（除了" + EffectColors.CURSE.HexTag() + "詛咒</color>）\n" : "\n");
                break;
            }
            /**
             * 展示：獲得 $1 點額外體力
             */
            case Affliction.AfflictionType.AddBonusStamina: {
                Affliction_AddBonusStamina effect = (Affliction_AddBonusStamina) affliction;
                result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "獲得</color> " + EffectColors.EXTRA_STAMINA.HexTag() + PrettyCount(effect.staminaAmount * 100f, 1) + " 點額外體力</color>\n";
                break;
            }
            /**
             * 展示1：獲得 $1 秒的無限奔跑體力，或獲得 $2 秒的無限攀爬體力\n
             * 展示2：獲得 $1 秒的無限體力\n
             */
            case Affliction.AfflictionType.InfiniteStamina: {
                Affliction_InfiniteStamina effect = (Affliction_InfiniteStamina) affliction;
                if (effect.climbDelay > 0) {
                    result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "獲得</color> " + PrettyCount(effect.totalTime + effect.climbDelay, 1) + "秒的" 
                              + EffectColors.EXTRA_STAMINA.HexTag() + "無限奔跑體力</color>，或獲得 " + PrettyCount(effect.totalTime, 1) + "秒的"
                              + EffectColors.EXTRA_STAMINA.HexTag() + "無限攀爬體力</color>\n";
                } else {
                    result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "獲得</color> " + PrettyCount(effect.totalTime, 1) + "秒的" 
                              + EffectColors.EXTRA_STAMINA.HexTag() + "無限體力</color>\n";
                }

                if (effect.drowsyAffliction != null) {
                    result += "然後獲得" + ProcessAffliction(effect.drowsyAffliction);
                }
                break;
            }
            /**
             * 展示：獲得/移除 $1% 點$2
             */
            case Affliction.AfflictionType.AdjustStatus: {
                Affliction_AdjustStatus effect = (Affliction_AdjustStatus)affliction;
                EffectColors eff = GetEffectColorsByStr(effect.statusType.ToString());
                result += (eff == EffectColors.EXTRA_STAMINA ? EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag().ToString() : EffectColors.ITEM_INFO_DISPLAY_NEGATIVE.HexTag());
                result += (effect.statusAmount > 0 ? "獲得</color> " : "移除</color> ");
                result += eff.HexTag() + PrettyCount(Mathf.Abs(effect.statusAmount) * 100f, 1) + " 點" + EffectColorLocalName(eff) + "</color>\n";
                break;
            }
            /**
             *  展示：$1 秒內合共獲得/移除 $2 點昏睡效果
             */
            case Affliction.AfflictionType.DrowsyOverTime: {
                Affliction_AdjustDrowsyOverTime effect = (Affliction_AdjustDrowsyOverTime) affliction; // 1.6.a
                result += PrettyCount(effect.totalTime,1) + " 秒内合共 ";
                result += (effect.statusPerSecond > 0 ? EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "獲得</color> " : EffectColors.ITEM_INFO_DISPLAY_NEGATIVE.HexTag() + "移除</color> ");
                result += EffectColors.DROWSY.HexTag() + PrettyCount(Mathf.Round((Mathf.Abs(effect.statusPerSecond) * effect.totalTime * 100f) * 0.4f) / 0.4f, 1) + " 點昏睡效果</color>\n";
                break;
            }
            /**
             *  展示：$1 秒內合共獲得/移除 $2 點寒冷效果
             */
            case Affliction.AfflictionType.ColdOverTime: {
                Affliction_AdjustColdOverTime effect = (Affliction_AdjustColdOverTime) affliction;
                result += PrettyCount(effect.totalTime, 1) + " 秒内合共 ";
                result += (effect.statusPerSecond > 0 ? EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "獲得</color> " : EffectColors.ITEM_INFO_DISPLAY_NEGATIVE.HexTag() + "移除</color> ");
                result += EffectColors.COLD.HexTag() + PrettyCount(Mathf.Round((Mathf.Abs(effect.statusPerSecond) * effect.totalTime * 100f) * 0.4f) / 0.4f, 1) + " 點寒冷效果</color>\n";
                break;
            }
            /**
             *  展示：清除所有狀態，然後隨機獲得飢餓，額外體力，受傷，中毒，寒冷，炎熱，昏睡
             */
            case Affliction.AfflictionType.Chaos: {
                result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE.HexTag() + "清除所有狀態</color>，然後隨機獲得\n"
                    + EffectColors.HUNGER.HexTag() + "飢餓</color>，" + EffectColors.EXTRA_STAMINA.HexTag() + "額外體力</color>，" 
                    + EffectColors.INJURY.HexTag() + "受傷</color>，" + EffectColors.POISON.HexTag() + "中毒</color>，"
                    + EffectColors.COLD.HexTag() + "寒冷</color>，" + EffectColors.HEAT.HexTag() + "炎熱</color>，"
                    + EffectColors.DROWSY.HexTag() + "昏睡</color>\n";
                break;

            }
            /**
             *  展示：慎防中暑！在台地的太陽下逗留超過 $1 秒後會開始獲得炎熱效果
             */
            case Affliction.AfflictionType.Sunscreen: {
                Affliction_Sunscreen effect = (Affliction_Sunscreen) affliction;
                result += "慎防中暑！在台地的太陽下逗留超過 " + PrettyCount(effect.totalTime,1) + " 秒後會開始獲得" + EffectColors.HEAT.HexTag() + "炎熱效果</color>\n";
                break;
            }


            default: break;
        }

        return result;
    }

    private static EffectColors GetEffectColorsByStr(string effect) {
        return (EffectColors)Enum.Parse(typeof(EffectColors), effect.ToUpper());
    }

    private static void AddDisplayObject(){
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

    private static string EffectColorLocalName(EffectColors eff) {
        return eff switch {
            EffectColors.HUNGER => "饑餓",
            EffectColors.EXTRA_STAMINA => "額外體力",
            EffectColors.INJURY => "受傷",
            EffectColors.CRAB => "蟹蟹",
            EffectColors.POISON => "中毒",
            EffectColors.COLD => "寒冷",
            EffectColors.HEAT => "高溫",
            EffectColors.SLEEPY => "倦睏",
            EffectColors.DROWSY => "昏睡",
            EffectColors.CURSE => "詛咒",
            EffectColors.WEIGHT => "負重",
            EffectColors.THORNS => "荊棘",
            EffectColors.SHIELD => "護盾",
            EffectColors.ITEM_INFO_DISPLAY_POSITIVE => "正面效果",
            EffectColors.ITEM_INFO_DISPLAY_NEGATIVE => "負面效果",
            _ => eff.ToString(),
        };
    }

    private static string PrettyCount(float num, int dec) {
        return num.ToString("F" + dec.ToString()).Replace(".0", "");
    }
}