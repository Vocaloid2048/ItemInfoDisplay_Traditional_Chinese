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
        // 展示：$1 負重值
        suffixWeight += EffectColors.HUNGER + PrettyCount((item.carryWeight + Mathf.Min(Ascents.itemWeightModifier, 0)) * 2.5f, 1) + "負重值</color>";

        switch (itemGameObj.name) {
            case "Bugle(Clone)": 
                itemInfoDisplayTextMesh.text += "圍着隊友吹響它吧！\n"; break;
            case "Pirate Compass(Clone)": 
                itemInfoDisplayTextMesh.text += EffectColors.INJURY + "指向</color>"+"最近的行李箱\n"; break;
            case "Compass(Clone)":
                itemInfoDisplayTextMesh.text += EffectColors.INJURY + "指向</color>"+"山頂\n"; break;
            case "Shell Big(Clone)":
                itemInfoDisplayTextMesh.text += "試試把它" + EffectColors.HUNGER + "丟向</color>"+"椰子\n"; break;
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
                    prefixStatus += ProcessEffect((effect.restorationAmount * -1f), EffectColors.HUNGER); break;
                }
                case Action_GiveExtraStamina effect: {
                    prefixStatus += ProcessEffect(effect.amount, EffectColors.EXTRA_STAMINA); break;
                }
                case Action_InflictPoison effect: {
                    prefixStatus += "經過 " + effect.delay.ToString() + "秒後，" + ProcessEffectOverTime(effect.poisonPerSecond, 1f, effect.inflictionTime, EffectColors.POISON); break;
                }
                case Action_AddOrRemoveThorns effect: {
                    // TODO: Search for thorns amount per applied thorn
                    prefixStatus += ProcessEffect((effect.thornCount * 0.05f), EffectColors.THORNS); break;
                }
                case Action_ModifyStatus effect: {
                    prefixStatus += ProcessEffect(effect.changeAmount, GetEffectColorsByStr(effect.statusType.ToString())); break;
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
                // NOT DONE YET, START FROM Action_ApplyAffliction
            }
        }

        if ((prefixStatus.Length > 0) && isConsumable){
            itemInfoDisplayTextMesh.text = prefixStatus + "\n" + itemInfoDisplayTextMesh.text;
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

        result += (effect == EffectColors.EXTRA_STAMINA ? EffectColors.ITEM_INFO_DISPLAY_POSITIVE : EffectColors.ITEM_INFO_DISPLAY_NEGATIVE);
        result += (amount > 0 ? "獲得</color> " : "移除</color> ") + effect + PrettyCount(Mathf.Abs(amount) * 100f, 1) + " 點" + EffectColorLocalName(effect);

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
        result += (effect == EffectColors.EXTRA_STAMINA ? EffectColors.ITEM_INFO_DISPLAY_POSITIVE : EffectColors.ITEM_INFO_DISPLAY_NEGATIVE);
        result += (amountPerSecond > 0 ? "獲得</color> " : "移除</color> ") + effect + PrettyCount(Mathf.Abs(amountPerSecond) * time * (1 / rate) * 100f, 1) + " 點" + EffectColorLocalName(effect);

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
                    result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE + "獲得</color> " + PrettyCount(effect.totalTime + effect.climbDelay,1) + " 秒的 "
                  + EffectColors.EXTRA_STAMINA + PrettyCount(Mathf.Round(effect.moveSpeedMod * 100f),1) + "% 奔跑速度加成</color>，\n"
                  + EffectColors.ITEM_INFO_DISPLAY_POSITIVE + "或者獲得</color> " + PrettyCount(effect.totalTime, 1) + " 秒的 " 
                  + EffectColors.EXTRA_STAMINA + PrettyCount(Mathf.Round(effect.climbSpeedMod * 100f),1) + "% 攀爬速度加成</color>\n" 
                  + EffectColors.ITEM_INFO_DISPLAY_NEGATIVE + "然後獲得</color> " + EffectColors.DROWSY + PrettyCount(effect.drowsyOnEnd * 100f,1) + " 秒的昏睡效果</color>\n";
                    break;
            }
            /**
             * 展示：清除所有狀態（除了詛咒）\n
             */
            case Affliction.AfflictionType.ClearAllStatus: {
                Affliction_ClearAllStatus effect = (Affliction_ClearAllStatus) affliction;
                result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE + "清除所有狀態</color>" + (effect.excludeCurse ? "（除了" + EffectColors.CURSE + "詛咒</color>）\n" : "\n");
                break;
            }
            /**
             * 展示：獲得 $1 點額外體力
             */
            case Affliction.AfflictionType.AddBonusStamina: {
                Affliction_AddBonusStamina effect = (Affliction_AddBonusStamina) affliction;
                result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE + "獲得</color> " + EffectColors.EXTRA_STAMINA + PrettyCount(effect.staminaAmount * 100f, 1) + " 點額外體力</color>\n";
                break;
            }
            /**
             * 展示1：獲得 $1 秒的無限奔跑體力，或獲得 $2 秒的無限攀爬體力\n
             * 展示2：獲得 $1 秒的無限體力\n
             */
            case Affliction.AfflictionType.InfiniteStamina: {
                Affliction_InfiniteStamina effect = (Affliction_InfiniteStamina) affliction;
                if (effect.climbDelay > 0) {
                    result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE + "獲得</color> " + PrettyCount(effect.totalTime + effect.climbDelay, 1) + "秒的" 
                              + EffectColors.EXTRA_STAMINA + "無限奔跑體力</color>，或獲得 " + PrettyCount(effect.totalTime, 1) + "秒的"
                              + EffectColors.EXTRA_STAMINA + "無限攀爬體力</color>\n";
                } else {
                    result += EffectColors.ITEM_INFO_DISPLAY_POSITIVE + "獲得</color> " + PrettyCount(effect.totalTime, 1) + "秒的" 
                              + EffectColors.EXTRA_STAMINA + "無限體力</color>\n";
                }

                if (effect.drowsyAffliction != null) {
                    result += "然後獲得" + ProcessAffliction(effect.drowsyAffliction);
                }
                break;
            }
            // NOT DONE YET, START FROM AdjustStatus
            default: break;
        }

        return result;
    }

    private static EffectColors GetEffectColorsByStr(string effect) {
        return (EffectColors)Enum.Parse(typeof(EffectColors), effect);
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

    private enum EffectColors {
        [EnumMember(Value = "<#FFBD16>")] HUNGER,
        [EnumMember(Value = "<#BFEC1B>")] EXTRA_STAMINA,
        [EnumMember(Value = "<#FF5300>")] INJURY,
        [EnumMember(Value = "<#E13542>")] CRAB,
        [EnumMember(Value = "<#A139FF>")] POISON,
        [EnumMember(Value = "<#00BCFF>")] COLD,
        [EnumMember(Value = "<#C80918>")] HEAT,
        [EnumMember(Value = "<#FF5CA4>")] SLEEPY,
        [EnumMember(Value = "<#FF5CA4>")] DROWSY,
        [EnumMember(Value = "<#1B0043>")] CURSE,
        [EnumMember(Value = "<#A65A1C>")] WEIGHT,
        [EnumMember(Value = "<#768E00>")] THORNS,
        [EnumMember(Value = "<#D48E00>")] SHIELD,
        [EnumMember(Value = "<#DDFFDD>")] ITEM_INFO_DISPLAY_POSITIVE,
        [EnumMember(Value = "<#FFCCCC>")] ITEM_INFO_DISPLAY_NEGATIVE
    }

    private static string EffectColorLocalName(EffectColors eff) {
        switch (eff) {
            case EffectColors.HUNGER: return "饑餓";
            case EffectColors.EXTRA_STAMINA: return "額外體力";
            case EffectColors.INJURY : return "受傷";
            case EffectColors.CRAB : return "蟹蟹";
            case EffectColors.POISON : return "中毒";
            case EffectColors.COLD : return "寒冷";
            case EffectColors.HEAT : return "高溫";
            case EffectColors.SLEEPY : return "倦睏";
            case EffectColors.DROWSY : return "昏睡";
            case EffectColors.CURSE : return "詛咒";
            case EffectColors.WEIGHT : return "負重";
            case EffectColors.THORNS : return "荊棘";
            case EffectColors.SHIELD : return "護盾";
            case EffectColors.ITEM_INFO_DISPLAY_POSITIVE : return "正面效果";
            case EffectColors.ITEM_INFO_DISPLAY_NEGATIVE : return "負面效果";
            default: return eff.ToString();
        }
    }

    private static string PrettyCount(float num, int dec) {
        return num.ToString("F" + dec.ToString()).Replace(".0", "");
    }
}