using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements.Sanctum;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Models;
using ExileCore.Shared.Enums;
using GameOffsets.Native;

namespace PathfindSanctum;

public class RewardHelper(
    PluginBridge pluginBridge,
    IngameState gameState,
    Graphics graphics,
    PathFinder pathFinder,
    SanctumStateTracker stateTracker
)
{
    /** Meant to be used for day 1-2 when NinjaPricer is not working */
    private readonly Dictionary<string, double> baseCurrencyValues = new()
    {
        {"Armourer's Scrap", 0.11},
        {"Awakened Sextant", 1},
        {"Blacksmith's Whetstone", 0.07},
        {"Blessed Orb", 0.13},
        {"Cartographer's Chisel", 0.5},
        {"Chaos Orb", 1},
        {"Chromatic Orb", 0.1},
        {"Divine Orb", 170.5},
        {"Divine Vessel", 0.5},
        {"Enkindling Orb", 0.4},
        {"Exalted Orb", 12},
        {"Gemcutter's Prism", 1.1},
        {"Glassblower's Bauble", 0.45},
        {"Instilling Orb", 0.4},
        {"Jeweller's Orb", 0.04},
        {"Mirror of Kalandra", 128600},
        {"Orb of Alchemy", 0.04},
        {"Orb of Alteration", 0.25},
        {"Orb of Annulment", 4.3},
        {"Orb of Augmentation", 0.03},
        {"Orb of Binding", 0.03},
        {"Orb of Chance", 0.04},
        {"Orb of Fusing", 0.15},
        {"Orb of Horizon", 0.13},
        {"Orb of Regret", 0.8},
        {"Orb of Scouring", 0.25},
        {"Orb of Transmutation", 0.01},
        {"Orb of Unmaking", 0}, // 0 for Phrecia, 1 for normal leagues
        {"Regal Orb", 0.8},
        {"Sacred Orb", 11},
        {"Stacked Deck", 2},
        {"Vaal Orb", 0.17},
        {"Veiled Orb", 1700},
        {"Veiled Scarab", 1},
        {"Volatile Vaal Orb", 900}
    };

    bool usingDivinity = false;
    SanctumFloorWindow floorWindow;
    SanctumRewardWindow sanctumRewardWindow;
    IList<Element> rewardElements;
    int floor;

    // ExileCore's SanctumRewardWindowOffsets is stale on the current client; the reward container pointer now sits here.
    private const int SanctumRewardArrayContainerOffset = 0x2F0;

    private readonly Dictionary<string, double> currencyValueCache = new();

    public void DrawRewards()
    {
        usingDivinity = gameState.Data.MapStats.Any(x => x.Key.ToString() == "MapLycia2DuplicateUpToXDeferredRewards");
        floor = stateTracker.PlayerFloor;
        floorWindow = gameState.IngameUi.SanctumFloorWindow;
        sanctumRewardWindow = gameState.IngameUi.SanctumRewardWindow;
        if (floorWindow == null || sanctumRewardWindow == null || !sanctumRewardWindow.IsVisible)
            return;

        rewardElements = ResolveRewardElements(sanctumRewardWindow);
        if (rewardElements is not { Count: > 0 } || rewardElements[0].ChildCount >= 3)
            return;

        currencyValueCache.Clear();

        var rewardValues = GetRewardValues(rewardElements);
        DrawDivinityText(usingDivinity, sanctumRewardWindow.PositionNum);
        if (rewardValues.Count == 0)
            return;

        if (usingDivinity)
        {
            DrawRewardsDivinity(rewardValues);
        }
        else
        {
            DrawRewardsSimple(rewardValues);
        }
    }

    private class Reward
    {
        public string Name;
        public int Count;
        public double Value;
    }

    private static IList<Element> ResolveRewardElements(SanctumRewardWindow window)
    {
        if (window.RewardElements is { Count: > 0 } elements)
            return elements;

        try
        {
            var containerAddress = window.M.Read<long>(window.Address + SanctumRewardArrayContainerOffset);
            if (containerAddress != 0)
                return window.TheGame.GetObject<Element>(containerAddress)?.Children;
        }
        catch
        {
            // stale offset after a client patch, fall through
        }

        return null;
    }

    private void DrawDivinityText(bool usingDivinity, Vector2 position)
    {
        string text = usingDivinity ? "Divinity Detected" : "No Divinity Detected";
        graphics.DrawText(text, position - new Vector2(100, 40), SharpDX.Color.White, 45, FontAlign.Center);
    }

    private void DrawRewardElements(Dictionary<Element, Reward> rewardValues, KeyValuePair<Element, Reward> bestReward)
    {
        foreach (var reward in rewardValues)
        {
            var rewardPos = reward.Key.Children[1].GetClientRectCache;

            if (reward.Key.Address == bestReward.Key.Address)
            {
                DrawBestReward(reward, rewardPos);
            }
            else if (reward.Key.Address != rewardElements.Last().Address || floor == 4 || !usingDivinity)
            {
                DrawNonBestReward(reward, rewardPos);
            }
            else
            {
                DrawDivinityWarning(reward, rewardPos);
            }
        }
    }

    private void DrawBestReward(KeyValuePair<Element, Reward> reward, SharpDX.RectangleF rewardPos)
    {
        // Different color for divines & mirrors
        SharpDX.Color boxColor = reward.Value.Name.Contains("Divine Orb") || reward.Value.Name.Contains("Mirror") ? SharpDX.Color.Magenta : SharpDX.Color.DarkGreen;
        string text = reward.Value.Name.Contains("Divine Orb") ? "TAKE THE DIVINES..." : "TAKE THIS";

        graphics.DrawBox(rewardPos, boxColor);
        graphics.DrawText(text, new Vector2(rewardPos.Center.X, rewardPos.Center.Y) - new Vector2(0, 7), SharpDX.Color.White, 45, FontAlign.Center);
        graphics.DrawText($"{reward.Value.Count}x {reward.Value.Name} ({reward.Value.Value})", new Vector2(rewardPos.Center.X, rewardPos.Center.Y) + new Vector2(0, 8), SharpDX.Color.White, 45, FontAlign.Center);
    }

    private void DrawNonBestReward(KeyValuePair<Element, Reward> reward, SharpDX.RectangleF rewardPos)
    {
        graphics.DrawBox(rewardPos, SharpDX.Color.DarkRed);
        graphics.DrawText("DONT TAKE", new Vector2(rewardPos.Center.X, rewardPos.Center.Y) - new Vector2(0, 7), SharpDX.Color.White, 45, FontAlign.Center);
        graphics.DrawText($"{reward.Value.Count}x {reward.Value.Name}", new Vector2(rewardPos.Center.X, rewardPos.Center.Y) + new Vector2(0, 8), SharpDX.Color.White, 45, FontAlign.Center);
    }

    private void DrawDivinityWarning(KeyValuePair<Element, Reward> reward, SharpDX.RectangleF rewardPos)
    {
        graphics.DrawBox(rewardPos, SharpDX.Color.DarkRed);
        graphics.DrawText($"THIS IS FLOOR {floor}...", new Vector2(rewardPos.Center.X, rewardPos.Center.Y) - new Vector2(0, 10), SharpDX.Color.White, 45, FontAlign.Center);
        graphics.DrawText("DONT TAKE LAST REWARD", new Vector2(rewardPos.Center.X, rewardPos.Center.Y) + new Vector2(0, 5), SharpDX.Color.White, 45, FontAlign.Center);
        graphics.DrawText($"{reward.Value.Count}x {reward.Value.Name}", new Vector2(rewardPos.Center.X, rewardPos.Center.Y) + new Vector2(0, 20), SharpDX.Color.White, 45, FontAlign.Center);
    }

    private void DrawFloor4InfoText(Vector2 position, int pactCounter, int rewardsWeCanTake, int highValueRoomCount)
    {
        graphics.DrawText($"High Value Rooms Remaining: {highValueRoomCount}", position - new Vector2(160, 0), SharpDX.Color.White, 25, FontAlign.Left);
        graphics.DrawText($"Pacts Remaining: {pactCounter}", position - new Vector2(160, -20), SharpDX.Color.White, 25, FontAlign.Left);
        graphics.DrawText($"Rewards We Can Take: {rewardsWeCanTake}", position - new Vector2(160, -40), SharpDX.Color.White, 25, FontAlign.Left);
    }

    private void DrawRewardsForFloor4(Dictionary<Element, Reward> rewardValues)
    {
        var (highValueRoomCount, pactCounter, rewardsWeCanTake) = CalculateFloor4Metrics();

        DrawFloor4InfoText(sanctumRewardWindow.PositionNum, pactCounter, rewardsWeCanTake, highValueRoomCount);

        var best = SelectBest(rewardValues);

        if (highValueRoomCount <= 1 && rewardsWeCanTake >= 1 && !IsInPactRoom())
        {
            DrawRewardElements(rewardValues, best);
            return;
        }

        // No duplication budget left: keep the immediate reward unless something is worth at least a Divine.
        var immediate = rewardValues.First();
        DrawRewardElements(rewardValues, best.Value.Value >= GetCurrencyValue("Divine Orb") ? best : immediate);
    }

    private void DrawRewardsSimple(Dictionary<Element, Reward> rewardValues)
    {
        DrawRewardElements(rewardValues, SelectBest(rewardValues));
    }

    private void DrawRewardsDivinity(Dictionary<Element, Reward> rewardValues)
    {
        if (floor <= 2)
        {
            var bestReward = GetBestReward(rewardValues, rewardElements, 1);
            DrawRewardElements(rewardValues, bestReward);
        }
        else if (floor == 3)
        {
            var bestReward = DetermineBestRewardForFloor3(rewardValues);
            DrawRewardElements(rewardValues, bestReward);
        }
        else if (floor == 4)
        {
            DrawRewardsForFloor4(rewardValues);
        }
    }

    private static bool IsMirror(string currencyName) =>
        currencyName != null && currencyName.Contains("Mirror", StringComparison.OrdinalIgnoreCase);

    /** A Mirror outranks everything else, whatever its computed value. Ties keep the offer order. */
    private static KeyValuePair<Element, Reward> SelectBest(IEnumerable<KeyValuePair<Element, Reward>> candidates)
    {
        return candidates.OrderByDescending(x => IsMirror(x.Value.Name))
                         .ThenByDescending(x => x.Value.Value)
                         .FirstOrDefault();
    }

    /** Select the best reward, skipping the last one or two unless they are divine/mirror */
private static KeyValuePair<Element, Reward> GetBestReward(Dictionary<Element, Reward> rewardValues, IList<Element> rewardElements, int skipCount)
{
    var mirror = rewardValues.FirstOrDefault(x => IsMirror(x.Value.Name));
    if (mirror.Key != null) return mirror;

    var lastRewards = rewardElements.TakeLast(skipCount).Select(e => e.Address).ToHashSet();
    return SelectBest(rewardValues.Where(x => !lastRewards.Contains(x.Key.Address) || x.Value.Name.Contains("Divine Orb", StringComparison.OrdinalIgnoreCase) || x.Value.Name.Contains("Volatile Vaal Orb", StringComparison.OrdinalIgnoreCase)));
}

    private bool IsInPactRoom()
    {
        var currentRoom = stateTracker.GetCurrentRoom();
        return currentRoom.RewardType.Contains("Deal");
    }



    private Dictionary<Element, Reward> GetRewardValues(IEnumerable<Element> rewardElements) {
        
    var rewardValues = new Dictionary<Element, Reward>();
    var regex = new Regex(@"(Receive)\s((?'rewardcount'(\d+))x(?'rewardname'.*))\s(right now|at the end of the next Floor|at the end of the Floor|on completing the Sanctum)$");

    foreach (var reward in rewardElements)
    {
        var match = regex.Match(reward.Children[1].Text);
        if (!match.Success) continue;

        var rewardName = match.Groups["rewardname"].Value.Trim();
        if (!int.TryParse(match.Groups["rewardcount"].ValueSpan.Trim(), out var stackSize)) continue;

        var value = GetCurrencyValue(rewardName) * stackSize;

        rewardValues.Add(reward, new Reward { Name = rewardName, Count = stackSize, Value = value });
    }

    return rewardValues;
}

    private double GetCurrencyValue(string currencyName)
    {
        if (string.IsNullOrEmpty(currencyName)) return 0;
        if (currencyValueCache.TryGetValue(currencyName, out var cached)) return cached;

        var baseName = currencyName.Replace("Orbs", "Orb").Replace("Mirrors", "Mirror").TrimEnd('s');
        if (currencyName.Equals("Orb of Horizon", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "Orb of Horizons";
        }

        var data = new BaseItemType
        {
            BaseName = baseName,
            ClassName = "StackableCurrency",
            Metadata = ""
        };

        var fn = pluginBridge.GetMethod<Func<BaseItemType, double>>("NinjaPrice.GetBaseItemTypeValue");
        var value = fn?.Invoke(data) ?? 0;
        if (value == 0)
            value = baseCurrencyValues.GetValueOrDefault(baseName, 0);

        currencyValueCache[currencyName] = value;
        return value;
    }

    private KeyValuePair<Element, Reward> DetermineBestRewardForFloor3(Dictionary<Element, Reward> rewardValues)
    {
        if (floorWindow.FloorData.RoomChoices.Count == 8)
        {
            // Use Floor 4 logic since 2nd reward goes to "End of Floor 4"
            return GetBestReward(rewardValues, rewardElements, 2);
        }

        return GetBestReward(rewardValues, rewardElements, 1);
    }
    private (int highValueRoomCount, int pactCounter, int rewardsWeCanTake) CalculateFloor4Metrics()
    {
        int highValueRoomCount = 0;
        int pactCounter = 0;

        List<(int, int)> path = [];
        pathFinder.CreateRoomWeightMap();
        path = pathFinder.FindBestPath();

        var divineValue = GetCurrencyValue("Divine Orb");

        foreach (var room in path)
        {
            if (IsCurrentPlayerRoom(room)) continue;

            var sanctumRoom = floorWindow.RoomsByLayer[room.Item1][room.Item2];

            if (sanctumRoom?.Data?.RewardRoom?.Id.Contains("_Deal") ?? false)
            {
                pactCounter++;
            }

            // Rooms worth reserving a duplication slot for: a Mirror, or anything priced at least a Divine.
            if (new[] { sanctumRoom?.Data?.Reward1, sanctumRoom?.Data?.Reward2, sanctumRoom?.Data?.Reward3 }.Any(x => x != null && (IsMirror(x.CurrencyName) || GetCurrencyValue(x.CurrencyName) >= divineValue)))
            {
                highValueRoomCount++;
            }
        }

        var floorData = RemoteMemoryObject.GetObjectStatic<SanctumFloorData>(floorWindow.FloorData.Address - 0x88);
        var rewards = floorData.M.ReadStdVectorStride<long>(floorData.M.Read<StdVector>(floorData.Address + 0x70), 0x10)
            .Select(TheGame.pTheGame.Files.SanctumDeferredRewards.GetByAddressOrReload)
            .Where(x => x != null).ToList();


        int currentRewardCount = rewards.Count(x => x.DeferralCategory.Id.Contains("FinalBoss") || x.DeferralCategory.Id.Contains("RewardFloor"));

        var duplicateRewardStat = gameState.Data.MapStats
            .FirstOrDefault(x => x.Key.ToString() == "MapLycia2DuplicateUpToXDeferredRewards");

        int rewardsWeCanTake = duplicateRewardStat.Value - currentRewardCount - pactCounter - highValueRoomCount;

        return (highValueRoomCount, pactCounter, rewardsWeCanTake);
    }

    private bool IsCurrentPlayerRoom((int, int) room)
    {
        return room.Item1 == stateTracker.PlayerLayerIndex && room.Item2 == stateTracker.PlayerRoomIndex;
    }

}
