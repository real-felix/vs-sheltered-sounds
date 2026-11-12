using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ShelteredSounds;

public sealed class ShelteredSoundsSystem : ModSystem
{
    private const string HarmonyId = "shelteredsounds";
    private ICoreClientAPI? client;
    private RoomRegistry? rooms;
    private Harmony? harmony;
    private bool worldReady;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        client = api;
        rooms = api.ModLoader.GetModSystem<RoomRegistry>();
        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.LeaveWorld += OnLeaveWorld;
        CaveMusicPatches.IsSheltered = IsPlayerSheltered;

        harmony = new Harmony(HarmonyId);
        try
        {
            harmony.PatchAll(typeof(ShelteredSoundsSystem).Assembly);
        }
        catch
        {
            // Never leave a partially installed patch set behind.
            Dispose();
            throw;
        }
    }

    private void OnLevelFinalize() => worldReady = true;

    private void OnLeaveWorld() => worldReady = false;

    private bool IsPlayerSheltered()
    {
        if (!worldReady || client?.World.Player?.Entity is not { } player || rooms == null)
        {
            return false;
        }

        // Music decisions and loaded-track callbacks run on the client's main thread.
        // Query the registry there so block changes and movement take effect immediately
        // at the next music decision, without a second room cache or a flood fill of our own.
        return IsEnclosedRoom(rooms.GetRoomForPosition(player.Pos.AsBlockPos));
    }

    internal static bool IsEnclosedRoom(Room? room) =>
        room is { ExitCount: 0, AnyChunkUnloaded: 0 };

    public override void Dispose()
    {
        worldReady = false;
        CaveMusicPatches.IsSheltered = static () => false;
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        if (client != null)
        {
            client.Event.LevelFinalize -= OnLevelFinalize;
            client.Event.LeaveWorld -= OnLeaveWorld;
        }
        client = null;
        rooms = null;
        base.Dispose();
    }
}
